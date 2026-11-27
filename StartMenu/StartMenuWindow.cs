using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileExplorer.StartMenu;

/// SAB-02 (S6a, S6b): the owned menu's window at the orb that opened it, in StartAllBack's Windows 7 layout. The
/// programs column holds the pinned and recent programs (with their jump lists), All Programs (a cascading flyout or
/// an in-place list, as StartAllBack is set) and the search box; the right column holds the user's picture and
/// name, the known folders, Computer, Control Panel, PC settings and the commands, and the power button. Typing
/// searches (SAB-03); folders open in Pathweft (SAB-06). S6c and S6d follow StartAllBack's layout and style settings.
internal sealed class StartMenuWindow : Window
{
    readonly TextBox search = new() { MinHeight = 30, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock hint = new() { Text = "Search programs and files", IsHitTestVisible = false, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel home = new(), results = new(), inPlace = new(), places = new(), jump = new();
    readonly ScrollViewer left = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly ScrollViewer right = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly Button allPrograms;
    readonly TextBlock allProgramsText = new();
    readonly Border picture = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(8), Margin = new Thickness(10, 10, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
    readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(Math.Max(1, SystemParameters.MenuShowDelay)) };
    const double MenuWidth = 440, MenuHeight = 535, LeftWidth = 262;
    IntPtr handle, previous;
    StartMenuHost.Trigger openedBy;
    StartMenuOptions options = StartMenuOptions.Read();
    List<Entry> pinned = [], recent = [], column = [];
    HashSet<Entry> withJumpList = [];
    Task<List<Entry>> programs;
    DateTime programsRead;
    CancellationTokenSource searching;
    Entry jumpOwner, hovered;
    Button jumpRow;
    readonly List<FileSystemWatcher> watchers = [];
    internal bool IsOpen => IsVisible;
#if DEBUG
    internal StartMenuHost.Trigger OpenedByForVerification => openedBy;
#endif

    internal StartMenuWindow()
    {
        Title = "Start"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = true;
        Width = MenuWidth; Height = MenuHeight; Opacity = 0;
        AutomationProperties.SetName(this, "Pathweft Start menu");

        var leftColumn = new DockPanel { Margin = new Thickness(8, 8, 4, 8), Width = LeftWidth };
        var searchHost = new Grid();
        searchHost.Children.Add(search); searchHost.Children.Add(hint);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        notice.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        search.SetResourceReference(Control.BackgroundProperty, "Palette.0AFFFFFF");
        search.TextChanged += (_, _) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Search(); };
        AutomationProperties.SetName(search, "Search programs and files");
        DockPanel.SetDock(searchHost, Dock.Bottom); leftColumn.Children.Add(searchHost);
        DockPanel.SetDock(notice, Dock.Bottom); leftColumn.Children.Add(notice);
        allPrograms = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 6) };
        allPrograms.SetResourceReference(StyleProperty, "Ghost");
        var allContent = new DockPanel();
        var arrow = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(arrow, Dock.Right); allContent.Children.Add(arrow);
        allContent.Children.Add(allProgramsText);
        allPrograms.Content = allContent;
        allPrograms.Click += (_, _) => AllProgramsClicked();
        allPrograms.MouseEnter += (_, _) => WhenMoved(allPrograms, () => { if (options.Flyout && options.AutoCascade) Hover(null, () => OpenFlyout()); });
        DockPanel.SetDock(allPrograms, Dock.Bottom); leftColumn.Children.Add(allPrograms);
        var programsCard = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Child = left };
        programsCard.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        programsCard.SetResourceReference(Border.BorderBrushProperty, "CardBorder"); programsCard.BorderThickness = new Thickness(1);
        leftColumn.Children.Add(programsCard);
        left.Content = home;

        var rightColumn = new DockPanel { Margin = new Thickness(4, 8, 8, 8) };
        var power = new DockPanel { Margin = new Thickness(6, 6, 0, 0) };
        var powerMenu = Small("", "Shut down options");
        powerMenu.Click += (_, _) => PowerMenu(powerMenu);
        DockPanel.SetDock(powerMenu, Dock.Right); power.Children.Add(powerMenu);
        var shutDown = new Button { Content = new TextBlock { Text = "Shut down" }, Padding = new Thickness(10, 5, 10, 5) };
        shutDown.SetResourceReference(StyleProperty, "Ghost");
        AutomationProperties.SetName(shutDown, "Shut down");
        shutDown.Click += (_, _) => { Dismiss(false); StartMenuPower.Actions.First(a => a.Name == "Shut down").Run(); };
        power.Children.Add(shutDown);
        DockPanel.SetDock(power, Dock.Bottom); rightColumn.Children.Add(power);
        picture.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        picture.MouseLeftButtonUp += (_, _) => Launch(new Entry("Your info", EntryKind.Shell, "ms-settings:yourinfo"));
        AutomationProperties.SetName(picture, "User picture");
        DockPanel.SetDock(picture, Dock.Top); rightColumn.Children.Add(picture);
        right.Content = places;
        rightColumn.Children.Add(right);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(rightColumn, 1); columns.Children.Add(leftColumn); columns.Children.Add(rightColumn);
        // an opaque base under Pathweft's tint: the window is layered, so nothing blurs the desktop behind it (S6d brings StartAllBack's glass)
        var tinted = new Border { CornerRadius = new CornerRadius(8), Background = MainWindow.Tint(), Child = columns };
        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = tinted,
            Background = new SolidColorBrush(Appearance.Light ? Color.FromArgb(0xF7, 0xF3, 0xF3, 0xF3) : Color.FromArgb(0xF7, 0x20, 0x20, 0x20)) };
        frame.SetResourceReference(Border.BorderBrushProperty, "AccentEdge");
        Content = frame;

        hover.Tick += (_, _) => { hover.Stop(); if (!Resting()) hoverAction?.Invoke(); };
        Deactivated += (_, _) => Dismiss(false);
        PreviewKeyDown += Key;
        PreviewMouseMove += (_, _) => Moved();
        PreviewTextInput += (_, e) =>
        {
            if (search.IsKeyboardFocusWithin || e.Text.Length == 0 || char.IsControl(e.Text[0])) return;
            search.Focus(); search.Text += e.Text; search.CaretIndex = search.Text.Length; e.Handled = true;
        };
        Closing += (_, e) => { if (!StartMenuHost.Stopping) { e.Cancel = true; Dismiss(false); } };
        Closed += (_, _) => { foreach (var w in watchers) w.Dispose(); };
    }

    Action hoverAction;
    void Hover(Entry over, Action action) { hovered = over; hoverAction = action; hover.Stop(); hover.Start(); }

    // Windows cascades on hover only after the pointer really moves: a menu opening under a resting pointer must not
    // open the flyout or a submenu (which would also take the keyboard from search)
    // WPF reports the row under the pointer as entered when the menu shows and when a popup closes, before or after
    // the events that could arm a guard, so the guard compares the real cursor position whenever an action is due.
    POINT? resting; UIElement restingOver; Action restingAction;
    void Rest() { GetCursorPos(out var p); resting = p; restingOver = null; restingAction = null; hover.Stop(); }
    bool Resting()
    {
        if (resting is not { } r) return false;
        if (GetCursorPos(out var p) && Math.Abs(p.X - r.X) < 2 && Math.Abs(p.Y - r.Y) < 2) return true;
        resting = null;
        return false;
    }
    void WhenMoved(UIElement over, Action action)
    {
        if (!Resting()) action();
        else { restingOver = over; restingAction = action; }
    }
    void Moved()
    {
        if (resting == null || Resting()) return;
        var (over, action) = (restingOver, restingAction);
        restingOver = null; restingAction = null;
        if (over?.IsMouseOver == true) action?.Invoke();
    }

    // ---- rows ----

    Button Row(Entry e, int iconSize, bool hasJump = false, bool submenu = false, Action click = null)
    {
        var row = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = e, ToolTip = e.Tip };
        row.SetResourceReference(StyleProperty, "Ghost");
        var content = new DockPanel();
        if (hasJump || submenu)
        {
            var arrow = new Border { Background = Brushes.Transparent, Width = 22, Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            arrow.MouseEnter += (_, _) => WhenMoved(arrow, () => { hover.Stop(); if (hasJump) ShowJump(e, row, false); else OpenSubmenu(e, row); });
            DockPanel.SetDock(arrow, Dock.Right); content.Children.Add(arrow);
        }
        var image = new Image { Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(image);
        var text = new TextBlock { Text = e.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (e.New) { text.FontWeight = FontWeights.SemiBold; row.SetResourceReference(BackgroundProperty, "AccentSoft"); }
        content.Children.Add(text);
        row.Content = content;
        if (iconSize >= 24) row.MinHeight = iconSize + 8;
        AutomationProperties.SetName(row, e.Name);
        LoadIcon(image, e, iconSize);
        row.Click += (_, _) => { if (click != null) click(); else Launch(e); };
        row.MouseEnter += (_, _) => WhenMoved(row, () =>
        {
            if (hasJump) Hover(e, () => ShowJump(e, row, false));
            else if (submenu) Hover(e, () => OpenSubmenu(e, row));
            else if (jumpOwner != null && IsInLeft(row)) Hover(e, HideJump);
            else hover.Stop();
        });
        row.ContextMenu = ItemMenu(e);
        return row;
    }

    bool IsInLeft(DependencyObject d) { for (; d != null; d = VisualTreeHelper.GetParent(d)) if (d == left) return true; return false; }

    async void LoadIcon(Image image, Entry e, int size)
    {
        int pixels = (int)Math.Round(size * VisualTreeHelper.GetDpi(this).DpiScaleX);
        try { image.Source = await StartMenuShell.Icon(e, pixels); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    Button Small(string glyph, string name)
    {
        var b = new Button { Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 }, Padding = new Thickness(6, 5, 6, 5) };
        b.SetResourceReference(StyleProperty, "Ghost");
        AutomationProperties.SetName(b, name);
        return b;
    }

    static FrameworkElement Line() { var b = new Border { Height = 1, Margin = new Thickness(8, 5, 8, 5) }; b.SetResourceReference(Border.BackgroundProperty, "Palette.1FFFFFFF"); return b; }

    TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 8, 8, 3) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        return t;
    }

    /// Windows 7's item menu, in Pathweft's style: Open, Run as administrator, Open file location (in Pathweft), Pin to
    /// or Unpin from Start menu, Remove from this list for a recent program, and Properties.
    ContextMenu ItemMenu(Entry e)
    {
        if (e.Kind == EntryKind.Command) return null;
        var menu = new ContextMenu();
        menu.Closed += (_, _) => Rest();
        void Add(string header, Action run) { var item = new MenuItem { Header = header }; AutomationProperties.SetName(item, header); item.Click += (_, _) => run(); menu.Items.Add(item); }
        // the lists are read again at once, so the change shows while the menu is open
        void Change(Action change)
        {
            try { change(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Notice(ex.Message); }
            _ = Refresh();
        }
        Add("Open", () => Launch(e));
        bool file = e.Target != null && e.Target.Length > 2 && (e.Target[1] == ':' || e.Target.StartsWith(@"\\"));
        if (e.Kind == EntryKind.Program && file)
            Add("Run as administrator", () => { Dismiss(false); _ = ShellWorker.Run(() => StartMenuShell.Execute(e.Target, e.Arguments, e.WorkingDirectory, null, true, "runas")); });
        // a shortcut's file location is its program's, as the Windows menu has it
        if (file) Add("Open file location", () =>
        {
            Dismiss(false);
            string at = e.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && StartMenuShell.ReadShortcut(e.Target)?.Path is { } program && File.Exists(program) ? program : e.Target;
            StartMenuShell.Route(at.TrimEnd('\\'));
        });
        if (e.Kind == EntryKind.Program && file)
        {
            menu.Items.Add(new Separator());
            if (pinned.Contains(e)) Add("Unpin from Start menu", () => Change(() => StartMenuItems.Unpin(e)));
            else if (!StartMenuItems.IsPinned(e)) Add("Pin to Start menu", () => Change(() => StartMenuItems.Pin(e)));
            if (recent.Contains(e)) Add("Remove from this list", () => Change(() => StartMenuItems.ForgetRecent(e)));
        }
        if (file)
        {
            menu.Items.Add(new Separator());
            Add("Properties", () => { Dismiss(false); StartMenuShell.Properties(e.Target); });
        }
        return menu;
    }

    // ---- contents ----

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
        // a program installed or removed is in All Programs and in search at the next opening
        foreach (var root in StartMenuItems.ProgramRoots.Where(Directory.Exists))
        {
            var w = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            w.Created += (_, _) => Forget(); w.Deleted += (_, _) => Forget(); w.Renamed += (_, _) => Forget();
            w.EnableRaisingEvents = true;
            watchers.Add(w);
        }
        _ = Refresh();
    }

    void Forget() { programs = null; StartMenuSearch.Forget(); }

    /// At each opening: StartAllBack's settings, the pinned and recent programs and the right column, read again.
    async Task Refresh()
    {
        try
        {
            options = StartMenuOptions.Read();
            if (programs == null || DateTime.UtcNow - programsRead > TimeSpan.FromMinutes(5)) Forget(); // packaged apps have no folder to watch
            var o = options;
            var (p, r, c, jumps, user) = await ShellWorker.Run(() =>
            {
                var p = StartMenuItems.Pinned();
                var r = StartMenuItems.Recent(o.RecentPrograms, p);
                var jumps = p.Concat(r).Where(e => StartMenuShell.HasJumpList(e.Owner)).ToHashSet();
                return (p, r, StartMenuItems.RightColumn(o), jumps, StartMenuItems.UserPicture());
            });
            pinned = p; recent = r; column = c; withJumpList = jumps;
            BuildHome(); BuildPlaces(user);
            // All Programs is highlighted while it holds a new program, as Windows 7's menu does
            var all = await Programs();
            if (StartMenuItems.Flatten(all).Any(x => x.New)) allPrograms.SetResourceReference(BackgroundProperty, "AccentSoft"); else allPrograms.ClearValue(BackgroundProperty);
            _ = StartMenuSearch.Load(o);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Notice("The menu could not be read: " + ex.Message);
        }
    }

    void BuildHome()
    {
        home.Children.Clear();
        foreach (var e in pinned) home.Children.Add(Row(e, 32, withJumpList.Contains(e)));
        if (pinned.Count > 0 && recent.Count > 0) home.Children.Add(Line());
        foreach (var e in recent) home.Children.Add(Row(e, 32, withJumpList.Contains(e)));
        allProgramsText.Text = "All Programs";
        allProgramsText.FontWeight = FontWeights.Normal;
    }

    void BuildPlaces(string user)
    {
        places.Children.Clear();
        foreach (var e in column)
        {
            if (ReferenceEquals(e, StartMenuItems.Separator)) { places.Children.Add(Line()); continue; }
            var row = Row(e, options.RightPaneIcons ? 16 : 0, submenu: e.Children != null);
            if (e.Tip == "Personal folder") ((TextBlock)((DockPanel)row.Content).Children[^1]).FontWeight = FontWeights.SemiBold;
            places.Children.Add(row);
        }
        picture.Child = null;
        if (user != null)
            try
            {
                var image = new BitmapImage();
                image.BeginInit(); image.UriSource = new Uri(user); image.DecodePixelWidth = 96; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit();
                picture.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException) { }
    }

    // ---- All Programs ----

    Task<List<Entry>> Programs()
    {
        if (programs == null) { var o = options; programs = ShellWorker.Run(() => StartMenuItems.AllPrograms(o)); programsRead = DateTime.UtcNow; }
        return programs;
    }

    void AllProgramsClicked()
    {
        hover.Stop();
        if (left.Content == inPlace || left.Content == results && search.Text.Length > 0) { search.Text = ""; ShowHome(); return; }
        if (options.Flyout) OpenFlyout(); else _ = ShowInPlace();
    }

    ContextMenu flyout;

    /// StartAllBack's cascading All Programs: a menu beside the programs column, each folder a submenu.
    async void OpenFlyout()
    {
        if (flyout?.IsOpen == true) return;
        var list = await Programs();
        if (!IsVisible) return;
        flyout = new ContextMenu { PlacementTarget = allPrograms, Placement = PlacementMode.Right };
        foreach (var e in list) flyout.Items.Add(MenuItemFor(e, openFolders: false));
        Cascade(flyout);
        flyout.IsOpen = true;
    }

    /// The in-place list: All Programs replaces the pinned and recent programs; a folder opens below itself.
    async Task ShowInPlace()
    {
        var list = await Programs();
        inPlace.Children.Clear();
        foreach (var e in list) inPlace.Children.Add(InPlaceRow(e, 0));
        left.Content = inPlace;
        allProgramsText.Text = "Back";
        left.ScrollToTop();
    }

    Button InPlaceRow(Entry e, int depth)
    {
        bool open = false;
        Button row = null;
        row = Row(e, 16, click: e.Children == null ? null : Toggle);
        row.Margin = new Thickness(depth * 14, 0, 0, 0);
        if (e.Children == null) return row;
        async void Toggle()
        {
            int at = inPlace.Children.IndexOf(row) + 1;
            if (open)
            {
                // the folder's rows, and the rows of its open subfolders, are the deeper ones below it
                while (at < inPlace.Children.Count && inPlace.Children[at] is FrameworkElement f && f.Margin.Left > row.Margin.Left) inPlace.Children.RemoveAt(at);
                open = false; return;
            }
            open = true;
            var children = await ShellWorker.Run(e.Children);
            at = inPlace.Children.IndexOf(row) + 1;
            if (at == 0 || !open) return;
            foreach (var c in children) inPlace.Children.Insert(at++, InPlaceRow(c, depth + 1));
        }
        row.PreviewKeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Right && !open || args.Key == System.Windows.Input.Key.Left && open) { args.Handled = true; Toggle(); } };
        return row;
    }

    void ShowHome()
    {
        left.Content = home;
        allProgramsText.Text = "All Programs";
        HideJump();
    }

    MenuItem MenuItemFor(Entry e, bool openFolders)
    {
        var item = new MenuItem { Header = e.Name, ToolTip = e.Tip };
        if (e.New) { item.FontWeight = FontWeights.SemiBold; item.SetResourceReference(BackgroundProperty, "AccentSoft"); }
        var image = new Image { Width = 16, Height = 16 };
        item.Icon = image;
        LoadIcon(image, e, 16);
        AutomationProperties.SetName(item, e.Name);
        if (e.Children == null)
        {
            if (e.Kind != EntryKind.Command) { item.Click += (_, _) => Launch(e); item.ContextMenu = ItemMenu(e); }
            else item.IsEnabled = e.Invoke != null;
            return item;
        }
        item.Items.Add(new MenuItem { Header = "Loading…", IsEnabled = false });
        bool filled = false;
        item.SubmenuOpened += async (_, args) =>
        {
            if (filled || args.OriginalSource != item) return;
            filled = true;
            var children = await ShellWorker.Run(e.Children);
            item.Items.Clear();
            foreach (var c in children) item.Items.Add(MenuItemFor(c, openFolders));
        };
        // a folder of the right column's menus opens when clicked, as in Windows; All Programs' folders only cascade
        if (openFolders && e.Target != null)
            item.PreviewMouseLeftButtonUp += (_, args) => { if (args.OriginalSource is DependencyObject d && IsHeaderOf(item, d)) { args.Handled = true; Launch(e); } };
        return item;
    }

    static bool IsHeaderOf(MenuItem item, DependencyObject d)
    {
        for (; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is MenuItem m) return m == item;
            if (d is Popup) return false;
        }
        return false;
    }

    ContextMenu submenu;

    /// A right-column item shown as a menu (StartAllBack's "display as a menu"): its contents cascade beside it.
    async void OpenSubmenu(Entry e, Button row)
    {
        if (submenu?.IsOpen == true && submenu.PlacementTarget == row) return;
        if (submenu != null) submenu.IsOpen = false;
        var children = await ShellWorker.Run(e.Children);
        if (!IsVisible || !row.IsMouseOver && !row.IsKeyboardFocusWithin) return;
        submenu = new ContextMenu { PlacementTarget = row, Placement = PlacementMode.Right };
        foreach (var c in children) submenu.Items.Add(MenuItemFor(c, openFolders: true));
        Cascade(submenu);
        submenu.IsOpen = true;
    }

    /// An open menu holds the mouse, so the menu's own rows see no hover while it is open. As in Windows' cascading
    /// menus, moving onto another of them closes it, and that row's hover takes over.
    void Cascade(ContextMenu menu)
    {
        // only a real move: the menu taking the mouse reports where it rests, which may be over another row
        Point? from = Mouse.GetPosition(this);
        bool switching = false; // closed by moving onto another row: that row's hover goes on
        menu.Closed += (_, _) => { if (!switching) Rest(); };
        menu.PreviewMouseMove += (_, a) =>
        {
            if (a.OriginalSource != menu || !IsVisible) return; // over the menu or one of its submenus
            var at = a.GetPosition(this);
            if (from is { } start && Math.Abs(at.X - start.X) < 2 && Math.Abs(at.Y - start.Y) < 2) return;
            from = null;
            if (InputHitTest(at) is not DependencyObject over) return;
            for (; over != null && over is not Button; over = VisualTreeHelper.GetParent(over)) { }
            if (over is Button row && row != menu.PlacementTarget && (row.Tag is Entry || row == allPrograms)) { switching = true; menu.IsOpen = false; }
        };
    }

    void PowerMenu(Button from)
    {
        var menu = new ContextMenu { PlacementTarget = from, Placement = PlacementMode.Top };
        menu.Closed += (_, _) => Rest();
        foreach (var (name, run) in StartMenuPower.Available)
        {
            var item = new MenuItem { Header = name };
            AutomationProperties.SetName(item, name);
            item.Click += (_, _) => { Dismiss(false); run(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ---- jump lists ----

    async void ShowJump(Entry e, Button row, bool focus)
    {
        if (jumpOwner == e) { if (focus) FirstRow(jump)?.Focus(); return; }
        jumpOwner = e; jumpRow = row;
        var categories = await ShellWorker.Run(() => StartMenuShell.JumpList(e.Owner, e.Target));
        if (jumpOwner != e) return;
        jump.Children.Clear();
        foreach (var c in categories)
        {
            jump.Children.Add(Header(c.Name));
            foreach (var item in c.Items) jump.Children.Add(Row(item, 16));
        }
        if (categories.Count == 0) jump.Children.Add(new TextBlock { Text = "Nothing here yet.", Margin = new Thickness(8) });
        right.Content = jump;
        right.ScrollToTop();
        // from the keyboard (Right), into the list once it is there
        if (focus) Dispatcher.BeginInvoke(() => FirstRow(jump)?.Focus(), DispatcherPriority.Loaded);
    }

    void HideJump()
    {
        if (jumpOwner == null) return;
        jumpOwner = null; jumpRow = null;
        right.Content = places;
    }

    // ---- search (SAB-03) ----

    async void Search()
    {
        searching?.Cancel();
        HideJump(); Notice(null);
        string text = search.Text;
        if (text.Trim().Length == 0) { ShowHome(); return; }
        var cancel = searching = new CancellationTokenSource();
        var token = cancel.Token;
        await StartMenuSearch.Load(options);
        if (token.IsCancellationRequested) return;
        results.Children.Clear();
        left.Content = results;
        allProgramsText.Text = "Back";
        int shown = 0;
        foreach (var g in new[] { StartMenuSearch.Programs(text), StartMenuSearch.Settings(text) })
            if (g is { Total: > 0 }) shown += AddGroup(g);
        var pending = new TextBlock { Text = "Searching files and folders…", Margin = new Thickness(8, 6, 8, 6) };
        pending.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        results.Children.Add(pending);
        var more = Row(new Entry("See more results", EntryKind.Command, null) { Invoke = () => { Dismiss(false); StartMenuShell.Route(MainWindow.SearchRequest + text.Trim()); } }, 0);
        results.Children.Add(Line()); results.Children.Add(more);
        List<StartMenuSearch.Group> files; string problem = null;
        try { files = await StartMenuSearch.FilesAsync(text, token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException) { files = null; problem = ex.Message; }
        if (token.IsCancellationRequested) return;
        int at = results.Children.IndexOf(pending);
        results.Children.Remove(pending);
        if (files == null)
        {
            var t = new TextBlock { Text = "Files and folders were not searched: " + (problem ?? "the Pathweft index is not answering."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 6) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            results.Children.Insert(at, t);
            if (shown == 0) results.Children.Insert(at, new TextBlock { Text = "No programs or settings match your search.", Margin = new Thickness(8, 6, 8, 0) });
            return;
        }
        var groupPanel = new StackPanel();
        foreach (var g in files) shown += AddGroup(g, groupPanel);
        results.Children.Insert(at, groupPanel);
        if (shown == 0) results.Children.Insert(at, new TextBlock { Text = "No items match your search.", Margin = new Thickness(8, 6, 8, 6) });
    }

    int AddGroup(StartMenuSearch.Group g, Panel to = null)
    {
        to ??= results;
        to.Children.Add(Header($"{g.Name} ({g.Total:N0})"));
        int n = 0;
        foreach (var e in g.Items.Take(StartMenuSearch.PerGroup)) { to.Children.Add(Row(e, 16)); n++; }
        if (g.Notice != null) { var t = new TextBlock { Text = g.Notice, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 4), FontSize = 11 }; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); to.Children.Add(t); }
        return n;
    }

    void Notice(string text)
    {
        notice.Text = text ?? "";
        notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    async void Launch(Entry e)
    {
        if (e.Kind == EntryKind.Command && e.Invoke == null) return;
        if (e.Kind == EntryKind.Command) { e.Invoke(); return; }
        if (e.Children != null && e.Target == null) return;
        string problem = await StartMenuShell.Open(e);
        if (problem == null) Dismiss(false); else Notice(problem);
    }

    // ---- keyboard ----

    void Key(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Escape:
                e.Handled = true;
                if (search.Text.Length > 0) { search.Text = ""; search.Focus(); }
                else if (jumpOwner != null) { var row = jumpRow; HideJump(); row?.Focus(); }
                else if (left.Content != home) { ShowHome(); search.Focus(); }
                else Dismiss(true);
                break;
            case System.Windows.Input.Key.Enter when search.IsKeyboardFocusWithin:
                e.Handled = true;
                if (FirstRow(left.Content as Panel) is { } first) first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                break;
            case System.Windows.Input.Key.Down when search.IsKeyboardFocusWithin:
                e.Handled = true; FirstRow(left.Content as Panel)?.Focus();
                break;
            case System.Windows.Input.Key.Up when search.IsKeyboardFocusWithin:
                e.Handled = true; LastRow(left.Content as Panel)?.Focus();
                break;
            case System.Windows.Input.Key.Right when focused is Button { Tag: Entry entry } row && withJumpList.Contains(entry):
                e.Handled = true; ShowJump(entry, row, true);
                break;
            case System.Windows.Input.Key.Right when focused is Button { Tag: Entry entry } row && entry.Children != null && !IsInLeft(row):
                e.Handled = true; OpenSubmenu(entry, row);
                break;
            case System.Windows.Input.Key.Left when jumpOwner != null && focused != null && IsIn(focused, jump):
                e.Handled = true; var owner = jumpRow; HideJump(); owner?.Focus();
                break;
            case System.Windows.Input.Key.Down or System.Windows.Input.Key.Up when focused is Button:
                e.Handled = true;
                (focused as UIElement)?.MoveFocus(new TraversalRequest(e.Key == System.Windows.Input.Key.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up));
                break;
        }
    }

    static bool IsIn(DependencyObject d, DependencyObject panel) { for (; d != null; d = VisualTreeHelper.GetParent(d)) if (d == panel) return true; return false; }
    static Button FirstRow(Panel p) => p == null ? null : Rows(p).FirstOrDefault();
    static Button LastRow(Panel p) => p == null ? null : Rows(p).LastOrDefault();
    static IEnumerable<Button> Rows(Panel p)
    {
        foreach (var child in p.Children)
            if (child is Button b) yield return b;
            else if (child is Panel inner) foreach (var x in Rows(inner)) yield return x;
    }

    // ---- opening and placing (S6a) ----

    internal void Toggle(StartMenuHost.Trigger trigger, StartMenuHost.Orb orb)
    {
        if (IsVisible) { Dismiss(trigger != StartMenuHost.Trigger.Orb); return; }
        openedBy = trigger;
        previous = GetForegroundWindow();
        search.Text = "";
        Notice(null); ShowHome();
        Place(orb);
        Opacity = 1;
        Rest(); // before Show: showing reports the row under the pointer as entered
        Show();
        BringToFront(handle);
        search.Focus();
        Keyboard.Focus(search);
        StartMenuHost.WriteState();
        _ = Refresh();
    }

    /// Esc and the Win key give the foreground back to the window that had it, as Windows' menu does; a click elsewhere
    /// has already moved it.
    void Dismiss(bool restore)
    {
        if (!IsVisible) return;
        hover.Stop();
        searching?.Cancel();
        foreach (var m in new[] { flyout, submenu }) if (m != null) m.IsOpen = false;
        Opacity = 0; Hide();
        HideJump();
        if (restore && previous != IntPtr.Zero && IsWindow(previous)) SetForegroundWindow(previous);
        previous = IntPtr.Zero;
    }

    /// Device pixels on the orb's monitor: first onto that monitor so a DPI change rescales the window, then its corner
    /// against the orb on the taskbar's side, kept inside the monitor's work area.
    void Place(StartMenuHost.Orb orb)
    {
        // as tall as the pinned and recent programs need, never less than Windows 7's menu
        Width = MenuWidth;
        home.Measure(new Size(LeftWidth, double.PositiveInfinity));
        places.Measure(new Size(MenuWidth - LeftWidth, double.PositiveInfinity));
        Height = Math.Max(MenuHeight, Math.Max(home.DesiredSize.Height + 130, places.DesiredSize.Height + 130));
        var area = WorkArea(orb);
        SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0001 | 0x0004 | 0x0010); // no size, no z-order, no activate
        GetWindowRect(handle, out var now);
        // never taller or wider than the space beside the taskbar, so it never covers the orb that closes it
        int w = Math.Min(now.Right - now.Left, area.Right - area.Left), h = Math.Min(now.Bottom - now.Top, area.Bottom - area.Top);
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
        SetWindowPos(handle, (IntPtr)(-1), x, y, w, h, 0x0010); // topmost, no activate
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
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
}
