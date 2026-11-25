using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

// ARCHIVE-13: what NanaZip's menu offers for a selection, in Pathweft's Archive group, so that NanaZip's own command
// can leave Pathweft's menus: Open archive, Extract files… / here / to "<name>\", Test archive, Compress and email,
// and the Checksum submenu. Extraction is the same Transfers job as copying entries out of a browsed archive.
public partial class MainWindow
{
    /// The Archive group's glyphs (Segoe Fluent Icons), for commands that have no Explorer artwork of their own.
    internal static string ArchiveGlyph(string header) => header switch
    {
        "Compress to ZIP" or "Compress to 7z" or "Compress to RAR" => "",
        "Compress…" => "",
        "Compress to ZIP and email" or "Compress to 7z and email" => "",
        "Open archive" => "",
        "Extract files…" => "",
        "Extract here" => "",
        "Test archive" => "",
        "Checksum" => "",
        _ when header.StartsWith("Extract to ", StringComparison.Ordinal) => "",
        _ => null,
    };

    /// What the Archive group takes, on disk and inside archives.
    internal static bool IsArchiveCommand(string header) => header != null &&
        (header.StartsWith("Compress", StringComparison.Ordinal) || header.StartsWith("Extract", StringComparison.Ordinal) || header is "Open archive" or "Test archive" or "Checksum");

    /// NanaZip's packaged command (its submenu, "NanaZip"): what it offers is the Archive group's now, so Pathweft's
    /// menus leave it out; Windows' own menu (Ctrl+right-click) keeps it.
    internal static bool IsNanaZip(ShellMenu.Node node) => ShellMenu.Comparable(node.Text ?? "").StartsWith("nanazip", StringComparison.Ordinal);

    /// On disk, before the Compress commands: one archive file's own commands (NanaZip's Open / Extract / Test).
    void AddArchiveOpeners(ContextMenu m, PaneView p, List<Entry> sel)
    {
        var first = sel[0];
        if (sel.Count == 1 && !first.IsDir && Archive.IsArchiveName(first.Path) && File.Exists(first.Path))
        {
            string archive = first.Path;
            m.Items.Add(Item("Open archive", "", "", () => p.Navigate(archive)));
            m.Items.Add(Item("Extract files…", "", "", () => ExtractArchive(archive, null)));
            m.Items.Add(Item("Extract here", "", "", () => ExtractArchive(archive, Path.GetDirectoryName(archive))));
            string folder = ExtractFolderName(archive);
            m.Items.Add(Item($"Extract to \"{folder}\\\"", "", "", () => ExtractArchive(archive, Path.Combine(Path.GetDirectoryName(archive), folder), create: true)));
            m.Items.Add(Item("Test archive", "", "", () => TestArchive(archive)));
        }
    }

    /// On disk, after the Compress commands: for any selection, Compress and email and the Checksum submenu.
    void AddArchiveSenders(ContextMenu m, List<Entry> sel)
    {
        m.Items.Add(Item("Compress to ZIP and email", "", "", () => CompressAndEmail(".zip")));
        m.Items.Add(Item("Compress to 7z and email", "", "", () => CompressAndEmail(".7z")));
        var paths = sel.Select(e => e.Path).ToList();
        m.Items.Add(ChecksumMenu(names => FileChecksums(paths, names)));
    }

