using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

/// One entry of the left column's list: a column or property, its label and its group.
internal sealed record PickChoice(string Key, string Label, string Group, string Disabled = null);

// COLS-02 / COPY-02: the left column of More… and Copy…: every available column or property, grouped, with a
// search box and check boxes. The order in which boxes are checked is the order they are added.
internal sealed class PropertyPicker
{
    internal readonly DockPanel Panel = new();
    readonly StackPanel list = new();
    readonly TextBox search = new();
    readonly List<(PickChoice Choice, CheckBox Box)> rows = new();
    readonly List<(TextBlock Header, List<CheckBox> Boxes)> groups = new();
    internal readonly List<string> Chosen;
    internal event Action Changed;

    internal PropertyPicker(IEnumerable<PickChoice> choices, IEnumerable<string> chosen)
    {
        Chosen = chosen.Distinct().ToList();
        var field = new Border { Margin = new Thickness(0, 0, 0, 8) };
        field.SetResourceReference(FrameworkElement.StyleProperty, "AuxiliaryField");
        search.SetResourceReference(FrameworkElement.StyleProperty, "Bare");
        System.Windows.Automation.AutomationProperties.SetName(search, "Search properties");
        search.ToolTip = "Type to find a property by name";
        search.TextChanged += (_, _) => Filter();
        field.Child = search;
        DockPanel.SetDock(field, Dock.Top);
        Panel.Children.Add(field);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list, Padding = new Thickness(0, 0, 6, 0) };
        Panel.Children.Add(scroll);
        foreach (var group in choices.GroupBy(c => c.Group))
        {
            var header = new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 10, 0, 4) };
            list.Children.Add(header);
            var boxes = new List<CheckBox>();
            foreach (var choice in group)
            {
                var box = new CheckBox { Content = choice.Label, IsChecked = Chosen.Contains(choice.Key), IsEnabled = choice.Disabled == null, Margin = new Thickness(0, 0, 0, 3), Tag = choice.Key };
                System.Windows.Automation.AutomationProperties.SetName(box, choice.Label);
                if (choice.Disabled != null) { box.ToolTip = choice.Disabled; ToolTipService.SetShowOnDisabled(box, true); }
                else if (choice.Key.Contains('.')) box.ToolTip = choice.Key;
                box.Click += (_, _) => { Set(choice.Key, box.IsChecked == true); };
                list.Children.Add(box);
                rows.Add((choice, box)); boxes.Add(box);
            }
            groups.Add((header, boxes));
        }
    }

    internal void Focus() => search.Focus();

    internal void Set(string key, bool on)
    {
        Chosen.Remove(key);
        if (on) Chosen.Add(key);
        foreach (var (choice, box) in rows) if (choice.Key == key) box.IsChecked = on;
        Changed?.Invoke();
    }

    void Filter()
    {
        string q = search.Text.Trim();
        foreach (var (choice, box) in rows)
            box.Visibility = q.Length == 0 || choice.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase) || choice.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || choice.Group.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (header, boxes) in groups) header.Visibility = boxes.Any(b => b.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The Windows property list (COLS-02), grouped as Explorer's Choose Details groups it.
    internal static IEnumerable<PickChoice> WindowsProperties() =>
        ShellProperties.All().Select(d => new PickChoice(d.Name, d.Label, d.Group == "General" ? "Windows: general" : "Windows: " + d.Group));

    internal static IEnumerable<PickChoice> Hashes(bool archive) =>
        Checksums.Names.Select(n => new PickChoice("#" + n, n, "Hashes",
            Checksums.Unavailable(n) ?? (archive && n != "CRC-32" ? "Inside archives only the stored CRC-32 is available" : null)));
}

/// Pathweft's dialog chrome, as Resize images: acrylic, the tint, a caption, two columns and the action buttons.
internal abstract class TwoColumnDialog : Window
{
    protected readonly Grid Root = new();
    protected readonly TextBlock Caption = new() { Margin = new Thickness(2, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis };
    protected readonly Grid Columns = new();
    protected readonly DockPanel Footer = new() { Margin = new Thickness(0, 12, 0, 0) };
    protected readonly Button Accept = new() { IsDefault = true, Tag = "on" };

    protected TwoColumnDialog(Window owner, string title, string accept, double width, double height)
    {
        Title = title; Width = width; Height = height; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 12;
        SetResourceReference(ForegroundProperty, "Palette.E8EEF6");
        Caption.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA");
        var body = new DockPanel();
        body.SetResourceReference(MarginProperty, "AuxiliaryMargin");
        DockPanel.SetDock(Caption, Dock.Top); body.Children.Add(Caption);
        Accept.Content = accept;
        Accept.SetResourceReference(StyleProperty, "AuxiliaryAction");
        Accept.Click += (_, _) => Finish();
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 6, 0) };
        cancel.SetResourceReference(StyleProperty, "AuxiliaryAction");
        DockPanel.SetDock(Accept, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
        Footer.Children.Add(Accept); Footer.Children.Add(cancel);
        DockPanel.SetDock(Footer, Dock.Bottom); body.Children.Add(Footer);
        Columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        Columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(Columns);
        Root.Children.Add(body);
        Content = Root;
        AuxiliaryWindows.Prepare(this, Root);
        Owner = owner;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
    }

