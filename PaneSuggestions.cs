using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;

/// BROWSE-01/02: one suggestion list under the address field. Open tabs of both panes (switch to them),
/// every Quick access list, recent folders and saved searches come from memory; only the filesystem
/// completion of a typed path reads the disk (one listing of the typed parent, as before). Ranked by name
/// prefix, name substring, path substring, then kind and recency. An older keystroke's results never
/// replace newer ones and never touch the draft, caret or focus.
public partial class PaneView
{
    internal enum SuggestionKind { Tab, Pinned, Alias, SavedSearch, Recent, Completion, Variable }
    internal sealed record Suggestion(SuggestionKind Kind, string Path, string Name, string Location, PaneView Pane = null, int TabIndex = -1, SavedSearch Search = null)
    {
        internal int Match;   // 0 name prefix, 1 name substring, 2 path substring, 3 recent (empty field)
        internal int Recency; // place in the recent folders, int.MaxValue when not there
    }
    /// What MainWindow holds: both panes' tabs and the Quick access lists.
    internal static Func<IEnumerable<(PaneView Pane, int Index, string Path)>> AllTabs;
    internal static Func<IEnumerable<(string List, string Path)>> PinnedFolders;
    internal static Action<PaneView, int> TabRequested;
    internal static Action<SavedSearch> SavedSearchRequested;

    const int SuggestionsShown = 30;

    static string DisplayName(string path) =>
        path == ThisPC ? "This PC" : NameOf(path) is { Length: > 0 } name ? name : path.TrimEnd('\\');

