using System.Collections.Generic;

namespace FileExplorer.Search;

// SEARCH-06: one engine choice for every indexed caller. The Pathweft index
// service answers when its pipe connects; otherwise the existing Everything
// client does. A named Everything instance (diagnostics) always goes to Everything.
internal static class IndexedSearch
{
    internal const string EverythingSizeSource = "Everything index · excluded files may be omitted",
        ServiceSizeSource = "Pathweft index · unindexed folders are measured";
    // The engine that supplied the most recent indexed folder sizes.
    internal static volatile string SizeSource = EverythingSizeSource;
    internal static bool IsIndexSource(string source) => source == EverythingSizeSource || source == ServiceSizeSource;
#if DEBUG
    internal static IndexServiceClient ServiceOverride;
#endif
    // Null in verification profiles unless a verification substitutes one.
    internal static IndexServiceClient Service
    {
        get
        {
#if DEBUG
            if (ServiceOverride != null) return ServiceOverride;
#endif
            return Settings.Profile == null ? IndexServiceClient.Shared : null; // verification profiles keep their Everything baselines
        }
    }

    internal static string FolderExpression(IEnumerable<string> paths) => EverythingSearchClient.FolderExpression(paths);

    // Pane and widget searches: columns Name, Size, Type or Modified; only what the user can open.
    internal static async Task<IndexedPage> QueryAsync(string expression, string instance, uint offset, uint count, string column, bool descending, CancellationToken token)
    {
        if (instance.Length == 0 && Service is { } service)
        {
            string sort = column switch { "Name" => "name", "Size" => "size", "Type" => "extension", "Modified" => "modified", _ => throw new ArgumentException("The index cannot sort by that column. Choose Name, Size, Type or Modified.") };
            if (await service.QueryAsync(expression, offset, count, sort, descending, null, true, token).ConfigureAwait(false) is { } page) return page;
        }
        return await EverythingSearchClient.Shared.QueryAsync(expression, instance, offset, count, column, descending, token).ConfigureAwait(false);
    }

    // MCP search: every match, each with its visibility flag.
    internal static async Task<IndexedPage> SearchAsync(string expression, uint offset, uint count, string sort, bool descending, SearchSwitches switches, CancellationToken token)
    {
        if (sort is not ("name" or "path" or "size" or "extension" or "created" or "modified")) throw new ArgumentException("Sort by name, path, size, extension, created or modified");
        if (Service is { } service && await service.QueryAsync(expression, offset, count, sort, descending, switches, false, token).ConfigureAwait(false) is { } page) return page;
        return await EverythingSearchClient.Shared.SearchAsync(expression, offset, count, sort, descending, switches, token).ConfigureAwait(false);
    }

    // Exact folders; a missing key means unknown, and the caller measures it.
    internal static async Task<Dictionary<string, long>> FolderSizesAsync(IEnumerable<string> paths, string instance, CancellationToken token)
    {
        var list = paths as ICollection<string> ?? paths.ToList();
        if (instance.Length == 0 && Service is { } service && await service.FolderSizesAsync(list, token).ConfigureAwait(false) is { } sizes)
        { SizeSource = ServiceSizeSource; return sizes; }
        var result = await EverythingSearchClient.Shared.FolderSizesAsync(list, instance, token).ConfigureAwait(false);
        SizeSource = EverythingSizeSource;
        return result;
    }
}
