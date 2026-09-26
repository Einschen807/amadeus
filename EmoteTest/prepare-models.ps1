param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
& $Python (Join-Path $PSScriptRoot 'prepare_models.py')
if ($LASTEXITCODE -ne 0) { throw 'Model preparation failed' }
$compiler = Join-Path $PSScriptRoot '..\extracted_emote\tools\FreeMote\PsBuild.exe'
for ($i=0; $i -lt 4; $i++) {
    & $compiler -v 4 -ns -o (Join-Path $PSScriptRoot "cache\kurisu$i.psb") (Join-Path $PSScriptRoot "cache\kurisu$i.json")
    if ($LASTEXITCODE -ne 0) { throw "Model $i compile failed" }
}
