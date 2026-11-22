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

    // SEARCH-10, for space analysis, which has no watcher of its own: a folder written in the last Settle may not be
    // in the index yet (it reads the NTFS change journal every 250 ms), so its indexed size is not taken and the
    // analysis measures it, counting a file written just before. Pane rows keep FolderSizesAsync: their watcher
    // already sends a changed folder to measurement.
    internal static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);
    internal static async Task<Dictionary<string, long>> SettledFolderSizesAsync(IEnumerable<string> paths, CancellationToken token)
    {
        var sizes = await FolderSizesAsync(paths, token).ConfigureAwait(false);
        var recent = DateTime.UtcNow - Settle;
        foreach (var folder in sizes.Keys.ToArray())
            try { if (System.IO.Directory.GetLastWriteTimeUtc(folder) > recent) sizes.Remove(folder); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { sizes.Remove(folder); }
        return sizes;
    }
}
