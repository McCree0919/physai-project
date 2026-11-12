param([string]$UnityPath)
# Freezes the one-shot layer 3 confirmation set (ClimbConfirmSet.Freeze -> structures/box-stacks-v3-confirm). Refuses if it exists.
. "$PSScriptRoot/unity_common.ps1"
$root=Split-Path -Parent $PSScriptRoot
$snapshots=@{}
foreach($relative in @('ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/SceneTemplateSettings.json')) {
 $path=Join-Path $root $relative
 $snapshots[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
try {$null=Invoke-ReachUnity -UnityPath $UnityPath -LogName 'confirm-freeze.log' -ExecuteMethod 'ClimbConfirmSet.Freeze'}
finally {
 foreach($path in $snapshots.Keys){if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
}
