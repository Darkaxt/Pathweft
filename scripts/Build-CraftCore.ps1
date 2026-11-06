[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('wordcraft','deckcraft','gridcraft','photocraft','vectorcraft','figcraft','pdfcraft')][string]$Engine,
    [string]$ScratchRoot='D:\Temp\pathweft-s13-s9-s11\craft-cores',
    [string]$CacheRoot='D:\BuildCache\Pathweft',
    [string]$Manifest,
    [string]$ArchivesRoot='D:\BuildCache\Pathweft\office\sources',
    [ValidateSet('Build','Test','Fixtures')][string]$Operation='Build',
    [string]$FixtureRoot
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$scratch=[IO.Path]::GetFullPath($ScratchRoot)
if($scratch -notin @('D:\Temp\pathweft-s13-s9-s11\craft-cores','D:\Temp\pathweft-s16\craft-cores')){throw 'Use the registered native compiler root.'}
if(!$Manifest){$Manifest=Join-Path $root 'vendor/Office/runtime-manifest.json'}
$Manifest=[IO.Path]::GetFullPath($Manifest)
$donor=Join-Path $scratch "sources/$Engine"
$bridge=Join-Path $donor 'apps/pathweft-office-bridge'
if(!(Test-Path -LiteralPath (Join-Path $donor 'Cargo.lock'))){throw 'Prepare the hash-verified pinned donor source first.'}
$compatibilityJson=& python -B (Join-Path $PSScriptRoot 'Apply-CraftCompatibility.py') --engine $Engine --source $donor
if($LASTEXITCODE){throw 'Pinned core compatibility preparation failed.'}
$compatibility=@($compatibilityJson|ConvertFrom-Json)
[void][IO.Directory]::CreateDirectory((Join-Path $bridge 'src'))
Copy-Item -LiteralPath (Join-Path $root "native/office/$Engine/Cargo.toml") -Destination $bridge
foreach($source in Get-ChildItem -LiteralPath (Join-Path $root "native/office/$Engine/src") -File){Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $bridge 'src')}
foreach($source in Get-ChildItem -LiteralPath (Join-Path $root 'native/office/common') -Filter '*.rs' -File){Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $bridge 'src')}
$inputFiles=@((Join-Path $bridge 'Cargo.toml'))+@(Get-ChildItem -LiteralPath (Join-Path $bridge 'src') -File|Sort-Object Name|ForEach-Object FullName)
$inputHashes=@($inputFiles|ForEach-Object{(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}) -join ':'
$stamp=Join-Path $bridge 'pathweft-build-inputs.txt'
if(!(Test-Path -LiteralPath $stamp) -or [IO.File]::ReadAllText($stamp) -ne $inputHashes){
    # Copy-Item preserves source timestamps. An edit made while an earlier build
    # runs may therefore predate that build's final Cargo fingerprint. Changed
    # staged inputs must be newer than that fingerprint before invoking Cargo.
    foreach($file in $inputFiles){[IO.File]::SetLastWriteTimeUtc($file,[DateTime]::UtcNow)}
}
$names=@('RUSTUP_HOME','CARGO_HOME','CARGO_TARGET_DIR','TEMP','TMP','PATH','LIB','INCLUDE','RUSTC','CARGO_BUILD_JOBS','CRAFT_FONTS_DIR','CRAFT_FONTS_REQUIRED')
$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:RUSTUP_HOME=Join-Path $CacheRoot 'rustup-craft'
    $env:CARGO_HOME=Join-Path $CacheRoot 'cargo'
    # One serial compiler tree lets all three bridges reuse identical pinned
    # dependencies instead of recompiling and retaining three copies.
    $env:CARGO_TARGET_DIR=Join-Path $scratch 'target/native'
    $env:TEMP=Join-Path $scratch 'temp';$env:TMP=$env:TEMP
    [void][IO.Directory]::CreateDirectory($env:TEMP)
    $env:CARGO_BUILD_JOBS='1'
    if($Engine -in @('wordcraft','deckcraft','photocraft','vectorcraft','figcraft','pdfcraft')){
        $env:CRAFT_FONTS_DIR=Join-Path $scratch "fonts/$Engine"
        $env:CRAFT_FONTS_REQUIRED='1'
        if(!(Test-Path -LiteralPath (Join-Path $env:CRAFT_FONTS_DIR 'fonts/manifest.txt'))){throw 'Prepare the exact release-pinned Craft font source first.'}
    }
    $compiler=Join-Path $env:RUSTUP_HOME 'toolchains/1.95.0-x86_64-pc-windows-msvc/bin'
    $env:RUSTC=Join-Path $compiler 'rustc.exe'
    $vs=& 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if(!$vs){throw 'Windows native toolchain is unavailable.'}
    $vc=Join-Path $vs ('VC/Tools/MSVC/'+(Get-Content (Join-Path $vs 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim())
    $sdk='C:/Program Files (x86)/Windows Kits/10';$sdkVersion='10.0.26100.0'
    $env:PATH=(Join-Path $vc 'bin/Hostx64/x64')+';'+$compiler+';'+$env:PATH
    $env:LIB=(Join-Path $vc 'lib/x64')+';'+(Join-Path $sdk "Lib/$sdkVersion/um/x64")+';'+(Join-Path $sdk "Lib/$sdkVersion/ucrt/x64")
    $env:INCLUDE=(Join-Path $vc 'include')+';'+(Join-Path $sdk "Include/$sdkVersion/um")+';'+(Join-Path $sdk "Include/$sdkVersion/ucrt")+';'+(Join-Path $sdk "Include/$sdkVersion/shared")
    Push-Location $donor
    try{
        # The pinned upstream workspace, lock and patches govern its dependencies.
        # Only the Pathweft bridge is selected; no donor UI, CLI or MCP is built.
        if($Operation -eq 'Fixtures'){
            if(!$FixtureRoot){throw 'Exact fixture output is required.'}
            & (Join-Path $compiler 'cargo.exe') run --release --jobs 1 --package "pathweft-$Engine" --manifest-path (Join-Path $bridge 'Cargo.toml') --example pathweft-fixtures -- $FixtureRoot
        }else{
            & (Join-Path $compiler 'cargo.exe') $Operation.ToLowerInvariant() --release --jobs 1 --package "pathweft-$Engine" --manifest-path (Join-Path $bridge 'Cargo.toml')
        }
        if($LASTEXITCODE -ne 0){throw "The $Engine core bridge build failed."}
        & python -B (Join-Path $PSScriptRoot 'Verify-CraftCoreLock.py') --engine $Engine --scratch-root $scratch --manifest $Manifest --archives-root $ArchivesRoot
        if($LASTEXITCODE -ne 0){throw 'The native bridge changed pinned upstream dependencies.'}
        [IO.File]::WriteAllText($stamp,$inputHashes)
        $dll=Join-Path $env:CARGO_TARGET_DIR ('release/pathweft_'+$Engine+'.dll')
        $receipt=@{engine=$Engine;operation=$Operation;dll=$dll;bytes=(Get-Item -LiteralPath $dll).Length;sha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant();inputHashes=$inputHashes;source=(@((Get-Content -LiteralPath $Manifest -Raw|ConvertFrom-Json).apps)|Where-Object id -eq $Engine).sourceCommit}
        $receipt.compatibilityPatches=$compatibility
        $receipts=Join-Path $scratch 'receipts';[void][IO.Directory]::CreateDirectory($receipts)
        $receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $receipts "$Engine-$Operation.json") -Encoding utf8
        $receipt|ConvertTo-Json
    }finally{Pop-Location}
}finally{
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
}
