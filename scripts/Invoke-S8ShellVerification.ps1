[CmdletBinding()]
param([switch]$CallerOnly,[string]$Target,[string]$Verb,[string]$CallerReceipt,[string]$SelectionFile,[switch]$WinEInput,[switch]$InputOnly,[switch]$DirectSelection,[switch]$ExternalFolderLink)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $project 'tests/ShellRoutingProbe.cs') -ReferencedAssemblies @('System.dll','System.Core.dll','System.Windows.Forms.dll')
if($CallerOnly){
    $status=$null
    if($SelectionFile){$status=[ShellRoutingProbe]::SelectFile($Target,$SelectionFile)}else{[ShellRoutingProbe]::Open($Target,$Verb)}
    [ordered]@{bits=[ShellRoutingProbe]::Bits;target=$Target;verb=$Verb;hresult=$status} | ConvertTo-Json | Set-Content -LiteralPath $CallerReceipt -Encoding UTF8
    exit 0
}
if([IntPtr]::Size -ne 8){throw 'Use native 64-bit Windows PowerShell for the owning scenario.'}
$root='D:\Temp\pathweft-s8-delivery'
$run=Join-Path $root ('shell-trial-'+[guid]::NewGuid().ToString('N'))
$env:PATHWEFT_PROFILE=$null
$env:TEMP=$env:TMP=Join-Path $root 'temp'
$source=Join-Path $root 'artifacts/bin/FileExplorer/debug'
[void][IO.Directory]::CreateDirectory($run)
Copy-Item -Path (Join-Path $source '*') -Destination $run -Recurse
[IO.File]::WriteAllText((Join-Path $run 'portable.txt'),'S8 isolated portable launch trial.')
[IO.File]::WriteAllText((Join-Path $run 's8-shell-trial.txt'),'Debug-only offscreen Shell trial.')
$data=Join-Path $run 'Data';[void][IO.Directory]::CreateDirectory($data)
$folder=Join-Path $run ('folder spaced '+[char]0xE9+' '+[char]0x5B50);[void][IO.Directory]::CreateDirectory($folder)
$file=Join-Path $folder ('selected '+[char]0xE9+'.txt');[IO.File]::WriteAllText($file,'selection fixture')
[IO.File]::WriteAllText((Join-Path $folder '000-decoy.txt'),'selection must not be inferred from the first file')
[xml]$metadata=Get-Content -LiteralPath (Join-Path $project 'FileExplorer.csproj')
$version=($metadata.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
[IO.File]::WriteAllLines((Join-Path $data 'settings.txt'),@("lastseen=$version",'theme.native=1','theme.mode=dark','wine=0','tray=0','taskbarsearch=0',"tabs.Left=$folder","tabs.Right=$folder"))
$exe=Join-Path $run 'Pathweft.exe'
$signal=New-Object Threading.EventWaitHandle($false,[Threading.EventResetMode]::AutoReset,('Local\Pathweft-S8-Shell-'+[IO.Path]::GetFileName($run)))
$checks=New-Object Collections.Generic.List[object]
$owner=$null
function Run-Control([string]$argument){
    $child=Start-Process -FilePath $exe -ArgumentList $argument -WindowStyle Hidden -PassThru
    $child.WaitForExit();$child.Refresh()
    if($child.ExitCode -ne 0){throw "Control $argument exited $($child.ExitCode)"}
    $child.Dispose()
}
function Read-Receipt([string]$label){
    [void]$signal.WaitOne()
    $path=Join-Path $run 'shell-receipt.json'
    $receipt=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $path -Destination (Join-Path $run ($label+'.json'))
    if(!$receipt.offscreen -or $receipt.foregroundViolations -ne 0){throw 'Shell request disturbed the foreground.'}
    return $receipt
}
function Stop-Window{
    if($null -eq $script:owner){return}
    $original=$script:owner;$script:owner=$null
    try{
        if(!$original.HasExited){Run-Control '--s8-stop'}
        $original.WaitForExit()
        if($original.ExitCode -ne 0){throw 'Original Shell-launched window failed during exit.'}
    }finally{$original.Dispose()}
}
function Remember-Owner($receipt){
    if($null -eq $script:owner){$script:owner=[Diagnostics.Process]::GetProcessById($receipt.pid);[void]$script:owner.Handle}
    elseif($script:owner.Id -ne $receipt.pid){throw 'Warm routing created a duplicate application.'}
}
function Handlers{
    $items=foreach($type in @('Directory','Drive')){
        $base="Software\Classes\$type\shell"
        foreach($entry in @(@($base,''),@("$base\pathweft",''),@("$base\pathweft",'Icon'),@("$base\pathweft\command",''),@("$base\open\command",''),@("$base\open\command",'DelegateExecute'),@("$base\explore\command",''),@("$base\explore\command",'DelegateExecute'))){
            $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry[0])
            try{
                $exists=$null -ne $key -and $key.GetValueNames() -contains $entry[1]
                [ordered]@{key=$entry[0];name=$entry[1];exists=$exists;kind=$(if($exists){[string]$key.GetValueKind($entry[1])}else{$null});value=$(if($exists){$key.GetValue($entry[1],$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null})}
            }finally{if($null -ne $key){$key.Dispose()}}
        }
    }
    return ConvertTo-Json -InputObject @($items) -Depth 5 -Compress
}
$before=Handlers
$before | Set-Content -LiteralPath (Join-Path $run 'handlers-before.json')
$startup=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try{if($startup.GetValue('Pathweft Agent')){throw 'An existing Win+E owner must be preserved; this scenario cannot take its key.'}}finally{$startup.Dispose()}
$observer=New-Object ShellRoutingProbe+Observer
$registered=$false
try{
    Write-Output "S8 Shell trial: $run"
    Run-Control '--set-default';$registered=$true
    if($ExternalFolderLink){
        $InputOnly=$true
        $expected=if($Target){[IO.Path]::GetFullPath($Target)}else{[Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)}
        if(!(Test-Path -LiteralPath $expected -PathType Container)){throw 'The explicit filesystem folder must exist before the user-controlled check.'}
        $abortName='Local\Pathweft-S8-Abort-'+[IO.Path]::GetFileName($run)
        $abort=New-Object Threading.EventWaitHandle($false,[Threading.EventResetMode]::ManualReset,$abortName)
        [ordered]@{run=$run;abort=$abortName;expected=$expected} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'active-folder-link.json') -Encoding UTF8
        Write-Output 'Awaiting the one native StartAllBack folder-link action.'
        try{
            $outcome=[Threading.WaitHandle]::WaitAny([Threading.WaitHandle[]]@($signal,$abort))
            if($outcome -eq 0){
                $receipt=Get-Content -LiteralPath (Join-Path $run 'shell-receipt.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                Remember-Owner $receipt
                if(!$receipt.offscreen -or $receipt.foregroundViolations -ne 0){throw 'The user-controlled folder check disturbed the foreground.'}
                if(![string]::Equals($receipt.directory,$expected,[StringComparison]::OrdinalIgnoreCase)){throw 'A different folder reached the receiver; this is not proof of the requested filesystem-folder menu action.'}
                Copy-Item -LiteralPath (Join-Path $run 'shell-receipt.json') -Destination (Join-Path $run 'startallback-folder-link.json')
                $checks.Add([ordered]@{route='user-controlled StartAllBack filesystem-folder link';passed=$true;request=$receipt.request;directory=$receipt.directory;offscreen=$receipt.offscreen;foregroundViolations=$receipt.foregroundViolations;callerEvidence='User performs the menu action; receiver verifies the exact known folder. No host input automation.'})
            }else{$checks.Add([ordered]@{route='actual StartAllBack folder link';executed=$false;qualification='Trial cancelled before a native folder-link action was verified; this is not a failed routing claim.'})}
        }finally{$abort.Dispose()}
    }
    if(!$InputOnly){
    foreach($verbValue in @($null,'open','explore')){
        foreach($bits in @(64,32)){
            $label="folder-$bits-$verbValue"
            if($bits -eq 64){[ShellRoutingProbe]::Open($folder,$verbValue)}
            else{
                $callerArguments=@('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-CallerOnly','-Target',('"'+$folder+'"'),'-CallerReceipt',('"'+(Join-Path $run ($label+'-caller.json'))+'"'))
                if($verbValue){$callerArguments+=@('-Verb',$verbValue)}
                $caller=Start-Process -FilePath "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $callerArguments -WindowStyle Hidden -PassThru
                $caller.WaitForExit();$caller.Refresh();if($caller.ExitCode -ne 0){throw '32-bit caller failed.'};$caller.Dispose()
            }
            $receipt=Read-Receipt $label;Remember-Owner $receipt
            if($receipt.directory -ne $folder){throw 'Folder route changed its Unicode/spaced path.'}
            $checks.Add([ordered]@{route=$label;passed=$true;pid=$receipt.pid})
            Write-Output "$label passed"
        }
        Stop-Window
    }
    $shortcut=Join-Path $run 'folder shortcut.lnk'
    $shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut($shortcut);$link.TargetPath=$folder;$link.Save()
    [ShellRoutingProbe]::Open($shortcut,$null);$receipt=Read-Receipt 'shortcut';Remember-Owner $receipt
    if($receipt.directory -ne $folder){throw 'Shortcut route changed its target.'}
    $checks.Add([ordered]@{route='filesystem folder shortcut';passed=$true})
    # The application's documented folder-plus-selection CLI is a separate
    # launch boundary from the Shell's direct selection API.
    Run-Control ('/select,"'+$file+'"');$receipt=Read-Receipt 'select-cli';Remember-Owner $receipt
    if($receipt.selected -notcontains $file){throw 'Selection handoff lost the selected file.'}
    $checks.Add([ordered]@{route='folder-plus-selection CLI';passed=$true})
    [ShellRoutingProbe]::Open('D:\','open');$receipt=Read-Receipt 'drive';Remember-Owner $receipt
    if($receipt.directory -ne 'D:\'){throw 'Drive root routing changed the root path.'}
    $checks.Add([ordered]@{route='drive root';passed=$true})
    }
    if($WinEInput){
        Run-Control '--s8-wine-on'
        [ShellRoutingProbe]::WinE();$receipt=Read-Receipt 'wine';Remember-Owner $receipt
        Run-Control '--s8-wine-off'
        $checks.Add([ordered]@{route='native Win+E input with hidden target';passed=$true})
    }
    Stop-Window
    $checks.Add([ordered]@{route='covered routes produce no transient Explorer window';passed=($observer.ExplorerWindows.Count -eq 0);observed=$observer.ExplorerWindows})
    if($observer.ExplorerWindows.Count -ne 0){throw 'A covered route showed an Explorer window.'}
    if($DirectSelection){
        # This API explicitly requests Explorer. Observe its actual outcome,
        # qualify the boundary, and close only a new fixture window.
        $shellApplication=New-Object -ComObject Shell.Application
        $existing=@($shellApplication.Windows() | ForEach-Object {[long]$_.HWND})
        $observer.TargetTitle=[IO.Path]::GetFileName($folder)
        foreach($bits in @(64,32)){
        foreach($state in @('cold','warm')){
        $status=0
        if($bits -eq 64){$status=[ShellRoutingProbe]::SelectFile($folder,[IO.Path]::GetFileName($file))}
        else{
            $callerReceipt=Join-Path $run ("selection-$bits-$state-caller.json")
            $caller=Start-Process -FilePath "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-CallerOnly','-Target',('"'+$folder+'"'),'-SelectionFile',('"'+[IO.Path]::GetFileName($file)+'"'),'-CallerReceipt',('"'+$callerReceipt+'"')) -WindowStyle Hidden -PassThru
            $caller.WaitForExit();$caller.Refresh();if($caller.ExitCode -ne 0){throw '32-bit selection caller failed.'};$caller.Dispose()
            $status=(Get-Content -LiteralPath $callerReceipt -Raw -Encoding UTF8 | ConvertFrom-Json).hresult
        }
        $outcome=[Threading.WaitHandle]::WaitAny([Threading.WaitHandle[]]@($signal,$observer.ExplorerReady))
        if($outcome -eq 0){
            $receipt=Get-Content -LiteralPath (Join-Path $run 'shell-receipt.json') -Raw | ConvertFrom-Json
            Remember-Owner $receipt
            $checks.Add([ordered]@{route='direct SHOpenFolderAndSelectItems';bits=$bits;state=$state;hresult=('{0:X8}' -f [int]$status);target='Pathweft';supported=($status -eq 0 -and $receipt.selected -contains $file);selected=$receipt.selected;requested=$file})
        }else{
            $window=$shellApplication.Windows() | Where-Object {([long]$_.HWND -eq $observer.TargetWindow) -and $_.Document.Folder.Self.Path -eq $folder} | Select-Object -First 1
            if(!$window){throw 'Explorer title changed but its actual folder boundary is not yet verified; retain the exact trial for diagnosis.'}
            $selected=@($window.Document.SelectedItems() | ForEach-Object {$_.Path})
            $checks.Add([ordered]@{route='direct SHOpenFolderAndSelectItems';bits=$bits;state=$state;hresult=('{0:X8}' -f [int]$status);target='Windows Explorer';supportedByPathweft=$false;directory=$window.Document.Folder.Self.Path;selected=$selected})
            if($existing -notcontains ([long]$window.HWND)){$window.Quit()}
            else{throw 'Windows reused an existing Explorer window for its fixture tab. Close only that new test tab before cleanup; existing windows were preserved.'}
        }
        }
        Stop-Window
        }
    }
}finally{
    try{Stop-Window}finally{
        if($registered -or (Test-Path -LiteralPath (Join-Path $data 'shell-integration.json'))){Run-Control '--unset-default'}
        $after=Handlers;$after | Set-Content -LiteralPath (Join-Path $run 'handlers-after.json')
        $checks.Add([ordered]@{route='exact original handler value/kind restoration';passed=($before -eq $after)})
        $observer.Dispose();$signal.Dispose()
        [ordered]@{checks=$checks;run=$run;scope='Qualified native default-folder routes and separately reported selection-API limitations; no universal interception claim.'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding UTF8
        if($before -ne $after){throw 'Original handlers were not restored; inspect the recovery record before further work.'}
    }
}
Get-Content -LiteralPath (Join-Path $run 'results.json')
