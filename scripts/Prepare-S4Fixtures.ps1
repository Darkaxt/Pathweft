[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$' -and $scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Use registered archive native scratch.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
[void][IO.Directory]::CreateDirectory($env:TEMP)
& python -B (Join-Path $PSScriptRoot 'Prepare-S4Fixtures.py') --scratch-root $scratch
if ($LASTEXITCODE -ne 0) { throw 'Fixture preparation failed.' }
$fixture = Join-Path $scratch 'fixture'
$tool = Join-Path $env:USERPROFILE 'scoop/apps/7zip/current/7z.exe'
foreach ($format in @('zip','7z','tar')) {
    & $tool a ('-t' + $format) (Join-Path $fixture "containers/sample.$format") (Join-Path $fixture 'source/*') -mmt=2 -y
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture encoding failed.' }
}
& $tool a -t7z (Join-Path $fixture 'containers/encrypted.7z') (Join-Path $fixture 'source/nested/*') -ppathweft-fixture-only -mhe=on -mmt=2 -y
if ($LASTEXITCODE -ne 0) { throw 'Synthetic encrypted fixture encoding failed.' }
& $tool a -t7z (Join-Path $fixture 'containers/multipart.7z') (Join-Path $fixture 'source/payload.bin') -v64k -mmt=2 -y
if ($LASTEXITCODE -ne 0) { throw 'Multipart fixture encoding failed.' }
& makecab (Join-Path $fixture 'source/image.png') (Join-Path $fixture 'containers/sample.cab')
if ($LASTEXITCODE -ne 0) { throw 'CAB fixture encoding failed.' }
Write-Output 'Native/ZIP/TAR/TGZ/GZIP/CAB, corrupt, traversal, ambiguous, encrypted and multipart fixtures prepared.'
