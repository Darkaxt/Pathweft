using System.ComponentModel;
using System.IO;
using System.IO.Enumeration;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualBasic.FileIO;

namespace FileExplorer;

// Find duplicates (1.0.51): the files in a folder, and optionally its subfolders, that have the same content, and the
// shortcuts that open the same thing. Files are grouped by size first, so only files of equal size are ever read; of
// those, the first 64 KB are hashed, and only files still alike after that are read whole. xxHash-128 (System.IO.Hashing,
// already used by Verify copies): two different files share a digest with a chance of about 1 in 10^38. All of it runs in
// the background, with progress and Stop; the window is the user's to choose what goes.
public partial class DuplicatesWindow : Window
{
    public sealed record DupFile(string Path, long Size, DateTime Modified);

    /// Files with the same content, or shortcuts with the same target (`Target` set).
    public sealed class DupGroup
    {
        public long Size { get; init; }
        public string Target { get; init; }
        public List<DupFile> Files { get; init; }
        public long Wasted => Target != null ? 0 : Size * (Files.Count - 1);
        public string Title => Target != null
            ? $"{Files.Count} shortcuts to {Target}"
            : $"{Files.Count} copies  ·  {MainWindow.Fmt(Size)} each  ·  {MainWindow.Fmt(Wasted)} to free";
    }

    // Attributes of a file that is in the cloud only (OneDrive and other sync apps): reading it would download it
    const FileAttributes InCloud = FileAttributes.Offline | (FileAttributes)0x40000 /* RECALL_ON_OPEN */ | (FileAttributes)0x400000 /* RECALL_ON_DATA_ACCESS */;
    const int Head = 64 * 1024;

