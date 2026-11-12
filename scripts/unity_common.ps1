Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Process and log guard for the PowerShell launchers (2026-11-12; the Python runners use scripts/player_guard.py).
# Before a Unity editor or player is started: refuse if a player from either checkout's Builds/, a Unity editor on either project or an
# mlagents trainer is still running. While it runs: stop it if its -logFile grows past the cap. When the script ends, normally or by an
# exception/Ctrl-C: kill it if it is still running, then list leftovers again. Every check goes to results/process-guard.log.
$script:GuardCheckouts = @('C:\dev\physai-exp', 'C:\dev\physai-project')

function Write-GuardLog([string]$Message) {
    $line = "$(Get-Date -Format s) [$(Split-Path -Leaf $MyInvocation.PSCommandPath)] pid=$PID $Message"
    $folder = Join-Path (Split-Path -Parent $PSScriptRoot) 'results'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    [IO.File]::AppendAllText((Join-Path $folder 'process-guard.log'), $line + "`n")
    Write-Host "PLAYER_GUARD $line"
}

function Get-ClimbLeftovers {
    $builds = @($script:GuardCheckouts | ForEach-Object { (Join-Path $_ 'Builds').ToLower() })
    $projects = @($script:GuardCheckouts | ForEach-Object { $_.ToLower().Replace('\', '/') })
    @(Get-CimInstance Win32_Process | Where-Object {
        $path = ([string]$_.ExecutablePath).ToLower(); $cmd = ([string]$_.CommandLine).ToLower().Replace('\', '/')
        $player = @($builds | Where-Object { $path.StartsWith($_) }).Count -gt 0
        $editor = $_.Name -ieq 'Unity.exe' -and @($projects | Where-Object { $cmd.Contains($_) }).Count -gt 0
        $trainer = $_.Name -like 'python*' -and $cmd -match 'climb_train_worker|mlagents-learn|mlagents\.trainers'
        $player -or $editor -or $trainer
    } | ForEach-Object { [pscustomobject]@{ pid = $_.ProcessId; name = $_.Name; cmd = ([string]$_.CommandLine).Substring(0, [Math]::Min(200, ([string]$_.CommandLine).Length)) } })
}

function Assert-NoClimbLeftovers([string]$Stage) {
    $left = @(Get-ClimbLeftovers)
    if ($left.Count -eq 0) { Write-GuardLog "${Stage}: no leftover processes"; return }
    Write-GuardLog "${Stage}: $($left.Count) LEFTOVER process(es): $(ConvertTo-Json -InputObject $left -Compress)"
    if ($Stage -eq 'before') { throw "Leftover player/editor/trainer processes are running; close them first (results/process-guard.log)." }
}

function Start-GuardedProcess {
    param([string]$FilePath, [string]$ArgumentList, [string]$LogPath, [long]$CapBytes = 1GB)
    Assert-NoClimbLeftovers 'before'
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -WindowStyle Hidden -PassThru
    $null = $process.Handle  # keeps ExitCode readable after the process ends
    try {
        while (-not $process.WaitForExit(10000)) {
            if ($LogPath -and (Test-Path -LiteralPath $LogPath) -and (Get-Item -LiteralPath $LogPath).Length -gt $CapBytes) {
                Write-GuardLog "LOG_CAP exceeded: $LogPath is above $CapBytes bytes; killing pid $($process.Id)"
                & taskkill /PID $process.Id /T /F | Out-Null
                throw "Log cap exceeded: $LogPath"
            }
        }
        $process.WaitForExit()
    }
    finally {
        if (-not $process.HasExited) { Write-GuardLog "killing pid $($process.Id): the script ended before it"; & taskkill /PID $process.Id /T /F | Out-Null }
        Start-Sleep -Seconds 2
        Assert-NoClimbLeftovers 'after'
    }
    return $process
}

function Invoke-ReachUnity {
    param(
        [string]$UnityPath,
        [string]$LogName,
        [string]$ExecuteMethod
    )
    $projectRoot = Split-Path -Parent $PSScriptRoot
    if (-not $UnityPath) {
        $versionLine = Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') |
            Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
        $version = ($versionLine -split ': ', 2)[1]
        $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
    }
    if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
        throw "Unity executable not found: $UnityPath. Supply -UnityPath."
    }
    $logDirectory = Join-Path $projectRoot 'results'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    $logPath = Join-Path $logDirectory $LogName
    # Start-Process joins ArgumentList into a command line; explicitly quote paths.
    $unityArguments = "-batchmode -nographics -quit -projectPath `"$projectRoot`" -logFile `"$logPath`""
    if ($ExecuteMethod) { $unityArguments += " -executeMethod $ExecuteMethod" }
    Write-Host 'Close this project in the Unity Editor before running this command.'
    $process = Start-GuardedProcess -FilePath $UnityPath -ArgumentList $unityArguments -LogPath $logPath
    if (-not (Test-Path -LiteralPath $logPath)) { throw "Unity did not create $logPath" }
    $failures = @(Select-String -LiteralPath $logPath -Pattern @(
        'error (CS|BC)\d+', 'Scripts have compiler errors', 'Compilation failed',
        'BuildFailedException', 'Build completed with a result of.*Failed',
        'Aborting batchmode', 'executeMethod.*(could not|failed)',
        '^.*\b(Fatal Error|Exception|Error):', '^.*\b\w+Exception:'
    ) | Where-Object {
        # Unity Hub token refresh can fail while the installed license is valid.
        $_.Line -notmatch '^\[Licensing::Module\] Error: Access token is unavailable; failed to update'
    })
    if ($process.ExitCode -ne 0 -or $failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host $_.Line }
        throw "Unity failed (exit $($process.ExitCode)). Log: $logPath"
    }
    if (-not (Select-String -LiteralPath $logPath -SimpleMatch 'Exiting batchmode successfully')) {
        throw "Unity did not report a successful batchmode exit. Log: $logPath"
    }
    Write-Host "PASS: Unity exit 0, no compilation/build errors. Log: $logPath"
    return $logPath
}
