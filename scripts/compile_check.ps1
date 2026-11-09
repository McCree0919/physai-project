param([string]$UnityPath)
. "$PSScriptRoot/unity_common.ps1"
$null = Invoke-ReachUnity -UnityPath $UnityPath -LogName 'compile_check.log'