    protected static TextBlock Section(string text, double top = 0) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 8) };

    protected static Button Action(string text, Action run)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
        button.SetResourceReference(StyleProperty, "AuxiliaryAction");
        button.Click += (_, _) => run();
        return button;
    }

    protected static ComboBox Combo(string name)
    {
        var box = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        box.SetResourceReference(StyleProperty, "PreviewClipCombo");
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    protected static DockPanel Row(string label, UIElement control)
    {
        var row = new DockPanel { LastChildFill = true };
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA");
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text); row.Children.Add(control);
        return row;
    }

    protected void Place(UIElement element, int column) { Grid.SetColumn(element, column); Columns.Children.Add(element); }

    protected abstract void Finish();

    protected bool? Present()
    {
#if DEBUG
        HiddenTrialPresentation.Prepare(this);
#endif
        return ShowDialog();
    }
}

// COLS-02 More…: every column a view kind can show, Windows' property list included, and their order.
internal sealed class ChooseColumnsDialog : TwoColumnDialog
{
    internal sealed record Choice(List<string> Keys, bool Automatic);
    internal Choice Result { get; private set; }
    internal readonly PropertyPicker Picker;
    readonly ListBox order = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly string kind;
#if DEBUG
    /// Verification answers the dialog instead of a person: it sets the controls and calls Accept.
    internal static Action<ChooseColumnsDialog> VerificationAnswer;
    internal void AcceptForVerification() => Finish();
#endif

    internal static string KindLabel(string kind) => kind switch
    {
        ColumnCatalog.Archive => "archives", ColumnCatalog.Search => "search results", ColumnCatalog.Space => "space analysis", ColumnCatalog.Drives => "This PC", _ => "folders",
    };

    internal static Choice Show(Window owner, string kind, List<string> shown)
    {
        var dialog = new ChooseColumnsDialog(owner, kind, shown);
#if DEBUG
        if (VerificationAnswer is { } answer) { answer(dialog); dialog.Close(); return dialog.Result; }
#endif
        return dialog.Present() == true ? dialog.Result : null;
    }

    ChooseColumnsDialog(Window owner, string kind, List<string> shown) : base(owner, "Choose details", "OK", 780, 580)
    {
        this.kind = kind;
        Caption.Text = $"Columns for {KindLabel(kind)} · they apply at once to every open tab of this kind";
        bool archive = kind == ColumnCatalog.Archive;
        var choices = ColumnCatalog.Common(kind).Select(k => new PickChoice(k, ColumnCatalog.Header(k), "Basic"))
            .Concat(kind == ColumnCatalog.Drives ? Enumerable.Empty<PickChoice>() : PropertyPicker.Hashes(archive))
            .Concat(archive || kind == ColumnCatalog.Drives ? Enumerable.Empty<PickChoice>() : PropertyPicker.WindowsProperties()).ToList();
        Picker = new PropertyPicker(choices, shown.Where(k => choices.Any(c => c.Key == k)));
        Picker.Changed += Refresh;
        var left = new DockPanel();
        var heading = Section("Available"); DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading);
        left.Children.Add(Picker.Panel);
        Place(left, 0);

        var right = new DockPanel();
        var shownHeading = Section("Shown, in this order"); DockPanel.SetDock(shownHeading, Dock.Top); right.Children.Add(shownHeading);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
        buttons.Children.Add(Action("Move up", () => Move(-1)));
        buttons.Children.Add(Action("Move down", () => Move(1)));
        buttons.Children.Add(Action("Remove", () => { if (order.SelectedItem is ListBoxItem { Tag: string key }) Picker.Set(key, false); }));
        DockPanel.SetDock(buttons, Dock.Bottom); right.Children.Add(buttons);
        var hint = new TextBlock { Text = "Name always comes first. Columns can also be dragged in the list header.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Palette.6E7A8C");
        DockPanel.SetDock(hint, Dock.Bottom); right.Children.Add(hint);
        order.Background = Brushes.Transparent; order.BorderThickness = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(order, "Shown columns");
        order.KeyDown += (_, e) => { if (e.Key == Key.Delete && order.SelectedItem is ListBoxItem { Tag: string key }) Picker.Set(key, false); };
        right.Children.Add(order);
        Place(right, 2);

