<# LIGHT-01/LIGHT-04: build the light update of a release from the release-manifest.json
   of the release it updates. It contains only the files that changed or are new, deletes
   the ones the base had and this release no longer ships, and checks the installed base
   before changing anything (installer/Pathweft.iss, LightBase). No publication; staging
   stays in the caller's scratch folder. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaseManifest,
    [Parameter(Mandatory)][string]$PublishRoot,
    [Parameter(Mandatory)][string]$DeliveryRoot,
    [Parameter(Mandatory)][string]$Scratch,
    [string]$Compiler='D:\Tools\InnoSetup\6.7.4\ISCC.exe',
    [string]$Script=(Join-Path (Split-Path $PSScriptRoot) 'installer/Pathweft.iss'),
    [string[]]$Define=@()
)
$ErrorActionPreference='Stop'
function Read-Manifest([string]$path){
    $text=[IO.File]::ReadAllText($path).TrimStart([char]0xFEFF)
    $manifest=$text|ConvertFrom-Json
    $files=@{}
    foreach($file in $manifest.files){$files[$file.path.Replace('/','\')]=$file}
    [pscustomobject]@{version=$manifest.version;files=$files;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
$publish=[IO.Path]::GetFullPath($PublishRoot)
$base=Read-Manifest $BaseManifest
$next=Read-Manifest (Join-Path $publish 'release-manifest.json')
$version=$next.version
if([version]$base.version -ge [version]$version){throw "The base $($base.version) is not older than $version."}
$scratchRoot=[IO.Path]::GetFullPath($Scratch)
$stage=Join-Path $scratchRoot 'light-files'
if(Test-Path -LiteralPath $stage){throw "Light staging already exists: $stage"}
[void][IO.Directory]::CreateDirectory($stage)

$changed=@($next.files.Keys | Where-Object{!$base.files.ContainsKey($_) -or $base.files[$_].sha256 -ne $next.files[$_].sha256} | Sort-Object)
$removed=@($base.files.Keys | Where-Object{!$next.files.ContainsKey($_)} | Sort-Object)
foreach($path in $changed + 'release-manifest.json'){
    $target=Join-Path $stage $path
    [void][IO.Directory]::CreateDirectory((Split-Path $target))
    Copy-Item -LiteralPath (Join-Path $publish $path) -Destination $target
}

# LIGHT-04 logical check: base plus this package plus the deletions is exactly the new release
foreach($path in $next.files.Keys){
    $staged=Join-Path $stage $path
    $hash=if(Test-Path -LiteralPath $staged){(Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash}elseif($base.files.ContainsKey($path)){$base.files[$path].sha256}else{$null}
    if($hash -ne $next.files[$path].sha256){throw "The light update would leave $path different from $version."}
}
foreach($path in $removed){if($next.files.ContainsKey($path) -or (Test-Path -LiteralPath (Join-Path $stage $path))){throw "Removed file is still shipped: $path"}}
if((Get-FileHash -LiteralPath (Join-Path $stage 'release-manifest.json') -Algorithm SHA256).Hash -ne $next.sha256){throw 'The staged file list differs from the release.'}

# Lists for the installer: "<sha256> <relative path>", the base with its own file list
$utf8=[Text.UTF8Encoding]::new($true)
$check=Join-Path $scratchRoot 'light-base.txt'
$verify=Join-Path $scratchRoot 'light-new.txt'
$removal=Join-Path $scratchRoot 'light-removed.iss'
[IO.File]::WriteAllLines($check,[string[]](@($base.files.Keys|Sort-Object|ForEach-Object{$base.files[$_].sha256+' '+$_})+($base.sha256+' release-manifest.json')),$utf8)
[IO.File]::WriteAllLines($verify,[string[]](@($changed|ForEach-Object{$next.files[$_].sha256+' '+$_})+($next.sha256+' release-manifest.json')),$utf8)
[IO.File]::WriteAllLines($removal,[string[]](@('; files Pathweft '+$base.version+' had and '+$version+' no longer ships')+@($removed|ForEach-Object{'Type: files; Name: "{app}\'+$_+'"'})),$utf8)

$arguments=@("/DAppVersion=$version","/DPublishRoot=$publish","/DDeliveryRoot=$DeliveryRoot","/DLightBase=$($base.version)","/DLightRoot=$stage","/DLightCheck=$check","/DLightVerify=$verify","/DLightRemoved=$removal")+@($Define|ForEach-Object{"/D$_"})
& $Compiler @arguments $Script /Q
if($LASTEXITCODE -ne 0){throw 'Light update compilation failed.'}
$name="Pathweft-$version-update.exe"
$package=Get-Item -LiteralPath (Join-Path $DeliveryRoot $name)
$description=[ordered]@{version=$version;base=$base.version;baseManifestSha256=$base.sha256.ToLowerInvariant();installer=$name;bytes=$package.Length;sha256=(Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant();changed=$changed.Count;removed=$removed.Count}
$description|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $DeliveryRoot "Pathweft-$version-update.json") -Encoding utf8
[pscustomobject]$description