    /// The groups, most space to free first. `progress`: what is happening and how far along (0–1).
    internal static List<DupGroup> Find(string folder, bool subfolders, bool hidden, IProgress<(string Text, double Done)> progress, CancellationToken token)
    {
        // every file, without following junctions or symbolic links into other folders (a loop, or the same files twice)
        var options = new EnumerationOptions { RecurseSubdirectories = subfolders, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | (hidden ? 0 : FileAttributes.Hidden) };
        var all = new FileSystemEnumerable<DupFile>(folder, (ref FileSystemEntry e) => new DupFile(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.LocalDateTime), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && (e.Attributes & InCloud) == 0,
            ShouldRecursePredicate = (ref FileSystemEntry e) => (e.Attributes & FileAttributes.ReparsePoint) == 0,
        };
        var files = new List<DupFile>();
        var links = new List<DupFile>();
        long reported = 0;
        var buffer = new byte[1 << 20]; // one for every file: a fresh megabyte each was 300 MB of garbage for 300 files
        foreach (var f in all)
        {
            token.ThrowIfCancellationRequested();
            (f.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? links : files).Add(f);
            if ((files.Count + links.Count) % 500 == 0) progress?.Report(($"Looking at {files.Count + links.Count:N0} files…", 0));
        }

        var groups = new List<DupGroup>();
        // shortcuts: by what they open (a shortcut whose target is gone opens nothing, so it has no twin)
        foreach (var same in links.Select(l => (Link: l, Target: MainWindow.ShortcutTarget(l.Path))).Where(x => x.Target != x.Link.Path)
                                  .GroupBy(x => x.Target, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            groups.Add(new DupGroup { Target = same.Key, Size = same.First().Link.Size, Files = same.Select(x => x.Link).ToList() });

        // files: same size, then the same first 64 KB, then the same content. Empty files are all alike and free nothing.
        var bySize = files.Where(f => f.Size > 0).GroupBy(f => f.Size).Where(g => g.Count() > 1).ToList();
        long planned = bySize.Sum(g => g.Sum(f => Math.Min(f.Size, Head))), done = 0;
        var heads = new List<List<DupFile>>();
        foreach (var size in bySize)
        {
            var byHead = new Dictionary<UInt128, List<DupFile>>();
            foreach (var f in size)
                if (Hash(f.Path, Head, buffer, token, n => { done += n; Report("Comparing the first bytes", 0.1 * done / Math.Max(1, planned)); }) is { } h)
                    (byHead.TryGetValue(h, out var l) ? l : byHead[h] = new()).Add(f);
            heads.AddRange(byHead.Values.Where(l => l.Count > 1));
        }
        planned = heads.Where(l => l[0].Size > Head).Sum(l => l.Sum(f => f.Size));
        done = 0;
        foreach (var alike in heads)
        {
            if (alike[0].Size <= Head) { groups.Add(new DupGroup { Size = alike[0].Size, Files = alike }); continue; } // read whole already
            var byContent = new Dictionary<UInt128, List<DupFile>>();
            foreach (var f in alike)
                if (Hash(f.Path, long.MaxValue, buffer, token, n => { done += n; Report($"Reading {MainWindow.Fmt(planned)} to compare", 0.1 + 0.9 * done / Math.Max(1, planned)); }) is { } h)
                    (byContent.TryGetValue(h, out var l) ? l : byContent[h] = new()).Add(f);
            groups.AddRange(byContent.Values.Where(l => l.Count > 1).Select(l => new DupGroup { Size = l[0].Size, Files = l }));
        }
        return groups.OrderByDescending(g => g.Wasted).ThenByDescending(g => g.Files.Count).ToList();

        void Report(string text, double fraction)
        {
            // ten times a second is plenty for a bar
            if (Environment.TickCount64 - reported < 100) return;
            reported = Environment.TickCount64;
            progress?.Report((text, fraction));
        }
    }

    /// xxHash-128 of the first `length` bytes, or null when the file can't be read (locked, gone, no access).
    static UInt128? Hash(string path, long length, byte[] buffer, CancellationToken token, Action<int> read)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            var hash = new System.IO.Hashing.XxHash128();
            long left = length;
            while (left > 0)
            {
                token.ThrowIfCancellationRequested();
                int n = s.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n == 0) break;
                hash.Append(buffer.AsSpan(0, n));
                left -= n;
                read(n);
            }
            return hash.GetCurrentHashAsUInt128();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---- the window ----
    public sealed class Row(DupGroup group, DupFile file, string root)
    {
        public DupGroup Group { get; } = group;
        public DupFile File { get; } = file;
        public string Name => System.IO.Path.GetFileName(File.Path);
        public string Folder => System.IO.Path.GetRelativePath(root, System.IO.Path.GetDirectoryName(File.Path)) is var r && r != "." ? r : "(this folder)";
        public string ModifiedText => File.Modified.ToString("yyyy-MM-dd  HH:mm");
        public string Tip => File.Path; // the rows' tooltip (App.xaml)
    }

    readonly MainWindow main;
    readonly string folder;
    CancellationTokenSource running;
    List<Row> rows = new();

    public DuplicatesWindow(MainWindow owner, string folder)
    {
        InitializeComponent();
        Owner = main = owner;
        this.folder = folder;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        FolderText.Text = folder;
        FolderText.ToolTip = folder;
        ContentRendered += (_, _) => Search();
        Closed += (_, _) => { running?.Cancel(); Ferry.Finished -= MoveFinished; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (running != null) running.Cancel(); else Close(); e.Handled = true; }
            else if (e.Key == Key.Delete && running == null) { Delete_Click(null, null); e.Handled = true; }
        };
        Ferry.Finished += MoveFinished;
    }

