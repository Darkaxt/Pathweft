using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

using Clipboard = System.Windows.Forms.Clipboard;

namespace FileExplorer;

// Commands beyond copy/move/delete: undo, zip, compare, rename many, checksum, shortcuts, run as admin.
public partial class MainWindow
{
    internal void OpenSettingsFolder() => active.Navigate(Settings.Dir);
    internal static string DownloadsFolder()
    {
        var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");
        int result=SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out var path);
        if(result<0)return "";
        try{return Marshal.PtrToStringUni(path)??"";}
        finally{Marshal.FreeCoTaskMem(path);}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetKnownFolderPath(ref Guid folder,uint flags,IntPtr token,out IntPtr path);
    // ---- Undo (Ctrl+Z, the right-click menu, a transfer's Undo button) ----
    // Each operation is kept as the steps that take it back, in undo.json in the selected bulk-data folder, so it outlives a restart:
    // renames, new folders and files, zips, shortcuts, copies and moves (finished, or cancelled or failed halfway: what
    // they did), and deletes, put back from the Recycle Bin. A file that replaced another isn't undone, since the one it
    // replaced is gone. An entry stays until each of its steps has worked: what failed is tried again next time.
    // Kinds: recycle (Path, made by the operation), move (Path back to To, the exact old path, its old name included),
    // rename (Path to the name To), rmdir (Path, a folder it made, once empty), restore (Path: where it was deleted from;
    // To: that very item in the Recycle Bin, its $R file). Stamp (Ferry.Stamp): what the item looked like when the
    // operation left it; something put in its place since is left alone, and said so.
    internal record UndoStep(string Kind, string Path, string To = null, long Stamp = 0);
    internal record UndoEntry(string What, DateTime When, List<UndoStep> Steps, string Journal = null);

    readonly List<UndoEntry> undo = LoadUndo();
    static string UndoFile => Path.Combine(DataFolder.Bulk, "undo.json");
    static string JournalDir => Path.Combine(DataFolder.Bulk, "undo-journal");
    static readonly object UndoFileLock = new();
    Task<string> undoSave = Task.FromResult<string>(null);
    readonly HashSet<Task> undoWork = new();

