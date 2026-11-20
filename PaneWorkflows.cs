using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;

/// S22 in the pane: workspace tab sets (OPUS-01), the linked-navigation notice (OPUS-02), expandable folders
/// and the flat view (OPUS-03), collection listings (OPUS-04) and the rows a listing export reads (OPUS-06).
public partial class PaneView
{
    // ---- OPUS-01: a workspace's tabs ----

    /// Replace closes the unpinned tabs first (pinned ones stay, as Close other tabs keeps them); Add puts the
    /// workspace's tabs after the open ones. A pinned tab joins the pinned group; one already open there is not
    /// opened twice. Missing local folders recover to the nearest existing folder when shown, as restored tabs do.
    internal void OpenTabSet(IReadOnlyList<string> paths, int active, IReadOnlyList<int> pinned, bool replace)
    {
        var add = paths.Select((p, i) => (p, pin: pinned?.Contains(i) == true, i)).Where(x => !string.IsNullOrWhiteSpace(x.p)).ToList();
        if (add.Count == 0) return;
        ParkSpace(); RememberTab();
        if (replace) // the list is refilled below before anything reads it
            for (int i = tabs.Count - 1; i >= 0; i--)
                if (!pins[i]) { TabClosed?.Invoke(tabs[i]); tabs.RemoveAt(i); pins.RemoveAt(i); }
        string target = null;
        foreach (var (path, pin, index) in add)
        {
            int existing = FindTab(path);
            if (existing >= 0) { if (index == active) target = tabs[existing]; continue; } // already open (a pinned tab, or added twice)
            if (pin) { int at = pins.Count(p => p); tabs.Insert(at, path); pins.Insert(at, true); }
            else { tabs.Add(path); pins.Add(false); }
            if (index == active || target == null) target = path;
        }
        if (tabs.Count == 0) { tabs.Add(ThisPC); pins.Add(false); }
        tab = Math.Max(0, target == null ? 0 : FindTab(target));
        back.Clear(); fwd.Clear();
        Navigate(Dir, record: false);
        ResumeSpace();
    }

    // ---- OPUS-02: a line under the address saying why this pane did not follow ----
    internal void ShowNotice(string text)
    {
        if (InSearch || SpaceAnalyzing) { Error?.Invoke(text); return; }
        SearchCoverage.Text = text; SearchCoverage.Visibility = Visibility.Visible;
    }
    internal void ClearNotice() { if (!InSearch && !SpaceAnalyzing) SearchCoverage.Visibility = Visibility.Collapsed; }
    internal string NoticeText => SearchCoverage.Visibility == Visibility.Visible ? SearchCoverage.Text : "";

    // ---- OPUS-03: flat view and OPUS-04: a collection, both listed as results with their locations ----

    /// "flat" or the collection's name while the list shows one of them (InSearch holds too: Esc leaves).
    string listMode;
    internal bool FlatView => listMode == "flat";
    internal string ShownCollection => listMode is { } m && m != "flat" ? m : null;
    internal static Func<string, IReadOnlyList<string>> CollectionMembers;

    /// Every file below the folder, with where each one is. Bounded (MaxResults) and cancelled by any
    /// navigation; hidden items follow the folder's setting and links/junctions are not followed.
    internal void ShowFlat()
    {
        if (SpaceAnalyzing) { AfterSpace(ShowFlat); return; }
        if (IsServer(Dir) || IsVirtual(Dir)) { Error?.Invoke("Flat view lists a folder on a drive or share. Leave this virtual location first."); return; }
        string root = Dir;
        bool hidden = ShowsHidden;
        _ = RunListing("flat", "Flat view · every file below this folder, with its location; links and junctions are not followed. Esc returns to the folder.", (add, token) =>
        {
            int found = 0;
            foreach (var fsi in FileVisibility.Subtree(root, hidden))
            {
                if (token()) return null;
                if (fsi is not FileInfo file) continue;
                var where = Path.GetRelativePath(root, file.DirectoryName ?? root);
                add(new Entry
                {
                    Name = file.Name, Path = file.FullName, Modified = file.LastWriteTime, Location = where == "." ? "" : where,
                    Created = file.CreationTime, Accessed = file.LastAccessTime, Attributes = file.Attributes, Size = file.Length,
                    IsCut = CutPaths.Contains(file.FullName), ReadOnlyReason = ReadOnlyIcons.AttributeReason(false, file.Attributes),
                });
                if (++found >= MaxResults) return $" Showing the first {MaxResults:N0} files.";
            }
            return null;
        });
    }

