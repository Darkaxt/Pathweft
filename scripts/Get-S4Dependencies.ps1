[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [string]$CacheRoot = 'D:\BuildCache\Pathweft')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$') { throw 'Use registered S4 native/build scratch.' }
$inputRoot = Join-Path $scratch 'inputs'
[void][IO.Directory]::CreateDirectory($inputRoot)
$payload = Join-Path $inputRoot '7z2605-x64.exe'
Invoke-WebRequest -Uri 'https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.exe' -OutFile $payload
if ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne 'FD99E86F57D8F429EF5ED045BF98315AF962E2309B0F1BA87A147A44C71647D0') { throw 'Official native package digest differs.' }
$native = Join-Path $inputRoot 'native'
$bootstrap = Join-Path $env:USERPROFILE 'scoop/apps/7zip/current/7z.exe'
if (!(Test-Path -LiteralPath $bootstrap)) { throw 'The inspected extraction tool is unavailable; do not execute the installer.' }
& $bootstrap x $payload ('-o' + $native) -y '7z.dll' 'License.txt'
if ($LASTEXITCODE -ne 0) { throw 'Native library extraction failed.' }
$source = Join-Path $inputRoot '7z2605-src.tar.xz'
Invoke-WebRequest -Uri 'https://github.com/ip7z/7zip/releases/download/26.05/7z2605-src.tar.xz' -OutFile $source
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne '0B6080B8C3E7A579161D2D495224C0E51C467576CB4C7048B0AB485EF44CC22E') { throw 'Official source package digest differs.' }
$sourceRoot = Join-Path $inputRoot 'sevenzip-source'
[void][IO.Directory]::CreateDirectory($sourceRoot)
& tar -xf $source -C $sourceRoot
if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
$package = Join-Path $inputRoot 'sharpsevenzip.2.0.130.nupkg'
Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/sharpsevenzip/2.0.130/sharpsevenzip.2.0.130.nupkg' -OutFile $package
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '2C4D91BD996CF3F9C3EAC45D10CF1651CA9387EAB9DAF163148ABCD534FCD0ED') { throw 'Pinned managed package differs.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($package, (Join-Path $inputRoot 'sharpsevenzip-package'))
$target = Join-Path $CacheRoot 'sevenzip/26.05'
if (Test-Path -LiteralPath $target) { throw 'Inspect the existing dependency before replacing it.' }
[void][IO.Directory]::CreateDirectory($target)
Copy-Item -LiteralPath (Join-Path $native '7z.dll') -Destination $target
Copy-Item -LiteralPath (Join-Path $native 'License.txt') -Destination $target
[ordered]@{ version='26.05'; nativeArchiveSha256=(Get-FileHash $payload -Algorithm SHA256).Hash; sourceArchiveSha256=(Get-FileHash $source -Algorithm SHA256).Hash; dllSha256=(Get-FileHash (Join-Path $target '7z.dll') -Algorithm SHA256).Hash; managedPackageVersion='2.0.130'; managedPackageSha256=(Get-FileHash $package -Algorithm SHA256).Hash; consumer='Pathweft native archive integration and final package verification'; bootstrapSha256=(Get-FileHash $bootstrap -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'manifest.json') -Encoding utf8
Write-Output "Prepared pinned native library dependency: $target; installer was inspected as an archive, not executed."
