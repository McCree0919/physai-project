param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$root=Split-Path -Parent $PSScriptRoot
$snapshots=@{}
foreach($relative in @('ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/SceneTemplateSettings.json')) {
    $path=Join-Path $root $relative
    $snapshots[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
try {
    $null=Invoke-ReachUnity -UnityPath $UnityPath -LogName 'layer2-static-generate.log' -ExecuteMethod 'StaticStructuresEditor.BuildDemo'
    if(-not $UnityPath){$UnityPath=Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe'}
    $output=Join-Path $root 'results/layer2-static-tests.xml'
    $log=Join-Path $root 'results/layer2-static-tests.log'
    if(Test-Path -LiteralPath $output){Remove-Item -LiteralPath $output}
    $arguments="-batchmode -nographics -projectPath `"$root`" -runTests -testPlatform PlayMode -assemblyNames Climber.PlayModeTests;Structures.PlayModeTests -testResults `"$output`" -logFile `"$log`""
    Write-Host 'Close this project in Unity Editor before running batchmode.'
    $process=Start-GuardedProcess -FilePath $UnityPath -ArgumentList $arguments -LogPath $log
    if(-not(Test-Path -LiteralPath $output)){throw "No test results: $log"}
    [xml]$results=Get-Content -LiteralPath $output -Raw
    $run=$results.'test-run'
    if($process.ExitCode -ne 0 -or [int]$run.failed -ne 0 -or [int]$run.passed -ne [int]$run.total -or [int]$run.total -lt 31){throw "Layer 2 tests failed: $($run.passed)/$($run.total); log $log"}
    if(Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors|Compilation failed'){throw "Compilation error: $log"}
    Write-Host "PASS: $($run.passed)/$($run.total) PlayMode tests; existing tests unchanged."
}
finally {
    foreach($path in $snapshots.Keys){
        if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}
        elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}
    }
}
