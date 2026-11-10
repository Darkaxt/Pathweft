<# Complete a verified per-user to protected-install migration for one account.
   Run only after the new /MIGRATE=1 installer has finished in Program Files.
   The old uninstall owns its own files and Shell restoration; this script
   restores the previously enabled routes and startup owners from the new app. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OldInstall,
    [Parameter(Mandatory)][string]$NewInstall,
    [Parameter(Mandatory)][string]$SettingsDir,
    [Parameter(Mandatory)][string]$Receipt,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference='Stop'
$old=[IO.Path]::GetFullPath($OldInstall).TrimEnd('\')
$new=[IO.Path]::GetFullPath($NewInstall).TrimEnd('\')
$data=[IO.Path]::GetFullPath($SettingsDir).TrimEnd('\')
if(!$new.Equals((Join-Path $env:ProgramFiles 'Pathweft'),[StringComparison]::OrdinalIgnoreCase)){throw 'The protected installation must be in Program Files\Pathweft.'}
if($old.Equals($new,[StringComparison]::OrdinalIgnoreCase)){throw 'Old and new installations are identical.'}
$oldExe=Join-Path $old 'Pathweft.exe'
$newExe=Join-Path $new 'Pathweft.exe'
$uninstaller=Join-Path $old 'unins000.exe'
foreach($path in @($oldExe,$newExe,$uninstaller,(Join-Path $data 'folder-routes.json'),(Join-Path $data 'shell-integration.json'))){if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Required migration input is absent: $path"}}
if(![Diagnostics.FileVersionInfo]::GetVersionInfo($newExe).ProductVersion.StartsWith($Version+'+')){throw 'The protected executable version differs.'}
$front=Get-CimInstance Win32_Process -Filter "Name = 'Pathweft.exe'" |
    Where-Object { $_.ExecutablePath -eq $oldExe -and $_.CommandLine -notmatch '(?i)(^|\s)--agent(\s|$)' }
if($front){throw 'Close the old Pathweft front window and tray session before retiring it.'}

$clsid='{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}'
$routerFiles=@()
foreach($arch in @('x64','x86')){
    $view=if($arch -eq 'x64'){[Microsoft.Win32.RegistryView]::Registry64}else{[Microsoft.Win32.RegistryView]::Registry32}
    $machine=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
    try{
        $class=$machine.OpenSubKey("Software\Classes\CLSID\$clsid\InprocServer32")
        try{
            $expected=Join-Path $new "PathweftShell-$Version-$arch.dll"
            if($null -eq $class -or $class.GetValue('') -cne $expected -or $class.GetValue('ThreadingModel') -cne 'Apartment'){throw "The protected $arch machine COM registration differs."}
            $routerFiles+= $expected
        }finally{if($null -ne $class){$class.Dispose()}}
    }finally{$machine.Dispose()}
}

# Check the exact executable and COM DLL, including ownership. A machine COM
# server must not be replaceable by a non-administrator before old state moves.
$currentSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$weak=@('S-1-1-0','S-1-5-11','S-1-5-32-545',$currentSid)
$write=0
foreach($right in @('WriteData','AppendData','WriteExtendedAttributes','WriteAttributes','DeleteSubdirectoriesAndFiles','Delete','ChangePermissions','TakeOwnership')){
    $write=$write -bor [int][Enum]::Parse([Security.AccessControl.FileSystemRights],$right)
}
foreach($path in @($new,$newExe)+$routerFiles){
    $acl=Get-Acl -LiteralPath $path
    $owner=$acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if($owner -eq $currentSid){throw "Protected installation is owned by the current user: $path"}
    foreach($rule in $acl.Access){
        if($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow){continue}
        $sid=$rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if($sid -in $weak -and (([int]$rule.FileSystemRights -band $write) -ne 0)){throw "A non-admin principal can write the machine COM target: $path ($sid)"}
    }
}

$user=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
try{
    $run=$user.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try{
        $priorTray=$run?.GetValue('Pathweft')
        $priorAgent=$run?.GetValue('Pathweft Agent')
    }finally{if($null -ne $run){$run.Dispose()}}
    if($null -ne $priorTray -and $priorTray -cne "`"$oldExe`" --tray"){throw 'Another startup owner is present.'}
    if($null -ne $priorAgent -and $priorAgent -cne "`"$oldExe`" --agent"){throw 'Another Win+E owner is present.'}
    $folder=$user.OpenSubKey('Software\Classes\Folder\shell\open\command')
    try{
        if($null -eq $folder -or $folder.GetValue('') -cne "`"$oldExe`" `"%1`"" -or $folder.GetValue('DelegateExecute') -cne $clsid){throw 'The old opted-in Folder/open route differs.'}
    }finally{if($null -ne $folder){$folder.Dispose()}}
}finally{$user.Dispose()}

$receiptPath=[IO.Path]::GetFullPath($Receipt)
if(Test-Path -LiteralPath $receiptPath){throw 'An existing migration receipt must be resolved first.'}
$settingsFile=Join-Path $data 'settings.txt'
$routesFile=Join-Path $data 'folder-routes.json'
$locationFile=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Pathweft\location.txt'
foreach($path in @($settingsFile,$locationFile)){if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Required profile pointer or settings are absent: $path"}}
$preserved=@{}
foreach($path in @($settingsFile,$routesFile,$locationFile)){$preserved[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
$state=[ordered]@{Old=$old;New=$new;Settings=$data;Version=$Version;Tray=($null -ne $priorTray);Agent=($null -ne $priorAgent);Preserved=$preserved;Phase='preflight';RecordedUtc=[DateTime]::UtcNow.ToString('o')}
function Save-State{
    $temporary=$receiptPath+'.new'
    [IO.File]::WriteAllText($temporary,($state|ConvertTo-Json -Compress))
    [IO.File]::Move($temporary,$receiptPath,$true)
}
Save-State
$process=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
$process.WaitForExit()
if($process.ExitCode -ne 0){throw "The original uninstaller exited $($process.ExitCode); migration receipt is retained."}
$state.Phase='old-retired';Save-State
foreach($argument in @('--set-default','--resume-agent')){
    $process=Start-Process -FilePath $newExe -ArgumentList $argument -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw "Protected Pathweft $argument exited $($process.ExitCode); migration receipt is retained."}
}
$user=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
try{
    if($state.Tray){
        $run=$user.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
        try{
            if($null -ne $run.GetValue('Pathweft')){throw 'Startup owner changed during migration.'}
            $run.SetValue('Pathweft',"`"$newExe`" --tray",[Microsoft.Win32.RegistryValueKind]::String)
        }finally{$run.Dispose()}
    }
    $folder=$user.OpenSubKey('Software\Classes\Folder\shell\open\command')
    try{
        if($null -eq $folder -or $folder.GetValue('') -cne "`"$newExe`" `"%1`"" -or $folder.GetValue('DelegateExecute') -cne $clsid){throw 'The new Folder/open route did not read back.'}
    }finally{if($null -ne $folder){$folder.Dispose()}}
    if($state.Agent){
        $run=$user.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
        try{if($run?.GetValue('Pathweft Agent') -cne "`"$newExe`" --agent"){throw 'The new Win+E owner did not read back.'}}
        finally{if($null -ne $run){$run.Dispose()}}
    }
}finally{$user.Dispose()}
foreach($path in $preserved.Keys){
    if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $preserved[$path]){
        throw "A preserved settings or profile file changed: $path"
    }
}
$state.Phase='user-routes-restored';Save-State
$state|ConvertTo-Json -Compress
