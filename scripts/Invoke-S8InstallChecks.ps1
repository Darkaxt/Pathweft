[CmdletBinding()]
param([Parameter(Mandatory)][string]$DeliveryRoot,[Parameter(Mandatory)][string]$RunRoot,
      [Parameter(Mandatory)][string]$WaitOwnerDirectory,[switch]$LiveRoutingTrial)
$ErrorActionPreference='Stop'
$run=[IO.Path]::GetFullPath($RunRoot)
if($run -notmatch '^D:\\Temp\\pathweft-s8-delivery\\verification-[a-f0-9]{32}$'){throw 'Use the existing owned delivery-verification root.'}
$setupFiles=@(Get-ChildItem -LiteralPath $DeliveryRoot -Filter '*-setup.exe' -File)
if($setupFiles.Count -ne 1){throw 'Exactly one actual delivered installer is required.'}
$setup=$setupFiles[0].FullName
$destination=Join-Path $run 'installed'
$uninstallKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\{A52C677C-5F24-45E2-B814-66DCAABFB71A}_is1'
$existing=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
if($existing){$existing.Dispose();throw 'Preserve the existing Pathweft installation; this isolated lifecycle trial requires an absent installation identity.'}
$settings=Join-Path $env:APPDATA 'Pathweft'
if(Test-Path -LiteralPath (Join-Path $settings 'shell-integration.json')){throw 'Preserve the existing folder recovery owner before an installer trial.'}
$originalProfile=$env:PATHWEFT_PROFILE
$env:PATHWEFT_PROFILE=$null
$env:TEMP=$env:TMP=Join-Path $run 'temp'
$checks=New-Object Collections.Generic.List[object]
function Check([bool]$success,[string]$criterion){
    $checks.Add([ordered]@{criterion=$criterion;passed=$success})
    [ordered]@{checks=$checks;run=$run} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $run 'installer-results.json')
    if(!$success){throw $criterion}
}
function Run-Child([string]$file,[string[]]$arguments){
    $start=New-Object Diagnostics.ProcessStartInfo($file)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
    foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
    $child=[Diagnostics.Process]::Start($start);$child.WaitForExit()
    $code=$child.ExitCode;$child.Dispose()
    if($code -ne 0){throw "$([IO.Path]::GetFileName($file)) exited $code. Preserve the exact installer log and recovery state."}
}
function Snapshot-Handlers{
    $states=foreach($type in @('Directory','Drive')){
        $base="Software\Classes\$type\shell"
        foreach($entry in @(@($base,''),@("$base\pathweft",''),@("$base\pathweft",'Icon'),@("$base\pathweft\command",''),@("$base\open\command",''),@("$base\open\command",'DelegateExecute'),@("$base\explore\command",''),@("$base\explore\command",'DelegateExecute'))){
            $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry[0])
            try{
                $present=$key -and $key.GetValueNames() -contains $entry[1]
                [ordered]@{key=$entry[0];name=$entry[1];present=[bool]$present;kind=$(if($present){[string]$key.GetValueKind($entry[1])}else{$null});value=$(if($present){$key.GetValue($entry[1],$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null})}
            }finally{if($key){$key.Dispose()}}
        }
    }
    ConvertTo-Json -InputObject @($states) -Depth 5 -Compress
}
function Profile-Hashes{
    $values=foreach($name in @('settings.txt','editor.json','folder-sizes.txt')){
        $path=Join-Path $settings $name
        [ordered]@{name=$name;exists=(Test-Path -LiteralPath $path);sha256=$(if(Test-Path -LiteralPath $path){(Get-FileHash -LiteralPath $path).Hash}else{$null})}
    }
    ConvertTo-Json -InputObject @($values) -Compress
}
$handlers=Snapshot-Handlers;$profileBefore=Profile-Hashes
$startup=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try{$agentBefore=$startup.GetValue('Pathweft Agent');$startupBefore=$startup.GetValue('Pathweft')}finally{$startup.Dispose()}
$installed=$false
Add-Type -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/InstallerLogObserver.cs')
function Verify-UpdaterWait([bool]$matching){
    $tag=if($matching){'matched'}else{'mismatched'}
    $signal='Local\Pathweft-S8-Wait-'+[guid]::NewGuid().ToString('N')
    $ready=[Threading.EventWaitHandle]::new($false,[Threading.EventResetMode]::ManualReset,$signal+'-ready')
    $release=[Threading.EventWaitHandle]::new($false,[Threading.EventResetMode]::ManualReset,$signal+'-release')
    $owner=$null;$updater=$null;$observer=$null
    $log=Join-Path $run ('update-'+$tag+'.log')
    try{
        $start=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet.exe).Source)
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
        foreach($argument in @((Join-Path $WaitOwnerDirectory 'PackagedChecks.dll'),'--wait-owner',($signal+'-ready'),($signal+'-release'))){$start.ArgumentList.Add($argument)}
        $owner=[Diagnostics.Process]::Start($start)
        [InstallerLogObserver]::WaitReadyOrExit($ready,$owner)
        $created=$owner.StartTime.ToUniversalTime().ToFileTimeUtc()
        $expected=if($matching){$created.ToString('X16')}else{($created+1).ToString('X16')}
        $observer=[InstallerLogObserver]::new($log,'Waiting for the identified requesting process to exit.')
        $start=[Diagnostics.ProcessStartInfo]::new($setup)
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
        foreach($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',('/DIR='+$destination),('/LOG='+$log),('/WAITPID='+$owner.Id),('/WAITCREATED='+$expected))){$start.ArgumentList.Add($argument)}
        $updater=[Diagnostics.Process]::Start($start)
        if($matching){
            $observer.WaitOrExit($updater)
            Check (!$owner.HasExited -and !$updater.HasExited) 'The installer waits on the actual identified requesting process while it is alive'
            [void]$release.Set();$owner.WaitForExit()
        }
        $updater.WaitForExit()
        Check ($updater.ExitCode -eq 0) ('The '+$tag+' requesting-process identity update exits successfully')
        $text=[IO.File]::ReadAllText($log)
        if($matching){
            Check ($owner.ExitCode -eq 0 -and $text.Contains('The identified requesting process exited.')) 'The installer resumes after the original requesting process actually exits'
        }else{
            Check (!$owner.HasExited -and $text.Contains('Requesting process creation time differs; no wait for a reused PID.')) 'A mismatched creation identity never waits on an unrelated live process'
        }
    }finally{
        [void]$release.Set()
        if($owner){$owner.WaitForExit();$owner.Dispose()}
        if($updater){$updater.WaitForExit();$updater.Dispose()}
        if($observer){$observer.Dispose()}
        $ready.Dispose();$release.Dispose()
    }
}
try{
    Run-Child $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',('/DIR='+$destination),('/LOG='+(Join-Path $run 'install.log')))
    $installed=$true
    $exe=Join-Path $destination 'Pathweft.exe'
    Check ((Test-Path -LiteralPath $exe) -and [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.StartsWith('1.1.0')) 'The actual x64 installer produces the versioned app in its isolated directory'
    Check ((Snapshot-Handlers) -eq $handlers) 'Installation keeps folder routing off until explicitly enabled'
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
    try{Check ($key.GetValue('InstallLocation').TrimEnd('\') -eq $destination) 'The current-user installation identity points to the exact test directory'}finally{$key.Dispose()}
    if($LiveRoutingTrial){
        Run-Child $exe @('--set-default')
        Check ((Test-Path -LiteralPath (Join-Path $settings 'shell-integration.json')) -and (Snapshot-Handlers) -ne $handlers) 'Explicit opt-in writes actual Shell routing and durable prior-owner recovery'
    }
    Run-Child $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',('/DIR='+$destination),('/LOG='+(Join-Path $run 'update.log')))
    if($LiveRoutingTrial){Check (Test-Path -LiteralPath (Join-Path $settings 'shell-integration.json')) 'A real same-version update preserves the existing recovery owner'}
    else{Check ((Snapshot-Handlers) -eq $handlers) 'A quiet same-version update keeps existing folder routing unchanged'}
    Verify-UpdaterWait $true
    Verify-UpdaterWait $false
    Run-Child (Join-Path $destination 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+(Join-Path $run 'uninstall.log')))
    $installed=$false
    Check (!(Test-Path -LiteralPath $exe)) 'The actual uninstaller removes its own program files'
    Check ((Snapshot-Handlers) -eq $handlers -and !(Test-Path -LiteralPath (Join-Path $settings 'shell-integration.json'))) 'Uninstall restores exact prior Shell values/kinds and closes its recovery record'
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
    try{Check ($null -eq $key) 'Uninstall removes only the test installation identity'}finally{if($key){$key.Dispose()}}
    Check ((Profile-Hashes) -eq $profileBefore) 'Install/update/uninstall preserve pre-existing settings, editor state and folder cache bytes'
    $startup=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try{Check ($startup.GetValue('Pathweft Agent') -eq $agentBefore -and $startup.GetValue('Pathweft') -eq $startupBefore) 'Other startup owners are unchanged'}finally{$startup.Dispose()}
}finally{
    if($installed -and (Test-Path -LiteralPath (Join-Path $destination 'Pathweft.exe'))){
        # Restore a test-owned opt-in even if a later lifecycle assertion failed.
        Run-Child (Join-Path $destination 'Pathweft.exe') @('--unset-default')
    }
    $env:PATHWEFT_PROFILE=$originalProfile
}
Get-Content -LiteralPath (Join-Path $run 'installer-results.json')
