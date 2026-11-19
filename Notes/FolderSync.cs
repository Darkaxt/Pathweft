using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FileExplorer;

/// BROWSE-06: one difference between the two folders and what to do about it. Nothing runs until Apply.
internal sealed class SyncRow
{
    internal enum Act { Skip, ToRight, ToLeft, DeleteLeft, DeleteRight }
    public string Relative { get; init; } = "";
    public string Kind { get; init; } = "";
    public string LeftText { get; init; } = "";
    public string RightText { get; init; } = "";
    public bool IsDir { get; init; }
    internal Act[] Acts { get; init; } = { Act.Skip };
    public string[] Labels => Acts.Select(a => a switch
    {
        Act.ToRight => IsDir ? "Copy to the right  →" : "Copy  →", Act.ToLeft => IsDir ? "←  Copy to the left" : "←  Copy",
        Act.DeleteLeft => "Delete on the left", Act.DeleteRight => "Delete on the right", _ => "Skip",
    }).ToArray();
    public int Pick { get; set; }
    internal Act Chosen => Acts[Math.Clamp(Pick, 0, Acts.Length - 1)];
}

/// Compare with other pane > Synchronize…: both folders compared (with their subfolders, and by content when
/// asked: xxHash3 through the hash cache), each difference listed with a direction to review. Conflicts
/// (same date but different size or content, a file against a folder) start as Skip. Links are not followed.
internal sealed class FolderSyncDialog : AuxDialog
{
    readonly string left, right;
    readonly CheckBox subfolders, contents;
    readonly Button compare;
    readonly TextBlock state, problem;
    readonly ListBox rows = new() { MaxHeight = 420, MinHeight = 160, Margin = new Thickness(0, 10, 0, 0) };
    CancellationTokenSource running;
    internal List<SyncRow> Rows { get; private set; } = new();

