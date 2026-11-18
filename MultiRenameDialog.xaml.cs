using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

// Rename many at once (F2 with several selected), Total Commander's multi-rename in small:
// a name pattern with [N] name / [E] extension / [C] counter, then find → replace on the result by PowerRename
// (TOOLS-01): literal or regex, case conversion, ${} counters and $YYYY… dates. The options are remembered.
public partial class MultiRenameDialog : Window
{
    readonly List<Entry> items;
    List<(string Path, string NewName)> plan = new();

    MultiRenameDialog(Window owner, List<Entry> items)
    {
        InitializeComponent();
        AuxiliaryWindows.Prepare(this,Root);
        Owner = owner;
        this.items = items;
        typing.Tick += (_, _) => { typing.Stop(); Update(); };
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        var f = (RenameFlags)Settings.RenameOptions;
        RegexBox.IsChecked = f.HasFlag(RenameFlags.UseRegularExpressions);
        CaseBox.IsChecked = f.HasFlag(RenameFlags.CaseSensitive);
        AllBox.IsChecked = f.HasFlag(RenameFlags.MatchAllOccurrences);
        EnumBox.IsChecked = f.HasFlag(RenameFlags.EnumerateItems);
        FilesBox.IsChecked = !f.HasFlag(RenameFlags.ExcludeFiles);
        FoldersBox.IsChecked = !f.HasFlag(RenameFlags.ExcludeFolders);
        Pick(ApplyBox, f & (RenameFlags.NameOnly | RenameFlags.ExtensionOnly));
        Pick(CaseModeBox, f & (RenameFlags.Uppercase | RenameFlags.Lowercase | RenameFlags.Titlecase | RenameFlags.Capitalized));
        Pick(TimeBox, f & (RenameFlags.CreationTime | RenameFlags.ModificationTime | RenameFlags.AccessTime));
        Loaded += (_, _) => {
#if DEBUG
            if(!HiddenTrialPresentation.SuppressFocus(this))
#endif
                NameBox.Focus();
            NameBox.SelectAll();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter)
            {
                if (typing.IsEnabled) { typing.Stop(); Update(); } // Enter right after typing: the preview catches up first
                if (OkButton.IsEnabled) DialogResult = true;
            }
        };
        Update();
    }

    /// The renames to do (only the names that change), or null if cancelled.
    public static List<(string Path, string NewName)> Ask(Window owner, List<Entry> items)
    {
        var d = new MultiRenameDialog(owner, items);
#if DEBUG
        if (VerificationAnswer is { } answer)
        {
            bool accept = answer(d);
            d.Update();
            accept &= d.OkButton.IsEnabled;
            VerificationLast = (d.Problem.Text, d.rows);
            if (accept) d.Remember();
            d.Close();
            return accept ? d.plan : null;
        }
        HiddenTrialPresentation.Prepare(d);
#endif
        if (d.ShowDialog() != true) return null;
        d.Remember();
        return d.plan;
    }
#if DEBUG
    // Verification answers the dialog without showing it: set the fields, return true to press Rename (only if allowed).
    internal static Func<MultiRenameDialog, bool> VerificationAnswer;
    internal static (string Problem, List<Row> Rows) VerificationLast;
