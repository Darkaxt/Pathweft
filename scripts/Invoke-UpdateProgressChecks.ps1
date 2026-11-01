[CmdletBinding()]
param([string]$RunRoot='D:/Temp/pathweft-s8-delivery/update-native',[string]$Compiler='D:/Tools/InnoSetup/6.7.4/ISCC.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$run=[IO.Path]::GetFullPath($RunRoot)
if($run -notmatch '^D:\\Temp\\pathweft-s8-delivery\\update-native(?:-[a-f0-9]+)?$' -or (Test-Path $run)){throw 'Use a new exact owned native-update verification root.'}
$id='{84E8B650-1350-4C15-A1CE-FC2FD3575895}'
$registration='Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$id+'_is1'
$baseline=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registration)
if($baseline){$baseline.Dispose();throw 'Preserve an existing verification installer identity.'}
[void][IO.Directory]::CreateDirectory($run)
$env:TEMP=$env:TMP=Join-Path $run 'temp';[void][IO.Directory]::CreateDirectory($env:TEMP)
& (Join-Path $PSScriptRoot 'Build-ShellRouter.ps1') -OutputRoot (Join-Path $run 'native') -ProgressReceiver
if($LASTEXITCODE -ne 0){throw 'Installer fixture compilation failed.'}
$payload=Join-Path $run 'payload';$delivery=Join-Path $run 'delivery'
foreach($path in @($payload,$delivery)){[void][IO.Directory]::CreateDirectory($path)}
Copy-Item (Join-Path $run 'native/x64/InstallerProgressReceiver.exe') (Join-Path $payload 'Pathweft.exe')
foreach($arch in @('x64','x86')){Copy-Item (Join-Path $run ('native/'+$arch+'/PathweftShell.dll')) (Join-Path $payload ('PathweftShell-1.1.14-'+$arch+'.dll'))}
$bytes=New-Object byte[] (1024*1024);[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[IO.File]::WriteAllBytes((Join-Path $payload 'deployed.bin'),$bytes)
& $Compiler '/DAppVersion=1.1.14' ('/DAppIdGuid='+$id) '/DProgressVerification' ('/DPublishRoot='+$payload) ('/DDeliveryRoot='+$delivery) (Join-Path $repo 'installer/Pathweft.iss') /Q
if($LASTEXITCODE -ne 0){throw 'Actual progress-enabled installer did not compile.'}
Add-Type -Path (Join-Path $repo 'tests/UpdateProgressObserver.cs')
$setup=Join-Path $delivery 'Pathweft-1.1.14-setup.exe'
$checks=New-Object Collections.Generic.List[object]
function Check([bool]$value,[string]$criterion){$checks.Add(@{criterion=$criterion;passed=$value});if(!$value){throw $criterion}}
foreach($case in @(@{name='dark100';scale=100;back=0x141414;ink=0xF6EEE8;accent=0xAA8050},@{name='light100';scale=100;back=0xF3F3F3;ink=0x202020;accent=0x805020},@{name='dark150';scale=150;back=0x141414;ink=0xF6EEE8;accent=0xAA8050},@{name='light150';scale=150;back=0xF3F3F3;ink=0x202020;accent=0x805020},@{name='failed-prepare';scale=100;back=0x141414;ink=0xF6EEE8;accent=0xAA8050})){
    $folder=Join-Path $run $case.name;$destination=Join-Path $folder 'installed'
    [void][IO.Directory]::CreateDirectory($destination)
    Copy-Item (Join-Path $payload 'Pathweft.exe') (Join-Path $destination 'Pathweft.exe')
    if($case.name -eq 'failed-prepare'){[IO.File]::WriteAllText((Join-Path $destination 'prepare-fail.flag'),'Deliberate fixture-only preparation refusal.')}
    $eventName='Local\Pathweft-Progress114-'+[guid]::NewGuid().ToString('N')
    $observer=[UpdateProgressObserver]::new($eventName)
    $report=Join-Path $folder 'phase.txt';$phases=New-Object Collections.Generic.List[object]
    $start=[Diagnostics.ProcessStartInfo]::new($setup);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
    $muted=if($case.name.StartsWith('light')){0x565656}else{0xAEAEAE}
    $track=if($case.name.StartsWith('light')){0xDADADA}else{0x2C2C2C}
    foreach($arg in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=','/RESTARTAPP=1',('/DIR='+$destination),('/LOG='+(Join-Path $folder 'setup.log')),('/TESTREPORT='+$report),('/TESTEVENT='+$eventName),('/TESTSCALE='+$case.scale),('/UPDATEBACK='+$case.back),('/UPDATEINK='+$case.ink),('/UPDATEMUTED='+$muted),('/UPDATEACCENT='+$case.accent),('/UPDATETRACK='+$track))){$start.ArgumentList.Add($arg)}
    $failure=$null;$foreground=[UpdateProgressObserver]::GetForegroundWindow();$installed=$false
    try{
        $observer.Start($start)
        while($observer.Wait() -eq 0){
            try{
                $lines=[IO.File]::ReadAllLines($report)
                $phase=$lines[0];$handle=[long]$lines[1];$percent=[int]$lines[2]
                $owner=[UpdateProgressObserver]::Owner($handle)
                [UpdateProgressObserver]::Capture($handle,(Join-Path $folder ('frame-'+$phases.Count+'.bmp')))
                $phases.Add(@{phase=$phase;percent=$percent;owner=$owner;heading=$lines[3];status=$lines[4];detail=$lines[5];back=[long]$lines[6];accent=[long]$lines[7];width=[int]$lines[8];height=[int]$lines[9]})
                Check ($owner -gt 0 -and [long]$lines[6] -eq $case.back -and [long]$lines[7] -eq $case.accent) ('Actual installer owns the '+$case.name+' themed progress phase: '+$phase)
                $currentForeground=[UpdateProgressObserver]::GetForegroundWindow()
                Check ([UpdateProgressObserver]::Owner($currentForeground.ToInt64()) -ne $owner -and [UpdateProgressObserver]::Owner($currentForeground.ToInt64()) -ne $observer.Process.Id) ('Hidden native '+$case.name+' phase performs no foreground takeover: '+$phase)
            }catch{if(!$failure){$failure=$_}}
            finally{[void]$observer.Continue.Set()}
        }
        $observer.Process.WaitForExit();$code=$observer.Process.ExitCode
        $phases|ConvertTo-Json -Depth 5|Set-Content (Join-Path $folder 'phases.json')
        if($failure){throw $failure}
        if($case.name -eq 'failed-prepare'){
            Check ($code -ne 0 -and @($phases|Where-Object phase -eq 'Update could not finish').Count -eq 1 -and !@($phases|Where-Object phase -eq 'Update installed').Count) 'Real failed installer preparation reports failure without a success phase'
            Check (!(Test-Path (Join-Path $destination 'deployed.bin'))) 'Failed preparation replaces no application payload'
        }else{
            Check ($code -eq 0) ('Actual '+$case.name+' deployment and restart dispatch finish successfully')
            $installed=$true
            $percentages=@($phases|Where-Object phase -eq 'Installing files'|ForEach-Object percent)
            Check ($percentages.Count -ge 2 -and $percentages[-1] -eq 100 -and @($phases|Where-Object phase -eq 'Restoring integration').Count -eq 1 -and @($phases|Where-Object phase -eq 'Opening Pathweft').Count -eq 1) ('Actual '+$case.name+' installer callbacks report file progress, integration and reopen phases')
            Check ((Get-FileHash (Join-Path $destination 'deployed.bin')).Hash -eq (Get-FileHash (Join-Path $payload 'deployed.bin')).Hash) ('Actual '+$case.name+' deployment preserves source payload bytes')
            $calls=Get-Content (Join-Path $destination 'calls.txt')
            Check (@($calls|Where-Object{$_ -eq '--desktop-launch --resume-agent '}).Count -eq 1 -and @($calls|Where-Object{$_ -eq '--desktop-launch '}).Count -eq 1) ('Actual '+$case.name+' deployment invokes helper restoration and reopening once')
        }
    }finally{
        $observer.Dispose()
        $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registration)
        try{$installed=$null -ne $key;if($key -and $key.GetValue('InstallLocation').TrimEnd('\') -ne $destination){throw 'Test installation ownership changed; preserve it.'}}finally{if($key){$key.Dispose()}}
        if($installed){
            $uninstall=Start-Process -FilePath (Join-Path $destination 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
            $uninstall.WaitForExit();if($uninstall.ExitCode -ne 0){throw 'Owned fixture uninstall did not complete.'};$uninstall.Dispose()
        }
    }
}
$remaining=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registration)
try{Check ($null -eq $remaining) 'The actual test uninstaller removes only its distinct installation identity'}finally{if($remaining){$remaining.Dispose()}}
$checks|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run 'checks.json')
Write-Output ('Native progress verification passed: '+$run)
