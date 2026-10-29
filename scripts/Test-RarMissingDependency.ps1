[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [ValidateSet('Missing','Incompatible')][string]$Mode = 'Missing')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Exact registered native scratch required.' }
$copy = Join-Path $scratch ($Mode.ToLowerInvariant() + '-writer')
if (Test-Path -LiteralPath $copy) { throw 'Use a fresh qualification copy; do not mutate a running test app.' }
Copy-Item -LiteralPath (Join-Path $scratch 'artifacts/bin/RarContracts/debug') -Destination $copy -Recurse
$library = [IO.Path]::GetFullPath((Join-Path $copy 'Rars/pathweft_rars.dll'))
if (!$library.StartsWith($scratch + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Copied library escaped its fixture root.' }
Move-Item -LiteralPath $library -Destination ($library + '.absent')
if ($Mode -eq 'Incompatible') {
    # A real valid native DLL without the bridge exports exercises ABI rejection.
    Copy-Item -LiteralPath (Join-Path $scratch 'artifacts/bin/FileExplorer/debug/SevenZip/7z.dll') -Destination $library
}
& dotnet (Join-Path $copy 'RarContracts.dll') --missing-writer (Join-Path $scratch 'verified-contracts/transfer.rar')
if ($LASTEXITCODE -ne 0) { throw "$Mode-writer degraded behavior failed." }
