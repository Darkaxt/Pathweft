using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer.Preview;

// Registry Preview (TOOLS-05): a read-only key/value tree for .reg files with the source text one
// toggle away. Parsing runs off the UI thread and stops with the selection; nothing is imported.
internal sealed class RegistryViewer(MainWindow owner) : IViewer
{
    const long MaxBytes = 10 * 1024 * 1024;
    ContextObject context;
    string text;
    RegParse parse;
    ContentControl body;
    TreeView tree;
    TextBox source;
    Button toggle;
    bool showingSource;
#if DEBUG
    internal RegParse ParseForVerification => parse;
    internal TreeView TreeForVerification => tree;
    internal TextBox SourceForVerification => source;
    internal Button ToggleForVerification => toggle;
    internal bool ShowingSource => showingSource;
    internal ListBox ProblemsForVerification { get; private set; }
#endif
    public bool CanHandle(string path) => true;
    public void Init() { }
    public void Prepare(string path, ContextObject current) { context = current; current.Title = System.IO.Path.GetFileName(path) + " · Registry file"; }

    public async Task View(string path, ContextObject current)
    {
        var token = current.Token;
        text = await Task.Run(() => RegistryFile.ReadText(path, MaxBytes), token);
        parse = await Task.Run(() => RegistryFile.Parse(text, token), token);
        token.ThrowIfCancellationRequested();

        var root = new DockPanel();
        var bar = new DockPanel { Margin = new(8, 6, 8, 2) };
        toggle = PreviewControls.Icon(owner, "Show source text", "\uE943");
        toggle.Click += (_, _) => Show(!showingSource);
        DockPanel.SetDock(toggle, Dock.Right); bar.Children.Add(toggle);
        int keys = 0, values = 0;
        void Count(RegKey key) { keys++; values += key.Values.Count; foreach (var child in key.Keys) Count(child); }
        foreach (var key in parse.Roots) Count(key);
        var summary = new TextBlock { Text = $"{Plural(keys, "key")} · {Plural(values, "value")}" + (parse.Problems.Count > 0 ? " · " + Plural(parse.Problems.Count, "problem") : ""),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = parse.Header };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA");
        AutomationProperties.SetName(summary, "Registry file summary");
        bar.Children.Add(summary);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);

        if (parse.Problems.Count > 0)
        {
            var problems = new ListBox { MaxHeight = 120, Margin = new(8, 2, 8, 4), BorderThickness = new(0), Background = Brushes.Transparent };
            AutomationProperties.SetName(problems, "Registry file problems");
            ScrollViewer.SetHorizontalScrollBarVisibility(problems, ScrollBarVisibility.Disabled);
            foreach (var (line, message) in parse.Problems)
            {
                var item = new TextBlock { Text = $"Line {line}: {message}", TextWrapping = TextWrapping.Wrap, Tag = line, ToolTip = "Show this line in the source text" };
                item.SetResourceReference(TextBlock.ForegroundProperty, "PreviewWarning");
                problems.Items.Add(item);
            }
            problems.SelectionChanged += (_, _) => { if (problems.SelectedItem is TextBlock { Tag: int line }) GoToLine(line); };
            DockPanel.SetDock(problems, Dock.Top); root.Children.Add(problems);
#if DEBUG
            ProblemsForVerification = problems;
#endif
        }

        tree = Tree();
        body = new ContentControl { Content = tree, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        root.Children.Add(body);
        current.ViewerContent = root;
    }

    static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";

    internal void Show(bool sourceText)
    {
        if (body == null || !context.IsCurrent) return;
        showingSource = sourceText;
        if (sourceText && source == null) { source = BuiltinViewer.Text(text); AutomationProperties.SetName(source, "Registry file source text"); }
        body.Content = sourceText ? source : tree;
        PreviewControls.SetIcon(toggle, sourceText ? "Show key tree" : "Show source text", sourceText ? "\uE8B7" : "\uE943");
    }

