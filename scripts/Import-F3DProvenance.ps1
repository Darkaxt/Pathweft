[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [string]$CacheRoot = 'D:\BuildCache\Pathweft')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
$project = Split-Path -Parent $PSScriptRoot
$source = Join-Path $scratch 'f3d/source/f3d-b72c7b5c9244e4a0c12975ce6546645c1a8b2680'
$vendor = Join-Path $project 'vendor/F3D'
[void][IO.Directory]::CreateDirectory((Join-Path $vendor 'reference'))
foreach ($file in @('LICENSE.md','THIRD_PARTY_LICENSES.md')) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $vendor ($file + '.txt')) }
$files = @('c/engine_c_api.h','c/window_c_api.h','c/camera_c_api.h','c/image_c_api.h','c/options_c_api.h','c/scene_c_api.h','examples/libf3d/cpp/offscreen-thumbnail/main.cxx')
$manifest = foreach ($file in $files) {
    $input = Join-Path $source $file
    Copy-Item -LiteralPath $input -Destination (Join-Path $vendor ('reference/' + $file.Replace('/','_') + '.txt'))
    [ordered]@{ file=$file; sha256=(Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash; sourceCommit='b72c7b5c9244e4a0c12975ce6546645c1a8b2680' }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $vendor 'source-manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $CacheRoot 'f3d/4.0.0/bundle-manifest.json') -Destination (Join-Path $vendor 'runtime-manifest.json')
Write-Output 'Imported selected libf3d C API reference source, thumbnail example, pins and licenses; no standalone product executable is included.'
