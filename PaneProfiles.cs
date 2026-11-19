using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FileExplorer;

/// BROWSE-05: folder view profiles in a pane. The folder's own profile, or the nearest ancestor's saved for
/// its subfolders, sets the view mode, columns and widths, sort, date grouping and hidden files; a change made
/// while one applies goes into that profile. Elsewhere the user's defaults apply and stay as they were.
/// Search results, archives, This PC and space analysis never take or save a profile.
public partial class PaneView
{
    FolderProfile profile;
    string profileFolder;
    bool applyingProfile;
    // The pane defaults as they were when this folder was opened: a first save takes what was changed here
    // into the folder's profile and gives the defaults back, so other folders keep their view.
    string entryPath; ViewMode? entryMode; List<string> entryColumns;

    /// Hidden files for this pane: the profile's choice, else the global setting (Ctrl+H).
    internal bool ShowsHidden => profile?.Hidden ?? ShowHidden;
    internal bool HasFolderProfile => profile != null;
    bool ProfileEligible(string path) => !IsWidget && !string.IsNullOrEmpty(path) && path != ThisPC && ArchiveOf(path) == null && !IsServer(path) && !RemoteAddress.IsRemote(path);

    /// Called by GoTo before the folder is read, so the listing uses the profile's hidden-file choice.
    void ApplyProfile(string path)
    {
        var (folder, found) = ProfileEligible(path) && searchQuery == null ? FolderProfiles.Find(path) : (null, null);
        if (!string.Equals(path, entryPath, StringComparison.OrdinalIgnoreCase))
        {
            entryPath = path;
            entryMode = Settings.Views.TryGetValue(Name, out var current) ? current : null;
            entryColumns = ColumnCatalog.Layout(ColumnCatalog.Folder)?.ToList();
        }
        if (found == profile && folder == profileFolder) return;
        profile = found; profileFolder = folder;
        widthKind = null; // widths come from the profile now, or back from the defaults
        var mode = profile?.Mode is { } saved && Enum.TryParse<ViewMode>(saved, out var m) ? m : Settings.Views.TryGetValue(Name, out var d) ? d : ViewMode.Details;
        if (mode != Mode && IsLoaded)
        {
            applyingProfile = true;
            try { SetView(mode); } finally { applyingProfile = false; }
        }
        QueueColumns();
    }

    /// The pane's first view: its folder's profile, else the pane's default.
    void ApplyStartView()
    {
        var mode = profile?.Mode is { } saved && Enum.TryParse<ViewMode>(saved, out var m) ? m : Settings.Views.TryGetValue(Name, out var d) ? d : ViewMode.Details;
        applyingProfile = true;
        try { SetView(mode); } finally { applyingProfile = false; }
    }

