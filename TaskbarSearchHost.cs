// Taskbar-child attachment, Explorer notification and focus entry adapted from
// EverythingToolbar, MIT, Copyright (c) 2023 Stephan Rumswinkel.
// Timer-based attach/layout retries are deliberately replaced by actual shell events.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using FileExplorer.Search;

namespace FileExplorer;

internal sealed class TaskbarSearchHost : IDisposable
{
    readonly MainWindow main;
    readonly HwndSource source;
    readonly Native.WinEvent callback;
    readonly uint taskbarCreated;
    Window box;
    TextBox boxInput;
    IntPtr taskbar, eventHook;
    int generation;
    bool disposed, measuring, layoutPending, hotkey;
    internal SearchWidgetWindow Widget { get; private set; }
    internal string Status { get; private set; } = "Taskbar search is off.";
    internal event Action StatusChanged;
    internal event Action ExplorerRecreated;
    internal event Action SearchShown;
    internal Task LayoutCompletion { get; private set; } = Task.CompletedTask;
    internal bool BoxAttached => box != null;
    internal bool HotkeyRegistered => hotkey;

    internal TaskbarSearchHost(MainWindow owner)
    {
        main = owner;
        source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);
        taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        source.AddHook(Messages);
        callback = (_, _, _, _, _, _, _) => main.Dispatcher.BeginInvoke(RequestLayout);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }

    internal void ApplySettings()
    {
        if (disposed) return;
        ++generation;
        if (!Settings.TaskbarSearch)
        {
            if (hotkey) Native.UnregisterHotKey(source.Handle, 0x5057);
            hotkey = false;
            Unhook(); CloseBox();
            Widget?.Shutdown(); Widget = null;
            SetStatus("Taskbar search is off.");
            return;
        }
        if (!hotkey) hotkey = Native.RegisterHotKey(source.Handle, 0x5057, 0x4003, 0x20); // Ctrl+Alt+Space, no repeat
        RequestLayout();
    }

    internal void ShowSearch(string query = null)
    {
        if (!Settings.TaskbarSearch || disposed) return;
        Widget ??= new SearchWidgetWindow(main);
        Widget.ShowSearch(query);
        SearchShown?.Invoke();
    }

    void SetStatus(string reason)
    {
        Status = reason;
        StatusChanged?.Invoke();
    }

    string EntryStatus => hotkey ? "Ctrl+Alt+Space opens search while Pathweft is running." : "Ctrl+Alt+Space is unavailable (another application may own it). Use Open search in Settings.";

    internal void RequestLayout()
    {
        if (disposed || !Settings.TaskbarSearch) return;
        if (measuring) { layoutPending = true; return; }
        if (!Settings.TaskbarSearchBox)
        {
            CloseBox(); Unhook();
            SetStatus("Hotkey mode selected. " + EntryStatus);
            return;
        }
        var found = Native.FindWindow("Shell_TrayWnd", null);
        if (found != taskbar)
        {
            Unhook(); CloseBox(); taskbar = found;
            if (found != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(found, out uint process);
                eventHook = Native.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, callback, process, 0, 2);
            }
        }
        if (found == IntPtr.Zero || !Native.GetWindowRect(found, out var rect))
        {
            CloseBox(); SetStatus("Taskbar is unavailable; hotkey mode. " + EntryStatus); return;
        }
        if (Environment.OSVersion.Version.Build < 22000)
        {
            CloseBox(); SetStatus("This taskbar has no supported search-box host; hotkey mode. " + EntryStatus); return;
        }
        int revision = ++generation;
        measuring = true;
        LayoutCompletion = Measure(found, rect, revision);
    }

    async Task Measure(IntPtr handle, Native.RECT rect, int revision)
    {
        try
        {
            var obstacles = await Task.Run(() => TaskbarLayout.Measure(handle));
            if (disposed || revision != generation || handle != taskbar || !Settings.TaskbarSearchBox || !Settings.TaskbarSearch) return;
            if (!Native.GetWindowRect(handle, out var current) || current != rect) { layoutPending = true; return; }
            var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            var slot = TaskbarLayout.FindSlot(bounds, obstacles, Math.Max(96, Native.GetDpiForWindow(handle)) / 96.0);
            if (slot == null)
            {
                CloseBox(); SetStatus("No measurable 120-DIP taskbar space; hotkey mode. " + EntryStatus); return;
            }
            EnsureBox(handle);
            var hwnd = new WindowInteropHelper(box).Handle;
            var place = slot.Value;
            if (!Native.SetWindowPos(hwnd, IntPtr.Zero, (int)(place.Left - bounds.Left), (int)(place.Top - bounds.Top),
                (int)place.Width, (int)place.Height, 0x0014 | 0x0040)) throw new Win32Exception(Marshal.GetLastWin32Error());
            SetStatus("Taskbar search box attached. " + EntryStatus);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.Windows.Automation.ElementNotAvailableException or COMException)
        {
            CloseBox(); SetStatus("Taskbar search box could not attach: " + ex.Message + " Hotkey mode. " + EntryStatus);
        }
        finally
        {
            measuring = false;
            if (layoutPending && !disposed)
            {
                layoutPending = false;
                _ = main.Dispatcher.BeginInvoke(RequestLayout);
            }
        }
    }

    void EnsureBox(IntPtr parent)
    {
        if (box != null) return;
        boxInput = new TextBox { Text = "", ToolTip = "Search with Pathweft (Ctrl+Alt+Space)", Padding = new(8, 3, 8, 3),
            Foreground = MainWindow.Hex("#E8EEF6"), Background = MainWindow.Hex("#252830"), BorderBrush = MainWindow.Hex("#36FFFFFF"), FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(boxInput, "Pathweft taskbar search");
        boxInput.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { ShowSearch(boxInput.Text); e.Handled = true; } };
        boxInput.GotKeyboardFocus += (_, _) => ShowSearch(boxInput.Text.Length > 0 ? boxInput.Text : null);
        box = new Window { Title = "Pathweft taskbar search", Owner = main, ShowInTaskbar = false, ShowActivated = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Width = 200, Height = 32, Content = boxInput };
        var created = box;
#if DEBUG
        HiddenTrialPresentation.Prepare(created);
#endif
        created.Closed += (_, _) => { if (box == created) { box = null; boxInput = null; } };
        var hwnd = new WindowInteropHelper(created).EnsureHandle();
        var style = Native.GetWindowLong(hwnd, -16);
        Native.SetWindowLong(hwnd, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
        Marshal.SetLastPInvokeError(0);
        var previous = Native.SetParent(hwnd, parent);
        if (previous == IntPtr.Zero && Marshal.GetLastWin32Error() != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (Native.GetParent(hwnd) != parent) throw new InvalidOperationException("Windows did not retain the taskbar parent.");
        created.Show();
    }

    void CloseBox()
    {
        var previous = box; box = null; boxInput = null;
        previous?.Close();
    }
    void Unhook() { if (eventHook != IntPtr.Zero) Native.UnhookWinEvent(eventHook); eventHook = IntPtr.Zero; taskbar = IntPtr.Zero; }
    void DisplayChanged(object sender, EventArgs e) => main.Dispatcher.BeginInvoke(() => { RequestLayout(); if (Widget?.IsVisible == true) Widget.ConstrainToWorkArea(); });
    IntPtr Messages(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if ((uint)message == taskbarCreated) { Unhook(); CloseBox(); RequestLayout(); ExplorerRecreated?.Invoke(); }
        else if (message == 0x0312 && wparam == new IntPtr(0x5057)) { ShowSearch(); handled = true; }
        else if (message is 0x007E or 0x02E0) RequestLayout(); // display/DPI transitions
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; ++generation;
        if (hotkey) Native.UnregisterHotKey(source.Handle, 0x5057);
        hotkey = false; Unhook(); CloseBox();
        Widget?.Shutdown(); Widget = null;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        source.RemoveHook(Messages);
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal record struct RECT(int Left, int Top, int Right, int Bottom);
        internal delegate void WinEvent(IntPtr hook, uint kind, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr dll, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
