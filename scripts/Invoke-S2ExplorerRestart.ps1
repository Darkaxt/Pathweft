[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [switch]$Restart)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s2-[^\\]+$') { throw 'Use registered S2 native scratch.' }
# Inspection is the default. Restart is only for the coordinated real shell test.
# No force-shutdown, broad process stop, elevation or direct Explorer launch.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class S2RestartManager {
    [StructLayout(LayoutKind.Sequential)] public struct FileTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] public struct UniqueProcess { public uint Pid; public FileTime Started; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct ProcessInfo {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Service;
        public int Type;
        public uint Status, Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    public sealed class Report {
        public ProcessInfo[] Affected;
        public uint RebootReasons;
        public uint? ShutdownCode, RestartCode;
        public uint EndCode;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("rstrtmgr.dll", CharSet=CharSet.Unicode)] static extern uint RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet=CharSet.Unicode)] static extern uint RmRegisterResources(uint session, uint files, string[] names, uint applications, UniqueProcess[] processes, uint services, string[] serviceNames);
    [DllImport("rstrtmgr.dll")] static extern uint RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[] processes, out uint reasons);
    [DllImport("rstrtmgr.dll")] static extern uint RmShutdown(uint session, uint flags, IntPtr status);
    [DllImport("rstrtmgr.dll")] static extern uint RmRestart(uint session, uint flags, IntPtr status);
    [DllImport("rstrtmgr.dll")] static extern uint RmEndSession(uint session);
    static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
    public static Report Run(uint pid, long started, bool restart) {
        Check(RmStartSession(out uint session, 0, new StringBuilder(33)));
        var report = new Report();
        try {
            var target = new UniqueProcess { Pid=pid, Started=new FileTime { Low=(uint)started, High=(uint)((ulong)started >> 32) } };
            Check(RmRegisterResources(session, 0, null, 1, new[] { target }, 0, null));
            uint count=0, needed, reasons;
            ProcessInfo[] list=null;
            uint status;
            // ERROR_MORE_DATA is the API's buffer-size negotiation, not a timed retry.
            while ((status=RmGetList(session, out needed, ref count, list, out reasons)) == 234) {
                list=new ProcessInfo[needed]; count=needed;
            }
            Check(status);
            Array.Resize(ref list, (int)count);
            report.Affected=list; report.RebootReasons=reasons;
            if (!restart) return report;
            if (reasons != 0 || list.Length != 1 || list[0].Process.Pid != pid || list[0].Type != 4 || !list[0].Restartable)
                throw new InvalidOperationException("Restart Manager did not identify exactly one restartable Explorer process.");
            report.ShutdownCode=RmShutdown(session, 0x10, IntPtr.Zero); // registered-for-restart applications only
            report.RestartCode=RmRestart(session, 0, IntPtr.Zero); // required even after a partial shutdown error
            return report;
        }
        finally { report.EndCode=RmEndSession(session); }
    }
}
'@
[uint32]$shellPid = 0
[void][S2RestartManager]::GetWindowThreadProcessId([S2RestartManager]::GetShellWindow(), [ref]$shellPid)
if ($shellPid -eq 0) { throw 'No current shell process was identified.' }
$explorer = Get-Process -Id $shellPid
if ($explorer.Path -ne (Join-Path $env:WINDIR 'explorer.exe')) { throw 'Shell executable identity differs from Windows Explorer.' }
if ($Restart) {
    $ready = Get-Content -LiteralPath (Join-Path $scratch 'fixture/shell-ready.json') -Raw | ConvertFrom-Json
    $trial = Get-Process -Id $ready.pid
    if ($trial.Path -ne (Join-Path $scratch 'artifacts/bin/FileExplorer/debug/Pathweft.exe') -or $ready.phase -ne 'waiting-for-real-Explorer-restart' -or $trial.StartTime.ToUniversalTime().Ticks -ne $ready.startTicks) {
        throw 'The actual S2 receiver is not ready.'
    }
    $shell = New-Object -ComObject Shell.Application
    $folders = @($shell.Windows() | ForEach-Object { [ordered]@{ location=$_.LocationURL; hwnd=$_.HWND } })
    $folders | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'shell-folders-before.json') -Encoding utf8
}
$report = [S2RestartManager]::Run($shellPid, $explorer.StartTime.ToFileTime(), $Restart.IsPresent)
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $scratch 'explorer-restart-manager.json') -Encoding utf8
[ordered]@{ shellPid=$shellPid; inspectionOnly=(!$Restart); affected=@($report.Affected | Select-Object @{n='pid';e={$_.Process.Pid}},Type,Restartable); rebootReasons=$report.RebootReasons; shutdownCode=$report.ShutdownCode; restartCode=$report.RestartCode; sessionEnd=$report.EndCode } | ConvertTo-Json -Depth 5
if ($report.EndCode -ne 0 -or ($Restart -and ($report.ShutdownCode -ne 0 -or $report.RestartCode -ne 0))) { throw 'Restart Manager reported an incomplete operation; inspect the retained exact result.' }
