[CmdletBinding()]
param([Parameter(Mandatory)][int]$TrialProcessId,[switch]$TerminateClosedTrial,[switch]$PackageProbe,[switch]$PerformanceTrial)
$ErrorActionPreference='Stop'
$trial=Get-CimInstance Win32_Process -Filter "ProcessId=$TrialProcessId"
if($PackageProbe){
    if(!$TerminateClosedTrial -or !$trial -or $trial.Name -ne 'dotnet.exe' -or $trial.CommandLine -notmatch 'D:\\Temp\\pathweft-s8-delivery\\verification-[a-f0-9]{32}\\portable\\Pathweft\\PackagedChecks.dll' -or $trial.CommandLine -notmatch 'D:\\Temp\\pathweft-s8-delivery\\verification-[a-f0-9]{32}\\integration-[a-f0-9]{32}'){
        throw 'Only an inspected, closed owned package probe may be retired.'
    }
}elseif($PerformanceTrial){
    if(!$trial -or $trial.ExecutablePath -notlike 'D:\Temp\pathweft-s28\build-*\Pathweft.exe' -or $trial.CommandLine -notmatch '--verify-s8-appearance=D:\\Temp\\pathweft-s18\\specialists-[a-f0-9]{32}') {throw 'Exact owned S28 performance diagnostic required.'}
}elseif(!$trial -or $trial.ExecutablePath -notlike 'D:\Temp\pathweft-s13-s9-s11\*\Pathweft.exe' -or $trial.CommandLine -notmatch '--verify-s8-appearance=D:\\Temp\\pathweft-s13-s9-s11\\specialists-'){
    throw 'Only this task owned hidden specialist diagnostic can receive a graceful close.'
}
if($TerminateClosedTrial){
    $process=Get-Process -Id $TrialProcessId
    $children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$TrialProcessId")
    $consoleChildren=@($children|Where-Object {$PackageProbe -and $_.Name -eq 'conhost.exe' -and $_.ExecutablePath -eq (Join-Path $env:SystemRoot 'System32/conhost.exe')})
    if($process.MainWindowHandle -ne [IntPtr]::Zero -or $children.Count -ne $consoleChildren.Count){
        throw 'Diagnostic main window or native children remain; use normal close and inspect them first.'
    }
    $consoles=@($consoleChildren|ForEach-Object {Get-Process -Id $_.ProcessId})
    # A confirmed stuck diagnostic continuation survived its normal HWND closure. It has
    # no user document or remaining native child; retire only this exact task process.
    Stop-Process -InputObject $process
    $process.WaitForExit()
    foreach($console in $consoles){$console.WaitForExit();$console.Dispose()}
    Write-Output "Retired closed owned diagnostic $TrialProcessId; its result remains aborted, never acceptance."
    return
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HiddenTrialClose {
    public delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
}
'@
$script:sent=0
$callback=[HiddenTrialClose+WindowCallback]{param($window,$parameter)
    [uint32]$windowProcess=0
    [void][HiddenTrialClose]::GetWindowThreadProcessId($window,[ref]$windowProcess)
    if($windowProcess -eq $TrialProcessId){
        if([HiddenTrialClose]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)){$script:sent++}
    }
    return $true
}
[void][HiddenTrialClose]::EnumWindows($callback,[IntPtr]::Zero)
if(!$script:sent){throw 'No owned trial HWND accepted WM_CLOSE.'}
Write-Output "Requested normal close on $script:sent owned trial windows; observe the original managed process result."