#endif

    void Remember() { Settings.RenameOptions = (int)Flags(); Settings.Save(); }

    static void Pick(ComboBox box, RenameFlags value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => int.Parse((string)i.Tag) == (int)value) ?? box.Items[0];

    static RenameFlags Chosen(ComboBox box) => box.SelectedItem is ComboBoxItem { Tag: string t } ? (RenameFlags)int.Parse(t) : 0;

    internal RenameFlags Flags() =>
        (RegexBox.IsChecked == true ? RenameFlags.UseRegularExpressions : 0) | (CaseBox.IsChecked == true ? RenameFlags.CaseSensitive : 0)
        | (AllBox.IsChecked == true ? RenameFlags.MatchAllOccurrences : 0) | (EnumBox.IsChecked == true ? RenameFlags.EnumerateItems : 0)
        | (FilesBox.IsChecked == true ? 0 : RenameFlags.ExcludeFiles) | (FoldersBox.IsChecked == true ? 0 : RenameFlags.ExcludeFolders)
        | Chosen(ApplyBox) | Chosen(CaseModeBox) | Chosen(TimeBox);

    internal record Row(string Old, string New, Brush Brush, bool Bad);
    List<Row> rows = new();

    // With thousands selected, each keystroke would rebuild the whole preview: it waits for a pause in typing instead,
    // and what is on disk is looked up once per name, not on every keystroke.
    readonly System.Windows.Threading.DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly Dictionary<string, bool> onDisk = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, RenameTime), DateTime> times = new();

    void Option_Changed(object s, RoutedEventArgs e) { if (IsLoaded) Update(); }
    void Choice_Changed(object s, SelectionChangedEventArgs e) { if (IsLoaded) Update(); }

    // Read only when the replacement has date tokens; an item not on disk (inside an archive) uses its listed date.
    DateTime TimeOf(Entry e, RenameTime kind)
    {
        if (times.TryGetValue((e.Path, kind), out var t)) return t;
        try
        {
            FileSystemInfo info = e.IsDir ? new DirectoryInfo(e.Path) : new FileInfo(e.Path);
            t = !info.Exists ? e.Modified : kind switch
            {
                RenameTime.Modified => info.LastWriteTime,
                RenameTime.Accessed => info.LastAccessTime,
                _ => info.CreationTime,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { t = e.Modified; }
        return times[(e.Path, kind)] = t;
    }

    void Changed(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (items.Count < 500) { Update(); return; }
        OkButton.IsEnabled = false; // until the preview has caught up with what was typed
        typing.Stop(); typing.Start();
    }

    void Update()
    {
        int.TryParse(StartBox.Text, out int counter);
        var flags = Flags();
        var rule = new RenameRule(FindBox.Text, ReplaceBox.Text, flags);
        bool dated = PowerRename.UsesFileTime(rule);
        var kind = PowerRename.TimeOf(flags);
        var included = items.Where(e => !(e.IsDir ? flags.HasFlag(RenameFlags.ExcludeFolders) : flags.HasFlag(RenameFlags.ExcludeFiles))).ToHashSet();
        int digits = (counter + Math.Max(1, included.Count) - 1).ToString().Length; // 1..120 → 001..120, so names sort right
        int index = 0; // PowerRename's ${} counter: advances only past items the search matched
        rows = new List<Row>();
        plan = new();
        string problem = PowerRename.Problem(rule);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ours = items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in items.Where(e => !included.Contains(e))) taken.Add(e.Path); // left alone: their names stay taken
        foreach (var e in items)
        {
            if (!included.Contains(e)) { rows.Add(new Row(e.Name, e.Name, MainWindow.Hex("#8A97AA"), false)); continue; }
            var n = e.IsDir ? e.Name : Path.GetFileNameWithoutExtension(e.Name);
            var x = e.IsDir ? "" : Path.GetExtension(e.Name).TrimStart('.');
            var c = counter++.ToString().PadLeft(digits, '0');
            string Fill(string pattern) => pattern.Replace("[N]", n, StringComparison.OrdinalIgnoreCase)
                                                  .Replace("[E]", x, StringComparison.OrdinalIgnoreCase)
                                                  .Replace("[C]", c, StringComparison.OrdinalIgnoreCase);
            var ext = Fill(ExtBox.Text).Trim();
            var name = Fill(NameBox.Text) + (ext == "" ? "" : "." + ext);
            bool matched = PowerRename.Matches(rule, name, e.IsDir);
            name = PowerRename.NewName(rule, name, e.IsDir, false, index, dated ? TimeOf(e, kind) : default);
            if (matched) index++;
            name = name.Trim();

            string bad = name == "" ? "a name would be empty"
                : PowerRename.HasInvalidChars(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? $"“{name}” has characters Windows doesn't allow"
                : name.EndsWith('.') ? $"“{name}” ends with a dot, which Windows drops"
                : name.Length > 255 ? $"“{name[..40]}…” is longer than the 255 characters a name may have"
                : !taken.Add(Path.Combine(Path.GetDirectoryName(e.Path), name)) ? $"two files would be named “{name}”"
                : ExistsElsewhere(Path.Combine(Path.GetDirectoryName(e.Path), name), ours) ? $"“{name}” already exists"
                : null;
            problem ??= bad;
            rows.Add(new Row(e.Name, name, MainWindow.Hex(bad != null ? "#F87171" : name == e.Name ? "#8A97AA" : "#E8EEF6"), bad != null));
            if (name != e.Name) plan.Add((e.Path, name));
        }
        Preview.ItemsSource = rows;
        Problem.Text = problem ?? (plan.Count == 0 ? "Nothing changes yet" : $"{plan.Count} of {items.Count} will be renamed");
        Problem.Foreground = MainWindow.Hex(problem != null ? "#F87171" : "#8A97AA");
        OkButton.IsEnabled = problem == null && plan.Count > 0;
    }

    // Taken by a file that isn't part of this rename (those move out of the way first).
    bool ExistsElsewhere(string path, HashSet<string> ours)
    {
        if (ours.Contains(path)) return false;
        if (!onDisk.TryGetValue(path, out var there)) onDisk[path] = there = File.Exists(path) || Directory.Exists(path);
        return there;
    }

    void Ok_Click(object s, RoutedEventArgs e) => DialogResult = true;
    void Cancel_Click(object s, RoutedEventArgs e) => Close();
}
