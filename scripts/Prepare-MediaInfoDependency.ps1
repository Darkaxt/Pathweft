[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot,[switch]$PrepareCache)
$ErrorActionPreference='Stop'
$scratch=[IO.Path]::GetFullPath($ScratchRoot)
if($scratch -notmatch '^D:\\Temp\\pathweft-s13-s9-s11\\mediainfo$'){throw 'Use the registered native I/O scratch.'}
[void][IO.Directory]::CreateDirectory($scratch)
$inputs=@(
    @{name='MediaInfo_DLL_26.10_Windows_x64_WithoutInstaller.zip';url='https://mediaarea.net/download/binary/libmediainfo0/26.10/MediaInfo_DLL_26.10_Windows_x64_WithoutInstaller.zip';sha='4EFE172DF014693732E8B64A004CF382B8599EAF86BE26A9B38C0D2993E60E1D'},
    @{name='libmediainfo_26.10_AllInclusive.7z';url='https://mediaarea.net/download/source/libmediainfo/26.10/libmediainfo_26.10_AllInclusive.7z';sha='4AF638EC0DA9A81736A0FF02294F55EDD91ACF385CB4E3F8A0DD0B2F2D88FBED'}
)
$records=foreach($input in $inputs){
    $target=Join-Path $scratch $input.name
    if(!(Test-Path -LiteralPath $target)){Invoke-WebRequest -Uri $input.url -OutFile $target}
    if((Get-FileHash -LiteralPath $target).Hash -ne $input.sha){throw 'Official archive differs from the reviewed download.'}
    [ordered]@{name=$input.name;url=$input.url;bytes=(Get-Item -LiteralPath $target).Length;sha256=(Get-FileHash -LiteralPath $target).Hash}
}
$records|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $scratch 'retrieval.json') -Encoding utf8
$extractor=Join-Path $env:USERPROFILE 'scoop/apps/7zip/current/7z.exe'
if(!(Test-Path -LiteralPath $extractor)){throw 'Existing inspected extractor is unavailable.'}
& $extractor x (Join-Path $scratch $inputs[0].name) ('-o'+(Join-Path $scratch 'native')) -y
if($LASTEXITCODE -ne 0){throw 'Native archive extraction failed.'}
& $extractor x (Join-Path $scratch $inputs[1].name) ('-o'+(Join-Path $scratch 'sources')) -y
if($LASTEXITCODE -ne 0){throw 'Corresponding source extraction failed.'}
Get-Content -LiteralPath (Join-Path $scratch 'retrieval.json')
if($PrepareCache){
    $dll=Join-Path $scratch 'native/MediaInfo.dll'
    if((Get-FileHash -LiteralPath $dll).Hash -ne '8AA4687977E8624DC78504D83C51BEC9FC6CA6A1DF01444FB51D0B61F4C8B29C'){throw 'Reviewed DLL differs.'}
    if([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion -ne '26.10.0.0'){throw 'Native version differs.'}
    $signature=Get-AuthenticodeSignature -LiteralPath $dll
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*MediaArea*'){throw 'Official native signature differs.'}
    $cache='D:/BuildCache/Pathweft/mediainfo/26.10'
    if(Test-Path -LiteralPath $cache){throw 'Preserve an existing qualified cache.'}
    [void][IO.Directory]::CreateDirectory((Join-Path $cache 'sources'))
    [void][IO.Directory]::CreateDirectory((Join-Path $cache 'licenses'))
    Copy-Item -LiteralPath $dll -Destination $cache
    foreach($record in $records){Copy-Item -LiteralPath (Join-Path $scratch $record.name) -Destination (Join-Path $cache 'sources')}
    $source=Join-Path $scratch 'sources/libmediainfo_AllInclusive'
    foreach($item in @(@('MediaInfoLib/LICENSE','MediaInfoLib.txt'),@('ZenLib/License.txt','ZenLib.txt'),@('zlib/LICENSE','zlib.txt'))){
        Copy-Item -LiteralPath (Join-Path $source $item[0]) -Destination (Join-Path $cache ('licenses/'+$item[1]))
    }
    [ordered]@{version='26.10';dllSha256=(Get-FileHash $dll).Hash;signer=$signature.SignerCertificate.Subject;signerThumbprint=$signature.SignerCertificate.Thumbprint;archives=$records;consumer='Pathweft specialist MediaInfo inspector builds and final release';sourceBoundary='Official all-inclusive MediaInfoLib, ZenLib and zlib; original MSVC2022 DLL/library build projects'}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $cache 'manifest.json') -Encoding utf8
    Write-Output "Prepared pinned native inspector cache: $cache"
}
# No installer, Shell extension, registration script or inspected media executes.