    /// The history, plus what transfers that never finished (Pathweft closed or crashed while they ran) wrote in
    /// their journals. A history that can't be read is kept aside as undo.json.bad, not overwritten.
    static List<UndoEntry> LoadUndo()
    {
        List<UndoEntry> list;
        try { list = System.Text.Json.JsonSerializer.Deserialize<List<UndoEntry>>(File.ReadAllText(UndoFile)) ?? new(); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { list = new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            list = new();
            try { File.Copy(UndoFile, UndoFile + ".bad", overwrite: true); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
        }
        string[] journals;
        try { journals = Directory.Exists(JournalDir) ? Directory.GetFiles(JournalDir, "*.txt") : Array.Empty<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { journals = Array.Empty<string>(); }
        foreach (var j in journals)
            if (ReadJournal(j) is { } entry && !list.Any(saved => saved.Journal == entry.Journal)) list.Add(entry);
        if (journals.Length > 0 && WriteUndoFile(list) == null)
            foreach (var j in journals) try { File.Delete(j); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// A journal Ferry wrote while a transfer ran: "move|copy  destination", then "moved|came  from  to  stamp" per item
    /// that arrived, "kept  old  original  stamp" per existing file Keep both set aside, and "dir  path" per folder it made.
    internal static UndoEntry ReadJournal(string file)
    {
        try
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length < 2) return null;
            bool move = lines[0].StartsWith("move");
            var steps = new List<UndoStep>();
            foreach (var parts in lines.Skip(1).Select(l => l.Split('\t')))
                if (parts is ["dir", var dir]) steps.Add(new("rmdir", dir));
                else if (parts is ["removeddir", var removed]) steps.Add(new("mkdir", removed));
                else if (parts is ["kept", var old, var original, var keptStamp] && long.TryParse(keptStamp, out var ks)) steps.Add(new("unkeep", old, original, ks));
                else if (parts is [var how, var from, var to, var stamp] && long.TryParse(stamp, out var st))
                    steps.Add(how == "moved" || move && !Exists(from) ? new("move", to, from, st) : new("recycle", to, null, st));
            int items = steps.Count(s => s.Kind is not ("rmdir" or "unkeep"));
            return steps.Count == 0 ? null : new($"{(move ? "move" : "copy")} of {items:N0} items (interrupted)", File.GetCreationTime(file), steps, Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// Written whole to a side file, then put in place in one rename, so a crash mid-write leaves the old history whole.
    static string WriteUndoFile(List<UndoEntry> list)
    {
        lock (UndoFileLock)
            try
            {
                var tmp = UndoFile + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(UndoFile));
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    System.Text.Json.JsonSerializer.Serialize(stream, list);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tmp, UndoFile, overwrite: true);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// Saved off the UI thread; a failure is said in the status line. `then` (a transfer's journal) is deleted once
    /// the history that holds its steps is on disk.
    void SaveUndo(string then = null)
    {
        var snapshot = undo.ToList();
        _ = QueueUndoSave(snapshot, then);
    }

    Task<string> QueueUndoSave(List<UndoEntry> snapshot, string then = null)
    {
        var previous = undoSave;
        return undoSave = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            if (WriteUndoFile(snapshot) is { } error) { _ = Dispatcher.BeginInvoke(() => Status.Text = "Undo history not saved: " + error); return error; }
            if (then != null) try { File.Delete(then); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return null;
        });
    }

    async Task FinishUndoWork()
    {
        await undoCompletion;
        while (undoWork.Count > 0) await Task.WhenAll(undoWork.ToArray());
        await undoSave;
    }

    UndoEntry Remember(string what, IEnumerable<UndoStep> steps, DateTime? when = null, string then = null)
    {
        var entry = new UndoEntry(what, when ?? DateTime.Now, steps.ToList(), then == null ? null : Path.GetFileName(then));
        undo.Add(entry);
        if (undo.Count > 50) undo.RemoveAt(0);
        SaveUndo(then);
        return entry;
    }

    UndoEntry Remember(string what, params UndoStep[] steps) => Remember(what, steps, null);

    /// Something an operation just made, to send to the Recycle Bin on Undo while it still looks the same.
    static UndoStep Made(string path) => new("recycle", path, null, Ferry.Stamp(path));

    UndoEntry undoing; // a Ctrl+Z still under way
    Task undoCompletion = Task.CompletedTask;
    readonly Dictionary<Job, Action> undoJobs = new(); // moves that undo a move: not undoable themselves; each says when it's done
    readonly Dictionary<Job, UndoEntry> jobUndo = new(); // a transfer's card → its entry, for its Undo button

    void Undo() => Undo(undo.Count > 0 ? undo[^1] : null);

    async void Undo(UndoEntry entry)
    {
        if (undoing != null) { Status.Text = "Still undoing " + undoing.What; return; }
        if (entry == null || !undo.Contains(entry)) { Status.Text = "Nothing to undo"; return; }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        undoCompletion = completed.Task;
        undoing = entry;
        Status.Text = $"Undoing {entry.What}…";
        var left = new List<UndoStep>();
        string error = null;
        int gone = 0, changed = 0;
        // Off the UI thread: what was changed since is set aside, names go back (all together, so names swapped among
        // themselves don't collide), deleted items come back from the Recycle Bin, each the very item that was deleted.
        var ready = await Task.Run(() =>
        {
            var kept = new List<UndoStep>();
            foreach (var s in entry.Steps)
                if (s.Kind is "recycle" or "move" or "rename" or "unkeep" && Exists(s.Path) && (s.Stamp == 0 || Ferry.Stamp(s.Path) != s.Stamp)) changed++;
                else kept.Add(s);
            foreach (var s in kept.Where(s => s.Kind == "mkdir").OrderBy(s => s.Path.Length))
                try { TransferFile.RequirePlainDestination(s.Path); Directory.CreateDirectory(s.Path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { left.Add(s); error ??= ex.Message; }
            var renames = kept.Where(s => s.Kind == "rename" && Exists(s.Path)).ToList(); // an interrupted rename lists two places for each item
            if (renames.Count > 0)
                try { RenameAll(renames.Select(s => (s.Path, s.To)).ToList()); }
                catch (RenameFailed ex) { left.AddRange(ex.NotBack.Select(n => new UndoStep("rename", n.Now, n.Name, Ferry.Stamp(n.Now)))); error ??= ex.Message; }
            foreach (var s in kept.Where(s => s.Kind == "restore"))
                if (RecycleBinWindow.RestoreItem(s.To ?? "", out bool lost) is { } why) { left.Add(s); error ??= why; }
                else if (lost) gone++; // emptied from the bin: nothing left to bring back, so not kept either
            return kept;
        });
        // then through the shell and through transfers (each item to its exact old path); the folders it made go last, once emptied
        var recycle = ready.Where(s => s.Kind == "recycle" && Exists(s.Path)).Select(s => s.Path).ToList();
        var moves = ready.Where(s => s.Kind == "move" && Exists(s.Path)).GroupBy(s => Path.GetDirectoryName(s.To)).ToList();
        int waiting = (recycle.Count > 0 ? 1 : 0) + moves.Count;
        void OneDone() { if (--waiting == 0) Finish(); }
        async void Finish()
        {
            var rest = await Task.Run(() =>
            {
                // Keep both: once the arrival has gone, the file it set aside gets its name back (never over anything)
                foreach (var s in ready.Where(s => s.Kind == "unkeep" && File.Exists(s.Path)))
                    try { if (Exists(s.To)) throw new IOException(Path.GetFileName(s.To) + " already exists"); File.Move(s.Path, s.To); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { left.Add(s); error ??= ex.Message; }
                foreach (var s in ready.Where(s => s.Kind == "rmdir").OrderByDescending(s => s.Path.Length))
                    try { if (Directory.Exists(s.Path) && !Directory.EnumerateFileSystemEntries(s.Path).Any()) Directory.Delete(s.Path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                // what still stands where the operation put it is tried again next time (a folder still holding something stays, quietly)
                var stays = ready.Where(s => (s.Kind is "recycle" or "move") && Exists(s.Path)).ToList();
                if (stays.Count > 0 || left.Count > 0) stays.AddRange(ready.Where(s => s.Kind == "rmdir" && Directory.Exists(s.Path))); // emptied once the rest goes
                return stays;
            });
            left.AddRange(rest);
            undo.Remove(entry);
            if (left.Count > 0) undo.Add(entry with { Steps = left });
            SaveUndo();
            foreach (var (job, e) in jobUndo.Where(x => x.Value == entry).ToList()) { job.CanUndo = false; job.Refresh(); jobUndo.Remove(job); }
            undoing = null;
            var notes = string.Join(", ", new[] { gone > 0 ? $"{gone} no longer in the Recycle Bin" : null, changed > 0 ? $"{changed} changed since, left alone" : null }.Where(n => n != null));
            Status.Text = (left.Count == 0 ? $"Undone: {entry.What}" : $"Partly undone: {entry.What}. Ctrl+Z tries the rest")
                        + (notes != "" ? $" ({notes})" : "") + (left.Count > 0 && error != null ? $": {error}" : "");
            Left.Refresh(); Right.Refresh();
            completed.TrySetResult();
        }
        if (waiting == 0) { Finish(); return; }
        if (recycle.Count > 0) Recycle(recycle, UIOption.OnlyErrorDialogs, done: OneDone);
        foreach (var back in moves)
        {
            try
            {
                TransferFile.RequirePlainDestination(back.Key);
                Directory.CreateDirectory(back.Key);
                if (Submit(back.Select(s => s.Path).ToList(), back.Key, move: true, ask: false, names: back.ToDictionary(s => s.Path, s => Path.GetFileName(s.To), StringComparer.OrdinalIgnoreCase), journal: false) is { } job)
                {
                    // Undo never overwrites an item created at the original location since the move.
                    job.Resolve(Choice.Skip);
                    undoJobs[job] = OneDone;
                }
                else OneDone();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error ??= ex.Message; OneDone(); }
        }
    }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    // Only actual arrivals belong to Undo, including for a fully successful folder transfer.
    // Later additions to that folder must never be moved or recycled with it. Worked out off the UI thread;
    // the job's journal goes once the history holding the same steps is saved.
    async Task RememberJob(Job job)
    {
        if (undoJobs.Remove(job, out var undone)) { undone(); return; }
        if (job.ArchiveOperation) return;
        var steps = await Task.Run(() =>
        {
            List<string> dirs, removed;
            lock (job.CreatedDirs) dirs = job.CreatedDirs.ToList();
            lock (job.RemovedDirs) removed = job.RemovedDirs.ToList();
            return job.Completed.Select(c => c.Moved || job.IsMove && !Exists(c.From) ? new UndoStep("move", c.To, c.From, c.Stamp) : new UndoStep("recycle", c.To, null, c.Stamp))
                                .Concat(job.Kept.Where(k => File.Exists(k.Old)).Select(k => new UndoStep("unkeep", k.Old, k.Original, k.Stamp)))
                                .Concat(removed.Select(d => new UndoStep("mkdir", d)))
                                .Concat(dirs.Select(d => new UndoStep("rmdir", d))).ToList();
        });
        int items = steps.Count(s => s.Kind is not ("rmdir" or "unkeep"));
        if (steps.Count == 0)
        {
            if (job.Journal != null) _ = Task.Run(() => { try { File.Delete(job.Journal); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } });
            return;
        }
        var what = $"{(job.IsMove ? "move" : "copy")} of {(items == 1 ? Path.GetFileName(steps[0].Path) : $"{items:N0} items")}"
                   + (job.Phase == Phase.Done ? "" : job.IsCancelled ? " (cancelled)" : " (stopped)");
        var entry = Remember(what, steps, null, job.Journal);
        if (jobs.Contains(job)) { jobUndo[job] = entry; job.CanUndo = true; }
        job.Refresh();
    }

    void JobUndo_Click(object s, RoutedEventArgs e) { if (jobUndo.TryGetValue(JobOf(s), out var entry)) Undo(entry); }

    static void RenamePath(string path, string newName)
    {
        if (Directory.Exists(path)) FileSystem.RenameDirectory(path, newName);
        else FileSystem.RenameFile(path, newName);
    }

    static void MovePath(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    /// "name.zip", or "name (2).zip" etc. when taken.
    internal static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    // After a command created something: show it selected, or re-run the search the pane is showing.
    static void ShowCreated(PaneView pane, string path)
    {
        if (pane.InSearch) pane.Refresh();
        else pane.Navigate(pane.Dir, Path.GetFileName(path), record: false);
    }

    // ---- Rename many (F2 with several selected) ----
    // Off the UI thread, the count in the status line. Before the first name changes, a record of where each item may
    // end up (its temporary name or its new one, and how it looks) is saved in the Undo history, so if Pathweft stops
    // halfway Ctrl+Z can still put every name back; it is replaced by the plain entry once the rename is done.
    async void RenameMany()
    {
        var sel = active.Selected;
        var plan = MultiRenameDialog.Ask(this, sel);
        if (plan == null || plan.Count == 0) return;
        var pane = active;
        Status.Text = $"Renaming {plan.Count:N0} items…";
        var temps = plan.Select(p => TempBeside(p.Path)).ToList();
        var stamps = await Task.Run(() => plan.Select(p => Ferry.Stamp(p.Path)).ToList()); // a rename keeps size, dates and creation time
        if (stamps.Any(stamp => stamp == 0)) { Status.Text = "Rename not started: an item could not be identified."; return; }
        var pending = new UndoEntry($"rename of {plan.Count:N0} items (interrupted)", DateTime.Now, plan.SelectMany((p, i) => new[]
        {
            new UndoStep("rename", temps[i], Path.GetFileName(p.Path), stamps[i]),
            new UndoStep("rename", Path.Combine(Path.GetDirectoryName(p.Path), p.NewName), Path.GetFileName(p.Path), stamps[i]),
        }).ToList());
        undo.Add(pending);
        var snapshot = undo.ToList();
        var saveError = await QueueUndoSave(snapshot);
        if (saveError != null) { undo.Remove(pending); Status.Text = "Rename not started: " + saveError; return; }
        var (reverse, failure) = await Task.Run(() =>
        {
            try { return (RenameAll(plan, temps), (RenameFailed)null); }
            catch (RenameFailed ex) { return (null, ex); }
        });
        undo.Remove(pending);
        if (reverse != null)
        {
            Remember($"rename of {plan.Count} items", reverse.Select((r, i) => new UndoStep("rename", r.Path, r.NewName, stamps[i])), null);
            Status.Text = $"Renamed {plan.Count} items" + (saveError != null ? $" (Undo history not saved: {saveError})" : "");
        }
        else
        {
            // names that couldn't go back are kept as an Undo entry: Ctrl+Z tries them again
            if (failure.NotBack.Count > 0) Remember($"names left after a failed rename ({failure.NotBack.Count})", failure.NotBack.Select(n => new UndoStep("rename", n.Now, n.Name, Ferry.Stamp(n.Now))), null);
            else SaveUndo();
            MessageBox.Show(this, failure.NotBack.Count == 0 ? $"{failure.Message}\n\nEvery name was put back as it was."
                : $"{failure.Message}\n\n{failure.NotBack.Count} item(s) couldn't get their old name back, for example “{Path.GetFileName(failure.NotBack[0].Now)}”. Ctrl+Z tries again.", "Rename");
        }
        pane.Refresh();
    }

    static string TempBeside(string path) => Path.Combine(Path.GetDirectoryName(path), $".pathweft-{Guid.NewGuid():N}");

    /// Why a batch rename stopped. NotBack: items that couldn't get their old name back, where each is now.
    internal sealed class RenameFailed(string message, List<(string Now, string Name)> notBack) : IOException(message)
    {
        public List<(string Now, string Name)> NotBack { get; } = notBack;
    }

    /// Renames in two steps (everything to a temporary name first), so new names may reuse old ones —
    /// renumbering 3,4,5 into 1,2,3 would otherwise collide with itself. A failure puts every item back the
    /// same way, through temporary names (an old name may be held by another item of the batch until that one
    /// moves), and throws RenameFailed naming any that couldn't go back. Returns the plan that reverses it.
    internal static List<(string Path, string NewName)> RenameAll(List<(string Path, string NewName)> plan, List<string> temps = null)
    {
        var at = plan.Select(p => p.Path).ToList(); // where each item is right now
        try
        {
            for (int i = 0; i < plan.Count; i++)
            {
                var tmp = temps?[i] ?? TempBeside(plan[i].Path);
                MovePath(at[i], tmp);
                at[i] = tmp;
            }
            for (int i = 0; i < plan.Count; i++)
            {
                var final = Path.Combine(Path.GetDirectoryName(plan[i].Path), plan[i].NewName);
                if (File.Exists(final) || Directory.Exists(final)) throw new IOException($"{plan[i].NewName} already exists");
                MovePath(at[i], final);
                at[i] = final;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            for (int i = 0; i < plan.Count; i++)
                if (at[i] != plan[i].Path && !Path.GetFileName(at[i]).StartsWith(".pathweft-")) // under its new name: parked first
                    try { var tmp = TempBeside(plan[i].Path); MovePath(at[i], tmp); at[i] = tmp; }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            var notBack = new List<(string Now, string Name)>();
            for (int i = 0; i < plan.Count; i++)
                if (at[i] != plan[i].Path)
                    try { MovePath(at[i], plan[i].Path); at[i] = plan[i].Path; }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException) { notBack.Add((at[i], Path.GetFileName(plan[i].Path))); }
            throw new RenameFailed(ex.Message, notBack);
        }
        return plan.Select((p, i) => (at[i], Path.GetFileName(p.Path))).ToList();
    }

    void Compress() => CompressArchive(".zip");
    void CompressArchive(string extension)
    {
        var sel = active.Selected;
        if (sel.Count == 0) return;
        var pane = active;
        var stem = sel.Count == 1 ? (sel[0].IsDir ? sel[0].Name : Path.GetFileNameWithoutExtension(sel[0].Name)) : Path.GetFileName(pane.Dir.TrimEnd('\\'));
        var zip = Unique(Path.Combine(pane.Dir, (stem == "" ? "Archive" : stem) + extension));
        var paths = sel.Select(e => e.Path).ToList();
        SubmitArchiveEdit(paths, pane.Dir, "Create " + extension[1..].ToUpperInvariant(), job =>
        {
            Archive.Create(zip, Archive.Files(paths, ""), job);
            job.Arrivals = new() { Path.GetFileName(zip) };
        });
    }

    void Extract(Entry e, bool toFolder)
    {
        var pane = active;
        var here = Path.GetDirectoryName(e.Path);
        var dest = toFolder ? Unique(Path.Combine(here, Path.GetFileNameWithoutExtension(e.Name))) : here;
        Run(() =>
        {
            var top = Archive.List(e.Path, "").Select(x => Path.Combine(e.Path, x.Name)).ToList();
            if (top.Count == 0) { Status.Text = "The archive is empty."; return; }
            Directory.CreateDirectory(dest);
            Submit(top, dest, false);
        });
    }


    // ---- Disk images: Windows mounts them (as Explorer's Mount does), and the pane opens the new drive ----
    internal static readonly HashSet<string> DiskImageExt = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".img", ".vhd", ".vhdx" };

    async void MountImage(string path)
    {
        var before = DriveInfo.GetDrives().Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // the class, not the file's own: here .vhd belonged to VirtualBox, which has no Mount
        var cls = Path.GetExtension(path).ToLowerInvariant() is ".vhd" or ".vhdx" ? "Windows.VhdFile" : "Windows.IsoFile";
        var info = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), fMask = SEE_MASK_CLASSNAME, lpVerb = "mount", lpFile = path, lpClass = cls, nShow = 1,
                                          hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle };
        if (!ShellExecuteEx(ref info)) { if (Marshal.GetLastWin32Error() != 1223) Status.Text = $"Couldn't mount {Path.GetFileName(path)}"; return; }
        Status.Text = $"Mounting {Path.GetFileName(path)}…";
        var pane = active;
        for (int i = 0; i < 40; i++) // up to 10 s: a VHD waits for its UAC prompt
        {
            await Task.Delay(250);
            if (DriveInfo.GetDrives().FirstOrDefault(d => !before.Contains(d.Name)) is { } drive)
            {
                RefreshDrives();
                pane.NewTab();
                pane.Navigate(drive.Name);
                Status.Text = $"{Path.GetFileName(path)} is {drive.Name.TrimEnd('\\')} — eject it from the drive's right-click menu";
                return;
            }
        }
        Status.Text = $"{Path.GetFileName(path)} is mounted (it was already, or it has no drive letter)";
    }

    const uint SEE_MASK_CLASSNAME = 0x1;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd;
        public string lpVerb, lpFile, lpParameters, lpDirectory; public int nShow; public IntPtr hInstApp, lpIDList; public string lpClass;
        public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIconOrMonitor, hProcess;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    // ---- Compare the two panes (Shift+F2), Total Commander style ----
    // Selects, on each side, what is missing on the other side or newer than its twin (same date but a
    // different size selects both). Folders are compared by name only. F5 then copies the selection over.
    void ComparePanes()
    {
        if (Left.InSearch || Right.InSearch) { Status.Text = "Compare works on two folders, not on search results"; return; }
        var l = Left.Items.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var r = Right.Items.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        static bool Differs(Entry a, Dictionary<string, Entry> other)
        {
            if (!other.TryGetValue(a.Name, out var b) || a.IsDir != b.IsDir) return true;
            if (a.IsDir) return false;
            var seconds = (a.Modified - b.Modified).TotalSeconds;
            return seconds > 2 || Math.Abs(seconds) <= 2 && a.Size != b.Size; // 2 s: FAT and some shares round times
        }
        Left.SetSelection(e => Differs(e, r));
        Right.SetSelection(e => Differs(e, l));
        int nl = Left.Selected.Count, nr = Right.Selected.Count;
        Status.Text = nl + nr == 0 ? "The two folders have the same files" : $"Different: {nl} on the left, {nr} on the right  ·  Ctrl+F5 copies the selection across";
    }

    // ---- Checksum ----
    async void Checksum(Entry e)
    {
        Status.Text = $"Calculating SHA-256 of {e.Name}…";
        string hash;
        try { hash = await Task.Run(() => { using var s = new FileStream(e.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = ex.Message; return; }
        Status.Text = "";
        // A hash copied from a download page is compared automatically.
        var clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
        var label = clip.Length == 64 && clip.All(Uri.IsHexDigit)
            ? clip.Equals(hash, StringComparison.OrdinalIgnoreCase) ? "SHA-256 — matches the hash on the clipboard ✓" : "SHA-256 — does NOT match the hash on the clipboard ✗"
            : "SHA-256 (copy a published hash first and it is compared automatically)";
        if (PromptDialog.Ask(this, e.Name, label, hash, "Copy", selectAll: true) != null) Clipboard.SetText(hash);
    }

    // ---- Shortcuts ----
    void CreateShortcuts()
    {
        var pane = active;
        string last = null;
        foreach (var e in pane.Selected)
        {
            var lnk = Unique(Path.Combine(pane.Dir, e.Name + " - Shortcut.lnk"));
            try { SaveShortcut(lnk, e.Path, e.IsDir ? e.Path : Path.GetDirectoryName(e.Path)); }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { Status.Text = ex.Message; return; }
            Remember($"shortcut {Path.GetFileName(lnk)}", Made(lnk));
            last = lnk;
        }
        if (last != null) ShowCreated(pane, last);
    }

    internal static void SaveShortcut(string lnk, string target, string workingDir)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(workingDir);
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(lnk, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    /// What a shortcut points at, or the path itself when it is not one or points at nothing on disk.
    /// QuickLook is handed the document behind a shortcut, as Explorer's Space shows it (Frostpane does the same).
    internal static string ShortcutTarget(string path) => ShortcutTarget(path, out _);

    /// The folder a shortcut points at, or null: a middle-click opens it in a tab and a drop on the shortcut
    /// goes into it, as in Explorer.
    internal static string ShortcutFolder(string path) => ShortcutTarget(path, out bool folder) is var t && folder && t != path ? t : null;

    static string ShortcutTarget(string path, out bool folder)
    {
        folder = false;
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        var link = (IShellLinkW)new ShellLink();
        var buffer = Marshal.AllocHGlobal(2 * 1024);
        var found = Marshal.AllocHGlobal(592); // WIN32_FIND_DATAW: what the shortcut remembers about its target
        try
        {
            Marshal.WriteInt16(buffer, 0); // stays empty for a shortcut to something that is not a file (Control Panel…)
            Marshal.WriteInt32(found, 0);
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
            link.GetPath(buffer, 1024, found, 0);
            var target = Marshal.PtrToStringUni(buffer);
            if (string.IsNullOrEmpty(target)) return path;
            // a network target is not checked: a server that is off answers after 21 s, and Space runs on the UI
            // thread. Whether it is a folder comes from the shortcut itself (dwFileAttributes, recorded when made).
            if (PaneView.IsRemote(target)) { folder = (Marshal.ReadInt32(found) & (int)FileAttributes.Directory) != 0; return target; }
            folder = Directory.Exists(target);
            return folder || File.Exists(target) ? target : path;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException) { return path; } // not a readable shortcut
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(found); Marshal.ReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath(IntPtr file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation(IntPtr path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    // ---- Run as administrator ----
    internal static readonly HashSet<string> Runnable = Set(".exe", ".bat", ".cmd", ".msi", ".com");

    void RunAsAdmin(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path) }); }
        catch (System.ComponentModel.Win32Exception) { } // the UAC prompt was declined
    }
}
