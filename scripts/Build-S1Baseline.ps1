[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s1-[^\\]+$') { throw 'Use registered S1 native/build scratch.' }
$repository = Split-Path -Parent $PSScriptRoot
$archive = Join-Path $scratch 'baseline.zip'
$source = Join-Path $scratch 'baseline-source'
if (Test-Path -LiteralPath $source) { throw 'Preserve an existing validation source; do not overwrite it.' }
Push-Location $repository
try {
    & git archive --format=zip --output=$archive a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6
    if ($LASTEXITCODE -ne 0) { throw 'Baseline archive failed.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $source
    & (Join-Path $PSScriptRoot 'Build-Development.ps1') -ScratchRoot (Join-Path $scratch 'baseline-build') -ProjectFile (Join-Path $source 'FileExplorer.csproj')
    exit $LASTEXITCODE
}
finally { Pop-Location }
