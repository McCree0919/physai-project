Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
    $process = Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
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
