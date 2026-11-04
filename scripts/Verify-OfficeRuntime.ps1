[CmdletBinding()]
param([Parameter(Mandatory)][string]$ApplicationRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$manifestPath=Join-Path $repo 'vendor/Office/runtime-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$expected=@{ 'Office/runtime-manifest.json'=(Get-FileHash -LiteralPath $manifestPath).Hash }
foreach($engine in $manifest.apps){
    foreach($file in $engine.nativeFiles){$expected['Office/cores/'+$file.path]=$file.sha256}
    foreach($notice in $engine.notices){$expected[$notice.path.Replace('vendor/Office/licenses/','Office/licenses/')]=$notice.sha256}
    foreach($notice in $engine.fonts.notices){$expected[$notice.path.Replace('vendor/Office/licenses/','Office/licenses/')]=$notice.sha256}
}
$fallbackPath=Join-Path $repo 'vendor/Office/libreoffice-runtime-manifest.json'
$licensePath=Join-Path $repo 'vendor/Office/license-manifest.json'
$licenses=Get-Content -LiteralPath $licensePath -Raw | ConvertFrom-Json
$expected['Office/license-manifest.json']=(Get-FileHash -LiteralPath $licensePath).Hash
foreach($notice in $licenses.files){$expected['Office/'+$notice.path]=$notice.sha256}
$expected['Office/libreoffice-runtime-manifest.json']=(Get-FileHash -LiteralPath $fallbackPath).Hash
$fallback=Get-Content -LiteralPath $fallbackPath -Raw|ConvertFrom-Json
foreach($file in $fallback.files){$expected['Office/LibreOffice/'+$file.path]=$file.sha256}
$expected['Office/libreoffice-worker.py']=(Get-FileHash -LiteralPath (Join-Path $repo 'native/office/libreoffice/worker.py')).Hash
foreach($relative in $expected.Keys){
    $path=Join-Path $ApplicationRoot $relative
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing Office runtime member: $relative"}
    if((Get-FileHash -LiteralPath $path).Hash -ne $expected[$relative]){throw "Office runtime member differs: $relative"}
}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $ApplicationRoot 'Office') -Recurse -File){
    $relative=[IO.Path]::GetRelativePath($ApplicationRoot,$file.FullName).Replace('\','/')
    if(!$expected.ContainsKey($relative)){throw "Unselected Office runtime member: $relative"}
}
@{status=$manifest.status;verified_members=$expected.Count;engines=$manifest.apps.Count}|ConvertTo-Json
