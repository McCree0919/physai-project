param([int]$Trajectories=128,[int]$Seed=2026,[string]$Output='results/climb-snapshots')
# Runs the snapshot recorder player (build first with build_climb_snapshots.ps1) and checks its manifest.
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root $Output
if(Test-Path -LiteralPath $out){throw "Output exists, choose a new folder: $out"}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe=Join-Path $root 'Builds/ClimbSnapshots/ClimbSnapshots.exe'
$arguments="-batchmode -nographics -logFile `"$out\player.log`" --climb-snapshot-output=`"$out`" --climb-snapshot-trajectories=$Trajectories --climb-snapshot-seed=$Seed --climb-output=`"$out\runtime`""
$process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
$manifest=Join-Path $out 'manifest.json'
if(-not(Test-Path -LiteralPath $manifest)){throw "No manifest; see $out\player.log"}
Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json | Select-Object trajectories,stages,rejections | Format-List
