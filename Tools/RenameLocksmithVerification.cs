#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace FileExplorer;

// TOOLS-01/02 qualification: Rename all through the real RenameMany path (the dialog answered by its hook) and
// What's using this? against a holder this check starts and ends itself. Everything stays under `root`.
public partial class MainWindow
{
    internal async Task VerifyRenameLocksmith20(string root, Action<bool, string> check)
    {
        PowerRename.Checks((ok, label) => check(ok, "TOOLS-01: " + label));
        FileLocksmith.Checks(Path.Combine(root, "locksmith-engine"), (ok, label) => check(ok, "TOOLS-02: " + label));
        int options = Settings.RenameOptions;
        var answer = MultiRenameDialog.VerificationAnswer;
        try { await VerifyRename20(Path.Combine(root, "rename"), check); }
        finally { MultiRenameDialog.VerificationAnswer = answer; Settings.RenameOptions = options; Settings.Save(); }
        await VerifyLocksmith20(Path.Combine(root, "locksmith"), check);
    }

    async Task VerifyRename20(string root, Action<bool, string> check)
    {
        SetActive(Left);
        int n = 0;
        string Fixture(params string[] names)
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, (++n).ToString())).FullName;
            foreach (var name in names) File.WriteAllText(Path.Combine(dir, name), name);
            return dir;
        }
        static void Reset(MultiRenameDialog d)
        {
            d.NameBox.Text = "[N]"; d.ExtBox.Text = "[E]"; d.StartBox.Text = "1"; d.FindBox.Text = ""; d.ReplaceBox.Text = "";
            d.RegexBox.IsChecked = false; d.CaseBox.IsChecked = false; d.AllBox.IsChecked = true; d.EnumBox.IsChecked = false;
            d.FilesBox.IsChecked = true; d.FoldersBox.IsChecked = true;
            d.ApplyBox.SelectedIndex = 0; d.CaseModeBox.SelectedIndex = 0; d.TimeBox.SelectedIndex = 0;
        }
        // Selects `names` in this order in the left pane, answers the dialog and runs the real rename.
        async Task<string> Rename(string dir, string[] names, Action<MultiRenameDialog> set, bool accept = true)
        {
            await S1Verification.Navigate(Left, dir);
            Left.List.SelectedItems.Clear();
            foreach (var name in names) Left.List.SelectedItems.Add(Left.Items.Single(e => e.Name == name));
            MultiRenameDialog.VerificationAnswer = d => { Reset(d); set(d); return accept; };
            await RenameManyAsync();
            return MultiRenameDialog.VerificationLast.Problem;
        }
        // Exact names on disk, case included, each still holding its original content.
        bool Has(string dir, params (string Name, string Content)[] want)
        {
            var actual = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return actual.SequenceEqual(want.Select(w => w.Name).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal)
                && want.All(w => File.ReadAllText(Path.Combine(dir, w.Name)) == w.Content);
        }
        async Task UndoLast() { Undo(); await undoCompletion; await FinishUndoWork(); }

        var dir = Fixture("IMG_2020_photo.jpg", "IMG_2021_beach.jpg");
        await Rename(dir, new[] { "IMG_2020_photo.jpg", "IMG_2021_beach.jpg" }, d => { d.RegexBox.IsChecked = true; d.FindBox.Text = @"IMG_(\d+)_(\w+)"; d.ReplaceBox.Text = "$2-$1"; });
        check(Has(dir, ("photo-2020.jpg", "IMG_2020_photo.jpg"), ("beach-2021.jpg", "IMG_2021_beach.jpg")), "TOOLS-01: a regular expression with groups renames through Rename all");

        dir = Fixture("hello world.TXT", "the quick fox.txt");
        await Rename(dir, new[] { "hello world.TXT" }, d => d.CaseModeBox.SelectedIndex = 1);
        await Rename(dir, new[] { "the quick fox.txt" }, d => d.CaseModeBox.SelectedIndex = 3);
        check(Has(dir, ("HELLO WORLD.TXT", "hello world.TXT"), ("The Quick Fox.txt", "the quick fox.txt")), "TOOLS-01: UPPERCASE and Title Case convert names");

        dir = Fixture("a.txt", "skip.md", "b.txt");
        await Rename(dir, new[] { "a.txt", "skip.md", "b.txt" }, d =>
        {
            d.RegexBox.IsChecked = true; d.EnumBox.IsChecked = true; d.ApplyBox.SelectedIndex = 1;
            d.FindBox.Text = "^[ab]$"; d.ReplaceBox.Text = "file_${start=5,padding=3}";
        });
        check(Has(dir, ("file_005.txt", "a.txt"), ("skip.md", "skip.md"), ("file_006.txt", "b.txt")),
            "TOOLS-01: ${start=5,padding=3} numbers name-only matches and the counter skips an item the search didn't match");
        await UndoLast();
        check(Has(dir, ("a.txt", "a.txt"), ("skip.md", "skip.md"), ("b.txt", "b.txt")), "TOOLS-01: Undo restores every name of an enumerated rename");

        dir = Fixture("report.txt");
        var report = Path.Combine(dir, "report.txt");
        File.SetLastWriteTime(report, new DateTime(2019, 3, 4, 10, 0, 0));
        await Rename(dir, new[] { "report.txt" }, d => { d.TimeBox.SelectedIndex = 1; d.FindBox.Text = "report"; d.ReplaceBox.Text = "report-$YYYY-$MM-$DD"; });
        check(Has(dir, ("report-2019-03-04.txt", "report.txt")), "TOOLS-01: $YYYY-$MM-$DD come from the modified time when chosen");

        dir = Fixture("data.data", "keep.txt");
        await Rename(dir, new[] { "data.data" }, d => { d.ApplyBox.SelectedIndex = 1; d.FindBox.Text = "data"; d.ReplaceBox.Text = "x"; });
        await Rename(dir, new[] { "keep.txt" }, d => { d.ApplyBox.SelectedIndex = 2; d.FindBox.Text = "txt"; d.ReplaceBox.Text = "md"; });
        check(Has(dir, ("x.data", "data.data"), ("keep.md", "keep.txt")), "TOOLS-01: Name only and Extension only limit the replacement");

        dir = Fixture("1.txt", "2.txt");
        await Rename(dir, new[] { "2.txt", "1.txt" }, d => d.NameBox.Text = "[C]");
        check(Has(dir, ("1.txt", "2.txt"), ("2.txt", "1.txt")), "TOOLS-01: a name cycle 1↔2 through the [C] counter pattern swaps both items");
        await UndoLast();
        check(Has(dir, ("1.txt", "1.txt"), ("2.txt", "2.txt")), "TOOLS-01: Undo of a name cycle puts both names back");

        dir = Fixture("MiXeD.txt", "Ünïcødé 名前.txt");
        await Rename(dir, new[] { "MiXeD.txt" }, d => d.CaseModeBox.SelectedIndex = 2);
        await Rename(dir, new[] { "Ünïcødé 名前.txt" }, d => { d.FindBox.Text = "名前"; d.ReplaceBox.Text = "名前-✓"; });
        check(Has(dir, ("mixed.txt", "MiXeD.txt"), ("Ünïcødé 名前-✓.txt", "Ünïcødé 名前.txt")), "TOOLS-01: a case-only change and a Unicode name rename exactly");

        dir = Fixture("Old old.txt", "photo.txt");
        await Rename(dir, new[] { "Old old.txt" }, d => { d.FindBox.Text = "OLD"; d.ReplaceBox.Text = "new"; });
        await Rename(dir, new[] { "photo.txt" }, d => { d.NameBox.Text = "[N]_[C]"; d.StartBox.Text = "7"; });
        check(Has(dir, ("new new.txt", "Old old.txt"), ("photo_7.txt", "photo.txt")), "TOOLS-01: the old [N]/[C] patterns and case-insensitive every-occurrence Find keep their results");

        dir = Fixture("x1.txt", "x2.txt", "y.txt", "z.txt", "a.txt");
        var problem = await Rename(dir, new[] { "x1.txt", "x2.txt" }, d => { d.RegexBox.IsChecked = true; d.FindBox.Text = @"\d"; });
        check(problem.Contains("two files") && MultiRenameDialog.VerificationLast.Rows.Count(r => r.Bad) == 1, "TOOLS-01: two results with one name are shown in red and block Rename");
        problem = await Rename(dir, new[] { "z.txt" }, d => { d.FindBox.Text = "z"; d.ReplaceBox.Text = "y"; });
        check(problem.Contains("already exists"), "TOOLS-01: a result colliding with an item outside the rename blocks it");
        problem = await Rename(dir, new[] { "a.txt" }, d => { d.FindBox.Text = "a"; d.ReplaceBox.Text = "a:b"; });
        check(problem.Contains("characters"), "TOOLS-01: a result with an invalid character blocks Rename");
        problem = await Rename(dir, new[] { "a.txt" }, d => { d.RegexBox.IsChecked = true; d.FindBox.Text = "("; d.ReplaceBox.Text = "b"; });
        check(problem.StartsWith("Invalid regular expression"), "TOOLS-01: a regular expression error is shown and blocks Rename");
        problem = await Rename(dir, new[] { "a.txt" }, d => { d.FindBox.Text = "a"; d.ReplaceBox.Text = new string('n', 260); });
        check(problem.Contains("255"), "TOOLS-01: a name longer than 255 characters blocks Rename");
        check(Has(dir, ("x1.txt", "x1.txt"), ("x2.txt", "x2.txt"), ("y.txt", "y.txt"), ("z.txt", "z.txt"), ("a.txt", "a.txt")), "TOOLS-01: blocked renames leave every name unchanged");

        await Rename(dir, new[] { "a.txt" }, d => { d.RegexBox.IsChecked = true; d.CaseBox.IsChecked = true; d.AllBox.IsChecked = false; d.CaseModeBox.SelectedIndex = 1; d.TimeBox.SelectedIndex = 2; d.FindBox.Text = "a"; d.ReplaceBox.Text = "c"; }, accept: false);
        check(File.Exists(Path.Combine(dir, "a.txt")) && !File.Exists(Path.Combine(dir, "C.TXT")), "TOOLS-01: Cancel leaves every name unchanged");
        check(Settings.RenameOptions != (int)(RenameFlags.UseRegularExpressions | RenameFlags.CaseSensitive | RenameFlags.Uppercase | RenameFlags.AccessTime),
            "TOOLS-01: a cancelled dialog doesn't remember its options");
        await Rename(dir, new[] { "a.txt" }, d => { d.RegexBox.IsChecked = true; d.CaseBox.IsChecked = true; d.AllBox.IsChecked = false; d.CaseModeBox.SelectedIndex = 1; d.TimeBox.SelectedIndex = 2; d.FindBox.Text = "a"; d.ReplaceBox.Text = "c"; });
        check(Settings.RenameOptions == (int)(RenameFlags.UseRegularExpressions | RenameFlags.CaseSensitive | RenameFlags.Uppercase | RenameFlags.AccessTime) && File.Exists(Path.Combine(dir, "C.TXT")),
            "TOOLS-01: options of an accepted rename are remembered");
    }

    async Task VerifyLocksmith20(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        string child = Path.Combine(root, "held-by-child.txt"), mine = Path.Combine(root, "held-by-pathweft.txt");
        File.WriteAllText(child, "child"); File.WriteAllText(mine, "mine");
        // A disposable holder started here and ended here: PowerShell keeping the file open, unshared.
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command",
            $"$f=[IO.File]::Open('{child.Replace("'", "''")}','Open','ReadWrite','None'); [Console]::Out.WriteLine('held'); [Console]::Out.Flush(); Start-Sleep -Seconds 120" })
            start.ArgumentList.Add(a);
        using var holder = Process.Start(start);
        ToolReport w = null;
        FileStream ours = null;
        try
        {
            var line = holder.StandardOutput.ReadLineAsync();
            check(await Task.WhenAny(line, Task.Delay(30000)) == line && line.Result == "held", "TOOLS-02: the controlled holder process opened its file");
            bool refused = false;
            try { File.ReadAllBytes(child); } catch (IOException ex) { refused = InUse(ex.Message); }
            check(refused, "TOOLS-02: reading a file the holder keeps open fails with a sharing violation recognised as in use");
            ours = new FileStream(mine, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            w = WhatsUsing(new[] { child, mine });
            if (!w.IsLoaded)
            {
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                w.Loaded += (_, _) => loaded.TrySetResult();
                await Task.WhenAny(loaded.Task, Task.Delay(10000));
            }
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await w.VerificationRun();
            bool Lists(int pid, string path) => w.VerificationRows.Any(r => r.Name.EndsWith($"({pid})") && r.Value.Split('\n').Any(v => string.Equals(v, path, StringComparison.OrdinalIgnoreCase)));
            check(Lists(holder.Id, child), "TOOLS-02: What's using this? shows the holder process's PID and the path it holds");
            check(Lists(Environment.ProcessId, mine), "TOOLS-02: What's using this? shows Pathweft itself holding its own open file");
            var row = w.VerificationRows.FirstOrDefault(r => r.Name.EndsWith($"({holder.Id})"));
            check(row?.Detail?.Contains("powershell", StringComparison.OrdinalIgnoreCase) == true, "TOOLS-02: the holder's executable path is shown under it");
            check(w.VerificationSummary.Contains("administrator") == !FileLocksmith.IsElevated(), "TOOLS-02: the summary states the visibility limit exactly when not elevated");

            holder.Kill(entireProcessTree: true); await holder.WaitForExitAsync();
            ours.Dispose(); ours = null;
            w.ExtraButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await w.VerificationRun();
            check(!Lists(holder.Id, child) && !Lists(Environment.ProcessId, mine) && w.VerificationRows.All(r => r.State >= 0),
                "TOOLS-02: after both files are released, Refresh no longer lists them");
        }
        finally
        {
            ours?.Dispose();
            try { if (!holder.HasExited) { holder.Kill(entireProcessTree: true); holder.WaitForExit(10000); } } catch (InvalidOperationException) { }
            w?.Close();
        }
    }
}
#endif
