<# Versioned x64 installer/portable/source packaging. No deletion, signing claim,
   publication or live installation. Clean expendable staging through the host's
   transactional-cleanup workflow after the final consumer exits. #>
[CmdletBinding()]
param(
    [string]$ScratchRoot='D:\Temp\pathweft-s8-delivery',
    [string]$CacheRoot='D:\BuildCache\Pathweft',
    [string]$Compiler='D:\Tools\InnoSetup\6.7.4\ISCC.exe',
    [switch]$Candidate,
    [string]$ResumeCandidateRun
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$scratch=[IO.Path]::GetFullPath($ScratchRoot)
if($scratch -notmatch '^D:\\Temp\\[^\\]+$'){throw 'Use registered native/build I/O scratch.'}
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'FileExplorer.csproj')
$version=($project.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
$commit=(& git -C $root rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0){throw 'Source commit could not be identified.'}
$changes=@(& git -C $root status --porcelain)
if(!$Candidate -and $changes.Count -ne 0){throw 'Final packaging requires the completed verified source commit and a clean worktree. Use -Candidate for pre-commit qualification.'}
if(!(Test-Path -LiteralPath $Compiler)){throw 'Prepare and verify the pinned compiler first.'}
$run=if($ResumeCandidateRun){[IO.Path]::GetFullPath($ResumeCandidateRun)}else{Join-Path $scratch ('package-'+[guid]::NewGuid().ToString('N'))}
if($ResumeCandidateRun -and (!$Candidate -or $run -notmatch ('^'+[regex]::Escape($scratch)+'\\package-[a-f0-9]{32}$'))){throw 'Resume is limited to an exact owned qualification-candidate run, never a final release.'}
$publish=Join-Path $run 'app'
$deliveryName=if($ResumeCandidateRun){'delivery-'+[guid]::NewGuid().ToString('N')}else{'delivery'}
$delivery=if($Candidate){Join-Path $run $deliveryName}else{Join-Path $root "dist/$version"}
foreach($path in @($run,$delivery)){[void][IO.Directory]::CreateDirectory($path)}
if(@(Get-ChildItem -LiteralPath $delivery -Force).Count){throw 'Preserve an existing delivery; choose an explicit new version or review its retained assets first.'}
$env:TEMP=$env:TMP=Join-Path $scratch 'temp'
if($ResumeCandidateRun){
    $existing=Get-Content -LiteralPath (Join-Path $publish 'release-manifest.json') -Raw | ConvertFrom-Json
    if($existing.sourceState -ne 'qualification candidate; not a published release' -or $existing.version -ne $version -or $existing.sourceCommit -ne $commit){throw 'The interrupted candidate identity differs.'}
    foreach($file in $existing.files){if((Get-FileHash -LiteralPath (Join-Path $publish $file.path)).Hash -ne $file.sha256){throw "Interrupted candidate member changed: $($file.path)"}}
}else{
    & (Join-Path $root 'scripts/Build-Development.ps1') -ScratchRoot $run -CacheRoot $CacheRoot -Operation Publish -PublishDirectory $publish -Configuration Release -BuildProperty @('ContinuousIntegrationBuild=true','DebugType=embedded','DebugSymbols=false')
    if($LASTEXITCODE -ne 0){throw 'Self-contained publish failed.'}
}

# Verify native library bytes against the qualified source/runtime inventory.
$pins=@(
    @{path='SevenZip/7z.dll';manifest='vendor/SevenZip/runtime-manifest.json';field='dllSha256'},
    @{path='SharpSevenZip.dll';manifest='vendor/SharpSevenZip/runtime-manifest.json';field='dllSha256'},
    @{path='Rars/pathweft_rars.dll';manifest='vendor/rars/runtime-manifest.json';field='dllSha256'}
)
foreach($pin in $pins){
    $manifest=Get-Content -LiteralPath (Join-Path $root $pin.manifest) -Raw | ConvertFrom-Json
    if((Get-FileHash -LiteralPath (Join-Path $publish $pin.path) -Algorithm SHA256).Hash -ne $manifest.($pin.field)){throw "Native dependency changed: $($pin.path)"}
}
$f3d=Get-Content -LiteralPath (Join-Path $root 'vendor/F3D/runtime-manifest.json') -Raw | ConvertFrom-Json
foreach($file in $f3d.files){
    $relative=if($file.path.StartsWith('share\licenses\')){'F3D/licenses/'+$file.path.Substring('share\licenses\'.Length)}else{'F3D/'+$file.path}
    if((Get-FileHash -LiteralPath (Join-Path $publish $relative) -Algorithm SHA256).Hash -ne $file.sha256){throw "F3D dependency changed: $relative"}
}
if((Get-FileHash -LiteralPath (Join-Path $publish 'Everything/Everything.exe')).Hash -ne 'F191F756996A14A11E5445FA7103D302EFD510CF2FBF920E6C0C8ED51D512E36'){throw 'Bundled Everything engine changed.'}
Copy-Item -LiteralPath (Join-Path $root 'docs/RELEASE-USAGE.md') -Destination (Join-Path $publish 'README.txt')
Copy-Item -LiteralPath (Join-Path $root 'docs/THIRD-PARTY-NOTICES.md') -Destination (Join-Path $publish 'THIRD-PARTY-NOTICES.md')
$runtimeNotices=Join-Path $publish 'licenses/Microsoft'
[void][IO.Directory]::CreateDirectory($runtimeNotices)
$noticeFiles=@(
    @('microsoft.netcore.app.runtime.win-x64/10.0.14/LICENSE.TXT','DotNet-LICENSE.txt'),
    @('microsoft.netcore.app.runtime.win-x64/10.0.14/THIRD-PARTY-NOTICES.TXT','DotNet-THIRD-PARTY-NOTICES.txt'),
    @('microsoft.windowsdesktop.app.runtime.win-x64/10.0.14/LICENSE','Desktop-LICENSE.txt'),
    @('microsoft.web.webview2/1.0.4314.35/LICENSE.txt','WebView2-LICENSE.txt'),
    @('microsoft.web.webview2/1.0.4314.35/NOTICE.txt','WebView2-NOTICE.txt'),
    @('system.io.hashing/10.0.14/THIRD-PARTY-NOTICES.TXT','Hashing-THIRD-PARTY-NOTICES.txt')
)
foreach($notice in $noticeFiles){Copy-Item -LiteralPath (Join-Path $CacheRoot ('nuget-packages/'+$notice[0])) -Destination (Join-Path $runtimeNotices $notice[1])}
$files=@(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object Name -NE 'release-manifest.json' | Sort-Object FullName | ForEach-Object{
    [ordered]@{path=[IO.Path]::GetRelativePath($publish,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$receipt=[ordered]@{product='Pathweft';version=$version;sourceCommit=$commit;sourceState=$(if($Candidate){'qualification candidate; not a published release'}else{'clean committed source'});target='win-x64';framework='net10.0-windows10.0.19041.0';runtime='10.0.14';signing='Pathweft executable and installer are unsigned';files=$files}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $publish 'release-manifest.json') -Encoding utf8
& $Compiler "/DAppVersion=$version" "/DPublishRoot=$publish" "/DDeliveryRoot=$delivery" (Join-Path $root 'installer/Pathweft.iss') /Q
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$portableRoot=Join-Path $run 'portable'
[void][IO.Directory]::CreateDirectory($portableRoot)
Copy-Item -LiteralPath $publish -Destination (Join-Path $portableRoot 'Pathweft') -Recurse
[IO.File]::WriteAllText((Join-Path $portableRoot 'Pathweft/portable.txt'),'Portable Pathweft: settings remain in Data beside the application. Preserve Data when updating.')
& "$env:SystemRoot/System32/tar.exe" -a -c -f (Join-Path $delivery "Pathweft-$version-portable.zip") -C $portableRoot Pathweft
if($LASTEXITCODE -ne 0){throw 'Portable archive creation failed.'}

# Corresponding source includes full pinned archives, the interop patch and
# offline native Rust dependency sources, not only selected reference snippets.
$sources=Join-Path $scratch 'corresponding-sources'
if(!(Test-Path -LiteralPath (Join-Path $sources 'source-manifest.json'))){throw 'Prepare verified corresponding dependency sources first.'}
& "$env:SystemRoot/System32/tar.exe" -a -c -f (Join-Path $delivery "Pathweft-$version-dependency-sources.zip") -C $sources .
if($LASTEXITCODE -ne 0){throw 'Dependency-source archive creation failed.'}
if(!$Candidate){
    & git -C $root archive --format=zip --prefix=Pathweft/ -o (Join-Path $delivery "Pathweft-$version-source.zip") $commit
    if($LASTEXITCODE -ne 0){throw 'Matching application source archive failed.'}
}
Copy-Item -LiteralPath (Join-Path $publish 'release-manifest.json') -Destination $delivery
$checksums=Get-ChildItem -LiteralPath $delivery -File | Sort-Object Name | ForEach-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name}
[IO.File]::WriteAllLines((Join-Path $delivery 'SHA256SUMS.txt'),$checksums)
[ordered]@{run=$run;delivery=$delivery;version=$version;commit=$commit;candidate=[bool]$Candidate;publish=$publish} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'active-package.json')
Get-Content -LiteralPath (Join-Path $scratch 'active-package.json')
