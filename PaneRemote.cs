using System.IO;
using System.Windows;

namespace FileExplorer;

/// BROWSE-09: an sftp:// or webdav(s):// folder in a pane. Its rows carry remote addresses, never local
/// paths; nothing is watched, sized, indexed or thumbnailed, and changes go straight to the server with no
/// Undo. One client per pane, for the connection on screen; it closes when the pane leaves that
/// connection, closes, or Pathweft exits.
public partial class PaneView
{
    IRemoteFileSystem remoteFs;
    CancellationTokenSource remoteWork = new();
    Task<IRemoteFileSystem> remoteOpening;
    string remoteOpeningId;

    internal bool IsRemoteFolder => RemoteAddress.IsRemote(Dir);

    /// The connected client for the connection an address belongs to; asks for a password or a new host key when needed.
    internal Task<IRemoteFileSystem> RemoteClient(RemoteAddress address)
    {
        var connection = RemoteConnections.Resolve(address);
        if (remoteFs != null && remoteFs.Connection.Id == connection.Id) return Task.FromResult(remoteFs);
        if (remoteOpening != null && remoteOpeningId == connection.Id) return remoteOpening;
        CloseRemote();
        remoteOpeningId = connection.Id;
        var token = remoteWork.Token;
        return remoteOpening = Open();
        async Task<IRemoteFileSystem> Open()
        {
            try
            {
                var fs = await RemoteSessions.Open(Window.GetWindow(this), connection, token);
                if (token.IsCancellationRequested || remoteOpeningId != connection.Id) { fs.Dispose(); throw new OperationCanceledException(token); }
                remoteFs = fs;
                return fs;
            }
            finally { if (remoteOpeningId == connection.Id) { remoteOpening = null; remoteOpeningId = null; } }
        }
    }

    /// Ends the pane's connection (leaving it, closing, exit); work still running for it is cancelled.
    void CloseRemote()
    {
        remoteWork.Cancel(); remoteWork.Dispose(); remoteWork = new();
        remoteOpening = null; remoteOpeningId = null;
        remoteFs?.Dispose(); remoteFs = null;
    }
    /// Called by GoTo: a folder on another connection, or not remote at all, ends this one.
    void LeaveRemote(string path)
    {
        if (remoteFs == null && remoteOpening == null) return;
        string id = remoteFs?.Connection.Id ?? remoteOpeningId;
        if (RemoteAddress.TryParse(path, out var to) && RemoteConnections.Resolve(to).Id == id) return;
        CloseRemote();
    }

    void NavigateRemote(string address, string select, bool record)
    {
        if (!RemoteAddress.TryParse(address, out var at)) { Error?.Invoke("Not a valid remote address: " + address); return; }
        var connection = RemoteConnections.Resolve(at);
        string canonical = connection.Address(at.Path);
        bool again = canonical.Equals(Dir, StringComparison.Ordinal) && !InSearch;
        int v = GoTo(canonical, record, preserveAddress: again);
        ListingLoading(v, true);
        StopFolderWatch(); sizing?.Cancel(); prefetching?.Cancel();
        ListRemote(v, at, connection, select, again);
    }

    async void ListRemote(int v, RemoteAddress at, RemoteConnection connection, string select, bool again)
    {
        List<RemoteItem> rows = null; Exception failure = null;
        try { var fs = await RemoteClient(at); rows = await fs.List(at.Path, remoteWork.Token); }
        catch (Exception ex) when (RemoteSessions.IsConnectionFailure(ex)) { failure = ex; }
        if (v != navVersion) return;
        ListingLoaded(v);
        if (failure != null)
        {
            if (failure is not IOException) CloseRemote(); // a dropped or refused connection is opened afresh next time
            Listed = false;
            ShowItems(new List<Entry>(), null);
            ShowFolderFailure(failure);
            Error?.Invoke(failure is OperationCanceledException ? "Not connected to " + at.Host : failure.Message);
            StatsChanged?.Invoke();
            return;
        }
        bool hidden = ShowsHidden;
        var list = rows.Where(r => hidden || !r.Name.StartsWith('.')).Select(r => new Entry
        {
            Name = r.Name, Path = connection.Address(r.Path), IsDir = r.IsDir, Size = r.IsDir ? -2 : r.Size,
            Modified = r.Modified == default ? default : r.Modified.ToLocalTime(), RestrictionChecked = true,
            SmallIcon = Thumbnails.TypeIcon(r.Name, r.IsDir), Thumb = Thumbnails.TypeIcon(r.Name, r.IsDir),
        }).ToList();
        ClearFolderFailure();
        ShowItems(list, select, animate: !again, keep: again, concealed: rows.Count - list.Count);
        if (pendingSelect != null) ApplyPendingSelect();
        Listed = true;
        UpdateFooter();
        StatsChanged?.Invoke();
    }

