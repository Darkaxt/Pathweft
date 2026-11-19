[CmdletBinding()]
param([string]$ScratchRoot='D:\Temp\pathweft-s8-delivery',[string]$CacheRoot='D:\BuildCache\Pathweft',[string]$CompilerRoot='D:\Tools\InnoSetup\6.7.4')
$ErrorActionPreference='Stop'
if([IO.Path]::GetFullPath($ScratchRoot) -ne 'D:\Temp\pathweft-s8-delivery'){throw 'Use the registered S8 native I/O scratch.'}
$inputs=Join-Path $ScratchRoot 'release-inputs'
[void][IO.Directory]::CreateDirectory($inputs)
$env:TEMP=$env:TMP=Join-Path $ScratchRoot 'temp'
$compiler=Join-Path $inputs 'innosetup-6.7.4.exe'
if(!(Test-Path -LiteralPath $compiler)){Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $compiler}
if((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne 'A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C'){throw 'The official compiler asset differs from its published GitHub digest.'}
$signature=Get-AuthenticodeSignature -LiteralPath $compiler
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') {throw 'The official compiler publisher/signature was not verified.'}
if(!(Test-Path -LiteralPath (Join-Path $CompilerRoot 'ISCC.exe'))){
    $launch=New-Object Diagnostics.ProcessStartInfo($compiler)
    $launch.UseShellExecute=$false;$launch.CreateNoWindow=$true;$launch.WindowStyle='Hidden'
    foreach($argument in @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR='+$CompilerRoot),('/LOG='+(Join-Path $inputs 'compiler-install.log')))){$launch.ArgumentList.Add($argument)}
    # No implicit UAC. If the tool requires administrator installation, this
    # CreateProcess boundary reports that requirement for the managed broker.
    $process=[Diagnostics.Process]::Start($launch);$process.WaitForExit()
    if($process.ExitCode -ne 0){throw "Compiler installation exited $($process.ExitCode)."}
    $process.Dispose()
}
if(!(Test-Path -LiteralPath (Join-Path $CompilerRoot 'ISCC.exe'))){throw 'Compiler installation produced no ISCC.exe.'}
[ordered]@{compiler='6.7.4';compilerRoot=$CompilerRoot;compilerSha256=(Get-FileHash -LiteralPath $compiler).Hash;publisher=$signature.SignerCertificate.Subject;indexer='Pathweft index service, built from native/pathweft-index (SEARCH-09); Everything is no longer packaged'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputs 'dependencies.json')
Get-Content -LiteralPath (Join-Path $inputs 'dependencies.json')