    async void Search()
    {
        running?.Cancel();
        var cts = running = new CancellationTokenSource();
        SearchButton.Content = "Stop";
        SubfoldersBox.IsEnabled = false;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        Summary.Text = "Looking…";
        Status.Text = "";
        ShowGroups(new List<DupGroup>());
        bool sub = SubfoldersBox.IsChecked == true, hidden = PaneView.ShowHidden;
        var progress = new Progress<(string Text, double Done)>(p => { if (running == cts) { Summary.Text = p.Text; Progress.Value = p.Done; } });
        List<DupGroup> found = null;
        string error = null;
        try { found = await Task.Run(() => Find(folder, sub, hidden, progress, cts.Token), cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { error = ex.Message; }
        if (running != cts) return; // a newer search took over
        running = null;
        SearchButton.Content = "Search again";
        SubfoldersBox.IsEnabled = true;
        Progress.Visibility = Visibility.Collapsed;
        if (found == null) { Summary.Text = error ?? "Stopped. Search again to start over."; return; }
        ShowGroups(found);
    }

    void ShowGroups(List<DupGroup> groups)
    {
        rows = groups.SelectMany(g => g.Files.Select(f => new Row(g, f, folder))).ToList();
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Row.Group)));
        List.ItemsSource = view;
        ShowSummary();
    }

    void ShowSummary()
    {
        if (running != null) return;
        var groups = rows.Select(r => r.Group).Distinct().ToList();
        Summary.Text = groups.Count == 0 ? "No duplicates here."
            : $"{groups.Count} group{(groups.Count == 1 ? "" : "s")} of duplicates  ·  {MainWindow.Fmt(groups.Sum(g => g.Wasted))} could be freed";
    }

    void Subfolders_Click(object s, RoutedEventArgs e) => Search();
    void Search_Click(object s, RoutedEventArgs e) { if (running != null) running.Cancel(); else Search(); }

    List<Row> Selected => List.SelectedItems.Cast<Row>().ToList();

    void List_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var sel = Selected;
        Status.Text = sel.Count == 0 ? "" : $"{sel.Count} selected  ·  {MainWindow.Fmt(sel.Sum(r => r.File.Size))}";
    }

    /// All but one of every group: the oldest stays, as the likely original; between equals, the shortest path.
    void SelectExtra_Click(object s, RoutedEventArgs e)
    {
        List.SelectedItems.Clear();
        foreach (var g in rows.GroupBy(r => r.Group))
            foreach (var r in g.OrderBy(r => r.File.Modified).ThenBy(r => r.File.Path.Length).Skip(1)) List.SelectedItems.Add(r);
    }

    /// The selection, unless it takes every copy of something: one always stays.
    List<string> Chosen()
    {
        var sel = Selected;
        if (sel.Count == 0) { Status.Text = "Select the copies to remove first, or use Select extra copies."; return null; }
        int whole = sel.GroupBy(r => r.Group).Count(g => g.Count() == g.Key.Files.Count);
        if (whole > 0)
        {
            PromptDialog.Choose(this, "Keep at least one copy of each", $"Every copy is selected in {whole} group{(whole == 1 ? "" : "s")}. Leave one of them out of the selection, or use Select extra copies.", "OK");
            return null;
        }
        return sel.Select(r => r.File.Path).ToList();
    }

    void Delete_Click(object s, RoutedEventArgs e)
    {
        if (Chosen() is not { } paths) return;
        long bytes = Selected.Sum(r => r.File.Size);
        if (PromptDialog.Choose(this, $"Send {paths.Count} file{(paths.Count == 1 ? "" : "s")} to the Recycle Bin?", $"{MainWindow.Fmt(bytes)}. One copy of each stays where it is.", "Delete", "Cancel") != 0) return;
        Status.Text = "Deleting…";
        main.Recycle(paths, UIOption.OnlyErrorDialogs, done: Prune);
    }

    Job moving;
    void Move_Click(object s, RoutedEventArgs e)
    {
        if (Chosen() is not { } paths) return;
        var pick = new Microsoft.Win32.OpenFolderDialog { Title = $"Move {paths.Count} file{(paths.Count == 1 ? "" : "s")} to which folder?" };
        if (pick.ShowDialog(this) != true) return;
        moving = main.Submit(paths, pick.FolderName, move: true); // Ferry: progress, name conflicts and Ctrl+Z in the main window
        Status.Text = "Moving… the progress is in the main window.";
    }

    void MoveFinished(Job job) { if (job == moving) Dispatcher.BeginInvoke(Prune); } // Ferry raises it off the UI thread

    /// Drops the files that are gone (deleted, moved) and the groups that have no twin left.
    void Prune()
    {
        var gone = rows.Where(r => !System.IO.File.Exists(r.File.Path)).Select(r => r.File).ToHashSet();
        if (gone.Count == 0) { Status.Text = ""; return; }
        var left = rows.Select(r => r.Group).Distinct()
            .Select(g => new DupGroup { Size = g.Size, Target = g.Target, Files = g.Files.Where(f => !gone.Contains(f)).ToList() })
            .Where(g => g.Files.Count > 1).ToList();
        ShowGroups(left.OrderByDescending(g => g.Wasted).ToList());
        Status.Text = $"{gone.Count} file{(gone.Count == 1 ? "" : "s")} removed from here";
    }

    void List_DoubleClick(object s, MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null && d is not System.Windows.Controls.ListViewItem) d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        if ((d as FrameworkElement)?.DataContext is Row r) main.ShowFile(r.File.Path); // in File Labs, selected
    }
}