    /// The crumbs of a remote folder: the connection, then each folder down to this one.
    static List<(string Name, string Path)> RemoteCrumbs(RemoteAddress at)
    {
        var c = RemoteConnections.Resolve(at);
        var parts = new List<(string, string)> { (c.Name.Length > 0 ? c.Name : at.Host, c.Address("/")) };
        string walked = "/";
        foreach (var segment in at.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)) { walked = RemotePath.Combine(walked, segment); parts.Add((segment, c.Address(walked))); }
        return parts;
    }
    static string RemoteParent(RemoteAddress at) => at.Path == "/" ? null : RemoteConnections.Resolve(at).Address(RemotePath.Parent(at.Path));

    /// Open on a remote file: a copy downloaded into this run's temp folder opens with its usual app.
    async void OpenRemote(Entry e)
    {
        if (!RemoteAddress.TryParse(e.Path, out var at)) return;
        e.Message = "Downloading a copy to open…";
        try
        {
            var fs = await RemoteClient(at);
            var local = await RemoteTempCopies.Fetch(fs, new RemoteItem(e.Name, at.Path, false, e.Size, e.Modified), null, remoteWork.Token);
            e.Message = "";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(local) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(local) });
        }
        catch (Exception ex) when (RemoteSessions.IsConnectionFailure(ex) || ex is System.ComponentModel.Win32Exception)
        { e.Message = ex is OperationCanceledException ? "" : "Could not open: " + ex.Message; }
    }

    /// Runs one change on the server, then reads the folder again. False (with the reason shown) when it failed.
    internal async Task<bool> RemoteChange(string what, Func<IRemoteFileSystem, Task> change, string select = null)
    {
        if (!RemoteAddress.TryParse(Dir, out var at)) return false;
        try { await change(await RemoteClient(at)); }
        catch (Exception ex) when (RemoteSessions.IsConnectionFailure(ex))
        {
            if (ex is not OperationCanceledException) Error?.Invoke($"Could not {what}: {ex.Message}");
            Refresh();
            return false;
        }
        if (select != null) Navigate(Dir, select, record: false); else Refresh();
        return true;
    }

    internal void RemoteNewFolder()
    {
        if (!RemoteAddress.TryParse(Dir, out var at)) return;
        var taken = items.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        string name = "New folder"; for (int n = 2; taken.Contains(name); n++) name = $"New folder ({n})";
        name = PromptDialog.Ask(Window.GetWindow(this), "New folder", "Name", name, "Create", selectAll: true);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Contains('/') || name is "." or "..") { Error?.Invoke("A remote folder name cannot contain / or be . or .."); return; }
        _ = RemoteChange("create the folder", fs => fs.CreateFolder(RemotePath.Combine(at.Path, name), remoteWork.Token), name);
    }

    internal void RemoteRename(Entry entry, string name)
    {
        if (!RemoteAddress.TryParse(entry.Path, out var at)) return;
        if (name.Contains('/') || name is "." or "..") { Error?.Invoke("A remote name cannot contain / or be . or .."); return; }
        _ = RemoteChange("rename it", fs => fs.Move(at.Path, RemotePath.Combine(RemotePath.Parent(at.Path), name), remoteWork.Token), name);
    }

    /// Deleting on a server is permanent: there is no Recycle Bin there, and it is said before anything goes.
    internal void RemoteDelete()
    {
        var chosen = Selected.Where(e => !e.IsUp && RemoteAddress.IsRemote(e.Path)).ToList();
        if (chosen.Count == 0) return;
        string what = chosen.Count == 1 ? $"“{chosen[0].Name}”" : $"{chosen.Count:N0} items";
        bool folders = chosen.Any(e => e.IsDir);
        if (PromptDialog.Choose(Window.GetWindow(this), $"Permanently delete {what} on the server?",
                "There is no Recycle Bin on a server and this cannot be undone." + (folders ? " Folders are deleted with everything in them." : ""), "Delete permanently", "Cancel") != 0) return;
        _ = RemoteChange("delete", async fs =>
        {
            foreach (var e in chosen)
                if (RemoteAddress.TryParse(e.Path, out var item)) await fs.Delete(item.Path, e.IsDir, recursive: true, remoteWork.Token);
        });
    }
}
