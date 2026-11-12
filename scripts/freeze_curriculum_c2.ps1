param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$root=Split-Path -Parent $PSScriptRoot
$snapshots=@{}
foreach($relative in @('ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/SceneTemplateSettings.json')) {
 $path=Join-Path $root $relative
 $snapshots[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
try {$null=Invoke-ReachUnity -UnityPath $UnityPath -LogName 'curriculum-c2-freeze.log' -ExecuteMethod 'ClimbCurriculumSets.FreezeC2'}
finally {
 foreach($path in $snapshots.Keys){if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
}
