param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$logPath = Invoke-ReachUnity -UnityPath $UnityPath -LogName 'build.log' -ExecuteMethod 'BuildScript.Build'
$executable = Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds/Reach/Reach.exe'
if (-not (Select-String -LiteralPath $logPath -SimpleMatch 'REACH_BUILD_SUCCEEDED:') -or
    -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Build output could not be verified: $executable"
}
Write-Host "Build ready: $executable"
