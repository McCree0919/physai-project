param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$root=Split-Path -Parent $PSScriptRoot
$snapshots=@{}
foreach($relative in @('ProjectSettings/ProjectSettings.asset','ProjectSettings/EditorBuildSettings.asset','ProjectSettings/SceneTemplateSettings.json')) {
    $path=Join-Path $root $relative
    $snapshots[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
try {
    $log=Invoke-ReachUnity -UnityPath $UnityPath -LogName 'layer2-prep-build.log' -ExecuteMethod 'Layer2PrepBuild.Build'
    if(-not (Select-String -LiteralPath $log -SimpleMatch 'LAYER2_PREP_BUILD_OK')){throw 'Missing successful build marker'}
}
finally {
    foreach($path in $snapshots.Keys){
        if($null -ne $snapshots[$path]){[IO.File]::WriteAllBytes($path,[byte[]]$snapshots[$path])}
        elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}
    }
}