    FolderSyncDialog(Window owner, string left, string right) : base(owner, "Synchronize folders", "Apply", 900)
    {
        this.left = left; this.right = right;
        Body.Children.Add(Label("Left:  " + left, 0)); Body.Children.Add(Label("Right:  " + right, 2));
        var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        subfolders = Check("Compare subfolders", true); contents = Check("Compare contents (reads both files when size and date match)", false);
        subfolders.Margin = contents.Margin = new Thickness(2, 6, 18, 0);
        compare = Action("Compare again", Compare); compare.Margin = new Thickness(0, 2, 0, 0);
        options.Children.Add(subfolders); options.Children.Add(contents); options.Children.Add(compare);
        Body.Children.Add(options);
        rows.ItemTemplate = (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Grid Margin='2,3'><Grid.ColumnDefinitions><ColumnDefinition Width='2.2*'/><ColumnDefinition Width='1.3*'/><ColumnDefinition Width='1.3*'/><ColumnDefinition Width='190'/></Grid.ColumnDefinitions>" +
            "<StackPanel Margin='0,0,10,0'><TextBlock Text='{Binding Relative}' TextTrimming='CharacterEllipsis' ToolTip='{Binding Relative}'/>" +
            "<TextBlock Text='{Binding Kind}' FontSize='11' Opacity='0.65'/></StackPanel>" +
            "<TextBlock Grid.Column='1' Text='{Binding LeftText}' FontSize='11' Opacity='0.8' VerticalAlignment='Center' Margin='0,0,8,0'/>" +
            "<TextBlock Grid.Column='2' Text='{Binding RightText}' FontSize='11' Opacity='0.8' VerticalAlignment='Center' Margin='0,0,8,0'/>" +
            "<ComboBox Grid.Column='3' ItemsSource='{Binding Labels}' SelectedIndex='{Binding Pick, Mode=TwoWay}' VerticalAlignment='Center' MinHeight='26'/>" +
            "</Grid></DataTemplate>");
        rows.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(rows, ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetName(rows, "Differences");
        Body.Children.Add(rows);
        state = Note(""); Body.Children.Add(state);
        Body.Children.Add(Note("Apply copies through the Transfers window (replacements included, as reviewed here) and sends deletions to the Recycle Bin; Ctrl+Z undoes both."));
        problem = Note(""); Body.Children.Add(problem);
        Loaded += (_, _) => Compare();
        Closed += (_, _) => running?.Cancel();
    }

    async void Compare()
    {
        if (running != null) { running.Cancel(); return; }
        var cts = running = new CancellationTokenSource();
        compare.Content = "Stop"; rows.ItemsSource = null; problem.Text = "";
        state.Text = "Comparing…";
        bool deep = subfolders.IsChecked == true, byContent = contents.IsChecked == true;
        var progress = new Progress<string>(text => { if (running == cts) state.Text = text; });
        try
        {
            Rows = await Task.Run(() => FolderSync.Compare(left, right, deep, byContent, progress, cts.Token), cts.Token);
            rows.ItemsSource = Rows;
            int conflicts = Rows.Count(r => r.Chosen == SyncRow.Act.Skip);
            state.Text = Rows.Count == 0 ? "The two folders hold the same files." + (byContent ? " Contents compared." : " Compared by size and date.")
                : $"{Rows.Count:N0} differences, {conflicts:N0} left as Skip to decide." + (byContent ? " Same size and date with different content counts as a difference." : " Files with the same size and date were not read.");
        }
        catch (OperationCanceledException) { state.Text = "Comparison stopped."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { state.Text = "Could not compare: " + ex.Message; }
        finally { if (running == cts) running = null; cts.Dispose(); compare.Content = "Compare again"; }
    }

    protected override bool Validate()
    {
        if (running != null) { problem.Text = "Wait for the comparison to finish, or stop it."; return false; }
        if (Rows.All(r => r.Chosen == SyncRow.Act.Skip)) { problem.Text = "Every row is set to Skip: nothing to apply."; return false; }
        return true;
    }

    internal static List<SyncRow> Ask(Window owner, string left, string right)
    {
        var dialog = new FolderSyncDialog(owner, left, right);
        return dialog.Present() ? dialog.Rows.Where(r => r.Chosen != SyncRow.Act.Skip).ToList() : null;
    }
}

internal static class FolderSync
{
    sealed record Item(string Path, bool IsDir, long Size, DateTime Modified, FileAttributes Attributes);

    static Dictionary<string, Item> Read(string folder, CancellationToken cancel)
    {
        var found = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            cancel.ThrowIfCancellationRequested();
            bool dir = (info.Attributes & FileAttributes.Directory) != 0;
            found[info.Name] = new(info.FullName, dir, dir ? 0 : ((FileInfo)info).Length, info.LastWriteTime, info.Attributes);
        }
        return found;
    }
    static string Describe(Item i) => i == null ? "—" : i.IsDir ? "Folder" : $"{DrivePresentation.Size(i.Size)}  ·  {i.Modified:g}";

    internal static List<SyncRow> Compare(string left, string right, bool deep, bool byContent, IProgress<string> progress, CancellationToken cancel)
    {
        var rows = new List<SyncRow>();
        Walk("", left, right);
        return rows;

        void Walk(string relative, string l, string r)
        {
            progress?.Report("Comparing " + (relative.Length == 0 ? "the two folders" : relative) + "…");
            var a = Read(l, cancel); var b = Read(r, cancel);
            foreach (var name in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(n => n, ExplorerOrder.Instance))
            {
                cancel.ThrowIfCancellationRequested();
                a.TryGetValue(name, out var x); b.TryGetValue(name, out var y);
                string rel = relative.Length == 0 ? name : relative + "\\" + name;
                bool link = (x?.Attributes ?? 0).HasFlag(FileAttributes.ReparsePoint) || (y?.Attributes ?? 0).HasFlag(FileAttributes.ReparsePoint);
                if (x == null || y == null)
                {
                    var only = x ?? y; bool onLeft = x != null;
                    rows.Add(new SyncRow
                    {
                        Relative = rel, IsDir = only.IsDir, LeftText = Describe(x), RightText = Describe(y),
                        Kind = (onLeft ? "Only on the left" : "Only on the right") + (link ? " (a link: copied as a link, not followed)" : ""),
                        Acts = onLeft ? new[] { SyncRow.Act.ToRight, SyncRow.Act.DeleteLeft, SyncRow.Act.Skip } : new[] { SyncRow.Act.ToLeft, SyncRow.Act.DeleteRight, SyncRow.Act.Skip },
                    });
                    continue;
                }
                if (x.IsDir != y.IsDir)
                {
                    rows.Add(new SyncRow { Relative = rel, LeftText = Describe(x), RightText = Describe(y), Kind = "A file on one side, a folder on the other: decide by hand" });
                    continue;
                }
                if (x.IsDir)
                {
                    if (deep && !link) Walk(rel, x.Path, y.Path); // a junction or folder link is compared as itself, never walked into
                    continue;
                }
                double seconds = (x.Modified - y.Modified).TotalSeconds;
                bool sameTime = Math.Abs(seconds) <= 2; // FAT and some shares round times
                if (sameTime && x.Size == y.Size)
                {
                    if (!byContent) continue;
                    var (hx, hy) = (Hash(x), Hash(y));
                    if (hx == null || hy == null)
                    {
                        rows.Add(new SyncRow { Relative = rel, LeftText = Describe(x), RightText = Describe(y), Kind = "Same size and date; content not compared (online-only or unreadable)" });
                        continue;
                    }
                    if (hx == hy) continue;
                    rows.Add(new SyncRow
                    {
                        Relative = rel, LeftText = Describe(x), RightText = Describe(y), Kind = "Same size and date, different content",
                        Acts = new[] { SyncRow.Act.Skip, SyncRow.Act.ToRight, SyncRow.Act.ToLeft },
                    });
                    continue;
                }
                if (sameTime)
                {
                    rows.Add(new SyncRow { Relative = rel, LeftText = Describe(x), RightText = Describe(y), Kind = "Same date, different size", Acts = new[] { SyncRow.Act.Skip, SyncRow.Act.ToRight, SyncRow.Act.ToLeft } });
                    continue;
                }
                bool leftNewer = seconds > 0;
                rows.Add(new SyncRow
                {
                    Relative = rel, LeftText = Describe(x), RightText = Describe(y), Kind = leftNewer ? "Newer on the left" : "Newer on the right",
                    Acts = leftNewer ? new[] { SyncRow.Act.ToRight, SyncRow.Act.ToLeft, SyncRow.Act.Skip } : new[] { SyncRow.Act.ToLeft, SyncRow.Act.ToRight, SyncRow.Act.Skip },
                });
            }
        }

        string Hash(Item item)
        {
            if (HashCache.IsPlaceholder(item.Attributes)) return null; // an online-only file is not downloaded to compare it
            progress?.Report("Reading " + item.Path + "…");
            var key = HashCache.Identify(item.Path);
            if (key is { } k && HashCache.Get(k).TryGetValue("XXH3-64", out var known)) return known;
            try
            {
                var value = Checksums.OfFile(item.Path, new[] { "XXH3-64" }, cancel)["XXH3-64"];
                if (key is { } fresh) HashCache.Put(fresh, new Dictionary<string, string> { ["XXH3-64"] = value });
                return value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }
}

public partial class MainWindow
{
    /// Compare with other pane > Synchronize…: review, then Apply through the transfer engine and the Recycle Bin.
    void SynchronizePanes()
    {
        if (Settings.SinglePane) { Status.Text = "Synchronize needs both panes"; return; }
        string l = Left.Dir, r = Right.Dir;
        if (Left.InSearch || Right.InSearch || PaneView.IsVirtual(l) || PaneView.IsVirtual(r) || !Directory.Exists(l) || !Directory.Exists(r))
        { Status.Text = "Synchronize works on two local or network folders"; return; }
        if (l.TrimEnd('\\').Equals(r.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { Status.Text = "Both panes show the same folder"; return; }
        if (FolderSyncDialog.Ask(this, l, r) is not { Count: > 0 } chosen) return;
        // copies: one transfer per destination folder; the replacements in them were reviewed, so they are confirmed
        foreach (var group in chosen.Where(c => c.Chosen is SyncRow.Act.ToRight or SyncRow.Act.ToLeft)
                                    .GroupBy(c => (c.Chosen == SyncRow.Act.ToRight, Path.GetDirectoryName(c.Relative) ?? "")))
        {
            var (toRight, folder) = group.Key;
            string from = toRight ? l : r, into = Path.Combine(toRight ? r : l, folder);
            try { Directory.CreateDirectory(into); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = ex.Message; continue; }
            var job = Submit(group.Select(c => Path.Combine(from, c.Relative)).ToList(), into, move: false, ask: false);
            job?.Resolve(Choice.Overwrite);
        }
        var deletions = chosen.Where(c => c.Chosen is SyncRow.Act.DeleteLeft or SyncRow.Act.DeleteRight)
                              .Select(c => Path.Combine(c.Chosen == SyncRow.Act.DeleteLeft ? l : r, c.Relative)).ToList();
        if (deletions.Count > 0) RecycleUndoably(deletions, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs);
        Status.Text = $"Synchronizing: {chosen.Count:N0} reviewed changes";
    }
}
