using System.IO;
using System.Windows;
using System.Windows.Controls;
using VbFs = Microsoft.VisualBasic.FileIO.FileSystem;

namespace FileExplorer;

/// BROWSE-09: copy and move with a remote side, as a card in the Transfers window (progress, pause, cancel).
/// Existing names are asked about once (replace or skip). A move deletes each source only after it fully
/// arrived; a local source goes to the Recycle Bin. Nothing is journaled: remote work has no Undo.
public partial class MainWindow
{
    void RemoteTransfer(List<string> sources, string dest, bool move)
    {
        if (sources.Count == 0) return;
        bool toRemote = RemoteAddress.IsRemote(dest);
        string verb = move ? "Move" : "Copy";
        string description = toRemote ? $"{verb} to {PaneView.NameOf(dest)} on the server" : $"{verb} from the server";
        var job = Ferry.SubmitRemote(sources, dest, move, description, j => RunRemoteTransfer(j));
        TrackSubmittedJob(job);
    }

    sealed class Bytes(Action<long> report) : IProgress<long> { public void Report(long value) => report(value); }
    sealed record Planned(string Source, string Relative, bool IsDir, long Size, string Top);

    void RunRemoteTransfer(Job job)
    {
        using var cancel = new CancellationTokenSource();
        using var watch = new Timer(_ => { if (job.IsCancelled) try { cancel.Cancel(); } catch (ObjectDisposedException) { } }, null, 200, 200);
        var token = cancel.Token;
        var clients = new Dictionary<string, IRemoteFileSystem>();
        try
        {
            IRemoteFileSystem Client(RemoteAddress at)
            {
                var c = RemoteConnections.Resolve(at);
                if (!clients.TryGetValue(c.Id, out var fs)) clients[c.Id] = fs = RemoteSessions.Open(this, c, token).GetAwaiter().GetResult();
                return fs;
            }
            bool destRemote = RemoteAddress.TryParse(job.Destination, out var destAt);
            var destFs = destRemote ? Client(destAt) : null;

            // Same server, move: renamed in place, nothing travels.
            if (job.IsMove && destRemote && job.Sources.All(s => RemoteAddress.TryParse(s, out var a) && RemoteConnections.Resolve(a).Id == destFs.Connection.Id))
            {
                job.Phase = Phase.Copying; job.TotalFiles = job.Sources.Count;
                foreach (var source in job.Sources)
                {
                    token.ThrowIfCancellationRequested(); job.WaitUntilResumed();
                    RemoteAddress.TryParse(source, out var from);
                    job.Current = RemotePath.Name(from.Path);
                    try { destFs.Move(from.Path, RemotePath.Combine(destAt.Path, RemotePath.Name(from.Path)), token).GetAwaiter().GetResult(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { job.Errors.Enqueue($"{job.Current}: {ex.Message}"); }
                    job.DoneFiles++; job.Refresh();
                }
                job.Arrivals = job.Sources.Select(s => PaneView.NameOf(s)).ToList();
                return;
            }

            // What travels: each source with everything inside it.
            var plan = new List<Planned>();
            foreach (var source in job.Sources)
            {
                token.ThrowIfCancellationRequested();
                if (RemoteAddress.TryParse(source, out var at))
                {
                    var fs = Client(at);
                    string top = RemotePath.Name(at.Path);
                    var parent = fs.List(RemotePath.Parent(at.Path), token).GetAwaiter().GetResult().FirstOrDefault(i => i.Name == top)
                        ?? throw new IOException($"{top} is no longer on the server.");
                    plan.Add(new(source, top, parent.IsDir, Math.Max(0, parent.Size), top));
                    if (parent.IsDir) Walk(fs, at, at.Path, top, top);
                }
                else if (Directory.Exists(source))
                {
                    string top = Path.GetFileName(source.TrimEnd('\\'));
                    plan.Add(new(source, top, true, 0, top));
                    foreach (var path in Directory.EnumerateFileSystemEntries(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    {
                        bool dir = Directory.Exists(path);
                        plan.Add(new(path, top + "/" + Path.GetRelativePath(source, path).Replace('\\', '/'), dir, dir ? 0 : new FileInfo(path).Length, top));
                    }
                }
                else if (File.Exists(source)) { string top = Path.GetFileName(source); plan.Add(new(source, top, false, new FileInfo(source).Length, top)); }
                else job.Errors.Enqueue($"{source} no longer exists.");
            }
            void Walk(IRemoteFileSystem fs, RemoteAddress root, string folder, string relative, string top)
            {
                foreach (var item in fs.List(folder, token).GetAwaiter().GetResult())
                {
                    token.ThrowIfCancellationRequested();
                    string rel = relative + "/" + item.Name;
                    plan.Add(new(RemoteConnections.Resolve(root).Address(item.Path), rel, item.IsDir, Math.Max(0, item.Size), top));
                    if (item.IsDir) Walk(fs, root, item.Path, rel, top);
                }
            }
            job.TotalFiles = plan.Count(p => !p.IsDir); job.TotalBytes = plan.Where(p => !p.IsDir).Sum(p => p.Size);

            // Names already there: one question for all of them.
            var tops = plan.Select(p => p.Top).Distinct(StringComparer.Ordinal).ToList();
            var existing = destRemote ? destFs.List(destAt.Path, token).GetAwaiter().GetResult().Select(i => i.Name).ToHashSet(StringComparer.Ordinal)
                                      : tops.Where(t => Path.Exists(Path.Combine(job.Destination, t))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var clashes = tops.Where(existing.Contains).ToList();
            bool replace = false;
            if (clashes.Count > 0)
            {
                job.Phase = Phase.Conflicts;
                int answer = Dispatcher.Invoke(() => PromptDialog.Choose(this, clashes.Count == 1 ? $"“{clashes[0]}” is already there" : $"{clashes.Count:N0} of these are already there",
                    "Replace the files with the same names, or skip them? Folders are merged either way." + (destRemote ? " Replaced files on the server cannot be brought back." : ""), "Replace", "Skip them", "Cancel"));
                if (answer is < 0 or 2) { job.Cancel(); return; }
                replace = answer == 0;
            }
            job.Phase = Phase.Copying;

            var failedTops = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in plan)
            {
                token.ThrowIfCancellationRequested(); job.WaitUntilResumed(); token.ThrowIfCancellationRequested();
                string name = item.Relative.Split('/')[^1];
                job.Current = name; job.CurrentSize = item.Size; job.CurrentDone = 0;
                long before = job.DoneBytes;
                var progress = new Bytes(done => { job.CurrentDone = done; Interlocked.Exchange(ref job.DoneBytes, before + done); });
                try
                {
                    if (destRemote)
                    {
                        string target = RemotePath.Combine(destAt.Path, item.Relative);
                        if (item.IsDir) { if (!(clashes.Contains(item.Top) && item.Relative == item.Top)) TryCreate(destFs, target); continue; }
                        string local = item.Source;
                        if (RemoteAddress.TryParse(item.Source, out var from))
                        {
                            // server to server: through this run's temp copies
                            local = RemoteTempCopies.Fetch(Client(from), new RemoteItem(name, from.Path, false, item.Size, default), null, token).GetAwaiter().GetResult();
                        }
                        try { destFs.Upload(local, target, replace, progress, token).GetAwaiter().GetResult(); job.CopiedFiles++; }
                        catch (IOException) when (!replace) { job.SkippedFiles++; }
                    }
                    else
                    {
                        string target = Path.Combine(job.Destination, item.Relative.Replace('/', '\\'));
                        if (item.IsDir) { Directory.CreateDirectory(target); continue; }
                        if (File.Exists(target) && !replace) { job.SkippedFiles++; continue; }
                        RemoteAddress.TryParse(item.Source, out var from);
                        Client(from).Download(from.Path, target, progress, token).GetAwaiter().GetResult();
                        job.CopiedFiles++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or Renci.SshNet.Common.SshException)
                {
                    job.Errors.Enqueue($"{item.Relative}: {ex.Message}"); failedTops.Add(item.Top);
                }
                finally
                {
                    if (!item.IsDir) { Interlocked.Exchange(ref job.DoneBytes, before + item.Size); job.DoneFiles++; }
                    job.Refresh();
                }
            }
            job.Arrivals = tops.Where(t => !failedTops.Contains(t)).ToList();

            // A move removes each source only once all of it arrived.
            if (job.IsMove && !job.IsCancelled)
                foreach (var source in job.Sources)
                {
                    string top = RemoteAddress.TryParse(source, out var at) ? RemotePath.Name(at.Path) : Path.GetFileName(source.TrimEnd('\\'));
                    if (failedTops.Contains(top) || job.SkippedFiles > 0 && clashes.Contains(top)) continue; // something stayed behind: the source stays too
                    try
                    {
                        if (at != null) Client(at).Delete(at.Path, plan.First(p => p.Top == top).IsDir, recursive: true, token).GetAwaiter().GetResult();
                        else if (Directory.Exists(source)) VbFs.DeleteDirectory(source, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else VbFs.DeleteFile(source, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException or Renci.SshNet.Common.SshException)
                    { job.Errors.Enqueue($"{top} arrived, but the source could not be removed: {ex.Message}"); }
                }
        }
        catch (OperationCanceledException) when (job.IsCancelled || token.IsCancellationRequested) { job.Cancel(); }
        catch (Exception ex) when (RemoteSessions.IsConnectionFailure(ex)) { job.Errors.Enqueue(ex.Message); }
        finally { foreach (var fs in clients.Values) fs.Dispose(); }

        static void TryCreate(IRemoteFileSystem fs, string folder)
        {
            try { fs.CreateFolder(folder, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (IOException) { } // already there: merged into
        }
    }

    internal const long RemotePreviewLimit = 64L << 20;
    /// A preview's copy of a remote file, fetched through the pane that has its connection open.
    internal async Task<string> RemotePreviewCopy(Entry entry, CancellationToken token)
    {
        if (!RemoteAddress.TryParse(entry.Path, out var at)) throw new IOException("Not a remote address.");
        string id = RemoteConnections.Resolve(at).Id;
        var pane = new[] { active, Other }.FirstOrDefault(p => RemoteAddress.TryParse(p.Dir, out var d) && RemoteConnections.Resolve(d).Id == id)
            ?? throw new IOException("Open its folder on the server to preview it.");
        var fs = await pane.RemoteClient(at);
        return await RemoteTempCopies.Fetch(fs, new RemoteItem(entry.Name, at.Path, false, entry.Size, entry.Modified), null, token);
    }

    /// The reduced menu of remote rows: what a server can do, nothing that needs a local path.
    ContextMenu RemoteItemMenu(PaneView p, List<Entry> sel)
    {
        var m = new ContextMenu();
        bool one = sel.Count == 1;
        m.Items.Add(Item("Open", "", "Enter", () => p.OpenSelected(), one));
        if (one && sel[0].IsDir)
        {
            m.Items.Add(Item("Open in new tab", "", "Middle click", () => { p.NewTab(); p.Navigate(sel[0].Path); }));
            m.Items.Add(Item("Open in other pane", "", "", () => Other.Navigate(sel[0].Path), !Settings.SinglePane));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Item("Copy to other pane", "", "F5", () => Transfer(false), !Settings.SinglePane && (!PaneView.IsVirtual(Other.Dir) || RemoteAddress.IsRemote(Other.Dir))));
        m.Items.Add(Item("Move to other pane", "", "F6", () => Transfer(true), !Settings.SinglePane && (!PaneView.IsVirtual(Other.Dir) || RemoteAddress.IsRemote(Other.Dir))));
        m.Items.Add(Item("Copy address", "", "", () => PutText(string.Join(Environment.NewLine, sel.Select(e => e.Path)), "Address copied")));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Rename", "", "F2", () => p.BeginRename(), one));
        m.Items.Add(Item("Delete permanently…", "", "Del", () => p.RemoteDelete()));
        return m;
    }
    ContextMenu RemoteFolderMenu(PaneView p)
    {
        var m = new ContextMenu();
        m.Items.Add(Item("Paste", "", "Ctrl+V", () => ClipboardPaste(), HasTransferClipboard()));
        m.Items.Add(Item("New folder", "", "Ctrl+Shift+N", p.RemoteNewFolder));
        m.Items.Add(Item("Refresh", "", "F5", () => p.Refresh(force: true)));
        m.Items.Add(new Separator());
        var view = Item("View", "", "", () => { });
        foreach (var item in p.ViewMenuItems()) view.Items.Add(item);
        m.Items.Add(view);
        m.Items.Add(Item("Copy address", "", "", () => PutText(p.Dir, "Address copied")));
        return m;
    }
}
