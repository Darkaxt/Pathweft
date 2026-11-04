[CmdletBinding()]
param([Parameter(Mandatory)][string]$DeliveryRoot,[switch]$InstallTrial,[string]$MediaInputs=$env:PATHWEFT_MEDIA113_INPUTS,[string]$SpecialistInputs=$env:PATHWEFT_SPECIALIST_INPUTS)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$scratch='D:\Temp\pathweft-s8-delivery'
$run=Join-Path $scratch ('verification-'+[guid]::NewGuid().ToString('N'))
$cleanup='C:/Users/darka/.agents/skills/transactional-cleanup/scripts/invoke_transactional_cleanup.py'
$registered=& python $cleanup begin --workspace $project --scan-root $run
if($LASTEXITCODE -ne 0){throw 'Could not inventory package verification scratch.'}
$transaction=($registered -join "`n"|ConvertFrom-Json).transaction_id
$registered=& python $cleanup register --transaction $transaction --path $run --kind compiler-output --evidence 'Independently extracted Pathweft package, native checks and hidden app-owned integration fixtures'
if($LASTEXITCODE -ne 0){throw 'Could not register package verification output.'}
Write-Output ('Cleanup transaction: '+$transaction)
[void][IO.Directory]::CreateDirectory($run)
$env:TEMP=$env:TMP=Join-Path $run 'temp'
[void][IO.Directory]::CreateDirectory($env:TEMP)
foreach($line in Get-Content -LiteralPath (Join-Path $DeliveryRoot 'SHA256SUMS.txt')){
    if($line -notmatch '^([a-fA-F0-9]{64})  (.+)$'){throw 'Malformed delivered checksum file.'}
    $expected=$Matches[1];$name=$Matches[2]
    if([IO.Path]::GetFileName($name) -ne $name){throw 'A checksum target escapes its delivery.'}
    if((Get-FileHash -LiteralPath (Join-Path $DeliveryRoot $name)).Hash -ne $expected){throw "Delivered asset checksum differs: $name"}
}
$portables=@(Get-ChildItem -LiteralPath $DeliveryRoot -Filter '*-portable.zip' -File)
if($portables.Count -ne 1){throw 'Exactly one portable package is required.'}
$portable=$portables[0]
Expand-Archive -LiteralPath $portable.FullName -DestinationPath (Join-Path $run 'portable')
$product=Join-Path $run 'portable/Pathweft'
$manifest=Get-Content -LiteralPath (Join-Path $product 'release-manifest.json') -Raw | ConvertFrom-Json
function Verify-Members([string]$directory){
    foreach($file in $manifest.files){
        $target=[IO.Path]::GetFullPath((Join-Path $directory $file.path))
        if(!$target.StartsWith([IO.Path]::GetFullPath($directory)+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'A packaged manifest escapes its directory.'}
        if((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256){throw "Actual packaged member differs: $($file.path)"}
    }
}
Verify-Members $product
if(!$MediaInputs -or !(Test-Path -LiteralPath $MediaInputs)){throw 'Supply the source-bound SDR/audio/DV fixture inventory with -MediaInputs.'}
$env:PATHWEFT_MEDIA113_INPUTS=[IO.Path]::GetFullPath($MediaInputs)
if(!$SpecialistInputs -or !(Test-Path -LiteralPath (Join-Path $SpecialistInputs 'manifest.json'))){throw 'Supply the pinned specialist fixture inventory with -SpecialistInputs.'}
$env:PATHWEFT_SPECIALIST_INPUTS=[IO.Path]::GetFullPath($SpecialistInputs)
& (Join-Path $PSScriptRoot 'Build-ShellRouter.ps1') -OutputRoot (Join-Path $run 'native-shell') -Verify -RevealProbe -ProductDirectory $product
$env:PATHWEFT_PROFILE=Join-Path $run 'profile'
[void][IO.Directory]::CreateDirectory($env:PATHWEFT_PROFILE)
[IO.File]::WriteAllLines((Join-Path $env:PATHWEFT_PROFILE 'settings.txt'),@('wine=0','tray=0','taskbarsearch=0','theme.native=1','theme.mode=light',"lastseen=$($manifest.version)","tabs.Left=$run","tabs.Right=$run"))
$selftest=Start-Process -FilePath (Join-Path $product 'Pathweft.exe') -ArgumentList '--selftest' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'selftest-output.txt') -RedirectStandardError (Join-Path $run 'selftest-error.txt')
$selftest.WaitForExit();$selftest.Refresh()
if($selftest.ExitCode -ne 0){Get-Content (Join-Path $env:TEMP 'pathweft-selftest.txt') -ErrorAction SilentlyContinue;throw 'Actual portable app-host self-test failed.'}
& (Join-Path $PSScriptRoot 'Invoke-S8PackageProbe.ps1') -ProductDirectory $product -RunRoot $run
if($LASTEXITCODE -ne 0){throw 'Delivered assembly integration check failed.'}
Verify-Members $product
Get-Content -LiteralPath (Join-Path $run 'results.json')
if($InstallTrial){
    & (Join-Path $PSScriptRoot 'Invoke-S8InstallChecks.ps1') -DeliveryRoot $DeliveryRoot -RunRoot $run -WaitOwnerDirectory $product
    if($LASTEXITCODE -ne 0){throw 'Installer lifecycle check failed.'}
}
[ordered]@{run=$run;product=$product;delivery=$DeliveryRoot;version=$manifest.version;sourceCommit=$manifest.sourceCommit;installed=[bool]$InstallTrial} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'active-package-verification.json')
Get-Content -LiteralPath (Join-Path $scratch 'active-package-verification.json')
