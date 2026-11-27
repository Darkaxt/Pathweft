using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FileExplorer.StartMenu;

/// SAB-02 and SAB-04 (S6a): Pathweft's own Start menu beside StartAllBack, in the windowless helper (Agent.cs).
/// A lone Win press, Ctrl+Esc and a press on a taskbar's Start orb open Pathweft's menu instead of StartAllBack's:
/// low-level keyboard and mouse hooks in this ordinary user process, the lone Win masked with the unassigned key 0xE8
/// so the shell sees a key combination and opens nothing. No Explorer injection, hook or patch and nothing of
/// StartAllBack's is touched: when this process exits, crashes or the setting is off, the hooks are gone and
/// StartAllBack's menu opens as before. Win key combinations pass through untouched.
internal static class StartMenuHost
{
    /// Our own injected keys carry this mark and pass the hook untouched ("PWFT").
    internal static readonly IntPtr Mark = new(0x50574654);
    static StartMenuWindow menu;
    static IntPtr mouseHook;
    static LowLevelProc mouseCallback;
    static bool winDown, winCombo, swallowEscUp, swallowOrbUp;
    static Dispatcher dispatcher;
    /// While the helper stops, the menu window really closes; otherwise closing it only hides it.
    internal static bool Stopping { get; private set; }
    internal static string StatePath => Path.Combine(Settings.Dir, "startmenu-state.json");
    internal sealed record State(bool Hooks, int Orbs, string Note, DateTime Checked);

