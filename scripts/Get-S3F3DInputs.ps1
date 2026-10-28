[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
$root = Join-Path $scratch 'f3d'
[void][IO.Directory]::CreateDirectory($root)
$pin = 'b72c7b5c9244e4a0c12975ce6546645c1a8b2680'
$native = Join-Path $root 'F3D-4.0.0-Windows-x86_64.zip'
$source = Join-Path $root 'f3d-source.zip'
Write-Output 'Retrieving F3D v4.0.0 source and its official Windows SDK package.'
Invoke-WebRequest -Uri 'https://github.com/f3d-app/f3d/releases/download/v4.0.0/F3D-4.0.0-Windows-x86_64.zip' -OutFile $native
$nativeHash = (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash
if ($nativeHash -ne '98E3DB83A0E486D1A5D69F0D10D0B018432E9D200C8FECD0020AFC7DA26AB71E') { throw 'The native archive differs from the official GitHub release asset digest.' }
Invoke-WebRequest -Uri "https://codeload.github.com/f3d-app/f3d/zip/$pin" -OutFile $source
Expand-Archive -LiteralPath $native -DestinationPath (Join-Path $root 'sdk')
Expand-Archive -LiteralPath $source -DestinationPath (Join-Path $root 'source')
[ordered]@{ version='4.0.0'; sourceCommit=$pin; nativeSha256=$nativeHash; sourceArchiveSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; nativeDigestOrigin='GitHub release asset digest'; purpose='S3 source-derived F3D GLB preview module' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'input-manifest.json') -Encoding utf8
Write-Output "Pinned F3D inputs verified and extracted under $root"