    /// The in-memory part, ranked (cheap: a few hundred strings at most).
    List<Suggestion> MemorySuggestions(string text)
    {
        string query = text.Trim().Trim('"');
        bool empty = query.Length == 0;
        var recent = Settings.RecentFolders;
        int RecencyOf(string path) { int i = recent.FindIndex(r => r.Equals(path, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
        int MatchOf(string name, string path)
        {
            if (empty) return 3;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
            return path.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
        }
        var found = new List<Suggestion>();
        void Add(Suggestion s, string matchName, string matchPath)
        {
            int match = MatchOf(matchName, matchPath);
            if (match < 0) return;
            s.Match = match; s.Recency = s.Path == null ? int.MaxValue : RecencyOf(s.Path);
            found.Add(s);
        }
        if (empty)
        {
            // An empty field: where you have been lately.
            foreach (var path in recent.Where(p => !p.Equals(Dir, StringComparison.OrdinalIgnoreCase)).Take(12))
                Add(new(SuggestionKind.Recent, path, DisplayName(path), Location(path)), "", path);
            return found;
        }
        foreach (var (pane, index, path) in AllTabs?.Invoke() ?? Enumerable.Empty<(PaneView, int, string)>())
        {
            if (path is null or "" || pane == this && index == tab) continue; // where you already are
            Add(new(SuggestionKind.Tab, path, DisplayName(path), (pane == this ? "Tab in this pane" : "Tab in the other pane") + "  ·  " + Location(path), pane, index), DisplayName(path), path);
        }
        foreach (var (list, path) in PinnedFolders?.Invoke() ?? Enumerable.Empty<(string, string)>())
            Add(new(SuggestionKind.Pinned, path, DisplayName(path), list + "  ·  " + Location(path)), DisplayName(path), path);
        // OPUS-07: @name rows for a text starting with @ (an alias's name is matched without its @)
        if (query.StartsWith('@'))
            foreach (var alias in Aliases.All)
                if (alias.Name.StartsWith(query[1..], StringComparison.OrdinalIgnoreCase))
                    found.Add(new(SuggestionKind.Alias, alias.Target, "@" + alias.Name, "Alias  ·  " + alias.Target) { Match = 0, Recency = int.MaxValue });
        // BROWSE-13: %name rows, as Explorer offers them: the environment variables naming a folder
        if (query.StartsWith('%') && query.IndexOf('%', 1) < 0 && !query.Contains('\\'))
            foreach (var (name, folder) in VariableFolders(query[1..]))
                found.Add(new(SuggestionKind.Variable, folder, "%" + name + "%", "Environment variable  ·  " + folder) { Match = 0, Recency = int.MaxValue });
        foreach (var search in SavedSearches.All)
            Add(new(SuggestionKind.SavedSearch, null, search.Name, $"Saved search  ·  {search.Query} in {search.ScopeText}", Search: search), search.Name, search.Query);
        foreach (var path in recent)
            Add(new(SuggestionKind.Recent, path, DisplayName(path), Location(path)), DisplayName(path), path);
        return found;
    }
    /// The variables whose name starts with the typed text and whose value is a local folder (a network value is not probed).
    internal static List<(string Name, string Folder)> VariableFolders(string prefix)
    {
        var found = new List<(string, string)>();
        foreach (System.Collections.DictionaryEntry v in Environment.GetEnvironmentVariables())
        {
            if (v.Key is not string name || v.Value is not string value || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            value = value.Trim().Trim('"');
            if (value.Length < 3 || value[1] != ':' || value[2] != '\\' || value.Contains(';') || RemoteAddress.IsRemote(value)) continue;
            try { if (Directory.Exists(value)) found.Add((name.ToUpperInvariant(), System.IO.Path.TrimEndingDirectorySeparator(value))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return found.OrderBy(f => f.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }
    static string Location(string path) =>
        path == ThisPC ? "Drives and devices" : RemoteAddress.IsRemote(path) ? path : System.IO.Path.GetDirectoryName(path.TrimEnd('\\')) ?? path;

    /// Ranks and collapses equal destinations; an open tab keeps its own switch row.
    static List<Suggestion> Rank(IEnumerable<Suggestion> all)
    {
        var ordered = all.OrderBy(s => s.Match).ThenBy(s => s.Kind).ThenBy(s => s.Recency).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shown = new List<Suggestion>();
        foreach (var s in ordered)
        {
            if (s.Kind != SuggestionKind.Tab && s.Path != null && !seen.Add(s.Path.TrimEnd('\\'))) continue;
            shown.Add(s);
            if (shown.Count == SuggestionsShown) break;
        }
        return shown;
    }

    void PathBox_Changed(object s, TextChangedEventArgs e)
    {
        if (PathBox.Visibility != Visibility.Visible) return;
        string raw = PathBox.Text;
        var text = TypedPath;
        int v = ++suggestVersion;
        bool hidden = ShowsHidden;
        var memory = MemorySuggestions(raw);
        ShowSuggestions(Rank(memory), raw);
        int cut = text.LastIndexOf('\\');
        if (cut < 0) return;
        // The filesystem completion of a typed path: the one directory listing, off the UI thread.
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = hidden ? 0 : FileAttributes.Hidden };
        Task.Run(() =>
        {
            List<string> found = new();
            string parent = text[..(cut + 1)], prefix = text[(cut + 1)..];
            try
            {
                if (IsServer(parent)) found = NetShares(parent.TrimEnd('\\'))?.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => parent + x).ToList() ?? new();
                else if (!RemoteAddress.IsRemote(parent) && Directory.Exists(parent)) found = Directory.EnumerateDirectories(parent, prefix + "*", opts).Where(d => FileVisibility.Visible(System.IO.Path.GetFileName(d), 0, hidden)).Take(12).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            found.RemoveAll(f => f.Equals(text.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)); // already typed in full
            if (found.Count == 0) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (v != suggestVersion || hidden != ShowsHidden || PathBox.Visibility != Visibility.Visible) return;
                var completions = found.Select(f => new Suggestion(SuggestionKind.Completion, f, DisplayName(f), Location(f)) { Match = 0, Recency = int.MaxValue });
                // the folders being typed into lead: they are what Tab completes
                ShowSuggestions(Rank(completions.Concat(memory.Where(m => m.Match <= 1))), raw, keepSelection: true);
            });
        });
    }

    void ShowSuggestions(List<Suggestion> shown, string typed, bool keepSelection = false)
    {
        var selected = keepSelection ? (SuggestList.SelectedItem as FrameworkElement)?.Tag as Suggestion : null;
        SuggestList.ItemsSource = shown.Select(s => SuggestionRow(s, typed.Trim().Trim('"'))).ToList();
        if (selected != null)
            SuggestList.SelectedItem = SuggestList.Items.Cast<FrameworkElement>().FirstOrDefault(r => r.Tag is Suggestion t && t.Kind == selected.Kind && t.Path == selected.Path && t.Name == selected.Name);
        Suggest.IsOpen = shown.Count > 0 && PathBox.IsKeyboardFocusWithin;
    }

    FrameworkElement SuggestionRow(Suggestion s, string query)
    {
        var grid = new Grid { Tag = s, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string glyph = s.Kind switch
        {
            SuggestionKind.Tab => "", SuggestionKind.Pinned => "", SuggestionKind.SavedSearch => "",
            SuggestionKind.Recent => "", _ => "",
        };
        grid.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        int at = query.Length > 0 ? s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) : -1;
        if (at < 0) name.Text = s.Name;
        else
        {
            if (at > 0) name.Inlines.Add(new Run(s.Name[..at]));
            name.Inlines.Add(new Run(s.Name.Substring(at, query.Length)) { FontWeight = FontWeights.Bold });
            if (at + query.Length < s.Name.Length) name.Inlines.Add(new Run(s.Name[(at + query.Length)..]));
        }
        text.Children.Add(name);
        var where = new TextBlock { Text = s.Location, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.7 };
        text.Children.Add(where);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        string action = s.Kind switch { SuggestionKind.Tab => "Switch to tab", SuggestionKind.SavedSearch => "Search", _ => "" };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        if (action.Length > 0) right.Children.Add(new TextBlock { Text = action, FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        if (s.Kind == SuggestionKind.Recent)
        {
            var remove = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 },
                Tag = "remove", Focusable = false, Width = 22, Height = 22, ToolTip = "Remove from history (the folder itself stays)",
            };
            remove.SetResourceReference(StyleProperty, "Ghost");
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + s.Name + " from history");
            right.Children.Add(remove);
        }
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        System.Windows.Automation.AutomationProperties.SetName(grid, $"{s.Name}, {s.Location}{(action.Length > 0 ? ", " + action : "")}");
        return grid;
    }

    static Suggestion SuggestionOf(object item) => (item as FrameworkElement)?.Tag as Suggestion;

    /// Tab: the selected (else first) path suggestion goes into the field, ready for the next level.
    void CompleteSuggestion()
    {
        var s = SuggestionOf(SuggestList.SelectedItem) ?? SuggestList.Items.Cast<object>().Select(SuggestionOf).FirstOrDefault(x => x?.Path != null);
        if (s?.Path == null || s.Path == ThisPC || RemoteAddress.IsRemote(s.Path)) return;
        PathBox.Text = (s.Kind == SuggestionKind.Variable ? s.Name : s.Path.TrimEnd('\\')) + "\\"; // %NAME%\ stays a variable, as Explorer writes it
        PathBox.CaretIndex = PathBox.Text.Length;
    }

    /// Enter or a click on a suggestion: go there, switch to the tab, or run the saved search.
    internal void ActivateSuggestion(Suggestion s)
    {
        EndEditPath();
        switch (s.Kind)
        {
            case SuggestionKind.Tab: TabRequested?.Invoke(s.Pane, s.TabIndex); return;
            case SuggestionKind.SavedSearch: SavedSearchRequested?.Invoke(s.Search); return;
            case SuggestionKind.Alias when !Directory.Exists(s.Path): Error?.Invoke($"Alias {s.Name} points to {s.Path}, which is missing or offline now"); return;
            default: Navigate(s.Path); FocusList(); return;
        }
    }

    /// A history row's remove button: only that history entry goes (never the folder, a pin or a tab).
    internal void RemoveFromHistory(Suggestion s)
    {
        Settings.RecentFolders.RemoveAll(p => p.Equals(s.Path, StringComparison.OrdinalIgnoreCase));
        Settings.Save();
        ++suggestVersion;
        ShowSuggestions(Rank(MemorySuggestions(PathBox.Text)), PathBox.Text);
    }

    // Mouse down, handled: the list must not take focus, or the path box would lose it and close.
    void Suggest_MouseDown(object s, MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        bool remove = false;
        while (d != null && d is not ListBoxItem)
        {
            if (d is Button { Tag: "remove" }) remove = true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        if (d is ListBoxItem { Content: FrameworkElement { Tag: Suggestion picked } })
        {
            if (remove) RemoveFromHistory(picked);
            else ActivateSuggestion(picked);
        }
        e.Handled = true;
    }
}
