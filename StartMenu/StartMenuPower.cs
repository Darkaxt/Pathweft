using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FileExplorer.StartMenu;

/// SAB-02 (S6b): the power button and its menu, doing what Windows' own Start menu does: Shut down (with fast
/// startup when it is on), Restart, Sleep, Hibernate when available, Sign out, Lock and Switch user.
internal static class StartMenuPower
{
    internal static readonly (string Name, Action Run)[] Actions =
    [
        ("Switch user", () => WTSDisconnectSession(IntPtr.Zero, -1, false)),
        ("Sign out", () => ExitWindowsEx(0 /* EWX_LOGOFF */, Planned)),
        ("Lock", () => LockWorkStation()),
        ("Restart", () => Shutdown(0x4 /* SHUTDOWN_RESTART */)),
        ("Sleep", () => SetSuspendState(false, false, false)),
        ("Hibernate", () => SetSuspendState(true, false, false)),
        ("Shut down", () => Shutdown(0x8 /* SHUTDOWN_POWEROFF */ | (FastStartup ? 0x400u /* SHUTDOWN_HYBRID */ : 0))),
    ];

    internal static IEnumerable<(string Name, Action Run)> Available => Actions.Where(a => a.Name switch
    {
        "Hibernate" => IsPwrHibernateAllowed() && !HibernateHidden,
        "Sleep" => IsPwrSuspendAllowed(),
        _ => true,
    });

    const uint Planned = 0x80000000; // SHTDN_REASON_FLAG_PLANNED

    static bool FastStartup => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0) is 1;
    /// Windows' menu offers Hibernate only when the power options show it.
    static bool HibernateHidden => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings", "ShowHibernateOption", 0) is not 1;

    static void Shutdown(uint flags)
    {
        EnablePrivilege("SeShutdownPrivilege");
        InitiateShutdown(null, null, 0, flags, Planned);
    }

    static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) return;
        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid)) return;
            var state = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };
            AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint InitiateShutdown(string machine, string message, uint grace, uint flags, uint reason);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool LookupPrivilegeValue(string system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("wtsapi32.dll")] static extern bool WTSDisconnectSession(IntPtr server, int session, bool wait);
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("powrprof.dll")] static extern bool IsPwrHibernateAllowed();
    [DllImport("powrprof.dll")] static extern bool IsPwrSuspendAllowed();
}
