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
 $process=Start-GuardedProcess -FilePath $UnityPath -ArgumentList $arguments -LogPath $log
 if(-not(Test-Path -LiteralPath $output)){throw "No test results: $log"}
 [xml]$results=Get-Content -LiteralPath $output -Raw;$run=$results.'test-run'
 # Pass: no failures and every test either passed or skipped (the CLIMB_RUN_DIAGNOSTICS-gated audits are skipped by design).
 if($process.ExitCode -ne 0 -or [int]$run.failed -ne 0 -or [int]$run.total -lt 47 -or ([int]$run.passed+[int]$run.skipped) -ne [int]$run.total){throw "Climb tests failed: passed $($run.passed), failed $($run.failed), skipped $($run.skipped), total $($run.total); log $log"}
 Write-Host "PASS: $($run.passed)/$($run.total) PlayMode tests passed, $($run.skipped) skipped, 0 failed"
} finally {
 foreach($path in $snapshots.Keys){if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
}
