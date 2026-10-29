[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$') { throw 'Use registered S4 native scratch.' }
& (Join-Path $PSScriptRoot 'Build-Development.ps1') -ScratchRoot $scratch -Configuration Debug -ProjectFile (Join-Path $PSScriptRoot '../tests/ArchiveContracts/ArchiveContracts.csproj')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet (Join-Path $scratch 'artifacts/bin/ArchiveContracts/debug/ArchiveContracts.dll') (Join-Path $scratch ('contracts-fixture-' + [guid]::NewGuid().ToString('N')))
exit $LASTEXITCODE
