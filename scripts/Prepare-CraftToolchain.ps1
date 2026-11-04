[CmdletBinding()]
param([string]$CacheRoot='D:\BuildCache\Pathweft')
$ErrorActionPreference='Stop'
$cache=[IO.Path]::GetFullPath($CacheRoot)
if($cache -notmatch '^D:\\BuildCache\\Pathweft$'){throw 'Use the existing Pathweft dependency cache.'}
$names=@('RUSTUP_HOME','CARGO_HOME','TEMP','TMP')
$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:RUSTUP_HOME=Join-Path $cache 'rustup-craft'
    $env:CARGO_HOME=Join-Path $cache 'cargo'
    $env:TEMP='D:\Temp\pathweft-s13-s9-s11\craft-cores\temp'
    $env:TMP=$env:TEMP
    [void][IO.Directory]::CreateDirectory($env:TEMP)
    # Explicit toolchain installation in a new D:-bound home. No change to the
    # user's existing Rust home, default toolchain, global environment or PATH.
    & 'C:\Users\darka\.cargo\bin\rustup.exe' toolchain install 1.95.0 --profile minimal --no-self-update
    if($LASTEXITCODE -ne 0){throw 'The pinned Craft compiler could not be prepared.'}
    & (Join-Path $env:RUSTUP_HOME 'toolchains/1.95.0-x86_64-pc-windows-msvc/bin/rustc.exe') --version
    if($LASTEXITCODE -ne 0){throw 'The installed compiler did not report its identity.'}
}finally{
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
}
