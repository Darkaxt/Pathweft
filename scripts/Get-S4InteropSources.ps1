[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$') { throw 'Use registered S4 native scratch.' }
$pin = '9b2379be603a9d4ebff06c56944f87f0c623f8db'
$archive = Join-Path $scratch 'inputs/sharpsevenzip-source.zip'
Invoke-WebRequest -Uri "https://codeload.github.com/JeremyAnsel/SharpSevenZip/zip/$pin" -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '03171712C5FCBC6117C08E1D1508995415191145F20792DC92206942EF1DC3EA') { throw 'Pinned managed source archive differs.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archive, (Join-Path $scratch 'inputs/interop-source'))
Write-Output "Retrieved the managed package's pinned source commit: $pin"
