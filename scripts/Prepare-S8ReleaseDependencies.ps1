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
$indexerZip=Join-Path $inputs 'Everything-1.4.1.1032.x64.zip'
$hashes=Join-Path $inputs 'Everything-1.4.1.1032.sha256'
if(!(Test-Path -LiteralPath $hashes)){Invoke-WebRequest -Uri 'https://www.voidtools.com/Everything-1.4.1.1032.sha256' -OutFile $hashes}
if(!(Test-Path -LiteralPath $indexerZip)){Invoke-WebRequest -Uri 'https://www.voidtools.com/Everything-1.4.1.1032.x64.zip' -OutFile $indexerZip}
$expected=(Get-Content -LiteralPath $hashes | Where-Object {$_ -match '\s\*?Everything-1\.4\.1\.1032\.x64\.zip$'}).Split(' ',[StringSplitOptions]::RemoveEmptyEntries)[0]
if(!$expected -or (Get-FileHash -LiteralPath $indexerZip -Algorithm SHA256).Hash -ne $expected){throw 'Official Everything portable archive digest differs.'}
$target=Join-Path $CacheRoot 'everything/1.4.1.1032'
if(!(Test-Path -LiteralPath $target)){Expand-Archive -LiteralPath $indexerZip -DestinationPath $target}
$indexer=Join-Path $target 'Everything.exe'
if((Get-FileHash -LiteralPath $indexer -Algorithm SHA256).Hash -ne 'F191F756996A14A11E5445FA7103D302EFD510CF2FBF920E6C0C8ED51D512E36'){throw 'Pinned Everything engine differs from the qualified executable.'}
if(!(Test-Path -LiteralPath (Join-Path $target 'License.txt'))){Invoke-WebRequest -Uri 'https://www.voidtools.com/License.txt' -OutFile (Join-Path $target 'License.txt')}
[ordered]@{compiler='6.7.4';compilerRoot=$CompilerRoot;compilerSha256=(Get-FileHash -LiteralPath $compiler).Hash;publisher=$signature.SignerCertificate.Subject;indexer='1.4.1.1032';indexerArchiveSha256=$expected;indexerSha256=(Get-FileHash -LiteralPath $indexer).Hash;indexerUse='Packaged unchanged; existing default instance remains selected. No automatic index creation or service installation.'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputs 'dependencies.json')
Get-Content -LiteralPath (Join-Path $inputs 'dependencies.json')
