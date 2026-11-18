using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;

// Inside an archive, the same commands as on disk (issue #9): what reads an entry works on an extracted working copy,
// what changes the archive is one staged, verified edit run as a Transfers job.
public partial class MainWindow
{
    ContextMenu ArchiveMenu(PaneView p, string archive)
    {
        var rows = ArchiveSelection(p).Where(e => !e.IsUp).ToList();
        var am = new ContextMenu();
        string changeReason = Archive.ChangeReason(archive);
        bool writable = changeReason.Length == 0;
        MenuItem Writes(MenuItem item)
        {
            if (!writable) { item.IsEnabled = false; item.ToolTip = changeReason; ToolTipService.SetShowOnDisabled(item, true); }
            return item;
        }
        MenuItem WriteAction(string label, string key, Action action, bool enabled = true) => Writes(Item(label, "", key, action, enabled));
        if (rows.Count > 0)
        {
            var first = rows[0];
            bool one = rows.Count == 1, dir = one && first.IsDir;
            var files = rows.Where(e => !e.IsDir).ToList();
            var ext = Path.GetExtension(first.Name);
            am.Items.Add(Item("Open", "", "Enter", p.OpenSelected, one));
            if (dir || one && Archive.IsArchiveName(first.Name)) // a folder, or an archive inside this one: both browse
            {
                am.Items.Add(Item("Open in new tab", "", "Middle click", () => { p.NewTab(); p.Navigate(first.Path); }));
                am.Items.Add(Item("Open in other pane", "", "", () => Other.Navigate(first.Path), !Settings.SinglePane));
            }
            if (one && !dir)
            {
                am.Items.Add(Item("Open with…", "", "", () => ArchiveOpenWith(p, first)));
                if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    am.Items.Add(Item("Run with PowerShell", "", "", () => ArchiveRun(p, first, script: true, administrator: false)));
                    am.Items.Add(Item("Run as administrator", "", "", () => ArchiveRun(p, first, script: true, administrator: true)));
                }
                else if (Runnable.Contains(ext)) am.Items.Add(Item("Run as administrator", "", "", () => ArchiveRun(p, first, script: false, administrator: true)));
            }
            if (files.Count > 0) am.Items.Add(Item("Edit", "", "F4", EditSelected));
            if (rows.Count == 2 && files.Count == 2) am.Items.Add(Item("Compare in editor", "", "", () => ArchiveCompare(p, files)));
            if (NotepadPlusPlus is { } npp && files.Count > 0) am.Items.Add(Item("Edit with Notepad++", "", "", () => ArchiveNotepad(p, files, npp)));
            am.Items.Add(Item("Quick View", "", "Space", () => ToggleQuickView(p, this), one));
            am.Items.Add(WriteAction("Cut", "Ctrl+X", () => ClipboardPut(true)));
            am.Items.Add(Item("Copy", "", "Ctrl+C", () => ClipboardPut(false)));
            am.Items.Add(dir ? WriteAction("Paste into folder", "", () => ClipboardPaste(first.Path), HasTransferClipboard())
                             : WriteAction("Paste", "Ctrl+V", () => ClipboardPaste(), HasTransferClipboard()));
            // the disk menu's own commands: taking entries out of an archive is a copy like any other
            am.Items.Add(Item("Copy to other pane", "", "Ctrl+F5", () => Transfer(false), !Settings.SinglePane));
            am.Items.Add(WriteAction("Move to other pane", "F6", () => Transfer(true), !Settings.SinglePane));
            am.Items.Add(SendTo("Copy to", "", p, rows, move: false));
            am.Items.Add(Writes(SendTo("Move to", "", p, rows, move: true)));
            am.Items.Add(Writes(Item("Group into new folder…", "", "", () => ArchiveGroupIntoFolder(p, rows))));
            am.Items.Add(Item("Add to Drop bin", "", "Ctrl+B", () => AddToBin(rows.Select(x => x.Path))));
            // as Explorer's Copy as path inside a ZIP: the archive's path followed by the entry's
            am.Items.Add(Item("Copy path", "", "Ctrl+Shift+C", () => Clipboard.SetText(Entry.QuotedPaths(rows))));
            am.Items.Add(Item("Copy name", "", "", () => Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(x => x.Name)))));
            foreach (var detail in CopyDetailItems(p, rows)) am.Items.Add(detail);
            am.Items.Add(one ? WriteAction("Rename", "F2", p.BeginRename) : WriteAction("Rename all…", "F2", () => ArchiveRenameMany(p)));
            am.Items.Add(WriteAction("Delete", "Del", () => DeleteFromArchive(p)));
            am.Items.Add(Item("Compress to ZIP", "", "", () => ArchiveCompress(p, ".zip")));
            am.Items.Add(Item("Compress to 7z", "", "", () => ArchiveCompress(p, ".7z")));
            string rarUnavailable = RarNative.AvailabilityReason();
            var rarCreate = Item("Compress to RAR", "", "", () => ArchiveCompress(p, ".rar"), rarUnavailable.Length == 0);
            if (rarUnavailable.Length > 0) { rarCreate.ToolTip = rarUnavailable; ToolTipService.SetShowOnDisabled(rarCreate, true); }
            am.Items.Add(rarCreate);
            am.Items.Add(Item("Compress…", "", "", () => ArchiveCompress(p, null)));
            if (one && !dir) am.Items.Add(ChecksumMenu(names => ArchiveChecksum(p, first, names)));
        }
        else
        {
            am.Items.Add(WriteAction("Paste", "Ctrl+V", () => ClipboardPaste(), HasTransferClipboard()));
            am.Items.Add(WriteAction("New folder", "Ctrl+Shift+N", () => ArchiveNewFolder(p)));
            am.Items.Add(WriteAction("New file…", "Ctrl+N", () => ArchiveNewFile(p)));
            var view = new MenuItem { Header = $"View  ·  {p.ViewModeName}" };
            foreach (var item in p.ViewMenuItems()) view.Items.Add(item);
            am.Items.Add(view);
            var sort = new MenuItem { Header = $"Sort by  ·  {p.SortKey}" };
            foreach (var item in p.SortMenuItems()) sort.Items.Add(item);
            am.Items.Add(sort);
            am.Items.Add(Item("Select by pattern…", "", "Num +", () => SelectByPattern(true)));
            am.Items.Add(Item("Invert selection", "", "Num *", p.InvertSelection));
            am.Items.Add(Item("Refresh", "", "F5 / Ctrl+R", () => p.Refresh(force: true)));
            am.Items.Add(Item(Settings.ShowHidden ? "Hide hidden files" : "Show hidden files", "", "Ctrl+H", ToggleHidden));
        }
        am.Items.Add(Item("Enter archive password…", "", "", () => ArchivePassword(p)));
        am.Items.Add(Item("Forget archive password", "", "", () => { Archive.SetPassword(archive, null); p.Refresh(); }));
        am.Items.Add(Item("Archive working copies…", "", "", ShowArchiveWorkingCopies));
        // the archive file itself, beside it in its folder; for an archive inside an archive, its entry in the outer one
        am.Items.Add(Item("Show the archive", "", "", () => p.Navigate(Path.GetDirectoryName(p.ArchivePath), Path.GetFileName(p.ArchivePath))));
        am.Items.Add(Item("Properties", "", "Alt+Enter", () => ArchiveProperties(p)));
        GroupArchiveMenu(am);
        return am;
    }

    // Reparented as the disk menus are (GroupItemMenu, GroupFolderMenu).
    static void GroupArchiveMenu(ContextMenu menu)
    {
        var remaining = menu.Items.OfType<MenuItem>().ToList(); menu.Items.Clear();
        var show = remaining.Single(item => item.Header as string == "Show the archive"); remaining.Remove(show);
        var properties = remaining.Single(item => item.Header as string == "Properties"); remaining.Remove(properties);
        void Separate() { if (menu.Items.Count > 0 && menu.Items[^1] is not Separator) menu.Items.Add(new Separator()); }
        void Take(ItemsControl target, Func<MenuItem, bool> predicate) { foreach (var item in remaining.Where(predicate).ToArray()) { remaining.Remove(item); target.Items.Add(item); } }
        void Group(string name, string glyph, Func<MenuItem, bool> predicate) { var group = Item(name, glyph, "", () => { }); Take(group, predicate); if (group.Items.Count > 0) menu.Items.Add(group); }
        static bool Named(MenuItem item, params string[] labels) => labels.Contains(item.Header as string);
        static bool Starts(MenuItem item, string start) => (item.Header as string)?.StartsWith(start, StringComparison.Ordinal) == true;
        Take(menu, item => Named(item, "Open", "Open in new tab", "Open in other pane", "Open with…", "Run with PowerShell", "Edit", "Quick View"));
        Separate();
        Take(menu, item => Named(item, "Cut", "Copy", "Paste", "Paste into folder", "Rename", "Rename all…", "Delete"));
        Group("New", "", item => Named(item, "New folder", "New file…"));
        Separate();
        Take(menu, item => Starts(item, "View  ·") || Starts(item, "Sort by  ·"));
        Group("Select", "", item => Named(item, "Select by pattern…", "Invert selection"));
        Take(menu, item => Named(item, "Refresh", "Show hidden files", "Hide hidden files"));
        Group("Transfer", "", item => Named(item, "Copy to other pane", "Move to other pane", "Copy to", "Move to", "Group into new folder…", "Add to Drop bin"));
        Group("Copy details", "", item => Named(item, "Copy path", "Copy name") || item.Tag as string == "copy-detail");
        Group("Archive", "", item => IsArchiveCommand(item.Header as string));
        Group("Tools", "", item => Named(item, "Run as administrator", "Compare in editor", "Edit with Notepad++"));
        Group("Archive tools", "", _ => true);
        Separate(); menu.Items.Add(show); menu.Items.Add(properties);
    }

    /// Real files for an app that needs them (Open with, Edit, Run): the entries extracted to a working copy, kept
    /// until released, as opening one does. Null when cancelled or failed (the status line says why).
    async Task<string[]> ArchiveFiles(PaneView pane, IList<Entry> entries, string purpose)
    {
        if (pane.ArchiveRoot is not { } at || entries.Count == 0) return null;
        if (!ArchivePasswords(entries.Select(e => e.Path))) return null;
        var names = entries.Select(e => e.Name).ToArray();
        try
        {
            var lease = await Task.Run(() => ArchiveContent.Extract(at.Archive, names, at.Inner, purpose));
            Status.Text = "Opened an extracted copy. Changes are not written into the archive. Release it after closing its app in Settings → Archive working copies.";
            return names.Select(n => Path.Combine(lease.Directory, "data", n)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "Could not extract from the archive: " + ex.Message; return null; }
    }
    async void ArchiveOpenWith(PaneView p, Entry e) { if (await ArchiveFiles(p, new[] { e }, "opened file") is [var file]) OpenWith(file); }
    async void ArchiveRun(PaneView p, Entry e, bool script, bool administrator)
    {
        if (await ArchiveFiles(p, new[] { e }, "opened file") is not [var file]) return;
        if (script) RunScript(file, administrator); else RunAsAdmin(file);
    }
    async void ArchiveCompare(PaneView p, List<Entry> two) { if (await ArchiveFiles(p, two, "opened file") is [var a, var b]) EditorWindow.Compare(a, b); }
    async void ArchiveNotepad(PaneView p, List<Entry> files, string npp)
    {
        if (await ArchiveFiles(p, files, "opened file") is { } copies) Process.Start(npp, string.Join(" ", copies.Select(c => $"\"{c}\"")));
    }
    async void ArchiveEdit(PaneView p, List<Entry> files)
    {
        if (await ArchiveFiles(p, files.Take(10).ToList(), "opened file") is { } copies) foreach (var copy in copies) EditorWindow.Open(copy);
    }

    bool ArchiveWritable(PaneView p, out (string Archive, string Inner) at)
    {
        at = default;
        if (p.ArchiveRoot is not { } root) return false;
        at = root;
        if (Archive.ChangeReason(root.Archive) is { Length: > 0 } reason) { Status.Text = reason; return false; }
        return true;
    }
    static bool ValidName(Window owner, string name, string title)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) return true;
        MessageBox.Show(owner, $"A name can't contain {string.Join(" ", Path.GetInvalidFileNameChars().Where(c => c >= ' '))}", title); return false;
    }

    void ArchiveNewFolder(PaneView p)
    {
        if (!ArchiveWritable(p, out var at)) return;
        var name = PromptDialog.Ask(this, "New folder", "Folder name", "New folder", "Create")?.Trim();
        if (string.IsNullOrEmpty(name) || !ValidName(this, name, "New folder")) return;
        SubmitArchiveEdit(new[] { p.Dir }, p.Dir, "New folder in archive", job => { Archive.AddNew(at.Archive, at.Inner, name, null, job); job.Arrivals = new() { name }; });
    }

    // Ctrl+N: Explorer's New list, the file made in a scratch folder and added as any file is
    void ArchiveNewFile(PaneView p)
    {
        if (!ArchiveWritable(p, out var at)) return;
        var dialog = new NewFileDialog(this);
        if (dialog.ShowDialog() != true) return;
        string directory = null;
        try
        {
            ArchiveContent.Reserve(out directory);
            string created = dialog.Create(directory);
            if (created == null) return;
            string made = Path.Combine(directory, created);
            string file = Directory.Exists(made) ? null : made;
            var job = SubmitArchiveEdit(new[] { made }, p.Dir, "New file in archive", j => { Archive.AddNew(at.Archive, at.Inner, created, file, j); j.Arrivals = new() { created }; });
            string scratch = directory; directory = null; // removed once the job has added it
            job.Completion.ContinueWith(_ => { try { Directory.Delete(scratch, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }, TaskScheduler.Default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Status.Text = ex.Message; }
        finally
        {
            if (directory != null) try { Directory.Delete(directory, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // F2 with several selected: one edit for all of them, so names can swap
    void ArchiveRenameMany(PaneView p)
    {
        if (!ArchiveWritable(p, out var at)) return;
        var plan = MultiRenameDialog.Ask(this, p.Selected);
        if (plan is not { Count: > 0 }) return;
        var items = plan.Select(x => (Path.GetFileName(x.Path.TrimEnd('\\')), x.NewName)).ToArray();
        SubmitArchiveEdit(plan.Select(x => x.Path), p.Dir, $"Rename {items.Length:N0} archive entries", job =>
        { Archive.Relocate(at.Archive, at.Inner, items, job); job.Arrivals = items.Select(i => i.Item2).ToList(); });
    }

    void ArchiveGroupIntoFolder(PaneView p, List<Entry> sel)
    {
        if (!ArchiveWritable(p, out var at)) return;
        var name = PromptDialog.Ask(this, "Group into new folder", $"Move the {(sel.Count == 1 ? "selected item" : $"{sel.Count} selected items")} into a new folder named", "New folder", "Group")?.Trim();
        if (string.IsNullOrEmpty(name) || !ValidName(this, name, "Group into new folder")) return;
        if (Archive.Exists(Path.Combine(p.Dir, name))) { MessageBox.Show(this, $"“{name}” already exists here.", "Group into new folder"); return; }
        var items = sel.Select(e => (e.Name, name + "/" + e.Name)).ToArray();
        SubmitArchiveEdit(sel.Select(e => e.Path), p.Dir, "Group into new folder", job => { Archive.Relocate(at.Archive, at.Inner, items, job, folder: name); job.Arrivals = new() { name }; });
    }

    // A new archive of the selection, beside the archive file (the outermost one, for an archive inside an archive).
    // `extension` null: Compress…, where the settings and the archive's place are chosen first (ARCHIVE-12).
    void ArchiveCompress(PaneView p, string extension)
    {
        if (p.ArchiveRoot is not { } at || p.Selected.Where(e => !e.IsUp).ToList() is not { Count: > 0 } sel) return;
        if (!ArchivePasswords(sel.Select(e => e.Path))) return;
        string outer = p.ArchivePath;
        while (outer != null && !File.Exists(outer)) outer = Path.GetDirectoryName(outer);
        if (outer == null) return;
        string folder = Path.GetDirectoryName(outer);
        var stem = sel.Count == 1 ? (sel[0].IsDir ? sel[0].Name : Path.GetFileNameWithoutExtension(sel[0].Name)) : Path.GetFileNameWithoutExtension(p.Dir.TrimEnd('\\'));
        ArchiveOptions options = null; string target;
        if (extension != null) target = Archive.UniqueNew(Path.Combine(folder, (stem == "" ? "Archive" : stem) + extension));
        else if (CompressDialog.Ask(this, Path.Combine(folder, stem == "" ? "Archive" : stem), null) is { } choice) (target, options) = (choice.Path, choice.Options);
        else return;
        var names = sel.Select(e => e.Name).ToArray();
        SubmitArchiveEdit(sel.Select(e => e.Path), Path.GetDirectoryName(target), "Create " + Path.GetExtension(target)[1..].ToUpperInvariant(), job =>
        {
            ArchiveContent.Reserve(out string directory);
            try
            {
                string data = Path.Combine(directory, "data");
                Archive.Extract(at.Archive, names, at.Inner, data);
                job.Arrivals = Archive.Create(target, Archive.Files(names.Select(n => Path.Combine(data, n)), ""), job, options);
            }
            finally { Directory.Delete(directory, recursive: true); }
        });
        Status.Text = $"Creating {Path.GetFileName(target)} beside {Path.GetFileName(outer)}";
    }

    void ArchiveChecksum(PaneView p, Entry e, IReadOnlyCollection<string> names) => checksumWork = ArchiveChecksumAsync(p, e, names);
    async Task ArchiveChecksumAsync(PaneView p, Entry e, IReadOnlyCollection<string> names)
    {
        if (p.ArchiveRoot is not { } at || !ArchivePasswords(new[] { e.Path })) return;
        Status.Text = $"Calculating {(names.Count == 1 ? names.First() : "checksums")} of {e.Name}…";
        Dictionary<string, string> values;
        try
        {
            values = await Task.Run(() =>
            {
                var raw = Archive.EntriesOf(at.Archive, at.Inner, e.Name).Single(x => !x.IsDir);
                var sink = new Checksums.Sink(names, CancellationToken.None);
                Archive.CopyEntry(at.Archive, raw, sink);
                return sink.Values();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Status.Text = ex.Message; return; }
        Status.Text = "";
        ShowChecksums(new() { (e.Name, values) }, names);
    }

    // ---- Properties of archive entries: what the archive records about them, and about itself ----
    async void ArchiveProperties(PaneView p)
    {
        if (p.ArchiveRoot is not { } at) return;
        var sel = p.Selected.Where(e => !e.IsUp).ToList();
        string virtualArchive = p.ArchivePath, dir = p.Dir;
        List<(string, string)> rows;
        try
        {
            rows = await Task.Run(() =>
            {
                var list = new List<(string, string)>();
                string Bytes(long n) => $"{DrivePresentation.Size(n)} ({n:N0} bytes)";
                string inner = Archive.Clean(at.Inner);
                // `root`: the folder counted from; folders are those below it, implied ones (only in file paths) too
                void Contents(List<Archive.Raw> entries, string root)
                {
                    var files = entries.Where(x => !x.IsDir).ToList();
                    var folders = new HashSet<string>(entries.Where(x => x.IsDir).Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
                    foreach (var file in files)
                        for (int slash = file.Path.LastIndexOf('/'); slash > 0; slash = file.Path.LastIndexOf('/', slash - 1)) folders.Add(file.Path[..slash]);
                    int below = folders.Count(x => root.Length == 0 || x.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
                    list.Add(("Size", Bytes(files.Sum(x => x.Size))));
                    list.Add(("Contains", $"{files.Count:N0} files, {below:N0} folders"));
                    if (files.Any(x => x.Encrypted)) list.Add(("Encrypted", $"{files.Count(x => x.Encrypted):N0} of {files.Count:N0} files"));
                }
                if (sel.Count == 1)
                {
                    var e = sel[0];
                    string path = (inner.Length > 0 ? inner + "/" : "") + e.Name;
                    var entries = Archive.EntriesOf(at.Archive, at.Inner, e.Name);
                    var own = entries.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); // none for an implied folder
                    list.Add(("Name", e.Name));
                    list.Add(("Type", Thumbnails.TypeName(e.Name, e.IsDir)));
                    list.Add(("Location", dir));
                    if (e.IsDir) Contents(entries, path);
                    else if (own != null)
                    {
                        list.Add(("Size", Bytes(own.Size)));
                        if (own.Method != null) list.Add(("Method", own.Method));
                        if (own.Crc is uint crc) list.Add(("CRC-32", crc.ToString("X8")));
                        list.Add(("Encrypted", own.Encrypted ? "Yes" : "No"));
                    }
                    if (own != null)
                    {
                        if (own.Modified != default) list.Add(("Modified", own.Modified.ToString("F")));
                        if (own.Created != default) list.Add(("Created", own.Created.ToString("F")));
                        var attributes = own.Attributes & ~FileAttributes.Directory;
                        if (attributes != 0) list.Add(("Attributes", attributes.ToString()));
                        if (own.Comment != null) list.Add(("Comment", own.Comment));
                    }
                }
                else
                {
                    list.Add(("Name", sel.Count == 0 ? Path.GetFileName(dir.TrimEnd('\\')) : $"{sel.Count:N0} items"));
                    list.Add(("Location", sel.Count == 0 ? Path.GetDirectoryName(dir.TrimEnd('\\')) : dir));
                    var scope = sel.Count > 0 ? sel.SelectMany(e => Archive.EntriesOf(at.Archive, at.Inner, e.Name)).ToList()
                              : inner.Length > 0 ? Archive.EntriesOf(at.Archive, "", inner) : Archive.Read(at.Archive);
                    Contents(scope.Where(x => !x.Path.Equals(inner, StringComparison.OrdinalIgnoreCase)).ToList(), inner);
                }
                var (packed, solid, format) = Archive.Summary(at.Archive);
                long unpacked = Archive.Read(at.Archive).Where(x => !x.IsDir).Sum(x => x.Size);
                list.Add(("", ""));
                list.Add(("Archive", virtualArchive));
                list.Add(("Format", format));
                list.Add(("Packed size", Bytes(packed)));
                list.Add(("Unpacked size", Bytes(unpacked)));
                if (unpacked > 0) list.Add(("Ratio", $"{100.0 * packed / unpacked:0.#}%"));
                list.Add(("Solid", solid ? "Yes" : "No"));
                return list;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "Archive properties unavailable: " + ex.Message; return; }

        var grid = new Grid { Margin = new(20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var (label, value) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int row = grid.RowDefinitions.Count - 1;
            if (label == "") { var rule = new Separator { Margin = new(0, 8, 0, 8) }; Grid.SetRow(rule, row); Grid.SetColumnSpan(rule, 2); grid.Children.Add(rule); continue; }
            var name = new TextBlock { Text = label, Foreground = Hex("#8A97AA"), Margin = new(0, 3, 16, 3) };
            var text = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new(0), Background = Brushes.Transparent, Foreground = Foreground, TextWrapping = TextWrapping.Wrap, Padding = new(0), Margin = new(0, 3, 0, 3) };
            System.Windows.Automation.AutomationProperties.SetName(text, label);
            Grid.SetRow(name, row); Grid.SetRow(text, row); Grid.SetColumn(text, 1);
            grid.Children.Add(name); grid.Children.Add(text);
        }
        var title = (sel.Count == 1 ? sel[0].Name : sel.Count == 0 ? Path.GetFileName(dir.TrimEnd('\\')) : $"{sel.Count:N0} items") + " Properties";
        var dialog = new Window
        {
            Owner = this, Title = title, Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, LayoutTransform = ArchiveTextScale() },
            Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 720, Background = Hex("#161B24"), Foreground = Foreground, FontFamily = FontFamily,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
#if DEBUG
        HiddenTrialPresentation.Prepare(dialog);
        archivePropertiesForVerification = dialog;
#endif
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) dialog.Close(); };
        dialog.Show();
    }
#if DEBUG
    internal Window archivePropertiesForVerification;
#endif
}
