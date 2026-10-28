#if DEBUG
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileExplorer;

// Only the isolated local scenario can opt in. Layout and real routed actions
// still run; physical focus/input claims require the separate visible trial.
internal static class HiddenTrialPresentation
{
    static IntPtr foregroundHook;
    static IntPtr activationHook;
    static readonly HashSet<IntPtr> noninteractiveWindows = new();
    static readonly CbtEvent activationCallback = (code, window, details) =>
        code == 5 && noninteractiveWindows.Contains(window) ? new IntPtr(1) : CallNextHookEx(activationHook, code, window, details);
    static readonly HashSet<IntPtr> monitorHidden = new();
    static readonly HashSet<IntPtr> interactiveWindows = new();
    internal static string Phase { get; set; } = "startup";
    internal static List<object> ForegroundEvents { get; } = new();
    internal static List<object> ActivationMessages { get; } = new();
    static readonly ForegroundEvent foregroundCallback = (_, _, hwnd, _, _, _, nativeTime) =>
    {
        GetWindowThreadProcessId(hwnd, out uint process);
        if (process == Environment.ProcessId && !interactiveWindows.Contains(hwnd))
        {
            ++ForegroundViolations;
            ForegroundEvents.Add(new { phase = Phase, window = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => new WindowInteropHelper(w).Handle == hwnd)?.GetType().Name, hwnd = hwnd.ToInt64(), nativeTime, callbackTime = Environment.TickCount, currentForeground = GetForegroundWindow().ToInt64() });
        }
    };
    internal static int ForegroundViolations { get; private set; }
    internal static bool ForegroundObserverActive => foregroundHook != IntPtr.Zero;
    internal static bool IsOutsideDisplays(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) return false;
        var display = new System.Drawing.Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        return !display.IntersectsWith(System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
    }
    internal static bool Enabled => Environment.GetEnvironmentVariable("PATHWEFT_TEST_HIDDEN") == "1" &&
        Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s2-", StringComparison.OrdinalIgnoreCase) == true;
    internal static bool SuppressFocus(Window window) => Enabled && !(window is SearchWidgetWindow && Environment.GetEnvironmentVariable("PATHWEFT_S2_NATIVE_ONLY") == "1");

    internal static void Prepare(Window window)
    {
        if (!Enabled) return;
        if (foregroundHook == IntPtr.Zero) foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
        if (activationHook == IntPtr.Zero)
        {
            // WPF HwndTarget.OnDpiChanged calls SetWindowPos without
            // SWP_NOACTIVATE, even for a hidden HWND. WM_WINDOWPOSCHANGING is
            // too late to prevent that activation. Veto only this UI thread's
            // explicitly noninteractive test windows at HCBT_ACTIVATE.
            activationHook = SetWindowsHookEx(5, activationCallback, IntPtr.Zero, GetCurrentThreadId());
            if (activationHook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        // An off-screen owner's automatic activation on child close is also a
        // disruption. Test presentation owns closure explicitly instead.
        window.Owner = null;
        if (!SuppressFocus(window))
        {
            window.SourceInitialized += (_, _) => interactiveWindows.Add(new WindowInteropHelper(window).Handle);
            return;
        }
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 8192;
        window.Top = SystemParameters.VirtualScreenTop;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            noninteractiveWindows.Add(hwnd);
            window.Closed += (_, _) => { noninteractiveWindows.Remove(hwnd); monitorHidden.Remove(hwnd); };
            // Keep every native placement outside all displays, including later
            // Show/reveal/owner positioning. Transparency does not hide DWM frames.
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000080);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (message == 0x0006 && (w.ToInt64() & 0xFFFF) != 0)
                    ActivationMessages.Add(new { phase = Phase, window = window.GetType().Name, time = Environment.TickCount, stack = Environment.StackTrace });
                if (message == 0x0046 && l != IntPtr.Zero)
                {
                    var position = Marshal.PtrToStructure<WindowPosition>(l);
                    if (monitorHidden.Contains(h)) position.Flags = (position.Flags & ~0x0040u) | 0x0094u;
                    else
                    {
                        position.X = GetSystemMetrics(76) + GetSystemMetrics(78) + 8192;
                        position.Y = GetSystemMetrics(77);
                        position.Flags = (position.Flags & ~0x0002u) | 0x0014u;
                    }
                    Marshal.StructureToPtr(position, l, false);
                }
                return IntPtr.Zero;
            });
        };
    }
    internal static void AllowNativelyHiddenMonitorMoves(Window window)
    {
        if (!Enabled) throw new InvalidOperationException("Monitor checks require hidden presentation.");
        window.Hide();
        var handle = new WindowInteropHelper(window).Handle;
        monitorHidden.Add(handle);
    }
    internal static bool IsNativelyHidden(Window window) => !IsWindowVisible(new WindowInteropHelper(window).Handle);
    internal static void StopObserver()
    {
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        if (activationHook != IntPtr.Zero) UnhookWindowsHookEx(activationHook);
        foregroundHook = activationHook = IntPtr.Zero;
    }
    delegate IntPtr CbtEvent(int code, IntPtr window, IntPtr details);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, CbtEvent callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr window, IntPtr details);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    delegate void ForegroundEvent(IntPtr hook, uint kind, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, ForegroundEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [StructLayout(LayoutKind.Sequential)] struct WindowPosition { internal IntPtr Hwnd, After; internal int X, Y, Width, Height; internal uint Flags; }
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
}
#endif
