using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

/// OPUS-04: a named list of references to files and folders anywhere. The originals never move; removing a
/// member or the whole collection forgets the reference only. Unlike a saved search (S21) the members are
/// the ones added, not whatever matches now; unlike the Drop bin it outlives the session.
internal sealed class FileCollection
{
    public string Name { get; set; } = "";
    public List<string> Items { get; set; } = new();
}

internal static class Collections
{
    /// OPUS-08: the preview's marked images are this collection.
    internal const string Marked = "Marked images";
    internal static readonly JsonStore<FileCollection> Store = new(() => Path.Combine(Settings.Dir, "collections.json"),
        list => list.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => { c.Items = (c.Items ?? new()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); return c; }).ToList());
    internal static IReadOnlyList<FileCollection> All => Store.All;
    internal static FileCollection Find(string name) => All.FirstOrDefault(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));

    /// Returns how many were new to it.
    internal static int Add(string name, IEnumerable<string> paths)
    {
        var existing = Find(name);
        var items = existing?.Items.ToList() ?? new();
        int added = 0;
        foreach (var p in paths) if (!items.Contains(p, StringComparer.OrdinalIgnoreCase)) { items.Add(p); added++; }
        Store.Put(new FileCollection { Name = existing?.Name ?? name, Items = items }, existing);
        return added;
    }

    internal static int Remove(string name, IEnumerable<string> paths)
    {
        if (Find(name) is not { } existing) return 0;
        var gone = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = existing.Items.Where(p => !gone.Contains(p)).ToList();
        int removed = existing.Items.Count - items.Count;
        if (removed > 0) Store.Put(new FileCollection { Name = existing.Name, Items = items }, existing);
        return removed;
    }

    internal static bool Contains(string name, string path) => Find(name)?.Items.Contains(path, StringComparer.OrdinalIgnoreCase) == true;
}

public partial class MainWindow
{
    void InitializeCollections()
    {
        PaneView.CollectionMembers = name => Collections.Find(name)?.Items ?? (IReadOnlyList<string>)Array.Empty<string>();
        Collections.Store.Changed += () =>
        {
            BuildCollections();
            foreach (var pane in new[] { Left, Right })
                if (pane.ShownCollection is { } shown) { if (Collections.Find(shown) != null) pane.Refresh(); else pane.ClearFilter(); }
            UpdateMarkButtons();
        };
        BuildCollections();
    }

    /// The sidebar's Collections section: click lists the members in the active pane.
    void BuildCollections()
    {
        CollectionList.Children.Clear();
        foreach (var saved in Collections.All)
        {
            var c = saved;
            var button = SideButton(c.Name == Collections.Marked ? "" : "", c.Name, "", click: () => OpenCollection(c.Name));
            button.ToolTip = $"{c.Name}\n{c.Items.Count:N0} item(s): references only, the files stay where they are";
            var menu = new ContextMenu();
            menu.Items.Add(Item("Show", "", "", () => OpenCollection(c.Name)));
            menu.Items.Add(Item("Show in other pane", "", "", () => { var o = Other; SetActive(o); OpenCollection(c.Name); }, !Settings.SinglePane));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Rename…", "", "", () => RenameCollection(c)));
            menu.Items.Add(Item("Forget missing items", "", "", () => ForgetMissing(c)));
            menu.Items.Add(Item("Remove collection", "", "", () => { Collections.Store.Remove(c); Status.Text = $"Removed collection “{c.Name}” (no files were touched)"; }));
            button.ContextMenu = menu;
            CollectionList.Children.Add(button);
        }
        if (Collections.All.Count == 0)
        {
            var hint = new TextBlock { Text = "Right-click files → Add to collection", FontSize = 11, Margin = new Thickness(8, 2, 0, 4) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Palette.59FFFFFF");
            CollectionList.Children.Add(hint);
        }
    }

    void OpenCollection(string name) { active.ShowCollection(name); active.FocusList(); }

    void RenameCollection(FileCollection c)
    {
        var name = PromptDialog.Ask(this, "Rename collection", "New name", c.Name, "Rename", selectAll: true);
        if (name == null || name == c.Name) return;
        name = name.Trim();
        if (name.Length == 0 || Collections.Find(name) is { } other && other != c) { Status.Text = $"A collection named “{name}” already exists"; return; }
        Collections.Store.Put(new FileCollection { Name = name, Items = c.Items }, c);
    }

    async void ForgetMissing(FileCollection c)
    {
        // offline members (their drive or share is not connected) stay: they are not known to be gone
        var gone = await Task.Run(() => c.Items.Where(p => Path.GetPathRoot(p) is { Length: > 0 } root && Directory.Exists(root) && !File.Exists(p) && !Directory.Exists(p)).ToList());
        int removed = Collections.Remove(c.Name, gone);
        Status.Text = removed == 0 ? $"Every item of “{c.Name}” is still there (or on a drive not connected now)" : $"Forgot {removed:N0} missing item(s) of “{c.Name}”";
    }

    /// The item menu's Add to collection submenu: each collection, then New collection….
    MenuItem AddToCollectionMenu(List<Entry> sel)
    {
        var paths = sel.Where(e => !e.IsUp && !PaneView.IsVirtual(e.Path)).Select(e => e.Path).ToList();
        var menu = Item("Add to collection", "", "", () => { }, paths.Count > 0);
        foreach (var saved in Collections.All)
        {
            var name = saved.Name;
            menu.Items.Add(Item(name, "", "", () => { int n = Collections.Add(name, paths); Status.Text = n == 0 ? $"Already in “{name}”" : $"Added {n:N0} item(s) to “{name}”"; }));
        }
        if (Collections.All.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Item("New collection…", "", "", () => NewCollection(paths)));
        return menu;
    }

    void NewCollection(IReadOnlyList<string> paths)
    {
        var name = PromptDialog.Ask(this, "New collection", "Name (the files stay where they are)", "", "Create", selectAll: true)?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        bool existed = Collections.Find(name) != null;
        int n = Collections.Add(name, paths);
        Status.Text = existed ? $"Added {n:N0} item(s) to “{name}”" : $"Created collection “{name}” with {n:N0} item(s)";
    }

    void RemoveFromCollection(PaneView p, List<Entry> sel)
    {
        if (p.ShownCollection is not { } name) return;
        int n = Collections.Remove(name, sel.Select(e => e.Path));
        Status.Text = $"Removed {n:N0} item(s) from “{name}” (the files were not touched)";
    }

    // The Drop bin's Save as collection: what it holds now, kept after the session.
    void BinSave_Click(object s, RoutedEventArgs e)
    {
        if (bin.Count == 0) { Status.Text = "The Drop bin is empty"; return; }
        NewCollection(bin.Select(b => b.Path).ToList());
    }
}
