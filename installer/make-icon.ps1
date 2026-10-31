[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Join-Path $root 'assets/pathweft-icon.png'
$target=Join-Path $root 'app.ico'
# Real alpha background; the visible folder fills 92% of a square canvas.
& magick $source -trim +repage -resize 236x236 -gravity center -background none -extent 256x256 -define icon:auto-resize=256,128,96,64,48,40,32,24,20,16 $target
if($LASTEXITCODE -ne 0){throw 'Windows icon generation failed.'}
