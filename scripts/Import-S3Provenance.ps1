[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
$project = Split-Path -Parent $PSScriptRoot
$donor = Join-Path $scratch 'donor'
$vendor = Join-Path $project 'vendor/QuickLook'
$manifest = Get-Content -LiteralPath (Join-Path $donor 'source-manifest.json') -Raw | ConvertFrom-Json
foreach ($source in $manifest) {
    $path = Join-Path $donor $source.file
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.sha256) { throw 'Pinned inspection input changed.' }
    if ($source.file.EndsWith('.cs')) {
        $destination = Join-Path $vendor ('original/' + $source.file + '.txt')
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $path -Destination $destination
    }
}
Copy-Item -LiteralPath (Join-Path $donor 'source-manifest.json') -Destination (Join-Path $vendor 'source-manifest.json')
Copy-Item -LiteralPath (Join-Path $donor 'QuickLook.Common/LICENSE') -Destination (Join-Path $vendor 'LICENSE-Common-MIT.txt')
Copy-Item -LiteralPath (Join-Path $donor 'LICENSE-GPL.txt') -Destination (Join-Path $project 'COPYING-GPL-3.0.txt')
$markdig = Join-Path $project 'vendor/Markdig'
[void][IO.Directory]::CreateDirectory($markdig)
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/xoofx/markdig/9900e46eacd9e17ba32f1b441a349bcab61777af/license.txt' -OutFile (Join-Path $markdig 'LICENSE.txt')
Write-Output 'Pinned original viewer text, source manifest and GPL/MIT/BSD terms imported. Original files are reference text, not separately compiled viewers.'