    /// "photos.tar.gz" → "photos": the folder Extract to makes, as NanaZip names it.
    internal static string ExtractFolderName(string archive)
    {
        string name = Path.GetFileName(archive);
        foreach (var double_ in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst" })
            if (name.EndsWith(double_, StringComparison.OrdinalIgnoreCase)) return name[..^double_.Length];
        string stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 0 ? stem : name + " (extracted)";
    }

    /// The Checksum submenu: each algorithm, then all of them at once. `run` gets the algorithms chosen.
    internal static MenuItem ChecksumMenu(Action<IReadOnlyCollection<string>> run)
    {
        var menu = Item("Checksum", "", "", () => { });
        foreach (var name in Checksums.Names)
        {
            var item = new MenuItem { Header = name };
            System.Windows.Automation.AutomationProperties.SetName(item, "Checksum " + name);
            if (Checksums.Unavailable(name) is { } why) { item.IsEnabled = false; item.ToolTip = why; ToolTipService.SetShowOnDisabled(item, true); }
            item.Click += (_, _) => run(new[] { name });
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var all = new MenuItem { Header = "All of them" };
        System.Windows.Automation.AutomationProperties.SetName(all, "Checksum all of them");
        all.Click += (_, _) => run(Checksums.Names);
        menu.Items.Add(all);
        return menu;
    }

    // ---- Checksums of files and folders on disk (a folder: every file in it, links not followed) ----
    internal Task checksumWork = Task.CompletedTask; // the last one, for verification
    void FileChecksums(List<string> paths, IReadOnlyCollection<string> names) => checksumWork = FileChecksumsAsync(paths, names);
    async Task FileChecksumsAsync(List<string> paths, IReadOnlyCollection<string> names)
    {
        string label = names.Count == 1 ? names.First() : "checksums";
        string root = paths.Count == 1 && Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0].TrimEnd('\\'));
        Status.Text = $"Calculating {label}…";
        List<(string Name, Dictionary<string, string> Values)> results;
        try
        {
            results = await Task.Run(() =>
            {
                var files = new List<string>();
                void Collect(string path)
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.Directory) == 0) { files.Add(path); return; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return;
                    foreach (var child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.OrdinalIgnoreCase)) Collect(child);
                }
                foreach (var p in paths) Collect(p);
                long total = files.Sum(f => new FileInfo(f).Length), before = 0;
                var list = new List<(string, Dictionary<string, string>)>();
                foreach (var f in files)
                {
                    long size = new FileInfo(f).Length, start = before;
                    // HASH-04: the cache the hash columns fill; asked for by name, so placeholders are read too
                    var values = HashCache.OfFile(f, names, CancellationToken.None, done =>
                    {
                        if (total > 64L << 20) Dispatcher.BeginInvoke(() => Status.Text = $"Calculating {label}… {(start + done) * 100 / Math.Max(1, total)}%");
                    }, recall: true);
                    before += size;
                    list.Add((Path.GetRelativePath(root, f), values));
                }
                return list;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = ex.Message; return; }
        Status.Text = "";
        ShowChecksums(results, names);
    }

    /// One value: the familiar prompt with Copy; several: the results window. A hash on the clipboard is compared.
    internal void ShowChecksums(List<(string Name, Dictionary<string, string> Values)> results, IReadOnlyCollection<string> names)
    {
        if (results.Count == 0) { Status.Text = "No files to checksum"; return; }
        string clip = Checksums.Clipboard(ClipboardText.TryGet().Trim());
        bool Same(string value) => clip != null && value != null && value.Equals(clip, StringComparison.OrdinalIgnoreCase);
        if (results.Count == 1 && names.Count == 1)
        {
            var (name, values) = results[0];
            string algorithm = names.First(), hash = values[algorithm];
            if (hash == null) { Status.Text = Checksums.Unavailable(algorithm); return; }
            string label = clip == null ? $"{algorithm} (copy a published hash first and it is compared automatically)"
                : Same(hash) ? $"{algorithm} — matches the hash on the clipboard ✓" : $"{algorithm} — does NOT match the hash on the clipboard ✗";
#if DEBUG
            ChecksumShownForVerification?.Invoke(label, hash);
            if (ChecksumShownForVerification != null) return;
#endif
            if (PromptDialog.Ask(this, Path.GetFileName(name), label, hash, "Copy", selectAll: true) != null) PutText(hash, "Hash copied");
            return;
        }
        var rows = new List<ReportRow>();
        bool matched = false;
        foreach (var (name, values) in results)
            foreach (var algorithm in names)
            {
                string value = values[algorithm];
                bool same = Same(value); matched |= same;
                string title = results.Count == 1 ? algorithm : names.Count == 1 ? name : $"{name}  ·  {algorithm}";
                rows.Add(new(title, value ?? Checksums.Unavailable(algorithm), same ? "Matches the hash on the clipboard ✓" : null, same ? 1 : value == null ? -1 : 0, Code: true));
            }
        string summary = clip == null ? $"{results.Count:N0} file(s)  ·  copy a published hash first and it is compared automatically"
            : matched ? "The hash on the clipboard matches ✓" : "The hash on the clipboard matches none of these ✗";
        string caption = results.Count == 1 ? Path.GetFileName(results[0].Name) : string.Join(", ", names);
        ToolReport.Open(this, "Checksum", caption, rows, summary, nameWidth: results.Count == 1 ? 90 : 260);
    }