    /// On the helper's UI thread, after its keyboard hook is in place.
    internal static bool Start()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        menu = new StartMenuWindow();
        menu.Prepare();
        mouseCallback = MouseHook;
        mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
        WriteState();
        return mouseHook != IntPtr.Zero;
    }

    internal static void Stop()
    {
        if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        Stopping = true; menu?.Close(); menu = null;
        try { File.Delete(StatePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// Whether Settings shows the menu as working: the helper's record of its hooks and of the orbs it found.
    internal static State ReadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// The orbs on every taskbar are looked up again when the taskbar is recreated (Explorer restart) and at each opening.
    internal static void WriteState()
    {
        int orbs = Orbs().Count;
        var state = new State(mouseHook != IntPtr.Zero, orbs,
            orbs == 0 ? "StartAllBack's Start button was not found: Win and Ctrl+Esc open the menu, the button opens StartAllBack's." : null, DateTime.UtcNow);
        try { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(StatePath, JsonSerializer.Serialize(state)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// From the helper's keyboard hook, before its Win+E handling. True: the event is consumed.
    internal static bool Keyboard(int vk, bool down, bool up, IntPtr extra)
    {
        if (extra == Mark) return false; // our own taps
        bool win = vk is 0x5B or 0x5C;
        if (win && down) { if (!winDown) winCombo = false; winDown = true; return false; }
        if (win && up)
        {
            winDown = false;
            if (winCombo) return false;
            // a lone Win: the shell sees Win+0xE8, a combination with no action, and its Win release
            Tap(0xE8, false, 0xE8, true, (ushort)vk, true);
            Open(Trigger.Win, null);
            return true;
        }
        if (down && winDown) { winCombo = true; return false; }
        if (down && vk == 0x1B && Held(0x11) && !Held(0x10) && !Held(0x12))
        {
            swallowEscUp = true; Tap(0xE8, false, 0xE8, true);
            Open(Trigger.CtrlEsc, null);
            return true;
        }
        if (up && vk == 0x1B && swallowEscUp) { swallowEscUp = false; return true; }
        return false;
    }

    static IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int m = message.ToInt32();
            if (m == 0x201) // left button down
            {
                var p = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
                if (OrbAt(p.X, p.Y) is { } orb)
                {
                    swallowOrbUp = true; Tap(0xE8, false, 0xE8, true);
                    Open(Trigger.Orb, orb);
                    return (IntPtr)1;
                }
            }
            else if (m == 0x202 && swallowOrbUp) { swallowOrbUp = false; return (IntPtr)1; }
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }

    internal enum Trigger { Win, CtrlEsc, Orb }
    /// The hook returns at once; the window work runs after it on the same thread.
    static void Open(Trigger trigger, Orb orb) => dispatcher.BeginInvoke(() => menu?.Toggle(trigger, orb ?? PrimaryOrb()));

    /// A taskbar's Start orb: StartAllBack's "Start" window inside the taskbar and the button window the taskbar owns
    /// over it share one rectangle. Matched by class and owner, not by its (localized) text.
    internal sealed record Orb(RECT Rect, RECT Taskbar, IntPtr TaskbarWindow);

    static Orb OrbAt(int x, int y)
    {
        var hit = WindowFromPoint(new POINT { X = x, Y = y });
        if (hit == IntPtr.Zero) return null;
        var root = GetAncestor(hit, 2);
        IntPtr tray = TrayOf(root);
        if (tray == IntPtr.Zero) return null; // not on a taskbar: nothing to look up
        foreach (var orb in OrbsOf(tray))
            if (x >= orb.Rect.Left && x < orb.Rect.Right && y >= orb.Rect.Top && y < orb.Rect.Bottom) return orb;
        return null;
    }

    /// The taskbar a window belongs to: the taskbar itself, or a window it owns (StartAllBack's orb button).
    static IntPtr TrayOf(IntPtr root)
    {
        if (IsTray(root)) return root;
        var owner = GetWindow(root, 4);
        return owner != IntPtr.Zero && IsTray(owner) ? owner : IntPtr.Zero;
    }
    static bool IsTray(IntPtr h) => ClassOf(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    internal static List<Orb> Orbs()
    {
        var all = new List<Orb>();
        foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            for (IntPtr tray = FindWindowEx(IntPtr.Zero, IntPtr.Zero, cls, null); tray != IntPtr.Zero; tray = FindWindowEx(IntPtr.Zero, tray, cls, null))
                all.AddRange(OrbsOf(tray));
        return all;
    }

    static List<Orb> OrbsOf(IntPtr tray)
    {
        var found = new List<Orb>();
        GetWindowRect(tray, out var bar);
        EnumChildWindows(tray, (h, _) =>
        {
            if (ClassOf(h) == "Start" && IsWindowVisible(h) && GetWindowRect(h, out var r) && r.Right > r.Left) { found.Add(new Orb(r, bar, tray)); return false; }
            return true;
        }, IntPtr.Zero);
        if (found.Count == 0)
            EnumWindows((h, _) =>
            {
                if (GetWindow(h, 4) == tray && ClassOf(h) == "Button" && IsWindowVisible(h) && GetWindowRect(h, out var r) && r.Right > r.Left) { found.Add(new Orb(r, bar, tray)); return false; }
                return true;
            }, IntPtr.Zero);
        return found;
    }

    /// Win and Ctrl+Esc open the menu at the main taskbar's orb, as Windows 7 did; with no orb, at the main taskbar's start.
    static Orb PrimaryOrb()
    {
        var tray = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;
        var orbs = OrbsOf(tray);
        if (orbs.Count > 0) return orbs[0];
        GetWindowRect(tray, out var bar);
        return new Orb(new RECT { Left = bar.Left, Top = bar.Top, Right = bar.Left, Bottom = bar.Bottom }, bar, tray);
    }

    internal static string ClassOf(IntPtr h)
    {
        var name = new StringBuilder(64);
        return GetClassName(h, name, name.Capacity) > 0 ? name.ToString() : "";
    }
    static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// Keys sent by this process, marked so the hook lets them through: pairs of key and whether it is a release.
    internal static void Tap(params object[] keys)
    {
        var input = new INPUT[keys.Length / 2];
        for (int i = 0; i < input.Length; i++)
        {
            input[i].type = 1;
            input[i].ki.wVk = Convert.ToUInt16(keys[i * 2]);
            input[i].ki.dwFlags = (bool)keys[i * 2 + 1] ? 2u : 0u;
            input[i].ki.dwExtraInfo = Mark;
        }
        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public int X, Y; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    delegate IntPtr LowLevelProc(int code, IntPtr message, IntPtr data);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc fn, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc fn, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc fn, IntPtr l);
}