    /// The members of a collection, where they are now. A member that is gone or whose drive is offline stays
    /// in the list with its state; it is never dropped from the collection by looking at it.
    internal void ShowCollection(string name)
    {
        if (SpaceAnalyzing) { AfterSpace(() => ShowCollection(name)); return; }
        var members = CollectionMembers?.Invoke(name) ?? Array.Empty<string>();
        _ = RunListing(name, $"Collection “{name}” · {members.Count:N0} item(s). Remove from collection never touches the files. Esc returns to the folder.", (add, token) =>
        {
            int missing = 0;
            foreach (var path in members)
            {
                if (token()) return null;
                add(MemberEntry(path, ref missing));
            }
            return missing > 0 ? $" {missing:N0} missing or offline." : null;
        });
    }

    internal static Entry MemberEntry(string path, ref int missing)
    {
        string name = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
        string where = Path.GetDirectoryName(path.TrimEnd('\\')) ?? "";
        try
        {
            var info = new FileInfo(path);
            var attributes = File.GetAttributes(path);
            bool dir = (attributes & FileAttributes.Directory) != 0;
            FileSystemInfo item = dir ? new DirectoryInfo(path) : info;
            return new Entry
            {
                Name = name, Path = item.FullName, IsDir = dir, Modified = item.LastWriteTime, Location = where, Attributes = attributes,
                Created = item.CreationTime, Accessed = item.LastAccessTime, Size = dir ? SizeCache.Get(item.FullName, item.LastWriteTimeUtc) : info.Length,
                IsCut = CutPaths.Contains(item.FullName), ReadOnlyReason = ReadOnlyIcons.AttributeReason(dir, attributes),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            missing++;
            bool offline = Path.GetPathRoot(path) is { Length: > 0 } root && !Directory.Exists(root);
            return new Entry
            {
                Name = name, Path = path, Location = where, Size = -2,
                Message = ex is FileNotFoundException or DirectoryNotFoundException && !offline ? "Missing: moved, renamed or deleted" : offline ? "Offline: its drive or share is not connected" : ex.Message,
            };
        }
    }

    /// Lists what `produce` finds, a batch at a time, as the filesystem search does; any navigation ends it.
    /// `produce` returns a note to add when it stopped early or found missing items.
    async Task RunListing(string mode, string notice, Func<Action<Entry>, Func<bool>, string> produce)
    {
        ClearFolderFailure();
        LeaveSearch(); StopLiveFilter();
        searchQuery = "";
        int v = ++navVersion;
        SearchLoading(v);
        searching = true;
        sizing?.Cancel();
        StopFolderWatch();
        var results = new System.Collections.ObjectModel.ObservableCollection<Entry>();
        ShowItems(results, null);
        listMode = mode;
        searchNotice = notice;
        SearchCoverage.Text = notice; SearchCoverage.Visibility = Visibility.Visible;
        string more = await Task.Run(() =>
        {
            var batch = new List<Entry>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            void Flush()
            {
                var b = batch; batch = new();
                Dispatcher.BeginInvoke(() => { if (v != navVersion) return; foreach (var e in b) results.Add(e); UpdateFooter(); });
            }
            string note;
            try
            {
                note = produce(e => { batch.Add(e); if (clock.ElapsedMilliseconds > 150) { Flush(); clock.Restart(); } }, () => v != navVersion);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { note = " Partial: " + ex.Message; }
            Flush();
            return note;
        });
        if (v != navVersion) return;
        if (more != null) { searchNotice += more; SearchCoverage.Text = searchNotice; }
        SearchLoaded(v);
        searching = false;
        UpdateFooter();
        StartSizing(); StartVersions(); StartIcons(); StartThumbs();
        StatsChanged?.Invoke();
    }

    /// Refresh in a flat view or a collection lists it again; false when neither is shown.
    bool RefreshListing()
    {
        if (listMode == null) return false;
        if (FlatView) ShowFlat(); else ShowCollection(listMode);
        return true;
    }

    // ---- OPUS-03: expandable folders in the ordinary Details listing ----

    /// View → Expandable folders: each folder row gets an expander; expanding lists its members indented
    /// below it. Remembered per folder shown; analysis mode keeps its own tree.
    bool nesting;
    readonly HashSet<string> nestOpen = new(StringComparer.OrdinalIgnoreCase);
    internal bool Nesting => nesting;
    bool NestingShown => nesting && !InSearch && !SpaceAnalyzing && !IsVirtual(Dir) && Dir != ThisPC;

    internal void ToggleNesting()
    {
        nesting = !nesting;
        if (!nesting)
        {
            nestOpen.Clear();
            if (items.Any(e => e.NestDepth > 0)) ShowItems(items.Where(e => e.NestDepth == 0).ToList(), null, animate: false, keep: true);
            foreach (var e in items) e.NestOpen = false;
        }
        ApplyNesting();
    }

    IComparer SortFor(string key, bool desc) => nesting ? new NestSort(new EntrySort(key, desc)) : new EntrySort(key, desc);

    /// The name cell with the expander when nesting shows; the plain one otherwise.
    void ApplyNesting()
    {
        if (fileNameCell == null || SpaceAnalyzing) return;
        var template = NestingShown ? (DataTemplate)FindResource("NestNameCell") : fileNameCell;
        if (NameColumn.CellTemplate != template) NameColumn.CellTemplate = template;
        if (view != null && !indexedSearch) view.CustomSort = SortFor(sortKey, sortDesc);
    }

    void NestExpand_Click(object sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Entry row) _ = ToggleNest(row);
    }

    /// Right on a closed folder row opens it, Left on an open one closes it (Explorer's tree keys).
    internal bool NestKey(System.Windows.Input.Key key)
    {
        if (!NestingShown || Mode != ViewMode.Details || List.SelectedItems.Count != 1 || List.SelectedItem is not Entry { IsDir: true } row || row.IsUp) return false;
        if (key == System.Windows.Input.Key.Right && !row.NestOpen || key == System.Windows.Input.Key.Left && row.NestOpen) { _ = ToggleNest(row); return true; }
        return false;
    }

    internal Task NestWork { get; private set; } = Task.CompletedTask;

    internal Task ToggleNest(Entry row) => NestWork = ToggleNestAsync(row);

    async Task ToggleNestAsync(Entry row)
    {
        if (!NestingShown || !row.IsDir || (row.Attributes & FileAttributes.ReparsePoint) != 0) return;
        if (row.NestOpen)
        {
            row.NestOpen = false;
            var gone = items.Where(e => IsBelow(e, row)).ToList();
            foreach (var e in gone) if (e.IsDir) nestOpen.Remove(e.Path);
            nestOpen.Remove(row.Path);
            ShowItems(items.Where(e => !gone.Contains(e)).ToList(), null, animate: false, keep: true);
            ReselectAfterNest(row);
            return;
        }
        row.NestOpen = true; nestOpen.Add(row.Path);
        int v = navVersion; string folder = Dir; bool hidden = ShowsHidden;
        var children = await Task.Run(() => ReadMembers(row.Path, hidden));
        if (v != navVersion || folder != Dir || !row.NestOpen || !items.Contains(row)) return;
        foreach (var child in children) { child.NestParent = row; child.NestDepth = row.NestDepth + 1; }
        ShowItems(items.Concat(children).ToList(), null, animate: false, keep: true);
        ReselectAfterNest(row);
        StartIcons(); StartSizing();
        // a folder opened inside it before (and still remembered) opens again
        foreach (var again in children.Where(c => c.IsDir && nestOpen.Contains(c.Path)).ToList()) await ToggleNestAsync(again);
    }

    void ReselectAfterNest(Entry row)
    {
        if (List.SelectedItems.Count == 0 && items.Contains(row)) { List.SelectedItem = row; List.ScrollIntoView(row); }
    }

    static bool IsBelow(Entry e, Entry ancestor) { for (var p = e.NestParent; p != null; p = p.NestParent) if (p == ancestor) return true; return false; }

    /// One folder's members, as the folder listing shows them: hidden ones by the folder's setting; a link's
    /// own row is listed but not opened.
    static List<Entry> ReadMembers(string folder, bool hidden)
    {
        var list = new List<Entry>();
        try
        {
            foreach (var fsi in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
            {
                if (!FileVisibility.Visible(fsi.Name, fsi.Attributes, hidden)) continue;
                bool dir = fsi is DirectoryInfo;
                list.Add(new Entry
                {
                    Name = fsi.Name, Path = fsi.FullName, IsDir = dir, Modified = fsi.LastWriteTime, Created = fsi.CreationTime, Accessed = fsi.LastAccessTime,
                    Attributes = fsi.Attributes, IsCut = CutPaths.Contains(fsi.FullName), ReadOnlyReason = ReadOnlyIcons.AttributeReason(dir, fsi.Attributes),
                    Size = dir ? SizeCache.Get(fsi.FullName, fsi.LastWriteTimeUtc) : ((FileInfo)fsi).Length,
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            list.Add(new Entry { Name = "(unavailable)", Path = Path.Combine(folder, "\u0001unavailable"), Size = -2, Message = ex.Message });
        }
        return list;
    }

    /// After a reread the rows are new: the folders that were open open again.
    void RestoreNesting()
    {
        if (!NestingShown || nestOpen.Count == 0) return;
        int v = navVersion;
        Dispatcher.BeginInvoke(async () =>
        {
            if (v != navVersion) return;
            foreach (var row in items.Where(e => e.NestDepth == 0 && e.IsDir && !e.NestOpen && nestOpen.Contains(e.Path)).ToList())
            { await ToggleNestAsync(row); if (v != navVersion) return; }
        }, DispatcherPriority.Background);
    }

    /// A folder and something inside it both selected: the folder carries it, so each item is sent once.
    static List<Entry> WithoutNested(List<Entry> selected)
    {
        if (!selected.Any(e => e.NestDepth > 0)) return selected;
        var set = selected.ToHashSet();
        return selected.Where(e => { for (var p = e.NestParent; p != null; p = p.NestParent) if (set.Contains(p)) return false; return true; }).ToList();
    }

    /// True when a selection reaches below the folder shown (expanded rows, a flat view or a collection):
    /// copying it then asks whether to keep the folder structure.
    internal bool SelectionIsNested(IEnumerable<Entry> selected) =>
        selected.Any(e => e.NestDepth > 0 || InSearch && !(Path.GetDirectoryName(e.Path.TrimEnd('\\')) ?? "").Equals(Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    // ---- OPUS-06: the rows as listed (order, filter) and the columns shown ----
    internal IReadOnlyList<Entry> ListingRows => ShownRows().Where(e => !e.IsUp).ToList();
}

/// The listing's order with expanded folders: each member right below its folder, members sorted among
/// themselves by the pane's own order.
sealed class NestSort(IComparer inner) : IComparer
{
    static List<Entry> Chain(Entry e) { var chain = new List<Entry>(); for (var p = e; p != null; p = p.NestParent) chain.Insert(0, p); return chain; }
    public int Compare(object a, object b)
    {
        var x = (Entry)a; var y = (Entry)b;
        if (ReferenceEquals(x, y)) return 0;
        if (x.NestParent == y.NestParent) return inner.Compare(x, y);
        var xs = Chain(x); var ys = Chain(y);
        int i = 0;
        while (i < xs.Count && i < ys.Count && ReferenceEquals(xs[i], ys[i])) i++;
        if (i == xs.Count) return -1; // x holds y
        if (i == ys.Count) return 1;
        return inner.Compare(xs[i], ys[i]);
    }
}
