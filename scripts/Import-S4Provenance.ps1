[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [string]$CacheRoot = 'D:\BuildCache\Pathweft')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$') { throw 'Use registered S4 source input.' }
$project = Split-Path -Parent $PSScriptRoot
$native = Join-Path $project 'vendor/SevenZip'
$interop = Join-Path $project 'vendor/SharpSevenZip'
[void][IO.Directory]::CreateDirectory($native)
Copy-Item -LiteralPath (Join-Path $CacheRoot 'sevenzip/26.05/License.txt') -Destination (Join-Path $native 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $CacheRoot 'sevenzip/26.05/manifest.json') -Destination (Join-Path $native 'runtime-manifest.json')
Copy-Item -LiteralPath (Join-Path $CacheRoot 'sharpsevenzip/2.0.130-pathweft.1/LICENSE.txt') -Destination (Join-Path $interop 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $CacheRoot 'sharpsevenzip/2.0.130-pathweft.1/manifest.json') -Destination (Join-Path $interop 'runtime-manifest.json')
$source = [IO.Compression.ZipFile]::OpenRead((Join-Path $scratch 'inputs/sharpsevenzip-source.zip'))
try {
    foreach ($file in @('ArchiveUpdateCallback.cs','ArchiveFileInfo.cs','Com.cs')) {
        $entry = @($source.Entries | Where-Object { $_.Name -eq $file -and $_.FullName.StartsWith('SharpSevenZip-9b2379be603a9d4ebff06c56944f87f0c623f8db/SharpSevenZip/SharpSevenZip/') })
        if ($entry.Count -ne 1) { throw "Pinned source reference is ambiguous: $file" }
        if (!$entry) { throw "Pinned source reference is missing: $file" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], (Join-Path $interop ($file + '.txt')), $true)
    }
}
finally { $source.Dispose() }
Write-Output 'Pinned native/interop manifests, licensing and selected interface references imported. No binaries are committed.'
