<# Read the actual delivered archives without extracting or modifying them. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$DeliveryRoot,[Parameter(Mandatory)][string]$Receipt)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $DeliveryRoot 'release-manifest.json') -Raw|ConvertFrom-Json
$version=$manifest.version
if($manifest.sourceState -ne 'clean committed source' -or $manifest.sourceCommit -ne (& git -C $repo rev-parse HEAD).Trim()){throw 'Delivery is not bound to the current completed source commit.'}
$source=[IO.Compression.ZipFile]::OpenRead((Join-Path $DeliveryRoot "Pathweft-$version-source.zip"))
$dependencies=[IO.Compression.ZipFile]::OpenRead((Join-Path $DeliveryRoot "Pathweft-$version-dependency-sources.zip"))
$entries=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$verified=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function ReadJson([string]$name){
    $entry=$source.GetEntry('Pathweft/'+$name)
    if(!$entry){throw ('Required source manifest missing: '+$name)}
    $reader=[IO.StreamReader]::new($entry.Open())
    try{return $reader.ReadToEnd()|ConvertFrom-Json -Depth 100}finally{$reader.Dispose()}
}
function CheckDependency([string]$name,[string]$expected){
    if(!$entries.ContainsKey($name)){throw ('Required dependency source/notice absent: '+$name)}
    $stream=$entries[$name].Open()
    try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
    if($actual -ne $expected){throw ('Delivered dependency source/notice differs: '+$name)}
    [void]$verified.Add($name)
}
try{
    foreach($entry in $dependencies.Entries){
        if($entry.FullName.EndsWith('/')){continue}
        $name=$entry.FullName
        if($name.StartsWith('./')){$name=$name.Substring(2)}
        if(!$entries.TryAdd($name,$entry)){throw ('Duplicate dependency archive member: '+$name)}
    }
    # Compare each source blob to the exact Git tree, not a similarly named
    # worktree or an archive produced before the final implementation.
    $tree=@(& git -C $repo -c core.quotePath=false ls-tree -r $manifest.sourceCommit)
    if($LASTEXITCODE -ne 0){throw 'Committed source inventory failed.'}
    $blobs=0
    foreach($line in $tree){
        if($line -notmatch '^([0-9]+) blob ([a-f0-9]{40})\t(.+)$'){throw ('Unsupported source tree member: '+$line)}
        $hash=$Matches[2];$name=$Matches[3];$entry=$source.GetEntry('Pathweft/'+$name)
        if(!$entry){throw ('Delivered application source missing: '+$name)}
        $digest=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
        $input=$entry.Open()
        try{
            $digest.AppendData([Text.Encoding]::UTF8.GetBytes('blob '+$entry.Length+[char]0))
            $buffer=[byte[]]::new(1048576)
            while(($read=$input.Read($buffer,0,$buffer.Length)) -gt 0){$digest.AppendData($buffer,0,$read)}
            if([Convert]::ToHexString($digest.GetHashAndReset()) -ne $hash){throw ('Delivered source blob differs: '+$name)}
        }finally{$input.Dispose();$digest.Dispose()}
        $blobs++
    }
    if(@($source.Entries|Where-Object {!$_.FullName.EndsWith('/')} ).Count -ne $blobs){throw 'Untracked extra member in application source delivery.'}
    $reader=[IO.StreamReader]::new($entries['source-manifest.json'].Open())
    try{$base=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    foreach($pin in $base){CheckDependency $pin.name $pin.sha256}
    $marked=ReadJson 'vendor/Marked/runtime-manifest.json'
    foreach($donor in @('Scoop','Win11Debloat','Win11Debloat-516fe42','RemoveWindowsAI','SophiApp','UnattendedWinstall','Bloatynosy','Sophia','WinUtil')){
        $pin=ReadJson ('vendor/'+$donor+'/source-manifest.json')
        if($pin.revision -notmatch '^[a-f0-9]{40}$' -or $pin.license -ne 'MIT' -or !$source.GetEntry('Pathweft/vendor/'+$donor+'/License.txt')){throw ('Maintenance donor provenance missing: '+$donor)}
        foreach($file in $pin.files){
            $entry=$source.GetEntry('Pathweft/vendor/'+$donor+'/'+$file.path)
            if(!$entry){throw ('Selected donor source missing: '+$file.path)}
            $stream=$entry.Open()
            try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
            if($actual -ne $file.sha256){throw ('Selected donor source differs: '+$file.path)}
        }
    }
    CheckDependency ('Marked/'+$marked.sourceArchive) $marked.sourceSha256
    CheckDependency 'Marked/marked-18.1.2.tgz' $marked.npmSha256
    $mpv=ReadJson 'vendor/MPV/runtime-manifest.json'
    foreach($pin in $mpv.sources){CheckDependency ('MPV/'+$pin.name) $pin.sha256}
    $specialists=ReadJson 'vendor/Specialists/source-manifest.json'
    $system=ReadJson 'vendor/SysOptimizer/source-manifest.json'
    foreach($pin in $system.selected){
        $entry=$source.GetEntry('Pathweft/'+$pin.selectedSource)
        if(!$entry){throw ('Selected SysOptimizer donor source absent: '+$pin.path)}
        $digest=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
        $input=$entry.Open()
        try{
            $digest.AppendData([Text.Encoding]::UTF8.GetBytes('blob '+$entry.Length+[char]0))
            $buffer=[byte[]]::new(65536)
            while(($read=$input.Read($buffer,0,$buffer.Length)) -gt 0){$digest.AppendData($buffer,0,$read)}
            if([Convert]::ToHexString($digest.GetHashAndReset()) -ne $pin.gitBlob){throw ('Selected donor blob differs: '+$pin.path)}
        }finally{$input.Dispose();$digest.Dispose()}
    }
    $performance=ReadJson 'vendor/Performance/source-manifest.json'
    foreach($pin in $performance.archives){CheckDependency ('Performance/'+$pin.name) $pin.sha256}
    foreach($pin in $performance.packages){CheckDependency ('Performance/'+$pin.packageArchive) $pin.packageSha256}
    foreach($pin in $specialists){CheckDependency ('Specialists/'+$pin.sourceArchive) $pin.sourceSha256;CheckDependency ('Specialists/'+$pin.packageArchive) $pin.packageSha256}
    $mediaInfo=ReadJson 'vendor/Specialists/mediainfo-runtime.json'
    foreach($pin in $mediaInfo.archives){CheckDependency ('Specialists/'+$pin.name) $pin.sha256}
    $office=ReadJson 'vendor/Office/runtime-manifest.json'
    foreach($engine in $office.apps){
        CheckDependency ('Office/sources/'+$engine.sourceArchive) $engine.sourceSha256
        if($engine.fonts){CheckDependency ('Office/fonts/'+$engine.fonts.archive) $engine.fonts.archiveSha256}
        foreach($kind in @('core','source')){
            $name=$engine.id+'-'+$kind+'-dependencies.json'
            $entry=$source.GetEntry('Pathweft/vendor/Office/'+$name)
            if(!$entry){throw ('Selected core graph missing: '+$name)}
            $input=$entry.Open()
            try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input))}finally{$input.Dispose()}
            CheckDependency ('Office/'+$name) $hash
        }
        foreach($name in @('Cargo.toml','src/engine.rs','src/lib.rs')){
            if(!$source.GetEntry('Pathweft/native/office/'+$engine.id+'/'+$name)){throw ('Selected native bridge source missing: '+$engine.id+'/'+$name)}
        }
    }
    $distribution=ReadJson 'vendor/Office/core-source-distribution.json'
    foreach($pin in $distribution.archives){CheckDependency ('Office/dependencies/'+$pin.name) $pin.sha256}
    $fallback=ReadJson 'vendor/Office/libreoffice-runtime-manifest.json'
    foreach($pin in $fallback.sources){CheckDependency ('Office/LibreOffice/'+$pin.name) $pin.sha256}
    $licenses=ReadJson 'vendor/Office/license-manifest.json'
    foreach($pin in $licenses.files){CheckDependency ('Office/'+$pin.path) $pin.sha256}
    foreach($name in @('FolderJumpList.cs','SpaceScan.cs','PaneSpace.cs','SizeCache.cs','SqliteRuntime.cs','vendor/Diskweft/source-manifest.json','vendor/Diskweft/NOTICES.txt','AuxiliaryWindows.cs','UpdateChecks.cs','Preview/OfficeWorker.cs','Preview/OfficeDocument.cs','PreviewModes.cs','native/office/common/extraction.rs','native/office/common/pathweft_office.h','scripts/Verify-CraftCoreLock.py','scripts/Apply-CraftCompatibility.py')){
        if(!$source.GetEntry('Pathweft/'+$name)){throw ('Integrated implementation/build source missing: '+$name)}
    }
    $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $DeliveryRoot "Pathweft-$version-setup.exe")
    if($signature.Status -ne 'NotSigned'){throw 'Unexpected installer signing state.'}
    $result=[ordered]@{version=$version;sourceCommit=$manifest.sourceCommit;gitBlobsVerified=$blobs;dependencySourcesAndNoticesVerified=$verified.Count;officeEngines=$office.apps.Count;officeDependencyArchives=$distribution.archives.Count;installerSignature=[string]$signature.Status}
    $result|ConvertTo-Json|Set-Content -LiteralPath $Receipt
    $result|ConvertTo-Json
}finally{$source.Dispose();$dependencies.Dispose()}
