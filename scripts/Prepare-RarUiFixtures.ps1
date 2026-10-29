[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Exact registered native scratch required.' }
$fixture = Join-Path $scratch 'fixture'
if (!(Test-Path -LiteralPath (Join-Path $fixture 'containers/sample.zip'))) {
    & (Join-Path $PSScriptRoot 'Prepare-S4Fixtures.ps1') -ScratchRoot $scratch
    if ($LASTEXITCODE -ne 0) { throw 'Archive UI fixture preparation failed.' }
}
if (!(Test-Path -LiteralPath (Join-Path $fixture 'containers/writable.rar'))) {
    & dotnet (Join-Path $scratch 'artifacts/bin/RarContracts/debug/RarContracts.dll') --prepare-ui $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Native RAR UI creation failed.' }
}
Copy-Item -LiteralPath (Join-Path $scratch 'rar-fixtures/encrypted-rar5.rar') -Destination (Join-Path $fixture 'containers/encrypted-rar5.rar')
Copy-Item -LiteralPath (Join-Path $scratch 'unsupported-fixtures/with_recovery.rar') -Destination (Join-Path $fixture 'containers/with_recovery.rar')
