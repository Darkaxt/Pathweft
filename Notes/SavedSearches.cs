using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileExplorer.Search;

namespace FileExplorer;

/// BROWSE-03: a named query that opens by running again through the normal pane search (the index),
/// never a frozen list of results. Folder is the scope ("This PC" for everywhere).
internal sealed class SavedSearch
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public string Folder { get; set; } = PaneView.ThisPC;
    public int Match { get; set; }
    public string Sort { get; set; } = "Name";
    public bool Descending { get; set; }
    internal bool Everywhere => Folder == PaneView.ThisPC;
    internal string ScopeText => Everywhere ? "This PC" : Folder;
    internal PaneSearchRequest Request => new(Folder, Query, Everywhere ? 1 : 0, PaneView.SortKeys.Contains(Sort) ? Sort : "Name", Descending, (SearchMatchOptions)Match);
}

/// saved-searches.json in the data folder. Renaming, editing and removing change only the entry.
internal static class SavedSearches
{
    static string FilePath => Path.Combine(Settings.Dir, "saved-searches.json");
    static readonly JsonSerializerOptions json = new() { WriteIndented = true };
    static List<SavedSearch> all;
    internal static event Action Changed;
    internal static IReadOnlyList<SavedSearch> All
    {
        get
        {
            if (all != null) return all;
            try { all = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<SavedSearch>>(File.ReadAllText(FilePath), json) : null; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { all = null; }
            all ??= new(); all.RemoveAll(s => string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Query));
            return all;
        }
    }
    internal static void Put(SavedSearch search, SavedSearch replacing = null)
    {
        var list = All.ToList();
        int at = replacing != null ? list.IndexOf(replacing) : -1;
        if (at >= 0) list[at] = search; else list.Add(search);
        Write(list);
    }
    internal static void Remove(SavedSearch search) { var list = All.ToList(); if (list.Remove(search)) Write(list); }
    static void Write(List<SavedSearch> list)
    {
        Directory.CreateDirectory(Settings.Dir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(list, json)); File.Move(temp, FilePath, true);
        all = list; Changed?.Invoke();
    }
}

/// Save search… and Edit…: name, query, where, match options.
internal sealed class SavedSearchDialog : AuxDialog
{
    readonly TextBox name, query;
    readonly ComboBox scope;
    readonly CheckBox matchCase, word, path, regex;
    readonly TextBlock problem;
    readonly string folder;
    SavedSearchDialog(Window owner, SavedSearch start, string title) : base(owner, title, "Save")
    {
        folder = start.Folder;
        Body.Children.Add(Label("Name", 0));
        name = Field(start.Name, "Saved search name", out var nameFrame); Body.Children.Add(nameFrame);
        Body.Children.Add(Label("Search for"));
        query = Field(start.Query, "Search query", out var queryFrame); Body.Children.Add(queryFrame);
        Body.Children.Add(Label("Look in"));
        scope = start.Everywhere ? Choice("Look in", "This PC") : Choice("Look in", start.Folder + " and its subfolders", "This PC");
        Body.Children.Add(scope);
        var options = (SearchMatchOptions)start.Match;
        matchCase = Check("Match case", options.HasFlag(SearchMatchOptions.Case));
        word = Check("Whole words", options.HasFlag(SearchMatchOptions.WholeWord));
        path = Check("Match the full path", options.HasFlag(SearchMatchOptions.Path));
        regex = Check("Regular expression", options.HasFlag(SearchMatchOptions.Regex));
        foreach (var box in new[] { matchCase, word, path, regex }) Body.Children.Add(box);
        Body.Children.Add(Note("Opening it searches again through the index, so the results are always current. Removing it never touches files."));
        problem = Note(""); Body.Children.Add(problem);
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
    protected override bool Validate()
    {
        if (name.Text.Trim().Length == 0) { problem.Text = "Give the search a name."; return false; }
        if (query.Text.Trim().Length == 0) { problem.Text = "Type what to search for."; return false; }
        return true;
    }
    SavedSearch Result(SavedSearch start) => new()
    {
        Name = name.Text.Trim(), Query = query.Text.Trim(), Sort = start.Sort, Descending = start.Descending,
        Folder = scope.SelectedItem as string == "This PC" ? PaneView.ThisPC : folder,
        Match = (int)((matchCase.IsChecked == true ? SearchMatchOptions.Case : 0) | (word.IsChecked == true ? SearchMatchOptions.WholeWord : 0)
                    | (path.IsChecked == true ? SearchMatchOptions.Path : 0) | (regex.IsChecked == true ? SearchMatchOptions.Regex : 0)),
    };
    internal static SavedSearch Ask(Window owner, SavedSearch start, string title)
    {
        var dialog = new SavedSearchDialog(owner, start, title);
        return dialog.Present() ? dialog.Result(start) : null;
    }
}

public partial class MainWindow
{
    /// Save this search…: the active pane's query, scope and options under a name.
    void SaveCurrentSearch(PaneView pane)
    {
        if (!pane.InSearch) { Status.Text = "Search first, then save the search"; return; }
        var request = pane.SearchRequest;
        var start = new SavedSearch { Name = request.Query, Query = request.Query, Folder = request.Scope == 1 ? PaneView.ThisPC : request.Folder, Match = (int)request.Match, Sort = request.Sort, Descending = request.Descending };
        if (SavedSearchDialog.Ask(this, start, "Save search") is not { } saved) return;
        SavedSearches.Put(saved);
        Status.Text = $"Saved search “{saved.Name}”";
    }
    internal void OpenSavedSearch(SavedSearch search, bool newTab = false)
    {
        if (newTab) active.NewTab();
        active.ApplySearchRequest(search.Request);
        active.FocusList();
    }
    void EditSavedSearch(SavedSearch search)
    {
        if (SavedSearchDialog.Ask(this, search, "Edit saved search") is { } edited) SavedSearches.Put(edited, search);
    }

    /// The sidebar's Saved searches section.
    void BuildSavedSearches()
    {
        SavedSearchList.Children.Clear();
        foreach (var search in SavedSearches.All)
        {
            var s = search;
            var button = SideButton("\xE721", s.Name, "",click: () => OpenSavedSearch(s));
            button.ToolTip = $"{s.Query}\nin {s.ScopeText}";
            button.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { OpenSavedSearch(s, newTab: true); e.Handled = true; } };
            var menu = new ContextMenu();
            menu.Items.Add(Item("Open", "", "", () => OpenSavedSearch(s)));
            menu.Items.Add(Item("Open in new tab", "", "Middle click", () => OpenSavedSearch(s, newTab: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Edit…", "", "", () => EditSavedSearch(s)));
            menu.Items.Add(Item("Remove", "", "", () => { SavedSearches.Remove(s); Status.Text = $"Removed saved search “{s.Name}” (no files were touched)"; }));
            button.ContextMenu = menu;
            SavedSearchList.Children.Add(button);
        }
        if (SavedSearchList.Children.Count == 0)
            SavedSearchList.Children.Add(new TextBlock { Text = "Search, then right-click the results: Save this search", FontSize = 11, Margin = new(8, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Palette.59FFFFFF") });
    }
}