        var automatic = Action("Automatic", ChooseAutomatic);
        automatic.ToolTip = "Show the columns the files have, as before any were chosen";
        DockPanel.SetDock(automatic, Dock.Left); Footer.Children.Insert(0, automatic);
        Refresh();
        Loaded += (_, _) => Picker.Focus();
    }

    internal void ChooseAutomatic()
    {
        Result = new Choice(null, true);
        if (IsVisible) DialogResult = true;
    }

    void Refresh()
    {
        object selected = (order.SelectedItem as ListBoxItem)?.Tag;
        order.Items.Clear();
        order.Items.Add(new ListBoxItem { Content = "Name", Tag = null, IsEnabled = false });
        foreach (var key in Picker.Chosen) order.Items.Add(new ListBoxItem { Content = ColumnCatalog.Header(key), Tag = key });
        foreach (ListBoxItem item in order.Items) if (item.Tag != null && item.Tag.Equals(selected)) order.SelectedItem = item;
    }

    void Move(int by)
    {
        if (order.SelectedItem is not ListBoxItem { Tag: string key }) return;
        int at = Picker.Chosen.IndexOf(key), to = at + by;
        if (at < 0 || to < 0 || to >= Picker.Chosen.Count) return;
        Picker.Chosen.RemoveAt(at); Picker.Chosen.Insert(to, key);
        Refresh();
    }

    protected override void Finish()
    {
        Result = new Choice(Picker.Chosen.ToList(), false);
        if (IsVisible) DialogResult = true;
    }
}

// COPY-02 Copy…: which properties, in which format, with a live preview of the first files.
internal sealed class CopyDetailsDialog : TwoColumnDialog
{
    internal CopyChoice Result { get; private set; }
    internal readonly PropertyPicker Picker;
    internal readonly ComboBox FormatBox = Combo("Format"), PathsBox = Combo("Paths"), CaseBox = Combo("Hash case");
    internal readonly CheckBox HeaderBox = new() { Content = "Header row", Margin = new Thickness(110, 0, 0, 8) };
    internal readonly TextBox Preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Padding = new Thickness(8), BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock note = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly List<Entry> rows;
    readonly string root;
    readonly bool archive;
    bool loading;
#if DEBUG
    internal static Action<CopyDetailsDialog> VerificationAnswer;
    internal void AcceptForVerification() => Finish();
    internal string PreviewText => Preview.Text;
#endif

    internal static CopyChoice Ask(Window owner, PaneView pane, List<Entry> rows)
    {
        var dialog = new CopyDetailsDialog(owner, pane, rows);
#if DEBUG
        if (VerificationAnswer is { } answer) { answer(dialog); dialog.Close(); return dialog.Result; }
#endif
        return dialog.Present() == true ? dialog.Result : null;
    }

    static readonly string[] Basic = { "Name", "Path", "Extension", "Size", "Modified", "Created", "Accessed", "Type", "Attributes", "Owner", "Version" };