#if DEBUG
    internal Action<string, string> ChecksumShownForVerification;
#endif

    // ---- Extraction: the archive's top-level entries copied out through Transfers (password, conflicts, verify, cancel) ----
    internal Job lastExtractJob; // for verification
    async void ExtractArchive(string archive, string destination, bool create = false)
    {
        if (destination == null)
        {
#if DEBUG
            if (ExtractFolderForVerification != null) destination = ExtractFolderForVerification(archive);
            else
#endif
            {
                var pick = new Microsoft.Win32.OpenFolderDialog { Title = "Extract " + Path.GetFileName(archive) + " to", InitialDirectory = Path.GetDirectoryName(archive) };
                if (pick.ShowDialog(this) != true) return;
                destination = pick.FolderName;
            }
            if (destination == null) return;
        }
        if (Archive.HidesNames(archive) && !Archive.HasPassword(archive) && !AskArchivePassword(archive)) return;
        List<Archive.Item> top;
        Status.Text = $"Reading {Path.GetFileName(archive)}…";
        try { top = await Task.Run(() => Archive.List(archive, "")); }
        catch (IOException ex) { Status.Text = ex.Message; return; }
        Status.Text = "";
        if (top.Count == 0) { Status.Text = $"{Path.GetFileName(archive)} is empty"; return; }
        if (create)
            try { Directory.CreateDirectory(destination); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = ex.Message; return; }
        lastExtractJob = Submit(top.Select(i => Path.Combine(archive, i.Name)).ToList(), destination, move: false);
    }
#if DEBUG
    internal Func<string, string> ExtractFolderForVerification;
