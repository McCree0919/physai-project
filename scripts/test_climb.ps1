param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$root=Split-Path -Parent $PSScriptRoot
$snapshots=@{}
foreach($relative in @('ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/SceneTemplateSettings.json')) {
 $path=Join-Path $root $relative
 $snapshots[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
try {
 $null=Invoke-ReachUnity -UnityPath $UnityPath -LogName 'climb-build.log' -ExecuteMethod 'ClimbTrainingBuilder.Build'
 if(-not $UnityPath){$UnityPath=Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe'}
 $output=Join-Path $root 'results/climb-tests.xml';$log=Join-Path $root 'results/climb-tests.log'
 if(Test-Path -LiteralPath $output){Remove-Item -LiteralPath $output}
 $arguments="-batchmode -nographics -projectPath `"$root`" -runTests -testPlatform PlayMode -assemblyNames Climber.PlayModeTests;Structures.PlayModeTests;ClimbLearning.Tests -testResults `"$output`" -logFile `"$log`""
 $process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
 $process.WaitForExit()
 if(-not(Test-Path -LiteralPath $output)){throw "No test results: $log"}
 [xml]$results=Get-Content -LiteralPath $output -Raw;$run=$results.'test-run'
 if($process.ExitCode -ne 0 -or [int]$run.failed -ne 0 -or [int]$run.total -lt 47 -or [int]$run.passed -ne [int]$run.total){throw "Climb tests failed: $($run.passed)/$($run.total); log $log"}
 Write-Host "PASS: $($run.passed)/$($run.total) PlayMode tests"
} finally {
 foreach($path in $snapshots.Keys){if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
}
