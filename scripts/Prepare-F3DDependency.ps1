[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [string]$CacheRoot = 'D:\BuildCache\Pathweft')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
$cache = [IO.Path]::GetFullPath($CacheRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$' -or $cache -notmatch '^D:\\' -or $cache.StartsWith('D:\Temp\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use registered native scratch and explicit persistent D: dependency storage.' }
$sdk = Join-Path $scratch 'f3d/sdk/F3D-4.0.0-Windows-x86_64'
$target = Join-Path $cache 'f3d/4.0.0'
$manifestPath = Join-Path $target 'bundle-manifest.json'
if (Test-Path -LiteralPath $target) {
    if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Preserve an existing dependency directory without this producer manifest.' }
    $existing = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($existing.nativeArchiveSha256 -ne '98E3DB83A0E486D1A5D69F0D10D0B018432E9D200C8FECD0020AFC7DA26AB71E') { throw 'Existing dependency provenance differs.' }
    foreach ($file in $existing.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $target $file.path) -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Existing native dependency changed; preserve it for inspection.' }
    }
    Write-Output "Verified existing F3D library dependency: $target"
    exit 0
}
if (!(Test-Path -LiteralPath (Join-Path $sdk 'bin/f3d_c_api.dll'))) { throw 'Retrieve and verify the official source/SDK inputs first.' }
[void][IO.Directory]::CreateDirectory((Join-Path $target 'bin'))
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $sdk 'bin') -File -Filter '*.dll' | Where-Object Name -NE 'F3DShellExtension.dll') {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target ('bin/' + $file.Name))
}
[void][IO.Directory]::CreateDirectory((Join-Path $target 'share'))
Copy-Item -LiteralPath (Join-Path $sdk 'share/licenses') -Destination (Join-Path $target 'share/licenses') -Recurse
$files = @(Get-ChildItem -LiteralPath $target -File -Recurse | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath($target,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes=$_.Length }
})
[ordered]@{ version='4.0.0'; sourceCommit='b72c7b5c9244e4a0c12975ce6546645c1a8b2680'; nativeArchiveSha256='98E3DB83A0E486D1A5D69F0D10D0B018432E9D200C8FECD0020AFC7DA26AB71E'; consumer='Pathweft S3 F3D preview module and final packaged verification'; files=$files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Prepared pinned F3D library dependency in persistent D: storage: $target. No executable, shell extension or installation is used."
