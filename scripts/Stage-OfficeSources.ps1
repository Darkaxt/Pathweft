<# Stage the exact corresponding Office sources for the final dependency archive.
   Native bridges/worker/build scripts also ship in the matching Pathweft source.
   No runtime installation, compilation, restore or dependency change occurs. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination,[string]$CacheRoot='D:\BuildCache\Pathweft')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$target=[IO.Path]::GetFullPath($Destination)
[void][IO.Directory]::CreateDirectory($target)
$expected=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
function CopyChecked([string]$Source,[string]$Relative,[string]$Hash){
    $path=[IO.Path]::GetFullPath((Join-Path $target $Relative))
    if(!$path.StartsWith($target.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Office source path escaped the destination.'}
    if((Get-FileHash -LiteralPath $Source).Hash -ne $Hash){throw "Office source digest differs: $Source"}
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    # This exact generated staging directory is incrementally refreshed from
    # the verified canonical inputs; source archives themselves stay pinned.
    if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $Hash){Copy-Item -LiteralPath $Source -Destination $path -Force}
    if((Get-FileHash -LiteralPath $path).Hash -ne $Hash){throw "Staged Office source digest differs: $Relative"}
    $expected[$Relative.Replace('\','/')]=$Hash
}
$office=Get-Content -LiteralPath (Join-Path $repo 'vendor/Office/runtime-manifest.json') -Raw|ConvertFrom-Json
foreach($engine in $office.apps){
    CopyChecked (Join-Path $CacheRoot ('office/sources/'+$engine.sourceArchive)) ('sources/'+$engine.sourceArchive) $engine.sourceSha256
    if($engine.fonts){CopyChecked (Join-Path $CacheRoot ('office/sources/'+$engine.fonts.archive)) ('fonts/'+$engine.fonts.archive) $engine.fonts.archiveSha256}
}
$distribution=Get-Content -LiteralPath (Join-Path $repo 'vendor/Office/core-source-distribution.json') -Raw|ConvertFrom-Json
foreach($archive in $distribution.archives){CopyChecked (Join-Path $CacheRoot $archive.cache) ('dependencies/'+$archive.name) $archive.sha256}
$fallback=Get-Content -LiteralPath (Join-Path $repo 'vendor/Office/libreoffice-runtime-manifest.json') -Raw|ConvertFrom-Json
foreach($archive in $fallback.sources){CopyChecked (Join-Path $CacheRoot ('office/libreoffice/sources/'+$archive.name)) ('LibreOffice/'+$archive.name) $archive.sha256}
foreach($name in @('README.md','runtime-manifest.json','libreoffice-runtime-manifest.json','libreoffice-dependencies.json','core-source-distribution.json','license-manifest.json','wordcraft-core-dependencies.json','gridcraft-core-dependencies.json','deckcraft-core-dependencies.json')){
    $source=Join-Path $repo ('vendor/Office/'+$name)
    CopyChecked $source $name (Get-FileHash -LiteralPath $source).Hash
}
$licenses=Get-Content -LiteralPath (Join-Path $repo 'vendor/Office/license-manifest.json') -Raw|ConvertFrom-Json
foreach($notice in $licenses.files){CopyChecked (Join-Path $repo ('vendor/Office/'+$notice.path)) $notice.path $notice.sha256}
foreach($file in Get-ChildItem -LiteralPath $target -File -Recurse){
    $relative=[IO.Path]::GetRelativePath($target,$file.FullName).Replace('\','/')
    if(!$expected.ContainsKey($relative)){throw "Unselected member in Office corresponding sources: $relative"}
}
@{files=$expected.Count;donorCores=$office.apps.Count;dependencyArchives=$distribution.archives.Count;fallbackSources=$fallback.sources.Count;destination=$target}|ConvertTo-Json
