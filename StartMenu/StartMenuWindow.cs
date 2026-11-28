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
/// searches (SAB-03); folders open in Pathweft (SAB-06). S6c follows StartAllBack's layout and contents settings
/// (icon sizes, sort order, glyphs, user picture, power button, search, recent items), live; S6d its visual styles
/// (StartMenuSkin): the window is the panel itself, over Windows' glass, with the user picture in its own window above.
internal sealed class StartMenuWindow : Window
{
    readonly TextBox search = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
    readonly TextBlock hint = new() { Text = "Search programs and files", IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock lens = new() { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border lensHost = new() { Width = 24, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
    ThemePart searchGlyph; // a theme's own search glyph (Windows 7: at the box's right), else the lens glyph at its left
    readonly SearchFrame searchFrame = new();
    readonly Grid searchHost = new();
    readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel home = new(), results = new(), inPlace = new(), places = new(), jump = new();
    readonly ScrollViewer left = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly ScrollViewer right = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly DockPanel leftArea = new();
    readonly Grid panel = new() { ClipToBounds = true };
    readonly StackPanel power = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    SkinButton allPrograms, powerButton, powerMenu;
    readonly TextBlock allProgramsText = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ContentControl allProgramsArrow = new() { VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false };
    readonly TextBlock allProgramsLens = new() { Text = "\uE721", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly StartMenuPicture picture = new();
    readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(Math.Max(1, SystemParameters.MenuShowDelay)) };
    const double PlaceRow = 35;
    StartMenuSkin skin;
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
    readonly TextBlock powerText = new() { VerticalAlignment = VerticalAlignment.Center };
    StartMenuSettingsWatcher settingsWatcher;
    BitmapSource userPicture;
    internal bool IsOpen => IsVisible;
#if DEBUG
    internal StartMenuHost.Trigger OpenedByForVerification => openedBy;
#endif

    internal StartMenuWindow()
    {
        Title = "Start"; WindowStyle = WindowStyle.None; AllowsTransparency = false; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = true;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
        AutomationProperties.SetName(this, "Pathweft Start menu");
        skin = StartMenuSkin.For(options);

        // the search box: the frame drawn behind a bare text box, the lens (a clear button while there is text) at its left
        search.Template = BareTextBox();
        search.TextChanged += (_, _) =>
        {
            bool empty = search.Text.Length == 0;
            hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ShowLens();
            Search();
        };
        search.GotKeyboardFocus += (_, _) => { SelectFirst(); searchFrame.Show(searchHost.IsMouseOver, true); };
        search.LostKeyboardFocus += (_, _) => { SelectFirst(); searchFrame.Show(searchHost.IsMouseOver, false); };
        searchHost.MouseEnter += (_, _) => searchFrame.Show(true, search.IsKeyboardFocusWithin);
        searchHost.MouseLeave += (_, _) => searchFrame.Show(false, search.IsKeyboardFocusWithin);
        lensHost.MouseLeftButtonUp += (_, _) => { if (search.Text.Length > 0) { search.Text = ""; search.Focus(); } };
        AutomationProperties.SetName(search, "Search programs and files");
        searchHost.Children.Add(searchFrame); searchHost.Children.Add(search); searchHost.Children.Add(hint); searchHost.Children.Add(lensHost);

        // All Programs, which during a search is "See more results"
        allPrograms = new SkinButton(SkinButton.Area.AllPrograms, skin);
        var allContent = new DockPanel();
        DockPanel.SetDock(allProgramsArrow, Dock.Right); allContent.Children.Add(allProgramsArrow);
        allContent.Children.Add(allProgramsLens);
        allContent.Children.Add(allProgramsText);
        allPrograms.Content = allContent;
        allPrograms.Click += (_, _) => AllProgramsClicked();
        allPrograms.MouseEnter += (_, _) => WhenMoved(allPrograms, () => { if (options.Flyout && options.AutoCascade && !Searching) Hover(null, () => OpenFlyout()); });

        // the left column: the programs (or All Programs in place, or the search results) and a notice under them
        DockPanel.SetDock(notice, Dock.Bottom); leftArea.Children.Add(notice);
        leftArea.Children.Add(left);
        left.Content = home;

        // StartAllBack's power button: its action, the options arrow right beside it
        powerMenu = new SkinButton(SkinButton.Area.SplitArrow, skin) { Width = 22, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(powerMenu, "Shut down options");
        powerMenu.Click += (_, _) => PowerMenu(powerMenu);
        var powerContent = new StackPanel { Orientation = Orientation.Horizontal };
        powerContent.Children.Add(new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        powerContent.Children.Add(powerText);
        powerButton = new SkinButton(SkinButton.Area.SignOut, skin) { Content = powerContent, HorizontalContentAlignment = HorizontalAlignment.Left };
        powerButton.Click += (_, _) => { Dismiss(false); StartMenuPower.Button(options.PowerAction).Run(); };
        power.Children.Add(powerButton); power.Children.Add(powerMenu);
        right.Content = places;

        picture.Clicked += () => Launch(new Entry("Your info", EntryKind.Shell, "ms-settings:yourinfo"));
        Content = panel;
        Restyle();

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
        Closed += (_, _) => { foreach (var w in watchers) w.Dispose(); settingsWatcher?.Dispose(); picture.Close(); };
    }

    static ControlTemplate BareTextBox()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        host.SetValue(FocusableProperty, false);
        t.VisualTree = host;
        t.Seal();
        return t;
    }

    bool Searching => left.Content == results;

    /// The search box's glyph: the theme's own while the box is empty, else the lens; a clear button while there is text.
    void ShowLens()
    {
        bool empty = search.Text.Length == 0;
        if (empty && searchGlyph != null) lensHost.Child = searchGlyph;
        else { lens.Text = empty ? "\uE721" : "\uE711"; lensHost.Child = lens; }
        lensHost.Cursor = empty ? null : Cursors.Arrow;
    }
    readonly List<UIElement> homeParts = [], searchParts = [];

    /// The panel as the current style lays it out: the left column, All Programs and the bottom band down the left;
    /// the right column beside them; under them Default's bands and separators, or the theme's parts. Rows already made
    /// are made again by Refresh, which follows.
    void Restyle()
    {
        var s = skin;
        Width = s.Width;
        panel.Children.Clear(); panel.RowDefinitions.Clear(); panel.ColumnDefinitions.Clear();
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(s.AllProgramsHeight) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(s.BottomVisible) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s.LeftWidth) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Put(UIElement e, int row, int col, int rows = 1, int cols = 1) { Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetRowSpan(e, rows); Grid.SetColumnSpan(e, cols); panel.Children.Add(e); }
        homeParts.Clear(); searchParts.Clear();
        if (s.Themed)
        {
            ThemePart P(int id) => new(s.Part(id));
            void Home(UIElement e, int row, int col, int rows = 1) { Put(e, row, col, rows); homeParts.Add(e); }
            void Find(UIElement e, int row, int col, int cols = 1) { Put(e, row, col, cols: cols); searchParts.Add(e); }
            Home(P(StartMenuSkin.ProgramsBack), 0, 0);
            var line = P(StartMenuSkin.ProgramsSeparator);
            line.Height = 1; line.Width = 232; line.HorizontalAlignment = HorizontalAlignment.Left; line.VerticalAlignment = VerticalAlignment.Bottom; line.Margin = new Thickness(18, 0, 0, 2);
            Home(line, 0, 0);
            Home(P(StartMenuSkin.AllProgramsBand), 1, 0);
            Home(P(StartMenuSkin.PlacesBack), 0, 1, rows: 2);
            Find(P(StartMenuSkin.SearchBack), 0, 0, cols: 2);
            Find(P(StartMenuSkin.SearchBand), 1, 0, cols: 2);
            // the bottom parts at their full height, the taskbar's part of them cut off
            foreach (var (id, col, home) in new[] { (StartMenuSkin.ProgramsBottom, 0, true), (StartMenuSkin.PlacesBottom, 1, true),
                (StartMenuSkin.SearchBottom, 0, false), (StartMenuSkin.SearchBottomRight, 1, false) })
            {
                var part = P(id); part.Height = s.BottomHeight; part.VerticalAlignment = VerticalAlignment.Top;
                if (home) Home(part, 2, col); else Find(part, 2, col);
            }
            var arrow = P(StartMenuSkin.AllProgramsArrow); arrow.Width = 12; arrow.Height = 12;
            allProgramsArrow.Content = arrow;
            allPrograms.Margin = new Thickness(11, 3, 4, 4); allPrograms.Padding = new Thickness(26, 0, 0, 0);
        }
        else
        {
            var band = new Border { Background = s.BottomBand };
            Put(band, 2, 0, cols: 2);
            Put(new Border { Background = s.Separator, Height = 1, VerticalAlignment = VerticalAlignment.Top }, 2, 0, cols: 2);
            Put(new Border { Background = s.Separator, Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 8, 0) }, 1, 0);
            allProgramsArrow.Content = new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 };
            allPrograms.Margin = new Thickness(8, 2, 8, 1); allPrograms.Padding = new Thickness(29, 0, 14, 0);
        }
        allPrograms.Skin = s; allPrograms.Foreground = s.AllProgramsText;
        allProgramsLens.Foreground = s.AllProgramsText;
        Put(leftArea, 0, 0); Put(allPrograms, 1, 0);
        leftArea.Margin = new Thickness(8, 8, 8, 4);
        right.Margin = new Thickness(s.Themed ? 4 : 1, s.PlacesTop, 8, 4);
        Put(right, 0, 1, rows: 2);
        var r = s.SearchRect;
        searchHost.Width = r.Width; searchHost.Height = r.Height; searchHost.HorizontalAlignment = HorizontalAlignment.Left; searchHost.VerticalAlignment = VerticalAlignment.Top;
        searchHost.Margin = new Thickness(r.X, r.Y, 0, 0);
        searchFrame.Skin = s;
        search.Foreground = s.SearchText; search.CaretBrush = s.SearchText; hint.Foreground = s.SearchHint; lens.Foreground = s.SearchHint;
        searchGlyph = s.Themed && s.SearchPart(2) is { DpiImages.Length: > 0 } glyphPart ? new ThemePart(glyphPart) : null;
        lensHost.HorizontalAlignment = searchGlyph != null ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        search.Margin = searchGlyph != null ? new Thickness(6, 0, 24, 0) : new Thickness(26, 0, 4, 0);
        hint.Margin = searchGlyph != null ? new Thickness(8, 0, 24, 0) : new Thickness(28, 0, 0, 0);
        ShowLens();
        Put(searchHost, 2, 0);
        var o = s.SignOutRect;
        power.Margin = new Thickness(o.X, o.Y, 0, 0);
        powerButton.Height = powerMenu.Height = o.Height; powerButton.MinWidth = o.Width;
        powerButton.Skin = powerMenu.Skin = s;
        powerButton.Foreground = powerMenu.Foreground = s.SignOutText;
        powerMenu.Content = s.Themed && s.Part(StartMenuSkin.SplitButton) != null
            ? new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 8 }
            : new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 };
        Put(power, 2, 1);
        notice.Foreground = s.Muted;
        picture.Restyle(s);
        if (handle != IntPtr.Zero) s.ApplyGlass(handle);
        ShowMode();
    }

    /// Searching, the results take both columns (the right column hides) and All Programs becomes "See more results",
    /// as in StartAllBack; otherwise the left column holds the programs or All Programs in place.
    void ShowMode()
    {
        bool searching = Searching;
        foreach (var e in homeParts) e.Visibility = searching ? Visibility.Hidden : Visibility.Visible;
        foreach (var e in searchParts) e.Visibility = searching ? Visibility.Visible : Visibility.Hidden;
        Grid.SetColumnSpan(leftArea, searching ? 2 : 1);
        Grid.SetColumnSpan(allPrograms, searching ? 2 : 1);
        right.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        leftArea.Margin = searching ? new Thickness(11, 8, 10, 4) : new Thickness(8, 8, 8, 4);
        allProgramsLens.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        allProgramsArrow.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        allProgramsText.Text = searching ? "See more results" : left.Content == inPlace ? "Back" : "All Programs";
        AutomationProperties.SetName(allPrograms, allProgramsText.Text);
        SelectFirst();
    }

    /// The search result Enter opens shows highlighted while the search box has the keyboard.
    void SelectFirst()
    {
        var first = Searching && search.IsKeyboardFocusWithin ? FirstRow(results) : null;
        foreach (var row in Rows(results)) if (row is SkinButton b) b.Selected = b == first;
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

    Button Row(Entry e, int iconSize, bool hasJump = false, bool submenu = false, Action click = null, bool glyph = false, SkinButton.Area area = SkinButton.Area.LeftItem)
    {
        var row = new SkinButton(area, skin) { Tag = e, ToolTip = e.Tip, Padding = new Thickness(6, 0, 0, 0) };
        row.Foreground = area == SkinButton.Area.RightItem ? skin.RightText : skin.LeftText;
        var content = new DockPanel();
        if (hasJump || submenu)
        {
            var arrow = new Border { Background = Brushes.Transparent, Width = 22, Child = Arrow(area) };
            arrow.MouseEnter += (_, _) => WhenMoved(arrow, () => { hover.Stop(); if (hasJump) ShowJump(e, row, false); else OpenSubmenu(e, row); });
            DockPanel.SetDock(arrow, Dock.Right); content.Children.Add(arrow);
        }
        var image = new Image { Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        if (glyph && e.Glyph != null)
            content.Children.Add(new TextBlock { Text = e.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = iconSize, Width = iconSize, Margin = image.Margin, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
        else content.Children.Add(image);
        var text = new TextBlock { Text = e.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (e.New) { text.FontWeight = FontWeights.SemiBold; row.Background = skin.NewFill; }
        content.Children.Add(text);
        // a right-column item shows its large icon in the user picture's place while it is hot
        if (area == SkinButton.Area.RightItem)
        {
            row.MouseEnter += (_, _) => ShowItemIcon(e, row);
            row.MouseLeave += (_, _) => { if (pictureFor == row) { pictureFor = null; picture.ShowIcon(null); } };
        }
        row.Content = content;
        if (iconSize >= 24) row.MinHeight = iconSize + 8;
        AutomationProperties.SetName(row, e.Name);
        if (image.Parent != null) LoadIcon(image, e, iconSize);
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

    Button pictureFor;
    async void ShowItemIcon(Entry e, Button row)
    {
        pictureFor = row;
        if (!options.UserPicture || e.Kind == EntryKind.Command) return;
        int pixels = (int)Math.Round(48 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        ImageSource icon = null;
        try { icon = await StartMenuShell.Icon(e, pixels); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { }
        if (pictureFor == row && row.IsMouseOver) picture.ShowIcon(icon);
    }

    /// A submenu's or jump list's arrow: the theme's arrow glyph (Plain8's images), the small solid triangle StartAllBack
    /// draws for a theme without one (Windows 7), or Default's chevron.
    UIElement Arrow(SkinButton.Area area)
    {
        if (skin.Themed && skin.Part(StartMenuSkin.PlaceArrow) is { DpiImages.Length: > 0 } part)
            return new ThemePart(part) { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ink = area == SkinButton.Area.RightItem ? skin.RightText : skin.LeftText;
        if (skin.Themed)
            return new System.Windows.Shapes.Polygon { Points = [new Point(0, 0), new Point(4, 4), new Point(0, 8)], Fill = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    /// A separator: the theme's separator part, or Default's faint grey line.
    FrameworkElement Line()
    {
        if (skin.Themed && skin.Part(StartMenuSkin.ProgramsSeparator) is { } part)
            return new ThemePart(part) { Height = 1, Margin = new Thickness(8, 5, 8, 5) };
        return new Border { Height = 1, Margin = new Thickness(8, 5, 8, 5), Background = skin.Separator };
    }

    /// A group's heading in the search results (StartAllBack's larger "Programs", "Settings"), or a jump list's.
    TextBlock Header(string text, bool rightColumn = false) =>
        new() { Text = text, FontSize = rightColumn ? 12 : 15, FontWeight = rightColumn ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(rightColumn ? 8 : 0, 6, 8, 4), Foreground = rightColumn ? skin.RightText : skin.LeftText };

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
        // the window's own surface clear, so the glass under it shows through wherever the panel draws nothing
        HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor = Colors.Transparent;
        var all = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref all);
        skin.ApplyGlass(handle);
        new WindowInteropHelper(picture) { Owner = handle }.EnsureHandle();
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
        // StartAllBack's settings changed while Pathweft runs: an open menu follows at once, a closed one at its opening
        settingsWatcher = new StartMenuSettingsWatcher(() =>
        {
            var was = options;
            options = StartMenuOptions.Read();
            if (was.Flyout != options.Flyout || was.AppsFolder != options.AppsFolder || was.SortOrder != options.SortOrder || was.NotifyNewApps != options.NotifyNewApps) Forget();
            if (IsVisible) { _ = Refresh(); PlacePicture(); }
        });
        _ = Refresh();
    }

    void Forget() { programs = null; StartMenuSearch.Forget(); }

    /// At each opening: StartAllBack's settings, the pinned and recent programs and the right column, read again.
    async Task Refresh()
    {
        try
        {
            options = StartMenuOptions.Read();
            // a new visual style, translucency or dark mode: the panel laid out and drawn again before its rows are made
            if (options.AlterStyle != skin.Options.AlterStyle || options.Alpha != skin.Options.Alpha || options.Blur != skin.Options.Blur || options.Dark != skin.Options.Dark || options.AppsDark != skin.Options.AppsDark
                || StartMenuTheme.Load(StartMenuTheme.Resolve(options.AlterStyle)) != skin.Theme)
            {
                skin = StartMenuSkin.For(options);
                Restyle();
            }
            if (programs == null || DateTime.UtcNow - programsRead > TimeSpan.FromMinutes(5)) Forget(); // packaged apps have no folder to watch
            var o = options;
            var (p, r, c, jumps, user) = await ShellWorker.Run(() =>
            {
                var p = StartMenuItems.Pinned();
                var r = o.TrackPrograms ? StartMenuItems.Recent(o.RecentPrograms, p) : new List<Entry>();
                var jumps = p.Concat(r).Where(e => StartMenuShell.HasJumpList(e.Owner, e.Target, o.JumpListItems, o.TrackDocuments)).ToHashSet();
                return (p, r, StartMenuItems.RightColumn(o), jumps, StartMenuItems.UserPicture());
            });
            pinned = p; recent = r; column = c; withJumpList = jumps;
            BuildHome(); BuildPlaces(user);
            // All Programs is highlighted while it holds a new program, as Windows 7's menu does
            var all = await Programs();
            if (StartMenuItems.Flatten(all).Any(x => x.New)) allPrograms.Background = skin.NewFill; else allPrograms.ClearValue(BackgroundProperty);
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
        foreach (var e in pinned) home.Children.Add(Row(e, options.ProgramIcon, withJumpList.Contains(e)));
        if (pinned.Count > 0 && recent.Count > 0) home.Children.Add(Line());
        foreach (var e in recent) home.Children.Add(Row(e, options.ProgramIcon, withJumpList.Contains(e)));
    }

    void BuildPlaces(string user)
    {
        places.Children.Clear();
        foreach (var e in column)
        {
            if (ReferenceEquals(e, StartMenuItems.Separator)) { places.Children.Add(Line()); continue; }
            var row = Row(e, 16, submenu: e.Children != null, glyph: options.RightPaneGlyphs, area: SkinButton.Area.RightItem);
            // StartAllBack's rows: a 32-pixel highlight every 35
            row.Height = PlaceRow - 3; row.Margin = new Thickness(0, 3, 0, 0);
            if (e.Tip == "Personal folder") ((TextBlock)((DockPanel)row.Content).Children[^1]).FontWeight = FontWeights.SemiBold;
            places.Children.Add(row);
        }
        var action = StartMenuPower.Button(options.PowerAction).Name;
        powerText.Text = action;
        // StartAllBack shows a key beside Sign out and the power symbol beside the other actions
        ((TextBlock)((StackPanel)powerButton.Content).Children[0]).Text = action == "Sign out" ? "\uE8D7" : "\uE7E8";
        AutomationProperties.SetName(powerButton, action);
        userPicture = null;
        if (user != null && options.UserPicture)
            try
            {
                var image = new BitmapImage();
                image.BeginInit(); image.UriSource = new Uri(user); image.DecodePixelWidth = 128; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit();
                userPicture = image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException) { }
        picture.Picture = userPicture;
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
        // "See more results": the search goes on in Pathweft's own search
        if (Searching && search.Text.Trim().Length > 0) { var text = search.Text.Trim(); Dismiss(false); StartMenuShell.Route(MainWindow.SearchRequest + text); return; }
        if (left.Content == inPlace) { ShowHome(); return; }
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
        foreach (var e in list) flyout.Items.Add(MenuItemFor(e, openFolders: false, options.AllProgramsIcon));
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
        ShowMode();
        left.ScrollToTop();
    }

    Button InPlaceRow(Entry e, int depth)
    {
        bool open = false;
        Button row = null;
        row = Row(e, options.AllProgramsIcon, click: e.Children == null ? null : Toggle);
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
        ShowMode();
        HideJump();
    }

    MenuItem MenuItemFor(Entry e, bool openFolders, int iconSize)
    {
        var item = new MenuItem { Header = e.Name, ToolTip = e.Tip };
        if (e.New) { item.FontWeight = FontWeights.SemiBold; item.SetResourceReference(BackgroundProperty, "AccentSoft"); }
        var image = new Image { Width = iconSize, Height = iconSize };
        item.Icon = image;
        LoadIcon(image, e, iconSize);
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
            foreach (var c in children) item.Items.Add(MenuItemFor(c, openFolders, iconSize));
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
        foreach (var c in children) submenu.Items.Add(MenuItemFor(c, openFolders: true, options.FlyoutIcon));
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
        var o = options;
        var categories = await ShellWorker.Run(() => StartMenuShell.JumpList(e.Owner, e.Target, o.JumpListItems, o.TrackDocuments));
        if (jumpOwner != e) return;
        jump.Children.Clear();
        foreach (var c in categories)
        {
            jump.Children.Add(Header(c.Name, rightColumn: true));
            foreach (var item in c.Items) jump.Children.Add(Row(item, 16, area: SkinButton.Area.RightItem));
        }
        if (categories.Count == 0) jump.Children.Add(new TextBlock { Text = "Nothing here yet.", Margin = new Thickness(8), Foreground = skin.RightText });
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
        ShowMode();
        int shown = 0;
        var o = options;
        if (o.SearchPrograms)
            foreach (var g in new[] { StartMenuSearch.Programs(text), StartMenuSearch.Settings(text) })
                if (g is { Total: > 0 }) shown += AddGroup(g);
        // files not searched (Windows' "Don't search" for other files): programs and settings only
        if (o.SearchFiles == 0)
        {
            if (shown == 0) results.Children.Add(new TextBlock { Text = "No items match your search.", Margin = new Thickness(8, 6, 8, 6), Foreground = skin.LeftText });
            SelectFirst();
            return;
        }
        var pending = new TextBlock { Text = "Searching files and folders…", Margin = new Thickness(8, 6, 8, 6), Foreground = skin.Muted };
        results.Children.Add(pending);
        SelectFirst();
        List<StartMenuSearch.Group> files; string problem = null;
        try { files = await StartMenuSearch.FilesAsync(text, o.SearchFiles == 2, token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException) { files = null; problem = ex.Message; }
        if (token.IsCancellationRequested) return;
        int at = results.Children.IndexOf(pending);
        results.Children.Remove(pending);
        if (files == null)
        {
            var t = new TextBlock { Text = "Files and folders were not searched: " + (problem ?? "the Pathweft index is not answering."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 6), Foreground = skin.Muted };
            results.Children.Insert(at, t);
            if (shown == 0) results.Children.Insert(at, new TextBlock { Text = "No programs or settings match your search.", Margin = new Thickness(8, 6, 8, 0), Foreground = skin.LeftText });
            SelectFirst();
            return;
        }
        var groupPanel = new StackPanel();
        foreach (var g in files) shown += AddGroup(g, groupPanel);
        results.Children.Insert(at, groupPanel);
        if (shown == 0) results.Children.Insert(at, new TextBlock { Text = "No items match your search.", Margin = new Thickness(8, 6, 8, 6), Foreground = skin.LeftText });
        SelectFirst();
    }

    int AddGroup(StartMenuSearch.Group g, Panel to = null)
    {
        to ??= results;
        // StartAllBack heads each group with its name alone, in larger type
        to.Children.Add(Header(g.Name));
        int n = 0;
        foreach (var e in g.Items.Take(StartMenuSearch.PerGroup))
        {
            var row = Row(e, options.SearchIcon, area: SkinButton.Area.Result);
            // StartAllBack's result rows: a 24-pixel highlight (more for large icons), indented under the heading
            row.Height = Math.Max(24, options.SearchIcon + 8); row.Margin = new Thickness(9, 2, 0, 2); row.Padding = new Thickness(4, 0, 0, 0);
            // the result under the pointer has its name underlined
            if (row.Content is DockPanel dock && dock.Children.OfType<TextBlock>().LastOrDefault() is { } name)
            {
                row.MouseEnter += (_, _) => name.TextDecorations = TextDecorations.Underline;
                row.MouseLeave += (_, _) => name.TextDecorations = null;
            }
            to.Children.Add(row); n++;
        }
        if (g.Notice != null) to.Children.Add(new TextBlock { Text = g.Notice, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(9, 0, 8, 4), FontSize = 11, Foreground = skin.Muted });
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
        Rest(); // before Show: showing reports the row under the pointer as entered
        Show();
        PlacePicture();
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
        Hide(); picture.Hide();
        HideJump();
        if (restore && previous != IntPtr.Zero && IsWindow(previous)) SetForegroundWindow(previous);
        previous = IntPtr.Zero;
    }

    /// Device pixels on the orb's monitor: first onto that monitor so a DPI change rescales the window, then its corner
    /// against the orb on the taskbar's side, kept inside the monitor's work area.
    void Place(StartMenuHost.Orb orb)
    {
        Fit();
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
            // StartAllBack's Default floats a little in from the screen's edge along the taskbar
            int gap = (int)Math.Round(skin.EdgeGap * VisualTreeHelper.GetDpi(this).DpiScaleX);
            if (!vertical && bar.Top > area.Top) { x = b.Left + gap; y = bar.Top - h; }      // bottom
            else if (!vertical) { x = b.Left + gap; y = bar.Bottom; }                        // top
            else if (bar.Left > area.Left) { x = bar.Left - w; y = b.Top + gap; }            // right
            else { x = bar.Right; y = b.Top + gap; }                                         // left
        }
        x = Math.Max(area.Left, Math.Min(x, area.Right - w));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));
        SetWindowPos(handle, (IntPtr)(-1), x, y, w, h, 0x0010); // topmost, no activate
    }

    /// The user picture above the panel, centred on the right column and rising above the panel's top edge by the
    /// style's amount; shown with the menu, over it.
    void PlacePicture()
    {
        if (!IsVisible || !options.UserPicture || handle == IntPtr.Zero) { picture.Hide(); return; }
        GetWindowRect(handle, out var menu);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int size = (int)Math.Round(skin.PictureFrame * scale);
        int x = menu.Left + (int)Math.Round((skin.PictureCentre - skin.PictureFrame / 2) * scale), y = menu.Top - (int)Math.Round(skin.PictureRise * scale);
        if (!picture.IsVisible) picture.Show();
        var h = new WindowInteropHelper(picture).Handle;
        SetWindowPos(h, (IntPtr)(-1), x, y, size, size, 0x0010); // topmost, no activate
    }

    /// As tall as the pinned and recent programs need, never less than Windows 7's menu.
    void Fit()
    {
        Width = skin.Width;
        panel.Measure(new Size(skin.Width, double.PositiveInfinity));
        Height = Math.Max(skin.MinHeight, panel.DesiredSize.Height);
    }

#if DEBUG
    /// S6c side-by-side check: the menu as the current settings make it, rendered to a PNG without being shown.
    internal async Task<int> RenderForVerification(string png)
    {
        handle = new WindowInteropHelper(this).EnsureHandle();
        await Refresh();
        Fit();
        var root = (FrameworkElement)Content;
        void Layout() { root.Measure(new Size(Width, Height)); root.Arrange(new Rect(0, 0, Width, Height)); root.UpdateLayout(); }
        Layout();
        await Task.Delay(3000); // the icons load on the Shell thread
        Layout();
        var dpi = VisualTreeHelper.GetDpi(root);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(Width * dpi.DpiScaleX), (int)Math.Ceiling(Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(png)) encoder.Save(f);
        File.WriteAllLines(png + ".jumps.txt", pinned.Concat(recent).Select(e => e.Name + " | " + StartMenuShell.DiagnoseJumpList(e.Owner, e.Target, options.JumpListItems, options.TrackDocuments)));
        File.WriteAllText(png + ".txt", $"icon={options.ProgramIcon} power={options.PowerAction} glyphs={options.RightPaneGlyphs} picture={options.UserPicture} sort={options.SortOrder} pinned={pinned.Count} recent={recent.Count} dpi={dpi.PixelsPerInchX} jumplists={string.Join(",", pinned.Concat(recent).Where(withJumpList.Contains).Select(e => e.Name))} nojumplist={string.Join(",", pinned.Concat(recent).Where(e => !withJumpList.Contains(e)).Select(e => e.Name))}");
        return 0;
    }
#endif

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
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS margins);
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
