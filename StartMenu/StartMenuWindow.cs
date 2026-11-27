using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FileExplorer.StartMenu;

/// SAB-02 (S6a): the owned menu's window, at the orb that opened it, in StartAllBack's Win7 frame: the programs
/// column with All Programs and the search box, and the right column with the user's name. S6b fills both columns
/// and the power buttons; S6c and S6d follow StartAllBack's layout and visual style settings.
internal sealed class StartMenuWindow : Window
{
    readonly TextBox search = new() { MinHeight = 30, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock hint = new() { Text = "Search programs and files", IsHitTestVisible = false, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel programs = new(), places = new();
    IntPtr handle, previous;
    StartMenuHost.Trigger openedBy;
    internal bool IsOpen => IsVisible;
#if DEBUG
    internal StartMenuHost.Trigger OpenedByForVerification => openedBy;
#endif

    internal StartMenuWindow()
    {
        Title = "Start"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = true;
        Width = 400; Height = 535; Opacity = 0;
        AutomationProperties.SetName(this, "Pathweft Start menu");

        var left = new DockPanel { Margin = new Thickness(8, 8, 4, 8) };
        var searchHost = new Grid();
        searchHost.Children.Add(search); searchHost.Children.Add(hint);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        search.SetResourceReference(Control.BackgroundProperty, "Palette.0AFFFFFF");
        search.TextChanged += (_, _) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(search, "Search programs and files");
        DockPanel.SetDock(searchHost, Dock.Bottom); left.Children.Add(searchHost);
        var all = Row("All Programs", ""); all.Margin = new Thickness(0, 6, 0, 8);
        DockPanel.SetDock(all, Dock.Bottom); left.Children.Add(all);
        var programsCard = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Child = new ScrollViewer { Content = programs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        programsCard.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        programsCard.SetResourceReference(Border.BorderBrushProperty, "CardBorder"); programsCard.BorderThickness = new Thickness(1);
        left.Children.Add(programsCard);

        var right = new DockPanel { Margin = new Thickness(4, 8, 8, 8), Width = 150 };
        var user = new TextBlock { Text = Environment.UserName, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(10, 14, 6, 10), TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetName(user, "User name");
        DockPanel.SetDock(user, Dock.Top); right.Children.Add(user);
        right.Children.Add(places);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1); columns.Children.Add(left); columns.Children.Add(right);
        // an opaque base under Pathweft's tint: the window is layered, so nothing blurs the desktop behind it (S6d brings StartAllBack's glass)
        var tinted = new Border { CornerRadius = new CornerRadius(8), Background = MainWindow.Tint(), Child = columns };
        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = tinted,
            Background = new SolidColorBrush(Appearance.Light ? Color.FromArgb(0xF7, 0xF3, 0xF3, 0xF3) : Color.FromArgb(0xF7, 0x20, 0x20, 0x20)) };
        frame.SetResourceReference(Border.BorderBrushProperty, "AccentEdge");
        Content = frame;

        Deactivated += (_, _) => Dismiss(false);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(true); } };
        Closing += (_, e) => { if (!StartMenuHost.Stopping) { e.Cancel = true; Dismiss(false); } };
    }

    Button Row(string text, string glyph)
    {
        var row = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6, 8, 6) };
        row.SetResourceReference(StyleProperty, "Ghost");
        var content = new DockPanel();
        var arrow = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(arrow, Dock.Right); content.Children.Add(arrow);
        content.Children.Add(new TextBlock { Text = text });
        row.Content = content; AutomationProperties.SetName(row, text);
        return row;
    }

    /// Made once with the helper, hidden, so the first opening only places and shows it.
    internal void Prepare()
    {
        handle = new WindowInteropHelper(this).EnsureHandle();
        int ex = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, (ex | 0x80) & ~0x40000); // tool window, not app window: no taskbar button, no Alt+Tab
        // a new taskbar (Explorer restarted) has new orb windows: look them up again
        uint created = RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(handle).AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message == created) { Dismiss(false); StartMenuHost.WriteState(); }
            return IntPtr.Zero;
        });
    }

    internal void Toggle(StartMenuHost.Trigger trigger, StartMenuHost.Orb orb)
    {
        if (IsVisible) { Dismiss(trigger != StartMenuHost.Trigger.Orb); return; }
        openedBy = trigger;
        previous = GetForegroundWindow();
        search.Text = "";
        Place(orb);
        Opacity = 1;
        Show();
        BringToFront(handle);
        search.Focus();
        Keyboard.Focus(search);
        StartMenuHost.WriteState();
    }

    /// Esc and the Win key give the foreground back to the window that had it, as Windows' menu does; a click elsewhere
    /// has already moved it.
    void Dismiss(bool restore)
    {
        if (!IsVisible) return;
        Opacity = 0; Hide();
        if (restore && previous != IntPtr.Zero && IsWindow(previous)) SetForegroundWindow(previous);
        previous = IntPtr.Zero;
    }

    /// Device pixels on the orb's monitor: first onto that monitor so a DPI change rescales the window, then its corner
    /// against the orb on the taskbar's side, kept inside the monitor's work area.
    void Place(StartMenuHost.Orb orb)
    {
        var area = WorkArea(orb);
        SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0001 | 0x0004 | 0x0010); // no size, no z-order, no activate
        GetWindowRect(handle, out var now);
        int w = now.Right - now.Left, h = now.Bottom - now.Top;
        int x, y;
        if (orb == null) { x = area.Left; y = area.Bottom - h; }
        else
        {
            var bar = orb.Taskbar; var b = orb.Rect;
            bool vertical = bar.Bottom - bar.Top > bar.Right - bar.Left;
            if (!vertical && bar.Top > area.Top) { x = b.Left; y = bar.Top - h; }            // bottom
            else if (!vertical) { x = b.Left; y = bar.Bottom; }                              // top
            else if (bar.Left > area.Left) { x = bar.Left - w; y = b.Top; }                  // right
            else { x = bar.Right; y = b.Top; }                                               // left
        }
        x = Math.Max(area.Left, Math.Min(x, area.Right - w));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));
        SetWindowPos(handle, (IntPtr)(-1), x, y, 0, 0, 0x0001 | 0x0010); // topmost, no size, no activate
    }

    static StartMenuHost.RECT WorkArea(StartMenuHost.Orb orb)
    {
        var at = orb == null ? new POINT() : new POINT { X = (orb.Rect.Left + orb.Rect.Right) / 2, Y = (orb.Rect.Top + orb.Rect.Bottom) / 2 };
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(at, 2), ref info);
        return info.Work;
    }

    /// The hooks' input makes this process the last to have had input, which is what SetForegroundWindow asks; if
    /// Windows still refuses, one more unassigned-key tap from here, then attaching to the foreground thread.
    static void BringToFront(IntPtr h)
    {
        if (SetForegroundWindow(h) && GetForegroundWindow() == h) return;
        StartMenuHost.Tap(0xE8, false, 0xE8, true);
        if (SetForegroundWindow(h) && GetForegroundWindow() == h) return;
        uint other = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
        if (other == 0 || other == me) return;
        AttachThreadInput(me, other, true);
        SetForegroundWindow(h);
        AttachThreadInput(me, other, false);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public StartMenuHost.RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out StartMenuHost.RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
}
