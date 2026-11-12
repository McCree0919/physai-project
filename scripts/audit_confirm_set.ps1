param([string]$UnityPath)
# Scripted-controller audit of the confirmation set (FeasibilityAudit.AuditConfirmationSet -> results/confirm-audit/audit.csv). Reference only.
. "$PSScriptRoot/unity_common.ps1"
$root=Split-Path -Parent $PSScriptRoot
if(-not $UnityPath){$UnityPath=Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe'}
$snapshots=@{}
foreach($relative in @('ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/SceneTemplateSettings.json')) {
 $path=Join-Path $root $relative
 $snapshots[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
$log=Join-Path $root 'results/confirm-audit.log';$xml=Join-Path $root 'results/confirm-audit.xml'
$env:CLIMB_RUN_DIAGNOSTICS='1'
try {
 $arguments="-batchmode -nographics -projectPath `"$root`" -runTests -testPlatform PlayMode -assemblyNames Structures.PlayModeTests -testFilter FeasibilityAudit.AuditConfirmationSet -testResults `"$xml`" -logFile `"$log`""
 $process=Start-GuardedProcess -FilePath $UnityPath -ArgumentList $arguments -LogPath $log
 if($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'CONFIRM_AUDIT_COMPLETE')){throw "Audit failed (exit $($process.ExitCode)); log $log"}
 Write-Host "PASS: confirmation audit; $log"
}
finally {
 foreach($path in $snapshots.Keys){if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
}
