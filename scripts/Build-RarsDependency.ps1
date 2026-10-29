[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ScratchRoot,
    [ValidateSet('Fetch','Build','Fixtures')][string]$Action = 'Build',
    [string]$CacheRoot = 'D:\BuildCache\Pathweft'
)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Use the registered S4R native scratch root.' }
$cache = [IO.Path]::GetFullPath($CacheRoot)
if (!$cache.StartsWith('D:\BuildCache\Pathweft', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use the retained Pathweft dependency cache.' }
$project = Join-Path $PSScriptRoot '../native/pathweft-rars/Cargo.toml'
$environment = @{ CARGO_HOME=(Join-Path $cache 'cargo'); CARGO_TARGET_DIR=(Join-Path $scratch 'cargo-target'); TEMP=(Join-Path $scratch 'temp'); TMP=(Join-Path $scratch 'temp'); CARGO_BUILD_JOBS='2' }
$previous = @{}
try {
    $rustVersion = (& rustc --version)
    if ($LASTEXITCODE -ne 0 -or $rustVersion -notmatch '^rustc 1\.94\.0 ') { throw 'Use the qualified Rust 1.94.0 toolchain; requalify before changing compiler versions.' }
    foreach ($key in $environment.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $environment[$key], 'Process')
    }
    foreach ($path in $environment.Values) { if ($path -match '^[DE]:\\') { [void][IO.Directory]::CreateDirectory($path) } }
    if ($Action -eq 'Fetch') { & cargo fetch --manifest-path $project; if ($LASTEXITCODE -ne 0) { throw 'Pinned Cargo fetch failed.' }; exit 0 }
    if ($Action -eq 'Fixtures') {
        & cargo run --release --locked --manifest-path $project --example fixtures -- (Join-Path $scratch 'rar-fixtures')
        if ($LASTEXITCODE -ne 0) { throw 'RAR fixture generation failed.' }
        exit 0
    }
    & cargo build --release --locked --manifest-path $project --jobs 2
    if ($LASTEXITCODE -ne 0) { throw 'Native RAR build failed.' }
    $runtime = Join-Path $cache 'rars/0.12.3-pathweft.1'
    [void][IO.Directory]::CreateDirectory($runtime)
    Copy-Item -LiteralPath (Join-Path $environment.CARGO_TARGET_DIR 'release/pathweft_rars.dll') -Destination (Join-Path $runtime 'pathweft_rars.dll')
    Get-FileHash -LiteralPath (Join-Path $runtime 'pathweft_rars.dll') -Algorithm SHA256
}
finally { foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') } }
