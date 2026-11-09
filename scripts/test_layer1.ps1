param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$root = Split-Path -Parent $PSScriptRoot
# Dependencies/Test Runner can rewrite these files. Restore their exact bytes.
$snapshots = @{}
foreach ($relative in @('ProjectSettings/ProjectSettings.asset', 'ProjectSettings/EditorBuildSettings.asset', 'ProjectSettings/SceneTemplateSettings.json')) {
    $path = Join-Path $root $relative
    $snapshots[$path] = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllBytes($path) } else { $null }
}
try {
    $null = Invoke-ReachUnity -UnityPath $UnityPath -LogName 'layer1-generate.log' -ExecuteMethod 'ClimberSceneBuilder.Build'
    if (-not $UnityPath) {
        $line = Get-Content (Join-Path $root 'ProjectSettings/ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion: ' } | Select-Object -First 1
        $version = ($line -split ': ', 2)[1]
        $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
    }
    $logPath = Join-Path $root 'results/layer1-tests.log'
    $resultPath = Join-Path $root 'results/layer1-tests.xml'
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
    # Test Runner owns shutdown. Do not add -quit to this invocation.
    $arguments = "-batchmode -nographics -projectPath `"$root`" -runTests -testPlatform PlayMode -testFilter ClimberTests -testResults `"$resultPath`" -logFile `"$logPath`""
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if (-not (Test-Path -LiteralPath $resultPath)) { throw "No test result XML. Log: $logPath" }
    [xml]$results = Get-Content -LiteralPath $resultPath -Raw
    $run = $results.'test-run'
    if ($process.ExitCode -ne 0 -or [int]$run.failed -ne 0 -or [int]$run.total -eq 0 -or [int]$run.passed -ne [int]$run.total) {
        throw "Layer 1 failed: exit=$($process.ExitCode), passed=$($run.passed), total=$($run.total). Log: $logPath"
    }
    if (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors|Compilation failed') {
        throw "Compilation failed. Log: $logPath"
    }
    Write-Host "PASS: Layer 1 PlayMode $($run.passed)/$($run.total), Unity exit 0. Results: $resultPath"
}
finally {
    foreach ($path in $snapshots.Keys) {
        if ($null -ne $snapshots[$path]) { [IO.File]::WriteAllBytes($path, [byte[]]$snapshots[$path]) }
        elseif (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}
