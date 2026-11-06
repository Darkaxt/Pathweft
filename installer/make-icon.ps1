[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$source=Join-Path $root 'assets/pathweft-icon.png'
$target=Join-Path $root 'app.ico'
# Preserve the master and square canvas; increase visible area about14% over
# the prior236px artwork (252^2/236^2), retaining a transparent edge.
& magick $source -trim +repage -resize 252x252 -gravity center -background none -extent 256x256 -define icon:auto-resize=256,128,96,64,48,40,32,24,20,16 $target
if($LASTEXITCODE -ne 0){throw 'Windows icon generation failed.'}
