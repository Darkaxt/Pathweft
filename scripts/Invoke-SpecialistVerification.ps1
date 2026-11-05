#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ScratchRoot='D:\Temp\pathweft-s13-s9-s11',
    [ValidateSet('full','office','office-fallback','office-formats','policies','formats','runtime','sizes','space','space-entry','space-refresh','space-ui','indexed-sizes','live-indexed-sizes','existing','pdf','modes','icons','menus','menus-ui','menus-properties','menus-shell','jump-lists','windows','transfers','transfers-clipboard')][string]$Scenario='full',
    [string]$MenuDesktop,
    [string]$FixtureSource,
    [string]$MediaInputs,
    [switch]$WithoutMediaInfo
)
$ErrorActionPreference='Stop'
# Source-built renderer libraries run in Pathweft's own supervised worker. They
# are not registered Craft CLI/MCP application workloads or gate dependencies.
$scratch=[IO.Path]::GetFullPath($ScratchRoot)
if($scratch -ne 'D:\Temp\pathweft-s13-s9-s11'){throw 'Exact registered task root required.'}
$run=Join-Path $scratch ('specialists-'+[guid]::NewGuid().ToString('N'))
$cleanup='C:/Users/darka/.agents/skills/transactional-cleanup/scripts/invoke_transactional_cleanup.py'
$registration=& python $cleanup begin --workspace (Split-Path -Parent $PSScriptRoot) --scan-root $run
if($LASTEXITCODE -ne 0){throw ('Could not register the exact trial root: '+($registration -join "`n"))}
$transaction=($registration -join "`n"|ConvertFrom-Json).transaction_id
$registered=& python $cleanup register --transaction $transaction --path $run --kind compiler-output --evidence 'Owned hidden S13 app workflow; no installed profile or desktop input'
if($LASTEXITCODE -ne 0){throw 'Could not register the hidden trial output.'}
Write-Output ('Cleanup transaction: '+$transaction)
$profile=Join-Path $run 'profile'
$temp=Join-Path $run 'temp'
foreach($path in @($run,$profile,$temp)){[void][IO.Directory]::CreateDirectory($path)}
if($WithoutMediaInfo){
    if($Scenario -ne 'runtime' -or !$FixtureSource){throw 'Missing-runtime check needs its exact scenario and owned audio fixture.'}
    $copy=Join-Path $run 'app';[void][IO.Directory]::CreateDirectory($copy)
    foreach($file in Get-ChildItem -LiteralPath (Split-Path -Parent $Executable) -File){Copy-Item -LiteralPath $file.FullName -Destination $copy}
    $Executable=Join-Path $copy 'Pathweft.exe'
    $source=[IO.Path]::GetFullPath($FixtureSource)
    if($source -notmatch '^D:\\Temp\\pathweft-s13-s9-s11\\specialists-[a-f0-9]{32}$'){throw 'Owned audio fixture source required.'}
    Copy-Item -LiteralPath (Join-Path $source 'audio.wav') -Destination $run
}
$start=[Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
$start.ArgumentList.Add('--verify-s8-appearance='+$run)
if($Scenario -in @('menus','menus-properties','menus-shell','indexed-sizes','space-ui','transfers-clipboard')){
    if(!$MenuDesktop -or !(Test-Path -LiteralPath $MenuDesktop)){throw 'Menu checks require the private native desktop launcher.'}
    $start.FileName=[IO.Path]::GetFullPath($MenuDesktop)
    $start.ArgumentList.Clear()
    $start.ArgumentList.Add([IO.Path]::GetFullPath($Executable))
    $start.ArgumentList.Add($run)
    if($Scenario -in @('menus-shell','indexed-sizes','space-ui')){$start.ArgumentList.Add('--shell-only')}
}
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.WindowStyle='Hidden'
$start.Environment['PATHWEFT_PROFILE']=$profile
$start.Environment['PATHWEFT_TEST_HIDDEN']='1'
$start.Environment['PATHWEFT_VERIFY_SPECIALISTS13']='1'
$start.Environment['PATHWEFT_SPECIALIST_SCENARIO']=$Scenario
if($FixtureSource){$start.Environment['PATHWEFT_SPECIALIST_FIXTURE_SOURCE']=[IO.Path]::GetFullPath($FixtureSource)}
if($MediaInputs){$start.Environment['PATHWEFT_SPECIALIST_MEDIA_INPUTS']=[IO.Path]::GetFullPath($MediaInputs)}
if($Scenario -eq 'existing'){
    if(!$MediaInputs){throw 'Actual SDR/audio/DV fixture inventory required.'}
    $start.Environment['PATHWEFT_MEDIA113_INPUTS']=[IO.Path]::GetFullPath($MediaInputs)
    $start.Environment['PATHWEFT_VERIFY_DEPS117']='1'
}
$start.Environment['PATHWEFT_SPECIALIST_INPUTS']=Join-Path $PSScriptRoot '../.local/evidence/2026-11-04-specialists/inputs'
$start.Environment['TEMP']=$temp
$start.Environment['TMP']=$temp
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'),@('theme.native=1','theme.mode=dark','animations=0','tray=0','wine=0','taskbarsearch=0','lastseen=1.1.17',"tabs.Left=$run","tabs.Right=$run"))
Write-Output ('Fixture: '+$run)
$process=[Diagnostics.Process]::Start($start)
$process.WaitForExit()
$receiptPath=Join-Path $run 'results.json'
if(!(Test-Path -LiteralPath $receiptPath)){throw "Original verification process $($process.Id) exited $($process.ExitCode) without a receipt: $run"}
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$expectedPid=$process.Id
if($Scenario -in @('menus','menus-properties','menus-shell','indexed-sizes','space-ui','transfers-clipboard')){
    $desktop=Get-Content (Join-Path $run 'desktop-receipt.json') -Raw|ConvertFrom-Json
    if(!$desktop.jobEmpty -or $desktop.exitCode -ne $process.ExitCode){throw 'Private native desktop tree is not verified empty.'}
    $expectedPid=$desktop.childPid
}
if($receipt.pid -ne $expectedPid -or $receipt.result -ne $process.ExitCode){throw 'Original process and verification receipt differ.'}
if($Scenario -in @('existing','icons','office','office-fallback','office-formats','modes','menus','menus-ui','menus-properties','menus-shell','jump-lists','windows','transfers')){
    @{pid=$receipt.pid;result=$receipt.result;checks=$receipt.checks.Count;failures=@($receipt.checks|Where-Object {$_.passed -eq $false -or $_.failure});receipt=$receiptPath}|ConvertTo-Json -Depth 10
}else{$receipt | ConvertTo-Json -Depth 10}
Write-Output ('Fixture: '+$run)
exit $process.ExitCode