#endif

    // ---- Test archive ----
    internal Task<Archive.TestResult> lastTest; // for verification
    void TestArchive(string archive)
    {
        if (Archive.HidesNames(archive) && !Archive.HasPassword(archive) && !AskArchivePassword(archive)) return;
        try { if (Archive.NeedsPassword(archive, new[] { "" }) && !AskArchivePassword(archive)) return; }
        catch (IOException ex) { Status.Text = ex.Message; return; }
        var done = new TaskCompletionSource<Archive.TestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lastTest = done.Task;
        Archive.TestResult result = null; Exception failed = null;
        var job = SubmitArchiveEdit(new[] { archive }, Path.GetDirectoryName(archive), "Test archive", job =>
        {
            try
            {
                result = Archive.Test(archive, job);
                foreach (var (entry, problem) in result.Failures) job.Errors.Enqueue($"{entry}: {problem}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = ex; job.Errors.Enqueue(ex.Message); }
            job.Arrivals = new();
        });
        _ = job.Completion.ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            done.TrySetResult(result);
            if (job.IsCancelled) { Status.Text = "Test cancelled"; return; }
            string name = Path.GetFileName(archive);
            if (result == null) { ShowTestReport(name, new List<ReportRow> { new(name, failed?.Message ?? "Couldn't be tested", null, -1) }, "The archive couldn't be read"); return; }
            var rows = result.Failures.Select(f => new ReportRow(f.Entry, f.Problem, null, -1)).ToList();
            if (rows.Count == 0) rows.Add(new($"{result.Tested:N0} file(s)", $"Everything is OK  ·  {DrivePresentation.Size(result.Bytes)} decoded and checked", null, 1));
            ShowTestReport(name, rows, rows[0].State > 0 ? "No errors" : $"{result.Failures.Count:N0} error(s), {result.Tested:N0} file(s) OK");
        }), TaskScheduler.Default);
    }
    void ShowTestReport(string name, List<ReportRow> rows, string summary) => ToolReport.Open(this, "Test archive", name, rows, summary, nameWidth: 240);

    // ---- Compress and email: the archive made in Pathweft's own storage, then the default mail client's new message ----
    internal Task<string> lastMail; // the archive handed to the mail client, for verification
    void CompressAndEmail(string extension)
    {
        var sel = active.Selected;
        if (sel.Count == 0) return;
        var stem = sel.Count == 1 ? (sel[0].IsDir ? sel[0].Name : Path.GetFileNameWithoutExtension(sel[0].Name)) : Path.GetFileName(active.Dir.TrimEnd('\\'));
        var mailRoot = Path.Combine(DataFolder.Bulk, "mail");
        // what an earlier message no longer needs (the mail client has its own copy once the message window is open)
        try
        {
            if (Directory.Exists(mailRoot))
                foreach (var old in Directory.GetDirectories(mailRoot).Where(d => Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddHours(-12)))
                    try { Directory.Delete(old, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var folder = Directory.CreateDirectory(Path.Combine(mailRoot, Guid.NewGuid().ToString("N"))).FullName;
        var archive = Path.Combine(folder, (stem == "" ? "Archive" : stem) + extension);
        var paths = sel.Select(e => e.Path).ToList();
        var mailed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lastMail = mailed.Task;
        var job = SubmitArchiveEdit(paths, folder, "Create " + extension[1..].ToUpperInvariant() + " to email", job =>
        {
            job.Arrivals = Archive.Create(archive, Archive.Files(paths, ""), job, null);
        });
        _ = job.Completion.ContinueWith(_ =>
        {
            if (job.Phase != Phase.Done || !job.Errors.IsEmpty || !File.Exists(archive))
            {
                try { Directory.Delete(folder, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                mailed.TrySetResult(null);
                return;
            }
            var thread = new Thread(() =>
            {
                int result;
#if DEBUG
                if (MailForVerification != null) result = MailForVerification(archive);
                else
#endif
                    result = Mapi.Send(archive);
                mailed.TrySetResult(archive);
                Dispatcher.BeginInvoke(() => Status.Text = result switch
                {
                    0 or 1 => "",                                         // sent to the mail client, or its window closed
                    Mapi.NoClient => "No mail program is set up for sending (Windows' default mail app setting)",
                    _ => $"The mail program couldn't take the archive (MAPI error {result})",
                });
#if DEBUG
                if (MailForVerification != null) return; // verification inspects the archive, then removes it
#endif
                try { Directory.Delete(folder, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }) { IsBackground = true, Name = "Pathweft mail" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }, TaskScheduler.Default);
    }
#if DEBUG
    internal Func<string, int> MailForVerification;
#endif

    /// Simple MAPI, as NanaZip and 7-Zip hand an archive to the mail program: a new message with it attached, shown to
    /// the user (nothing is sent without them). The Unicode entry point first, so names outside the code page survive.
    static class Mapi
    {
        internal const int NoClient = 3; // MAPI_E_LOGIN_FAILURE: no profile/client; MAPI_E_NOT_SUPPORTED 26 too
        const uint LogonUi = 0x1, Dialog = 0x8;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        sealed class MessageW { public uint Reserved; public string Subject, NoteText, MessageType, DateReceived, ConversationId; public uint Flags; public IntPtr Originator; public uint RecipCount; public IntPtr Recips; public uint FileCount; public IntPtr Files; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FileDescW { public uint Reserved, Flags, Position; public IntPtr PathName, FileName, FileType; }
        [DllImport("mapi32.dll", EntryPoint = "MAPISendMailW", CharSet = CharSet.Unicode)]
        static extern uint MAPISendMailW(IntPtr session, IntPtr hwnd, MessageW message, uint flags, uint reserved);

        internal static int Send(string archive)
        {
            var file = new FileDescW { Position = uint.MaxValue, PathName = Marshal.StringToHGlobalUni(archive), FileName = Marshal.StringToHGlobalUni(Path.GetFileName(archive)) };
            var files = Marshal.AllocHGlobal(Marshal.SizeOf<FileDescW>());
            try
            {
                Marshal.StructureToPtr(file, files, false);
                var message = new MessageW { Subject = Path.GetFileName(archive), FileCount = 1, Files = files };
                try { return (int)MAPISendMailW(IntPtr.Zero, IntPtr.Zero, message, LogonUi | Dialog, 0); }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return NoClient; }
            }
            finally { Marshal.FreeHGlobal(files); Marshal.FreeHGlobal(file.PathName); Marshal.FreeHGlobal(file.FileName); }
        }
    }
}
