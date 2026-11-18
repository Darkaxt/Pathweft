#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;

// S50: column menu routing, per-kind column sets, hash columns through the shared queue and cache, Copy details,
// the two dialogs, and TABS-08 (Duplicate, + at This PC). Run with Invoke-AppearanceVerification.ps1 -CrcOnly.
public partial class MainWindow
{
    async Task VerifyColumns50(string root, Action<bool, string> check)
    {
        async Task Settle() => await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        async Task Hashed(PaneView pane)
        {
            for (int i = 0; i < 400; i++)
            {
                await Settle();
                if (pane.HashesPending == 0 && copyDetailsWork.IsCompleted) { await Task.Delay(300); await Settle(); if (pane.HashesPending == 0) return; }
                await Task.Delay(50);
            }
        }
        Entry Row(PaneView pane, string name) => pane.Items.Single(e => e.Name == name);
        string Cell(PaneView pane, string name, string key) => ColumnCatalog.Text(Row(pane, name), key);
        string copied = null;
        CopySink = text => copied = text;

        string folder = Path.Combine(root, "cols"), zip = Path.Combine(root, "cols.zip");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "check.txt"), "123456789", Encoding.ASCII); // CRC-32 CBF43926
        File.WriteAllText(Path.Combine(folder, "hello.txt"), "hello", Encoding.ASCII);     // CRC-32 3610A686, SHA-1 aaf4c61d…
        File.WriteAllBytes(Path.Combine(folder, "empty.bin"), Array.Empty<byte>());       // 00000000
        var big = new byte[2 << 20]; new Random(50).NextBytes(big);
        File.WriteAllBytes(Path.Combine(folder, "big.bin"), big);
        var bigSums = Checksums.OfFile(Path.Combine(folder, "big.bin"), new[] { "CRC-32", "MD5" }, CancellationToken.None);
        string bigCrc = bigSums["CRC-32"].ToUpperInvariant(), bigMd5 = bigSums["MD5"].ToUpperInvariant();
        ZipFile.CreateFromDirectory(folder, zip);
        const string helloSha1 = "AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D";

        SetSinglePane(true); SetActive(Left);
        Settings.ColumnSets.Clear(); Settings.HashLowercase = false; Settings.HashLimitMB = 1; Settings.ArchiveCrc = true; Settings.FolderCrc = false;

        // ---- archives: stored CRC-32, shown by default; the other hashes are not offered there ----
        var listed = Archive.List(zip, "").ToDictionary(i => i.Name);
        check(listed["check.txt"].Crc == 0xCBF43926 && listed["hello.txt"].Crc == 0x3610A686 && listed["sub"].Crc == null, "Archive listing carries each entry's stored CRC-32; folders have none");
        await S1Verification.Navigate(Left, zip); await Settle();
        check(Left.ColumnKind == ColumnCatalog.Archive && Left.ShownColumns.Contains("#CRC-32") && Cell(Left, "check.txt", "#CRC-32") == "CBF43926" && Cell(Left, "empty.bin", "#CRC-32") == "00000000" && Cell(Left, "sub", "#CRC-32") == "",
              "HASH-01: archives show the stored CRC-32 by default, 8 uppercase hex digits, blank for folders");
        Left.SetSort("#CRC-32", false); await Settle();
        var files = Left.List.Items.Cast<Entry>().Where(e => !e.IsDir).ToList();
        check(files.Count == 4 && files.Select(e => Cell(Left, e.Name, "#CRC-32")).SequenceEqual(files.Select(e => Cell(Left, e.Name, "#CRC-32")).OrderBy(v => v, StringComparer.Ordinal)),
              "Hash columns sort by value");
        Left.SetSort("Name", false);
        var menu = new ContextMenu(); Left.FillHeaderMenu(menu);
        var hashes = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Hashes");
        check(hashes.Items.OfType<MenuItem>().Single(i => (string)i.Header == "CRC-32").IsEnabled && !hashes.Items.OfType<MenuItem>().Single(i => (string)i.Header == "SHA-1").IsEnabled,
              "HASH-01: inside archives only CRC-32 can be chosen; the other hashes are disabled with the reason");

        // ---- COLS-01: the header has the column menu; the folder menu ignores header right-clicks ----
        await S1Verification.Navigate(Left, folder); await Settle();
        var grid = (GridView)Left.List.View;
        var header = Descendants(Left.List).OfType<GridViewColumnHeader>().FirstOrDefault(h => h.Column != null);
        check(grid.ColumnHeaderContextMenu != null && System.Windows.Automation.AutomationProperties.GetName(grid.ColumnHeaderContextMenu) == "Columns"
              && header != null && InColumnHeader(header, Left.List) && !InColumnHeader(Left.List, Left.List),
              "COLS-01: a Details column header opens the Columns menu, and the folder menu leaves header right-clicks to it");
        menu = new ContextMenu(); Left.FillHeaderMenu(menu);
        var labels = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToList();
        check(labels.Take(3).SequenceEqual(new[] { "Size column to fit", "Size all columns to fit", "Name" }) && labels.Contains("Date created") && labels.Contains("Owner") && labels.Contains("Hashes") && labels.Last() == "More…"
              && !menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Name").IsEnabled,
              "COLS-01/02: the column menu has Size to fit, Name fixed, the common columns, Hashes and More…");
        check(!Left.ShownColumns.Any(ColumnCatalog.IsHash) && Left.Items.All(e => !e.HasCell("#CRC-32")), "HASH-02: folders show no hash column by default and hash nothing");

        // ---- COLS-03: one layout per kind, applied to every open pane of that kind at once ----
        SetSinglePane(false); await S1Verification.Navigate(Right, folder); await Settle();
        lock (HashQueue.ReadLog) HashQueue.ReadLog.Clear();
        long readsBefore = HashCache.Reads;
        ColumnCatalog.SetLayout(ColumnCatalog.Folder, new[] { "Modified", "Size", "#CRC-32", "#SHA-1" });
        await Settle();
        check(Left.ShownColumns.SequenceEqual(new[] { "Modified", "Size", "#CRC-32", "#SHA-1" }) && Right.ShownColumns.SequenceEqual(Left.ShownColumns),
              "COLS-03: a column chosen in one pane shows at once in every open pane of that kind");
        await Hashed(Left); await Hashed(Right);
        check(Cell(Left, "check.txt", "#CRC-32") == "CBF43926" && Cell(Left, "hello.txt", "#SHA-1") == helloSha1 && Cell(Right, "hello.txt", "#CRC-32") == "3610A686" && Cell(Left, "sub", "#CRC-32") == "",
              "HASH-01: folder hash columns fill in by themselves and match the known values");
        List<(string Disk, string Path)> reads; lock (HashQueue.ReadLog) reads = HashQueue.ReadLog.Where(r => r.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList();
        check(reads.Count > 0 && reads.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).All(g => g.Count() == 1) && reads.Select(r => r.Disk).Distinct().Count() == 1,
              $"HASH-02: one read per file for both hashes, both panes served by one reader for the disk ({reads.Count} reads)");
        check(Row(Left, "big.bin").Offered("#CRC-32") && Cell(Left, "big.bin", "#CRC-32") == "Over 1 MB" && !reads.Any(r => r.Path.EndsWith("big.bin")),
              "HASH-03: a file over the size limit is not read; its cell says why and offers Hash");
        check(PaneView.HashSkipReason(new Entry { Name = "x", Path = Path.Combine(folder, "x"), Size = 10, Attributes = (FileAttributes)0x400000 }) == "Online-only" && HashCache.IsPlaceholder((FileAttributes)0x400000),
              "HASH-03: online-only files are never read by themselves");
        Left.HashNow(new[] { Row(Left, "big.bin") }); await Hashed(Left);
        check(Cell(Left, "big.bin", "#CRC-32") == bigCrc && !Row(Left, "big.bin").Offered("#CRC-32"), "HASH-03: Hash on an over-limit file calculates it");

        // ---- HASH-04: the cache answers a fresh listing without reading the files again ----
        long readsCached = HashCache.Reads;
        await S1Verification.Navigate(Left, root); await S1Verification.Navigate(Left, folder); await Hashed(Left);
        check(HashCache.Reads == readsCached && Cell(Left, "hello.txt", "#SHA-1") == helloSha1 && Cell(Left, "big.bin", "#CRC-32") == bigCrc,
              $"HASH-04: a folder listed again shows its hashes from the cache with no new reads ({readsCached - readsBefore} reads in all)");
        File.WriteAllText(Path.Combine(folder, "hello.txt"), "hello!", Encoding.ASCII); File.SetLastWriteTimeUtc(Path.Combine(folder, "hello.txt"), DateTime.UtcNow.AddMinutes(1));
        await S1Verification.Navigate(Left, root); await S1Verification.Navigate(Left, folder); await Hashed(Left);
        check(Cell(Left, "hello.txt", "#CRC-32") == "9A86C960", "HASH-04: a rewritten file is hashed again, not served its old value");
        Settings.HashLowercase = true; ColumnCatalog.CaseChanged(); await Settle();
        check(Cell(Left, "check.txt", "#CRC-32") == "cbf43926", "HASH-01: Lowercase applies to every hash column at once");
        Settings.HashLowercase = false; ColumnCatalog.CaseChanged(); await Settle();

        // ---- COPY-01: Copy <column> entries and what they copy ----
        var two = new List<Entry> { Row(Left, "check.txt"), Row(Left, "empty.bin") };
        var entries = CopyDetailItems(Left, two).Select(i => (string)i.Header).ToList();
        check(entries.SequenceEqual(new[] { "Copy date modified", "Copy size", "Copy CRC-32", "Copy SHA-1", "Copy…" }), "COPY-01: Copy details offers Copy <column> for each column shown, then Copy… (" + string.Join(", ", entries) + ")");
        var crcEntry = CopyDetailItems(Left, new List<Entry> { Row(Left, "check.txt") }).First(i => (string)i.Header == "Copy CRC-32");
        crcEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await copyDetailsWork;
        check(copied == "CBF43926", "COPY-01: one file copies the value alone");
        CopyDetailItems(Left, two).First(i => (string)i.Header == "Copy CRC-32").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await copyDetailsWork;
        check(copied == "CBF43926 *check.txt" + Environment.NewLine + "00000000 *empty.bin", "COPY-01: several files copy checksum lines, hash *name");
        // a hash nobody has calculated yet (over the limit, so the column left it): a transfer job, then copied
        ColumnCatalog.SetLayout(ColumnCatalog.Folder, new[] { "Size", "#MD5" }); await Settle(); await Hashed(Left);
        bool notYet = !Row(Left, "big.bin").HasCell("#MD5");
        CopyDetailItems(Left, new List<Entry> { Row(Left, "big.bin") }).First(i => (string)i.Header == "Copy MD5").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await copyDetailsWork;
        check(notYet && copied == bigMd5 && Cell(Left, "big.bin", "#MD5") == bigMd5, "COPY-01: a hash not calculated yet is calculated as a transfer job, then copied and shown");

        // ---- COPY-02: every format ----
        var rows = new List<Entry> { Row(Left, "check.txt"), Row(Left, "empty.bin") };
        rows[0].SetCell("#CRC-32", "CBF43926"); rows[1].SetCell("#CRC-32", "00000000");
        string NL = Environment.NewLine;
        string R(string format, bool headerRow = true, string hashCase = "columns") => CopyText.Render(rows, new CopyChoice(format, headerRow, false, hashCase, new() { "Name", "#CRC-32" }), folder);
        check(R("pairs") == $"Name: check.txt{NL}CRC-32: CBF43926{NL}{NL}Name: empty.bin{NL}CRC-32: 00000000", "COPY-02: Name: value blocks");
        check(R("lines") == $"check.txt{NL}CBF43926{NL}{NL}empty.bin{NL}00000000", "COPY-02: values one per line");
        check(R("tsv") == $"Name\tCRC-32{NL}check.txt\tCBF43926{NL}empty.bin\t00000000" && R("tsv", false) == $"check.txt\tCBF43926{NL}empty.bin\t00000000", "COPY-02: tab-separated, with and without the header row");
        check(R("csv") == $"Name,CRC-32{NL}check.txt,CBF43926{NL}empty.bin,00000000", "COPY-02: comma-separated");
        check(R("json").Contains("\"CRC-32\": \"CBF43926\"") && R("json").TrimStart().StartsWith("["), "COPY-02: JSON");
        check(R("markdown").StartsWith($"| Name | CRC-32 |{NL}| --- | --- |{NL}| check.txt | CBF43926 |"), "COPY-02: Markdown table");
        check(R("checksum") == $"CBF43926 *check.txt{NL}00000000 *empty.bin" && R("checksum", hashCase: "lower") == $"cbf43926 *check.txt{NL}00000000 *empty.bin", "COPY-02: checksum file, and the hash case choice");
        check(CopyChoice.Parse(new CopyChoice("csv", false, true, "lower", new() { "Name", "#SHA-1" }).Serialize()) is { Format: "csv", Header: false, FullPaths: true, Case: "lower" } parsed && parsed.Keys.SequenceEqual(new[] { "Name", "#SHA-1" }),
              "COPY-02: Copy…'s choices are remembered");

        // ---- the two dialogs ----
        string preview = null;
        CopyDetailsDialog.VerificationAnswer = d =>
        {
            foreach (ComboBoxItem item in d.FormatBox.Items) if ((string)item.Tag == "tsv") d.FormatBox.SelectedItem = item;
            d.HeaderBox.IsChecked = true; d.Refresh();
            preview = d.PreviewText;
            d.AcceptForVerification();
        };
        ColumnCatalog.SetLayout(ColumnCatalog.Folder, new[] { "Size", "#CRC-32" }); await Settle(); await Hashed(Left);
        CopyWithDialog(Left, new List<Entry> { Row(Left, "check.txt") }); await copyDetailsWork;
        CopyDetailsDialog.VerificationAnswer = null;
        check(preview == $"Name\tSize\tCRC-32{NL}check.txt\t{ColumnCatalog.Value(Row(Left, "check.txt"), "Size")}\tCBF43926" && copied == preview && CopyChoice.Parse(Settings.CopyDetails).Format == "tsv",
              "COPY-02: Copy… starts from the shown columns, previews the chosen format live, copies it and remembers the choice");
        ChooseColumnsDialog.VerificationAnswer = d => { d.Picker.Set("Created", true); d.Picker.Set("#CRC-32", false); d.AcceptForVerification(); };
        Left.ChooseColumns(ColumnCatalog.Folder); await Settle();
        check(Left.ShownColumns.SequenceEqual(new[] { "Size", "Created" }) && Right.ShownColumns.SequenceEqual(Left.ShownColumns) && Cell(Left, "check.txt", "Created").Length > 0,
              "COLS-02: More… adds and removes columns in the chosen order; Date created fills in");
        ChooseColumnsDialog.VerificationAnswer = d => d.ChooseAutomatic();
        Left.ChooseColumns(ColumnCatalog.Folder); await Settle();
        ChooseColumnsDialog.VerificationAnswer = null;
        check(ColumnCatalog.Layout(ColumnCatalog.Folder) == null && !Left.ShownColumns.Contains("Created"), "COLS-03: Automatic goes back to the columns the files have");
        check(ShellProperties.All().Count > 50 && ShellProperties.Find("System.Author") != null, $"COLS-02: More… lists Windows' properties ({ShellProperties.All().Count})");

        // ---- COLS-04: the space analyzer has the same menu ----
        await Left.ToggleSpaceAsync(); await Left.SpaceCompletion; await Settle();
        menu = new ContextMenu(); Left.FillHeaderMenu(menu);
        check(Left.ColumnKind == ColumnCatalog.Space && Left.CopyColumns.FirstOrDefault() == "Space used" && menu.Items.OfType<MenuItem>().Any(i => (string)i.Header == "Hashes"),
              "COLS-04: the space analyzer offers the column menu, Space used first for Copy details");
        await Left.ToggleSpaceAsync(); await Settle();

        // ---- TABS-08 ----
        int count = Left.TabCount;
        Left.DuplicateTab(Left.TabIndex); await Settle();
        check(Left.TabCount == count + 1 && Left.Dir.Equals(folder, StringComparison.OrdinalIgnoreCase), "TABS-08: Duplicate opens a second tab at the same place, beside it and active");
        Left.NewStartTab(); await Settle();
        check(Left.TabCount == count + 2 && Left.Dir == PaneView.ThisPC, "TABS-08: + and Ctrl+T open a new tab at This PC");
        Left.CloseTab(); Left.CloseTab(); await Settle();
        CopySink = null; Settings.HashLimitMB = 256; Settings.ColumnSets.Clear(); Settings.Save();
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
#endif
