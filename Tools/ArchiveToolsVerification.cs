#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;

// ARCHIVE-13 through the real menus on owned fixtures: the Archive group's icons, NanaZip left out of Pathweft's menu
// while installed, Open archive, each Extract choice (a name-encrypting 7z too), Test archive on a good and a damaged
// archive, every checksum against NanaZipC on the same file, and Compress and email up to the MAPI call.
public partial class MainWindow
{
    internal async Task VerifyArchiveTools20(string root, Action<bool, string> check)
    {
        Checksums.Checks(check);
        string work = Path.Combine(root, "archive-tools"), source = Path.Combine(work, "source"), archives = Path.Combine(work, "archives");
        string folder = Path.Combine(source, "photos 子");
        Directory.CreateDirectory(Path.Combine(folder, "inner"));
        Directory.CreateDirectory(archives);
        string text = Path.Combine(folder, "a 子.txt");
        File.WriteAllText(text, string.Concat(Enumerable.Repeat("Pathweft ARCHIVE-13 子\n", 5000)));
        var noise = new byte[1 << 20]; new Random(13).NextBytes(noise); File.WriteAllBytes(Path.Combine(folder, "inner", "noise.bin"), noise);
        const string secret = "archive-13 secret";
        string zip = Path.Combine(archives, "plain.zip"), sealed7z = Path.Combine(archives, "sealed.7z"), damaged = Path.Combine(archives, "damaged.zip"), damaged7z = Path.Combine(archives, "damaged-solid.7z");
        await Task.Run(() =>
        {
            Archive.Create(zip, Archive.Files(new[] { folder }, ""));
            Archive.Create(sealed7z, Archive.Files(new[] { folder }, ""), null, new ArchiveOptions { Format = "7z", Password = secret, EncryptNames = true });
            var bytes = File.ReadAllBytes(zip);
            for (int i = 0; i < 64; i++) bytes[bytes.Length / 2 + i] ^= 0x5A;
            File.WriteAllBytes(damaged, bytes);
            Archive.Create(damaged7z, Archive.Files(new[] { folder }, ""), null, new ArchiveOptions { Format = "7z", Solid = "on" });
            var solid = File.ReadAllBytes(damaged7z);
            for (int i = 0; i < 64; i++) solid[solid.Length / 2 + i] ^= 0x5A;
            File.WriteAllBytes(damaged7z, solid);
        });
        Archive.SetPassword(sealed7z, null);

        static IEnumerable<MenuItem> All(ItemsControl menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) { yield return item; foreach (var child in All(item)) yield return child; }
        }
        static bool HasIcon(MenuItem item) => item.Icon is TextBlock { Text.Length: > 0 } or Image;
        ContextMenu MenuOn(PaneView pane)
        {
            SetActive(pane); archiveMenuForVerification = null; captureS4Menu = true;
            try { pane.S4ContextAction(); } finally { captureS4Menu = false; }
            return archiveMenuForVerification ?? throw new IOException("Context menu was not constructed");
        }
        void Click(ContextMenu menu, Func<MenuItem, bool> which, string label)
        {
            try
            {
                var item = All(menu).Single(which);
                check(item.IsEnabled, "ARCHIVE-13: enabled in the real menu: " + label);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            finally { menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent)); }
        }
        async Task Until(Func<bool> done, string what)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException(what);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        async Task On(string dir, params string[] names)
        {
            await S1Verification.Navigate(Left, dir);
            SetActive(Left);
            Left.SetSelection(e => names.Contains(e.Name, StringComparer.OrdinalIgnoreCase));
            await PreviewSession.DrainAsync();
        }
        static string Hash(string path) => Checksums.OfFile(path, new[] { "SHA-256" }, CancellationToken.None)["SHA-256"];
        bool SameTree(string extracted) =>
            File.Exists(Path.Combine(extracted, "a 子.txt")) && Hash(Path.Combine(extracted, "a 子.txt")) == Hash(text)
            && File.Exists(Path.Combine(extracted, "inner", "noise.bin")) && Hash(Path.Combine(extracted, "inner", "noise.bin")) == Hash(Path.Combine(folder, "inner", "noise.bin"));
        var reports = new List<ToolReport>();
        ToolReport.Opened = w => reports.Add(w);
        try
        {
            // the group, its icons and NanaZip's absence
            await On(archives, "plain.zip");
            var menu = MenuOn(Left);
            var group = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Archive");
            var names = group.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
            foreach (var expected in new[] { "Open archive", "Extract files…", "Extract here", "Extract to \"plain\\\"", "Test archive", "Compress to ZIP", "Compress to 7z", "Compress…", "Compress to ZIP and email", "Compress to 7z and email", "Checksum" })
                check(names.Contains(expected), "ARCHIVE-13: the Archive group offers " + expected);
            var missingIcons = group.Items.OfType<MenuItem>().Where(i => i.Tag == null && !HasIcon(i)).Select(i => i.Header as string).ToList();
            check(HasIcon(group) && missingIcons.Count == 0, "ARCHIVE-13: every Archive command on disk has an icon" + string.Concat(missingIcons.Select(n => " | missing: " + n)));
            var sums = group.Items.OfType<MenuItem>().Single(i => i.Header as string == "Checksum").Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
            check(sums.SequenceEqual(Checksums.Names.Append("All of them")), "ARCHIVE-13: Checksum offers the ten algorithms and all of them");
            check(!All(group).Any(i => i.Header as string == "Checksum (SHA-256)") && !All(menu).Any(i => i.Header as string == "Checksum (SHA-256)"), "ARCHIVE-13: the old single SHA-256 command is gone");
            using (var shell = ShellMenu.LoadExtensions(this, new[] { zip }))
            {
                bool offered = shell.Others.Any(IsNanaZip) || shell.Found.Values.SelectMany(n => n).Any(IsNanaZip);
                check(offered, "ARCHIVE-13: NanaZip is installed here and still offers its command to Windows' menu");
            }
            // NanaZip as an app in Open with (Explorer's own list for .zip) is not its command and stays
            var openWith = menu.Items.OfType<MenuItem>().Where(i => (i.Header as string)?.StartsWith("Open with", StringComparison.Ordinal) == true).SelectMany(All).ToHashSet();
            var nana = All(menu).Where(i => !openWith.Contains(i)).Where(i => i.Header is string h && ShellMenu.Comparable(h).Contains("nanazip", StringComparison.Ordinal)).Select(i => $"{i.Header} ({i.Tag})").ToList();
            check(nana.Count == 0, "ARCHIVE-13: NanaZip's command is not in Pathweft's menu" + string.Concat(nana.Select(n => " | found: " + n)));
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));

            // Open archive browses into it
            await On(archives, "plain.zip");
            var openMenu = MenuOn(Left); Left.Listed = false;
            Click(openMenu, i => i.Header as string == "Open archive", "Open archive");
            await Until(() => Left.Listed && Left.Dir.Equals(zip, StringComparison.OrdinalIgnoreCase), "Open archive");
            check(Left.Items.Any(e => e.Name == "photos 子"), "ARCHIVE-13: Open archive browses into the archive");

            // inside the archive: the Archive group's icons and a checksum of an entry
            await S1Verification.Navigate(Left, Path.Combine(zip, "photos 子"));
            Left.SetSelection(e => e.Name == "a 子.txt"); await PreviewSession.DrainAsync();
            var inside = MenuOn(Left);
            var insideGroup = inside.Items.OfType<MenuItem>().Single(i => i.Header as string == "Archive");
            check(HasIcon(insideGroup) && insideGroup.Items.OfType<MenuItem>().All(HasIcon), "ARCHIVE-13: every Archive command inside an archive has an icon");
            (string Label, string Hash) shown = default;
            ChecksumShownForVerification = (label, hash) => shown = (label, hash);
            Click(inside, i => i.Header as string == "CRC-32" && i.Parent is MenuItem { Header: "Checksum" }, "Checksum CRC-32 inside an archive");
            await checksumWork;
            string oracle = NanaZipHash(text, "CRC-32");
            check(shown.Hash != null && shown.Hash == oracle, $"ARCHIVE-13: CRC-32 of an archive entry equals NanaZipC's for the original ({shown.Hash} / {oracle})");

            // Extract here, Extract to "<name>\", Extract files… (to a chosen folder)
            await On(archives, "plain.zip");
            async Task Extracted() { await Until(() => lastExtractJob != null, "extract job submitted"); await lastExtractJob.Completion; }
            lastExtractJob = null;
            Click(MenuOn(Left), i => i.Header as string == "Extract here", "Extract here");
            await Extracted();
            check(lastExtractJob.Phase == Phase.Done && lastExtractJob.Errors.IsEmpty && SameTree(Path.Combine(archives, "photos 子")), "ARCHIVE-13: Extract here writes the archive's contents beside it through Transfers");
            await On(archives, "plain.zip");
            lastExtractJob = null;
            Click(MenuOn(Left), i => i.Header as string == "Extract to \"plain\\\"", "Extract to");
            await Extracted();
            check(lastExtractJob.Errors.IsEmpty && SameTree(Path.Combine(archives, "plain", "photos 子")), "ARCHIVE-13: Extract to \"plain\\\" makes the folder and extracts into it");
            string chosen = Path.Combine(work, "chosen"); Directory.CreateDirectory(chosen);
            int asked = 0;
            ArchivePasswordAnswerForVerification = _ => { asked++; return secret; };
            ExtractFolderForVerification = _ => chosen;
            await On(archives, "sealed.7z");
            lastExtractJob = null;
            Click(MenuOn(Left), i => i.Header as string == "Extract files…", "Extract files…");
            await Extracted();
            check(asked >= 1 && lastExtractJob.Errors.IsEmpty && SameTree(Path.Combine(chosen, "photos 子")), "ARCHIVE-13: Extract files… asks the password of a name-encrypting 7z and extracts to the chosen folder");
            ExtractFolderForVerification = null;

            // Test archive: good, damaged, and the encrypted one
            async Task<Archive.TestResult> Test(string name)
            {
                await On(archives, name);
                int before = reports.Count;
                lastTest = null;
                Click(MenuOn(Left), i => i.Header as string == "Test archive", "Test archive " + name);
                await Until(() => lastTest != null, "Test archive started " + name);
                var result = await lastTest;
                await Until(() => reports.Count > before, "Test report " + name);
                return result;
            }
            var good = await Test("plain.zip");
            check(good != null && good.Failures.Count == 0 && good.Tested == 2 && reports[^1].VerificationRows.All(r => r.State == 1), "ARCHIVE-13: Test archive passes a good archive and says so");
            var bad = await Test("damaged.zip");
            check(bad != null && bad.Failures.Count > 0 && reports[^1].VerificationRows.Any(r => r.State == -1), "ARCHIVE-13: Test archive reports the entry that fails its stored check" + string.Concat((bad?.Failures ?? Array.Empty<(string, string)>()).Select(f => $" | {f.Entry}: {f.Problem}")));
            var badSolid = await Test("damaged-solid.7z");
            check(badSolid != null && badSolid.Failures.Count > 0 && badSolid.Failures[0].Entry != "(archive)" && reports[^1].VerificationRows.Any(r => r.State == -1), "ARCHIVE-13: Test archive tests a solid 7z in one pass and names the damaged entry" + string.Concat((badSolid?.Failures ?? Array.Empty<(string, string)>()).Select(f => $" | {f.Entry}: {f.Problem}")));
            Archive.SetPassword(sealed7z, null);
            var sealedResult = await Test("sealed.7z");
            check(sealedResult != null && sealedResult.Failures.Count == 0 && sealedResult.Tested == 2, "ARCHIVE-13: Test archive tests a password-protected 7z after asking for its password"
                + $" | tested {sealedResult?.Tested.ToString() ?? "null"} | report: {reports[^1].VerificationSummary}" + string.Concat((sealedResult?.Failures ?? Array.Empty<(string, string)>()).Select(f => $" | {f.Entry}: {f.Problem}")) + string.Concat(reports[^1].VerificationRows.Select(r => $" | row {r.Name}: {r.Value}")));
            ArchivePasswordAnswerForVerification = null;

            // every checksum against NanaZipC, on a file and on a folder
            await On(folder, "a 子.txt");
            int opened = reports.Count;
            Click(MenuOn(Left), i => i.Header as string == "All of them", "Checksum all of them");
            await checksumWork;
            await Until(() => reports.Count > opened, "Checksum report");
            var rows = reports[^1].VerificationRows;
            foreach (var name in Checksums.Names)
            {
                var row = rows.SingleOrDefault(r => r.Name == name);
                if (Checksums.Unavailable(name) != null) { check(row?.State == -1, $"ARCHIVE-13: {name} is reported unavailable on this Windows"); continue; }
                string expected = NanaZipHash(text, name);
                check(row != null && row.Value == expected, $"ARCHIVE-13: {name} equals NanaZipC's ({row?.Value} / {expected})");
            }
            foreach (var name in new[] { "SHA-256", "XXH3-64" })
            {
                shown = default;
                Click(MenuOn(Left), i => i.Header as string == name && i.Parent is MenuItem { Header: "Checksum" }, "Checksum " + name);
                await checksumWork;
                check(shown.Hash == NanaZipHash(text, name), $"ARCHIVE-13: one file, one algorithm ({name}) shows its value in the familiar prompt");
            }
            ChecksumShownForVerification = null;
            await On(source, "photos 子");
            opened = reports.Count;
            Click(MenuOn(Left), i => i.Header as string == "SHA-256" && i.Parent is MenuItem { Header: "Checksum" }, "Checksum of a folder");
            await checksumWork;
            await Until(() => reports.Count > opened, "Folder checksum report");
            var folderRows = reports[^1].VerificationRows;
            check(folderRows.Count == 2 && folderRows.Any(r => r.Value == Hash(text)), "ARCHIVE-13: a folder's checksum covers every file in it");

            // Compress and email: the archive made, handed to the mail call, never sent
            foreach (var (header, extension) in new[] { ("Compress to ZIP and email", ".zip"), ("Compress to 7z and email", ".7z") })
            {
                string handed = null;
                MailForVerification = path => { handed = path; return 0; };
                await On(source, "photos 子");
                Click(MenuOn(Left), i => i.Header as string == header, header);
                string mailed = await lastMail;
                MailForVerification = null;
                bool good_ = mailed != null && mailed == handed && File.Exists(mailed) && mailed.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    && mailed.StartsWith(Path.Combine(DataFolder.Bulk, "mail"), StringComparison.OrdinalIgnoreCase)
                    && Archive.Read(mailed).Any(e => e.Path.EndsWith("a 子.txt", StringComparison.Ordinal));
                check(good_, $"ARCHIVE-13: {header} hands a verified {extension} in Pathweft's storage to the MAPI call (no message is sent)");
                if (mailed != null) try { Directory.Delete(Path.GetDirectoryName(mailed), recursive: true); } catch (IOException) { }
            }
        }
        finally
        {
            ToolReport.Opened = null; ChecksumShownForVerification = null; MailForVerification = null;
            ArchivePasswordAnswerForVerification = null; ExtractFolderForVerification = null;
            foreach (var w in reports) w.Close();
            Archive.SetPassword(sealed7z, null);
        }
    }

    /// The value NanaZip's console prints for one algorithm ("SHA256 for data: …").
    static string NanaZipHash(string file, string name)
    {
        string nana = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\NanaZipC.exe");
        string method = name switch { "CRC-32" => "CRC32", "CRC-64" => "CRC64", "SHA-1" => "SHA1", "SHA-256" => "SHA256", "SHA-384" => "SHA384", "SHA-512" => "SHA512", "XXH3-64" => "XXH3_64bits", _ => name };
        var start = new ProcessStartInfo(nana) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(file) };
        foreach (var a in new[] { "h", "-scrc" + method, Path.GetFileName(file) }) start.ArgumentList.Add(a);
        using var p = Process.Start(start);
        string output = p.StandardOutput.ReadToEnd(); p.WaitForExit();
        var line = output.Split('\n').FirstOrDefault(l => l.Contains(" for data:", StringComparison.Ordinal));
        return line?[(line.IndexOf(':') + 1)..].Trim();
    }
}
#endif