    internal void GoToLine(int line)
    {
        Show(true);
        int start = 0;
        // Same line breaks as the parser: CR-LF, CR or LF
        for (int n = 1; n < line && start < text.Length; n++)
        {
            int cr = text.IndexOf('\r', start), lf = text.IndexOf('\n', start);
            int at = cr < 0 ? lf : lf < 0 ? cr : Math.Min(cr, lf);
            if (at < 0) { start = text.Length; break; }
            start = at + (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? 2 : 1);
        }
        int end = start;
        while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
        source.Select(start, end - start);
        source.Dispatcher.InvokeAsync(() => { if (context.IsCurrent) source.ScrollToLine(Math.Max(0, source.GetLineIndexFromCharacterIndex(start))); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    TreeView Tree()
    {
        var view = new TreeView { Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(6) };
        view.SetResourceReference(Control.ForegroundProperty, "Palette.E8EEF6");
        view.Resources[SystemColors.HighlightBrushKey] = Application.Current.FindResource("AccentSelect");
        view.Resources[SystemColors.HighlightTextBrushKey] = Application.Current.FindResource("AccentSelectText");
        view.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Application.Current.FindResource("AccentSelect");
        view.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Application.Current.FindResource("AccentSelectText");
        VirtualizingPanel.SetIsVirtualizing(view, true);
        VirtualizingPanel.SetVirtualizationMode(view, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(view, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(view, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(view, "Registry keys and values");
        foreach (var key in parse.Roots) view.Items.Add(KeyRow(key));
        if (view.Items.Count == 0) view.Items.Add(BuiltinViewer.Message("No registry keys to show."));
        else if (view.Items.Count == 1 && view.Items[0] is TreeViewItem only) only.IsExpanded = true;
        return view;
    }

    static TextBlock Part(string value, string brush, double left = 0)
    {
        var part = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, Margin = new(left, 0, 0, 0) };
        if (brush != null) part.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return part;
    }
    static TextBlock Glyph(string glyph) => new() { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14, Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };

    TreeViewItem KeyRow(RegKey key)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Glyph(key.Error ? "\uE7BA" : "\uE8B7"));
        var name = Part(key.Name, key.Error ? "PreviewWarning" : null); header.Children.Add(name);
        string state = key.Deleted ? "Deleted" : key.Error ? "Invalid key" : null;
        if (key.Deleted) name.TextDecorations = TextDecorations.Strikethrough;
        if (state != null) header.Children.Add(Part(state.ToLowerInvariant(), key.Error ? "PreviewWarning" : "Palette.8A97AA", 10));
        if (key.Error) ((TextBlock)header.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "PreviewWarning");
        var row = new TreeViewItem { Header = header, Tag = key, Padding = new(2, 3, 4, 3), ToolTip = $"{key.FullPath}\nLine {key.Line}" };
        row.SetResourceReference(Control.ForegroundProperty, "Palette.E8EEF6");
        AutomationProperties.SetName(row, key.Name);
        if (state != null) AutomationProperties.SetItemStatus(row, state);
        if (key.Keys.Count + key.Values.Count > 0)
        {
            // Rows are built on expansion so large files stay responsive
            row.Items.Add("…");
            row.Expanded += (_, e) =>
            {
                e.Handled = true;
                if (!context.IsCurrent || row.Items.Count != 1 || row.Items[0] is not string) return;
                row.Items.Clear();
                foreach (var child in key.Keys) row.Items.Add(KeyRow(child));
                foreach (var value in key.Values) row.Items.Add(ValueRow(value));
            };
        }
        return row;
    }

    static TreeViewItem ValueRow(RegValue value)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Glyph(value.Error ? "\uE7BA" : value.Type is "REG_SZ" or "REG_EXPAND_SZ" or "REG_MULTI_SZ" ? "\uE8D2" : "\uE9D9"));
        var name = Part(value.Name, value.Error ? "PreviewWarning" : null); header.Children.Add(name);
        string status;
        if (value.Deleted) { name.TextDecorations = TextDecorations.Strikethrough; header.Children.Add(Part("deleted value", "Palette.8A97AA", 10)); status = "Deleted"; }
        else
        {
            header.Children.Add(Part(value.Type == "ERROR" ? "invalid" : value.Type, value.Error ? "PreviewWarning" : "Palette.8A97AA", 10));
            var data = Part(value.DataOneLine, value.Error ? "PreviewWarning" : null, 10);
            data.MaxWidth = 520; data.TextTrimming = TextTrimming.CharacterEllipsis;
            header.Children.Add(data);
            status = value.Error ? "Invalid value" : null;
        }
        if (value.Error) ((TextBlock)header.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "PreviewWarning");
        var row = new TreeViewItem { Header = header, Tag = value, Padding = new(2, 3, 4, 3),
            ToolTip = value.Deleted ? $"Deletes this value\nLine {value.Line}" : $"{value.Type}\n{value.Data.Replace("\r", "\n")}\nLine {value.Line}" };
        row.SetResourceReference(Control.ForegroundProperty, "Palette.E8EEF6");
        AutomationProperties.SetName(row, value.Deleted ? value.Name + ", deleted" : $"{value.Name}, {value.Type}, {value.DataOneLine}");
        if (status != null) AutomationProperties.SetItemStatus(row, status);
        return row;
    }

    public void Cleanup() { body = null; tree = null; source = null; text = null; parse = null; }
}
