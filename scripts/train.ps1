param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]*$')]
    [string]$RunId,
    [switch]$Resume
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'Builds/Reach/Reach.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Reach.exe is missing. Run scripts/build.ps1 first.'
}
if (-not (Get-Command mlagents-learn -ErrorAction SilentlyContinue)) {
    throw 'mlagents-learn not found. Activate the mlagents conda environment first.'
}
$trainingArguments = @(
    'config/reach.yaml', "--env=$executable", '--num-envs=4',
    '--no-graphics', "--run-id=$RunId"
)
if ($Resume) { $trainingArguments += '--resume' }
Push-Location $projectRoot
try {
    & mlagents-learn @trainingArguments
    if ($LASTEXITCODE -ne 0) { throw "Training exited with code $LASTEXITCODE" }
}
finally { Pop-Location }