    /// BROWSE-07: notes changed (null: several or unknown): the rows show them, the columns and live filter follow.
    void NotesChanged(string path)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => NotesChanged(path)); return; }
        foreach (var entry in items) if (path == null || entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) entry.RefreshCells();
        QueueColumns();
        if (liveFiltering) ApplyFilter();
    }

    /// Where a view-mode change is kept: the applying profile, or the pane's default.
    void RememberMode(ViewMode mode)
    {
        if (IsWidget || applyingProfile) return;
        if (profile != null) { profile.Mode = mode.ToString(); FolderProfiles.Save(notify: false); }
        else Settings.Views[Name] = mode;
    }
    /// The sort a folder opens with: its profile's, else its own remembered order, else by name.
    (string Key, bool Desc) FolderSort(string path)
    {
        if (profile?.Sort is { } key && SortKeys.Contains(key)) return (key, profile.Descending);
        return Settings.FolderSorts.TryGetValue(path, out var sort) ? sort : ("Name", false);
    }
    /// A sort chosen in a profiled folder goes into the profile; true when it did.
    bool RememberSortInProfile(string key, bool desc)
    {
        if (profile == null) return false;
        profile.Sort = key; profile.Descending = desc; FolderProfiles.Save(notify: false);
        return true;
    }
    bool GroupsByDate => profile != null ? profile.GroupByDate : Settings.DateGroupFolders.Contains(Dir);
    List<string> ProfileColumns(string kind) => kind == ColumnCatalog.Folder ? profile?.Columns : null;
    double? ProfileWidth(string kind, string key) =>
        kind == ColumnCatalog.Folder && profile?.Widths != null && profile.Widths.TryGetValue(key, out var w) ? w : SavedWidth(kind, key);
    /// The columns chosen (null: automatic): into the applying profile, else the kind's defaults.
    void SetColumns(string kind, List<string> keys)
    {
        if (kind == ColumnCatalog.Folder && profile != null)
        {
            profile.Columns = keys?.Where(k => k != "Name" && k != "Message").Distinct().ToList();
            FolderProfiles.Save(notify: false); QueueColumns(); QueueHashes(); return;
        }
        if (keys == null) ColumnCatalog.ResetLayout(kind); else ColumnCatalog.SetLayout(kind, keys);
    }

    /// View > Folder view.
    internal MenuItem FolderViewMenu()
    {
        var root = new MenuItem { Header = "Folder view", Icon = new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13 } };
        bool eligible = ProfileEligible(Dir) && !InSearch && !SpaceAnalyzing;
        string state = profile == null ? "Uses your default view" : profileFolder.Equals(Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? (profile.Subfolders ? "Saved for this folder and its subfolders" : "Saved for this folder") : $"From {System.IO.Path.GetFileName(profileFolder)} (with its subfolders)";
        root.Items.Add(new MenuItem { Header = state, IsEnabled = false });
        root.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save for this folder", IsEnabled = eligible }; save.Click += (_, _) => SaveProfile(false); root.Items.Add(save);
        var below = new MenuItem { Header = "Save for this folder and its subfolders", IsEnabled = eligible }; below.Click += (_, _) => SaveProfile(true); root.Items.Add(below);
        var reset = new MenuItem { Header = profile != null && !profileFolder.Equals(Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? $"Reset (removes the view saved for {System.IO.Path.GetFileName(profileFolder)})" : "Reset to the default view", IsEnabled = eligible && profile != null };
        reset.Click += (_, _) => ResetProfile(); root.Items.Add(reset);
        return root;
    }

    internal void SaveProfile(bool subfolders)
    {
        if (!ProfileEligible(Dir) || InSearch || SpaceAnalyzing) return;
        var grid = details as GridView;
        var saved = new FolderProfile
        {
            Mode = Mode.ToString(), Sort = sortKey, Descending = sortDesc, GroupByDate = GroupsByDate, Hidden = ShowsHidden, Subfolders = subfolders,
            Columns = ProfileColumns(ColumnCatalog.Folder) ?? ColumnCatalog.Layout(ColumnCatalog.Folder)?.ToList(),
        };
        if (grid != null)
            foreach (var column in grid.Columns)
                if (column != NameColumn && colKey.TryGetValue(column, out var key) && key != "Message" && !double.IsNaN(column.Width)) saved.Widths[key] = column.Width;
        Settings.FolderSorts.Remove(Dir); Settings.DateGroupFolders.Remove(Dir); // now part of the profile
        bool wasDefault = profile == null && string.Equals(Dir, entryPath, StringComparison.OrdinalIgnoreCase);
        FolderProfiles.Set(Dir, saved);
        if (wasDefault)
        {
            if (entryMode is { } mode) Settings.Views[Name] = mode; else Settings.Views.Remove(Name);
            if (!(ColumnCatalog.Layout(ColumnCatalog.Folder) ?? new()).SequenceEqual(entryColumns ?? new()))
            { if (entryColumns == null) ColumnCatalog.ResetLayout(ColumnCatalog.Folder); else ColumnCatalog.SetLayout(ColumnCatalog.Folder, entryColumns); }
            Settings.Save();
        }
        Error?.Invoke(subfolders ? "View saved for this folder and its subfolders" : "View saved for this folder");
    }
    void ResetProfile()
    {
        if (profileFolder == null) return;
        FolderProfiles.Remove(profileFolder);
        Settings.FolderSorts.Remove(Dir); Settings.DateGroupFolders.Remove(Dir);
        Error?.Invoke("This folder uses your default view again");
    }

    /// Another pane or tab saved or removed a profile: take up what applies here now.
    void ProfilesChanged()
    {
        if (!ProfileEligible(Dir) || InSearch) return;
        bool hidden = ShowsHidden;
        ApplyProfile(Dir);
        var (key, desc) = FolderSort(Dir);
        if (key != sortKey || desc != sortDesc) { ShowSort(key, desc); if (view != null) view.CustomSort = new EntrySort(key, desc); }
        ApplyGrouping();
        if (hidden != ShowsHidden) RefreshVisibility();
    }

    /// Ctrl+H in a profiled folder changes that profile's hidden files; false when no profile applies.
    internal bool ToggleProfileHidden()
    {
        if (profile == null) return false;
        profile.Hidden = !profile.Hidden; FolderProfiles.Save(notify: false);
        RefreshVisibility();
        return true;
    }
}
