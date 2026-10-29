[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [string]$CacheRoot = 'D:\BuildCache\Pathweft')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Exact registered native scratch required.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $projectRoot 'native/pathweft-rars/Cargo.toml'
$previous = @{}
$environment = @{ CARGO_HOME=(Join-Path $CacheRoot 'cargo'); CARGO_TARGET_DIR=(Join-Path $scratch 'cargo-target'); TEMP=(Join-Path $scratch 'temp'); TMP=(Join-Path $scratch 'temp') }
try {
    foreach ($key in $environment.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key,'Process'); [Environment]::SetEnvironmentVariable($key,$environment[$key],'Process') }
    $metadata = (& cargo metadata --manifest-path $manifest --locked --offline --format-version 1 | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0) { throw 'Locked metadata resolution failed.' }
    $donor = $metadata.packages | Where-Object name -eq 'rars'
    if ($donor.version -ne '0.12.3' -or !$donor.source.EndsWith('#e2415a2eb12e6b62deaa0cca919864eabe4671b1')) { throw 'Donor identity differs.' }
    $dependencies = @($metadata.packages | Where-Object name -ne 'pathweft-rars' | Sort-Object name | ForEach-Object { [ordered]@{ name=$_.name; version=$_.version; license=$_.license; source=$_.source } })
    if ($dependencies | Where-Object { !$_.license }) { throw 'Dependency terms missing.' }
    $runtime = Join-Path $CacheRoot 'rars/0.12.3-pathweft.1'
    $receipt = [ordered]@{ backend='rars'; version='0.12.3-pathweft.1'; source='https://github.com/bitplane/rars'; sourceCommit='e2415a2eb12e6b62deaa0cca919864eabe4671b1'; abi=1; architecture='x86_64-pc-windows-msvc'; rustc=(& rustc --version); dll='pathweft_rars.dll'; dllSha256=(Get-FileHash (Join-Path $runtime 'pathweft_rars.dll') -Algorithm SHA256).Hash; bridgeSourceSha256=(Get-FileHash (Join-Path $projectRoot 'native/pathweft-rars/src/lib.rs') -Algorithm SHA256).Hash; lockSha256=(Get-FileHash (Join-Path $projectRoot 'native/pathweft-rars/Cargo.lock') -Algorithm SHA256).Hash; cargoManifestSha256=(Get-FileHash $manifest -Algorithm SHA256).Hash; license='Apache-2.0 donor; GPL-3.0-or-later Pathweft bridge'; dependencies=$dependencies }
    $json = ($receipt | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText((Join-Path $projectRoot 'vendor/rars/runtime-manifest.json'), $json)
    [IO.File]::WriteAllText((Join-Path $runtime 'manifest.json'), $json)
    Write-Output 'Exported source/lock/toolchain/DLL provenance and dependency license inventory without machine-local source paths.'
}
finally { foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') } }
