using System.Collections.Generic;

namespace FileExplorer.Search;

// Locations: CONTENT-05, the stored location objects (raw JSON) of a content hit's matches.
internal sealed record IndexedHit(string Path, bool IsFolder, long Size, DateTime Modified, DateTime Created = default, uint? Attributes = null, bool Visible = true, IReadOnlyList<string> Locations = null);
// MCP-03: the es.exe-equivalent switches.
internal sealed record SearchSwitches(bool MatchPath, bool MatchCase, bool WholeWord, bool Regex);
// Updating: SEARCH-06, the index service is still loading or scanning a store.
// UpdatingStores and ElapsedUs: MCP-08, the service's own fields, passed to MCP clients.
internal sealed record IndexedPage(IReadOnlyList<IndexedHit> Hits, uint Total, string Engine, bool FolderSizesIndexed=false, bool Updating=false, IReadOnlyList<string> UpdatingStores=null, long? ElapsedUs=null);

// SEARCH-09: the Pathweft index service is the only file-name index. Every
// indexed search and folder size goes to it; when it is absent, searches say so
// and folder-size callers measure the folders themselves.
internal static class IndexedSearch
{
    internal const string SizeSource = "Pathweft index · unindexed folders are measured";
    internal static bool IsIndexSource(string source) => source == SizeSource;
    internal const string Absent = "The Pathweft index service is not running. Install or start it in Settings → Windows integration → Pathweft index.";
#if DEBUG
    // Verification profiles query the installed service read-only, like the
    // product; scenarios that need a controlled service substitute one here.
    internal static IndexServiceClient ServiceOverride;
    internal static IndexServiceClient Service => ServiceOverride ?? IndexServiceClient.Shared;
#else
    internal static IndexServiceClient Service => IndexServiceClient.Shared;
#endif

    // Every indexed folder in the list, matched by its whole path.
    internal static string FolderExpression(IEnumerable<string> paths) => "folder: <" + string.Join(" | ", paths.Select(path => "path:wfn:\"" + System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)) + "\"")) + ">";

    static IndexedPage Answered(IndexedPage page, IndexServiceClient service)
        => page ?? throw new InvalidOperationException(service.LastConnectError is { Length: > 0 } error && error != IndexServiceClient.NotRunning ? error : Absent);

    // Pane and widget searches: columns Name, Size, Type or Modified; only what the user can open.
    internal static async Task<IndexedPage> QueryAsync(string expression, uint offset, uint count, string column, bool descending, CancellationToken token, int locations = 0)
    {
        string sort = column switch { "Name" => "name", "Size" => "size", "Type" => "extension", "Modified" => "modified", _ => throw new ArgumentException("The index cannot sort by that column. Choose Name, Size, Type or Modified.") };
        var service = Service;
        return Answered(await service.QueryAsync(expression, offset, count, sort, descending, null, true, token, locations).ConfigureAwait(false), service);
    }

    // MCP search: every match, each with its visibility flag, or only what the user can open (visibleOnly).
    // locations > 0 (MCP-08): content hits carry up to that many stored match locations.
    internal static async Task<IndexedPage> SearchAsync(string expression, uint offset, uint count, string sort, bool descending, SearchSwitches switches, CancellationToken token, bool visibleOnly = false, int locations = 0)
    {
        if (sort is not ("name" or "path" or "size" or "extension" or "created" or "modified")) throw new ArgumentException("Sort by name, path, size, extension, created or modified");
        var service = Service;
        return Answered(await service.QueryAsync(expression, offset, count, sort, descending, switches, visibleOnly, token, locations).ConfigureAwait(false), service);
    }

    // MCP-08: a read-only service reply, verbatim; an absent service is an error with the usual message.
    internal static async Task<System.Text.Json.JsonElement> ReadAsync(object request, string op, CancellationToken token)
    {
        var service = Service;
        return await service.ReadAsync(request, op, token).ConfigureAwait(false) ?? throw new InvalidOperationException(service.LastConnectError is { Length: > 0 } error && error != IndexServiceClient.NotRunning ? error : Absent);
    }

    // Exact folders; a missing key means unknown, and the caller measures it.
    // An absent service answers nothing, so every folder is measured.
    internal static async Task<Dictionary<string, long>> FolderSizesAsync(IEnumerable<string> paths, CancellationToken token)
    {
        if (await Service.FolderSizesAsync(paths, token).ConfigureAwait(false) is { } sizes) return sizes;
        return new(StringComparer.OrdinalIgnoreCase);
    }

    // CORE-32 (owner, 2026-11-24: "with the current index implementation there is no way this behavior makes sense"):
    // Analyze space reads its totals from the index wherever the index covers the folder. While the service is still
    // loading that store (C: takes about 13 s after a restart or an update) it is waited for, however long it takes,
    // never replaced by a disk walk; Ctrl+R measures on disk instead. Ready: the store is indexed. Unavailable: the
    // service is absent or did not answer, and the detail says why; the analysis measures on disk and says so.
    internal enum Coverage { Ready, NotCovered, Unavailable }
    internal static async Task<(Coverage Coverage, string Detail)> WaitForStoreAsync(string path, Action<string> waiting, CancellationToken token)
    {
        bool waited = false;
        string full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        while (true)
        {
            IndexServiceStatus status;
            var service = Service;
            try { status = await service.StatusAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException) { return (Coverage.Unavailable, ex.Message); }
            if (status == null) return (Coverage.Unavailable, service.LastConnectError is { Length: > 0 } error && error != IndexServiceClient.NotRunning ? error : "the Pathweft index service is not running");
            var store = status.Stores.Where(s => s.Name is { Length: > 0 } name && Covers(System.IO.Path.TrimEndingDirectorySeparator(name), full)).MaxBy(s => s.Name.Length);
            if (store == null) return (Coverage.NotCovered, "");
            if (store.State is not ("loading" or "reloading")) return store.State == "ready" ? (Coverage.Ready, store.Name) : (Coverage.Unavailable, store.Name + " is " + store.State);
            if (!waited) { waited = true; waiting(store.Name); }
            await Task.Delay(500, token).ConfigureAwait(false);
        }
    }
    static bool Covers(string store, string path)
        => path.StartsWith(store, StringComparison.OrdinalIgnoreCase) && (path.Length == store.Length || path[store.Length] == '\\');

    // SEARCH-10 (revised by CORE-32), for space analysis, which has no watcher of its own: the index reads the NTFS
    // change journal every 250 ms, so a folder written in the last CatchUp is asked for once CatchUp has passed since
    // that write, as pane rows do (CORE-28); it is not measured on disk. A file written just before is still counted.
    internal static readonly TimeSpan CatchUp = TimeSpan.FromMilliseconds(600);
    internal static async Task<Dictionary<string, long>> CurrentFolderSizesAsync(IReadOnlyCollection<(string Path, DateTime Modified)> folders, CancellationToken token)
    {
        var newest = folders.Select(folder => folder.Modified).DefaultIfEmpty(DateTime.MinValue).Max();
        if (newest > DateTime.MinValue && newest + CatchUp - DateTime.UtcNow is var wait && wait > TimeSpan.Zero)
            await Task.Delay(wait < CatchUp ? wait : CatchUp, token).ConfigureAwait(false);
        return await FolderSizesAsync(folders.Select(folder => folder.Path), token).ConfigureAwait(false);
    }
}
