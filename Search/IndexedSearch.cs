using System.Collections.Generic;

namespace FileExplorer.Search;

internal sealed record IndexedHit(string Path, bool IsFolder, long Size, DateTime Modified, DateTime Created = default, uint? Attributes = null, bool Visible = true);
// MCP-03: the es.exe-equivalent switches.
internal sealed record SearchSwitches(bool MatchPath, bool MatchCase, bool WholeWord, bool Regex);
// Updating: SEARCH-06, the index service is still loading or scanning a store.
internal sealed record IndexedPage(IReadOnlyList<IndexedHit> Hits, uint Total, string Engine, bool FolderSizesIndexed=false, bool Updating=false);

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
    internal static async Task<IndexedPage> QueryAsync(string expression, uint offset, uint count, string column, bool descending, CancellationToken token)
    {
        string sort = column switch { "Name" => "name", "Size" => "size", "Type" => "extension", "Modified" => "modified", _ => throw new ArgumentException("The index cannot sort by that column. Choose Name, Size, Type or Modified.") };
        var service = Service;
        return Answered(await service.QueryAsync(expression, offset, count, sort, descending, null, true, token).ConfigureAwait(false), service);
    }

    // MCP search: every match, each with its visibility flag.
    internal static async Task<IndexedPage> SearchAsync(string expression, uint offset, uint count, string sort, bool descending, SearchSwitches switches, CancellationToken token)
    {
        if (sort is not ("name" or "path" or "size" or "extension" or "created" or "modified")) throw new ArgumentException("Sort by name, path, size, extension, created or modified");
        var service = Service;
        return Answered(await service.QueryAsync(expression, offset, count, sort, descending, switches, false, token).ConfigureAwait(false), service);
    }

    // Exact folders; a missing key means unknown, and the caller measures it.
    // An absent service answers nothing, so every folder is measured.
    internal static async Task<Dictionary<string, long>> FolderSizesAsync(IEnumerable<string> paths, CancellationToken token)
    {
        if (await Service.FolderSizesAsync(paths, token).ConfigureAwait(false) is { } sizes) return sizes;
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
