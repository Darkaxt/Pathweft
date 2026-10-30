[CmdletBinding()]
param([string]$ScratchRoot='D:\Temp\pathweft-s8-delivery')
$ErrorActionPreference='Stop'
if([IO.Path]::GetFullPath($ScratchRoot) -ne 'D:\Temp\pathweft-s8-delivery'){throw 'Use registered S8 native/source I/O scratch.'}
$root=Join-Path $ScratchRoot 'corresponding-sources'
[void][IO.Directory]::CreateDirectory($root)
$env:TEMP=$env:TMP=Join-Path $ScratchRoot 'temp'
$inputs=@(
    @{name='7z2605-src.tar.xz';url='https://github.com/ip7z/7zip/releases/download/26.05/7z2605-src.tar.xz';sha='0B6080B8C3E7A579161D2D495224C0E51C467576CB4C7048B0AB485EF44CC22E';version='26.05'},
    @{name='SharpSevenZip-source.zip';url='https://codeload.github.com/JeremyAnsel/SharpSevenZip/zip/9b2379be603a9d4ebff06c56944f87f0c623f8db';sha='03171712C5FCBC6117C08E1D1508995415191145F20792DC92206942EF1DC3EA';version='9b2379be603a9d4ebff06c56944f87f0c623f8db'},
    @{name='f3d-4.0.0-source.zip';url='https://codeload.github.com/f3d-app/f3d/zip/b72c7b5c9244e4a0c12975ce6546645c1a8b2680';version='b72c7b5c9244e4a0c12975ce6546645c1a8b2680'},
    @{name='ffmpeg-9.0.1-source.zip';url='https://codeload.github.com/FFmpeg/FFmpeg/zip/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa';version='bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa'},
    @{name='openh264-2.6.0-source.zip';url='https://codeload.github.com/cisco/openh264/zip/refs/tags/v2.6.0';sha='B1CA643EADBBF16F74C86A66AB487F38691D8DBDDA435683B149B2CE2C8077EC';version='v2.6.0'},
    @{name='f3d-superbuild-source.zip';url='https://codeload.github.com/f3d-app/f3d-superbuild/zip/2aada7d4c6a1dff3f2288008031e8a31ffc0428f';sha='1E16A2CC7375D68E83FA3FAEEFAE0AD0FD8A818BFDD753E380CBEDEA702CE08F';version='2aada7d4c6a1dff3f2288008031e8a31ffc0428f'}
)
$manifest=foreach($sourceSpec in $inputs){
    $file=Join-Path $root $sourceSpec.name
    if(!(Test-Path -LiteralPath $file)){Invoke-WebRequest -Uri $sourceSpec.url -OutFile $file}
    $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if($sourceSpec.sha -and $hash -ne $sourceSpec.sha){throw "Source digest differs: $($sourceSpec.name)"}
    [ordered]@{name=$sourceSpec.name;url=$sourceSpec.url;revision=$sourceSpec.version;sha256=$hash;bytes=(Get-Item -LiteralPath $file).Length}
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'source-manifest.json')
# Include the superbuild's matching FFmpeg/OpenH264 configure and Windows patch
# recipes; the older recipes inside F3D itself describe a different x264 build.
# The Rust bridge and lockfile are in Pathweft source. Vendor the locked native
# dependency sources so its GPL corresponding-source copy is usable offline.
$project=Split-Path -Parent $PSScriptRoot
$env:CARGO_HOME='D:\BuildCache\Pathweft\cargo'
$env:CARGO_TARGET_DIR=Join-Path $ScratchRoot 'cargo-source-target'
$vendor=Join-Path $root 'rust-vendor'
if(!(Test-Path -LiteralPath (Join-Path $root 'cargo-config.toml'))){
    & cargo vendor --manifest-path (Join-Path $project 'native/pathweft-rars/Cargo.toml') --locked --versioned-dirs $vendor > (Join-Path $root 'cargo-config.toml')
    if($LASTEXITCODE -ne 0){throw 'Locked native dependency source export failed.'}
}
$configuration=Get-Content -LiteralPath (Join-Path $root 'cargo-config.toml') -Raw
[IO.File]::WriteAllText((Join-Path $root 'cargo-config.toml'),$configuration.Replace($vendor.Replace('\','/'),'rust-vendor'))
Get-Content -LiteralPath (Join-Path $root 'source-manifest.json')
