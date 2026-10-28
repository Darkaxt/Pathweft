using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer.Search;

namespace FileExplorer;

public partial class PaneView
{
    const uint SearchPageSize = 200;
    CancellationTokenSource indexedCancellation;
    bool indexedSearch;
    uint indexedTotal;
    string searchNotice = "";
    string indexedExpression;
    ObservableCollection<Entry> indexedResults;
    internal Task SearchCompletion { get; private set; } = Task.CompletedTask;
    internal uint IndexedTotal => indexedTotal;
    internal string SearchNotice => searchNotice;
    internal string SearchInstance = ""; // default existing instance; diagnostics can exercise an unavailable selection
    internal bool IsWidget { get; set; }
    internal void ConfigureWidgetColumns()
    {
        if (details is not GridView grid) return;
        foreach (var column in grid.Columns.Where(c => colKey.GetValueOrDefault(c) == "Version").ToArray())
            grid.Columns.Remove(column);
        FitName();
    }

    internal PaneSearchRequest SearchRequest => new(Dir, FilterBox.Text, SearchScope.SelectedIndex, sortKey, sortDesc);

    internal void ApplySearchRequest(PaneSearchRequest request)
    {
        LeaveSearch();
        searchQuery = null;
        if (Dir != request.Folder) Navigate(request.Folder);
        SearchScope.SelectedIndex = request.Scope;
        ShowSort(request.Sort, request.Descending);
        FilterBox.Text = request.Query;
        Search(request.Query);
    }

    internal void StopWidgetWork()
    {
        ++navVersion;
        indexedCancellation?.Cancel();
        sizing?.Cancel(); icons?.Cancel(); thumbs?.Cancel(); prefetching?.Cancel();
        StopFolderWatch();
    }

    public void Search(string query)
    {
#if DEBUG
        VerificationQueryEvents?.Add("SEARCH " + query + "\n" + Environment.StackTrace);
#endif
        if (string.IsNullOrWhiteSpace(query)) return;
        ++navVersion;
        searching = false;
        indexedCancellation?.Cancel();
        indexedCancellation?.Dispose();
        indexedCancellation = new();
        if (SearchScope.SelectedIndex == 2)
        {
            indexedSearch = false;
            MoreResults.Visibility = Visibility.Collapsed;
            searchNotice = "Filesystem · current subtree; excludes links/reparse points, inaccessible and hidden items (unless enabled). Content search skips cloud-only and files over 64 MB.";
            SearchCoverage.Text = searchNotice;
            SearchCoverage.Visibility = Visibility.Visible;
            SearchCompletion = SearchFileSystem(query);
            return;
        }
        try
        {
            if (SearchScope.SelectedIndex == 0 && (IsVirtual(Dir) || IsServer(Dir)))
                throw new ArgumentException("Choose a real folder for subtree search, or choose Everything · All.");
            indexedExpression = IndexedQuery.Compose(query, SearchScope.SelectedIndex == 0 ? Dir : null);
        }
        catch (ArgumentException ex) { SearchCoverage.Text = ex.Message; SearchCoverage.Visibility = Visibility.Visible; return; }
        indexedSearch = true;
        ArchiveRoot = null; // indexed hits are real paths, even when the origin tab is inside a ZIP
        searchQuery = query;
        FilterBox.Text = query;
        if (sortKey == "Version") ShowSort("Name", false);
        ++navVersion;
        sizing?.Cancel(); icons?.Cancel(); thumbs?.Cancel();
        StopFolderWatch();
        indexedResults = new(); indexedTotal = 0;
        searchNotice = "Querying the selected Everything index…";
        SearchCoverage.Visibility = Visibility.Visible;
        ShowItems(indexedResults, null, animate: false);
        SearchCompletion = LoadIndexedPage(indexedCancellation.Token, navVersion);
    }

    async Task LoadIndexedPage(CancellationToken token, int generation)
    {
        searching = true;
        MoreResults.IsEnabled = false;
        SearchCoverage.Text = searchNotice;
        UpdateFooter();
        try
        {
            var page = await EverythingSearchClient.Shared.QueryAsync(indexedExpression, SearchInstance,
                (uint)indexedResults.Count, SearchPageSize, sortKey, sortDesc, token);
            if (token.IsCancellationRequested || generation != navVersion) return;
            indexedTotal = page.Total;
            // The index can change between pages. Refresh is explicit; never
            // present a changing, deduplicated page set as a stable snapshot.
            var known = indexedResults.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (var hit in page.Hits)
            {
                if (!known.Add(hit.Path)) { changed = true; continue; }
                indexedResults.Add(new Entry
                {
                    Name = System.IO.Path.GetFileName(hit.Path), Path = hit.Path, IsDir = hit.IsFolder,
                    Size = hit.Size, Modified = hit.Modified,
                    Location = System.IO.Path.GetDirectoryName(hit.Path) ?? "", IsCut = CutPaths.Contains(hit.Path),
                });
            }
            searchNotice = page.Engine + " · Index coverage only; excluded, unindexed or offline items may be absent. Filesystem · Folder searches the live subtree.";
            if (changed) searchNotice += " Index changed during paging; refresh to restart.";
            // A duplicate page must not repeatedly request the same offset.
            MoreResults.Visibility = !changed && page.Hits.Count > 0 && indexedResults.Count < indexedTotal ? Visibility.Visible : Visibility.Collapsed;
            QueueVisibleSearchAssets();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != navVersion || token.IsCancellationRequested) return;
            searchNotice = ex.Message;
            MoreResults.Visibility = Visibility.Collapsed;
        }
        finally
        {
#if DEBUG
            VerificationQueryEvents?.Add($"PAGE END generation={generation} current={navVersion} cancelled={token.IsCancellationRequested} count={indexedResults?.Count}");
#endif
            if (generation == navVersion && !token.IsCancellationRequested)
            {
                searching = false;
                SearchCoverage.Text = searchNotice;
                MoreResults.IsEnabled = true;
                UpdateFooter(); StatsChanged?.Invoke();
            }
        }
    }

    void Search_Click(object sender, RoutedEventArgs e) { Search(FilterBox.Text.Trim()); FocusList(); }
    void MoreResults_Click(object sender, RoutedEventArgs e)
    {
        if (indexedSearch && !searching && indexedResults.Count < indexedTotal)
            SearchCompletion = LoadIndexedPage(indexedCancellation.Token, navVersion);
    }
    void Scope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SearchOptions == null) return;
        SearchOptions.IsEnabled = SearchScope.SelectedIndex == 2;
        if (searchQuery != null) Search(FilterBox.Text.Trim());
    }

    void LeaveSearch()
    {
        indexedCancellation?.Cancel();
        indexedSearch = false;
        searching = false;
        MoreResults.Visibility = SearchCoverage.Visibility = Visibility.Collapsed;
    }

    bool visibleAssetsQueued;
    void QueueVisibleSearchAssets()
    {
        if (!indexedSearch || visibleAssetsQueued) return;
        visibleAssetsQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            visibleAssetsQueued = false;
            if (indexedSearch) { StartIcons(); StartThumbs(); }
        }, DispatcherPriority.Loaded);
    }

    IEnumerable<Entry> AssetEntries()
    {
        if (!indexedSearch) return view?.Cast<Entry>() ?? Enumerable.Empty<Entry>();
        // Only realized rows in the viewport, including the panel's small
        // virtualization cache, can trigger shell calls or thumbnail decoding.
        return List.Items.Cast<Entry>().Where(e => List.ItemContainerGenerator.ContainerFromItem(e) is FrameworkElement { IsVisible: true }).ToArray();
    }
}