    CopyDetailsDialog(Window owner, PaneView pane, List<Entry> rows) : base(owner, "Copy details", "Copy", 940, 620)
    {
        this.rows = rows; root = pane.Dir; archive = pane.ArchiveRoot != null;
        Caption.Text = rows.Count == 1 ? rows[0].Name : $"{rows.Count:N0} items";
        var basics = archive ? Basic.Where(k => k is not ("Created" or "Accessed" or "Owner" or "Version")) : Basic;
        var choices = basics.Select(k => new PickChoice(k, k is "Name" or "Path" or "Extension" ? k : ColumnCatalog.Header(k), "Basic"))
            .Concat(PropertyPicker.Hashes(archive))
            .Concat(archive ? Enumerable.Empty<PickChoice>() : PropertyPicker.WindowsProperties()).ToList();
        var saved = CopyChoice.Parse(Settings.CopyDetails);
        // starts from what the pane shows: Name and its columns
        Picker = new PropertyPicker(choices, pane.CopyColumns.Prepend("Name").Where(k => choices.Any(c => c.Key == k && c.Disabled == null)));
        Picker.Changed += Refresh;
        var left = new DockPanel();
        var heading = Section("Properties"); DockPanel.SetDock(heading, Dock.Top); left.Children.Add(heading);
        left.Children.Add(Picker.Panel);
        Place(left, 0);

        loading = true;
        foreach (var f in CopyChoice.Formats) FormatBox.Items.Add(new ComboBoxItem { Content = CopyChoice.FormatLabel(f), Tag = f });
        PathsBox.Items.Add(new ComboBoxItem { Content = "Relative to this folder", Tag = false });
        PathsBox.Items.Add(new ComboBoxItem { Content = "Full paths", Tag = true });
        foreach (var c in CopyChoice.Cases) CaseBox.Items.Add(new ComboBoxItem { Content = CopyChoice.CaseLabel(c), Tag = c });
        Pick(FormatBox, saved.Format); Pick(PathsBox, saved.FullPaths); Pick(CaseBox, saved.Case);
        HeaderBox.IsChecked = saved.Header;
        loading = false;
        FormatBox.SelectionChanged += (_, _) => Refresh(); PathsBox.SelectionChanged += (_, _) => Refresh(); CaseBox.SelectionChanged += (_, _) => Refresh();
        HeaderBox.Click += (_, _) => Refresh();

        var right = new DockPanel();
        var output = new StackPanel();
        output.Children.Add(Section("Output"));
        output.Children.Add(Row("Format", FormatBox));
        output.Children.Add(HeaderBox);
        output.Children.Add(Row("Paths", PathsBox));
        output.Children.Add(Row("Hash case", CaseBox));
        output.Children.Add(Section("Preview", 6));
        DockPanel.SetDock(output, Dock.Top); right.Children.Add(output);
        note.SetResourceReference(TextBlock.ForegroundProperty, "Palette.6E7A8C");
        DockPanel.SetDock(note, Dock.Bottom); right.Children.Add(note);
        var frame = new Border { CornerRadius = new CornerRadius(6), Child = Preview };
        frame.SetResourceReference(Border.BackgroundProperty, "Palette.1FFFFFFF");
        System.Windows.Automation.AutomationProperties.SetName(Preview, "Preview");
        right.Children.Add(frame);
        Place(right, 2);
        Refresh();
        Loaded += (_, _) => Picker.Focus();
    }

    static void Pick(ComboBox box, object tag)
    {
        foreach (ComboBoxItem item in box.Items) if (Equals(item.Tag, tag)) { box.SelectedItem = item; return; }
        box.SelectedIndex = 0;
    }

    internal CopyChoice Current => new((string)((ComboBoxItem)FormatBox.SelectedItem).Tag, HeaderBox.IsChecked == true,
        (bool)((ComboBoxItem)PathsBox.SelectedItem).Tag, (string)((ComboBoxItem)CaseBox.SelectedItem).Tag, Picker.Chosen.ToList());

    internal void Refresh()
    {
        if (loading || FormatBox.SelectedItem == null) return;
        var choice = Current;
        HeaderBox.IsEnabled = choice.Format is "tsv" or "csv";
        var first = rows.Take(5).ToList();
        if (!archive)
        {
            // the preview's few rows read what they lack now; the rest are read when copied
            var properties = choice.Keys.Where(ColumnCatalog.IsProperty).Select(ShellProperties.Find).Where(d => d != null).ToList();
            bool owner = choice.Keys.Contains("Owner"), basics = choice.Keys.Any(k => k is "Created" or "Accessed" or "Attributes"), version = choice.Keys.Contains("Version");
            foreach (var e in first.Where(e => !e.IsUp))
            {
                var missing = properties.Where(d => !e.HasCell(d.Name)).ToList();
                bool o = owner && !e.HasCell("Owner"), b = basics && e.Created == default, v = version && !e.IsDir && e.Version.Length == 0 && !e.HasCell("~Version");
                if (missing.Count > 0 || o || b || v) PaneView.ApplyExtras(e, PaneView.ReadExtras(e.Path, e.IsDir, e.Attributes, b, o, v, missing));
            }
        }
        Preview.Text = choice.Keys.Count == 0 ? "" : CopyText.Render(first, choice, root) + (rows.Count > first.Count ? Environment.NewLine + $"… and {rows.Count - first.Count:N0} more" : "");
        var hashes = choice.Keys.Where(ColumnCatalog.IsHash).ToList();
        bool uncomputed = !archive && hashes.Count > 0 && rows.Any(e => !e.IsDir && hashes.Any(h => ColumnCatalog.HashValue(e, h) == null));
        note.Text = choice.Keys.Count == 0 ? "Choose at least one property."
            : choice.Format == "checksum" && hashes.Count == 0 ? "A checksum file needs a hash: choose one under Hashes."
            : uncomputed ? "Hashes not calculated yet are calculated when you copy, in the Transfers window." : "";
        Accept.IsEnabled = choice.Keys.Count > 0 && !(choice.Format == "checksum" && hashes.Count == 0);
    }

    protected override void Finish()
    {
        if (!Accept.IsEnabled) return;
        Result = Current;
        if (IsVisible) DialogResult = true;
    }
}
