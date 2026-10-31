using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer.Search;

namespace FileExplorer;

public partial class PaneView
{
    internal void RefreshAppearance()
    {
        foreach(var entry in Items)entry.RefreshAppearance();
    }
    const uint SearchPageSize = 200;
    CancellationTokenSource indexedCancellation;
    bool indexedSearch;
    uint indexedTotal;
    uint indexedOffset;
    bool indexedShowHidden;
    Dictionary<string, bool> visibilityParents = new(StringComparer.OrdinalIgnoreCase);
    string searchNotice = "";
    string indexedExpression;
    ObservableCollection<Entry> indexedResults;
    internal Task SearchCompletion { get; private set; } = Task.CompletedTask;
    internal Task AddressCompletion { get; private set; } = Task.CompletedTask;
    internal uint IndexedTotal => indexedTotal;
    internal string SearchNotice => searchNotice;
    internal string SearchInstance = ""; // default existing instance; diagnostics can exercise an unavailable selection
    internal bool IsWidget { get; set; }
    internal event Action<string> WidgetLocationOpened;
    bool widgetColumnsConfigured,indexedCanLoadMore;
    internal void ConfigureWidgetColumns()
    {
        if (details is not GridView grid) return;
        if(!widgetColumnsConfigured)
        {
            foreach (var column in grid.Columns.Where(c => colKey.GetValueOrDefault(c) is "Version" or "Type").ToArray())grid.Columns.Remove(column);
            NameColumn.CellTemplate=(DataTemplate)FindResource("WidgetNameCell");
            var location=new GridViewColumn{Header="Location",Width=220,CellTemplate=(DataTemplate)FindResource("WidgetLocationCell")};
            grid.Columns.Insert(1,location);colKey[location]="Location";
            foreach(var column in grid.Columns.Where(c=>colKey.GetValueOrDefault(c) is "Size" or "Modified"))column.CellTemplate=(DataTemplate)FindResource(colKey[column]=="Size"?"WidgetSizeCell":"WidgetDateCell");
            var header=new Style(typeof(GridViewColumnHeader));header.Setters.Add(new Setter(HeightProperty,0d));header.Setters.Add(new Setter(FocusableProperty,false));
            grid.ColumnHeaderContainerStyle=header;
            List.ItemContainerStyle=(Style)FindResource("WidgetResultRow");List.AllowDrop=false;
            ((FrameworkElement)TabBand.Parent).Visibility=Visibility.Collapsed;
            FolderStrip.Visibility=Visibility.Collapsed;((FrameworkElement)Footer.Parent).Visibility=Visibility.Collapsed;
            foreach(var button in PathBar.Children.OfType<Button>())button.Visibility=Visibility.Collapsed;
            PathBar.Margin=new(0,0,0,10);AddressField.CornerRadius=new(18);AddressField.Padding=new(12,3,8,3);AddressField.MinHeight=38;
            AddressField.ToolTip="Search files and folders with Everything";
            var field=(DockPanel)AddressField.Child;
            var icon=new TextBlock{Text="\uE721",FontFamily=new("Segoe Fluent Icons"),VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0),IsHitTestVisible=false};
            DockPanel.SetDock(icon,Dock.Left);field.Children.Insert(0,icon);
            Card.BorderThickness=new(0);Card.Background=System.Windows.Media.Brushes.Transparent;Glow.Visibility=Visibility.Collapsed;
            FilterBox.Visibility=Visibility.Visible;Crumbs.Visibility=Visibility.Collapsed;
            FilterBox.FontSize=14;FilterHint.Text="Search files and folders";
            System.Windows.Automation.AutomationProperties.SetName(FilterBox,"Search all files and folders");
            tabs[tab]=ThisPC;
            widgetColumnsConfigured=true;
            Search("");
        }
        FitName();
    }
    internal void FocusWidgetResults(){if(List.SelectedIndex<0&&List.Items.Count>0)List.SelectedIndex=0;FocusList();}
    internal void OpenFirstWidgetResult(){if(List.SelectedIndex<0&&List.Items.Count>0)List.SelectedIndex=0;OpenSelected();}
    void WidgetScrolled(ScrollChangedEventArgs change)
    {
        if(IsWidget&&indexedCanLoadMore&&!searching&&(change.VerticalChange>0||change.ExtentHeightChange>0)&&change.VerticalOffset+change.ViewportHeight>=change.ExtentHeight-change.ViewportHeight/2)
            SearchCompletion=LoadIndexedPage(indexedCancellation.Token,navVersion);
    }

    int SearchScopeIndex =>
#if DEBUG
        VerificationSearchScope ??
#endif
        (IsWidget||Dir==ThisPC ? 1 : 0);
#if DEBUG
    // Historical engine regression scenarios can still qualify the retired
    // filesystem backend; release UI has only location-derived Everything scope.
    internal int? VerificationSearchScope;
#endif
    bool updatingSearchOptions;
    internal SearchMatchOptions MatchOptions => (MatchCase.IsChecked==true?SearchMatchOptions.Case:0)
        |(MatchWord.IsChecked==true?SearchMatchOptions.WholeWord:0)
        |(MatchPath.IsChecked==true?SearchMatchOptions.Path:0)
        |(MatchRegex.IsChecked==true?SearchMatchOptions.Regex:0);
    internal PaneSearchRequest SearchRequest => new(Dir, searchQuery??"", SearchScopeIndex, sortKey, sortDesc,MatchOptions);
    void MatchOptions_Changed(object sender,RoutedEventArgs args)
    {
        if(MatchWord==null||MatchRegex==null||updatingSearchOptions)return;
        MatchWord.IsEnabled=MatchRegex.IsChecked!=true;
        if(IsWidget&&!string.IsNullOrWhiteSpace(FilterBox.Text)){Search(FilterBox.Text);return;}
        if(indexedSearch&&searchQuery!=null)Search(searchQuery);
    }
    void RestoreMatchOptions(SearchMatchOptions options)
    {
        updatingSearchOptions=true;
        try
        {
            MatchCase.IsChecked=options.HasFlag(SearchMatchOptions.Case);
            MatchWord.IsChecked=options.HasFlag(SearchMatchOptions.WholeWord);
            MatchPath.IsChecked=options.HasFlag(SearchMatchOptions.Path);
            MatchRegex.IsChecked=options.HasFlag(SearchMatchOptions.Regex);
            MatchWord.IsEnabled=MatchRegex.IsChecked!=true;
        }
        finally{updatingSearchOptions=false;}
    }

    internal void ApplySearchRequest(PaneSearchRequest request)
    {
        LeaveSearch();
        searchQuery = null;
        RestoreMatchOptions(request.Match);
        var folder=request.Scope==1?ThisPC:request.Folder;
        if (Dir != folder) Navigate(folder);
        ShowSort(request.Sort, request.Descending);
        FilterBox.Text = request.Query;
        Search(request.Query);
    }

    internal void StopWidgetWork()
    {
        indexedCanLoadMore=false;
        ++navVersion;
        indexedCancellation?.Cancel();
        sizing?.Cancel(); icons?.Cancel(); thumbs?.Cancel(); prefetching?.Cancel();
        StopFolderWatch();
    }

    internal Task SubmitAddressAsync()=>AddressCompletion=ResolveAddressAsync(FilterBox.Text);
    async Task ResolveAddressAsync(string text)
    {
        text=text.Trim();
        if(text.Length==0){ClearFilter();return;}
        int generation=navVersion,revision=suggestVersion;
        string origin=Dir;
        Suggest.IsOpen=false;
        var resolved=await Task.Run(()=>
        {
            string raw=Environment.ExpandEnvironmentVariables(text.Trim('"'));
            if(raw.Equals("This PC",StringComparison.OrdinalIgnoreCase)||raw.Equals("My PC",StringComparison.OrdinalIgnoreCase))return(Path:ThisPC,Folder:true);
            if(raw=="~"||raw.StartsWith("~\\")||raw.StartsWith("~/"))raw=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+raw[1..];
            try
            {
                var basis=IsVirtual(origin)||IsServer(origin)?Environment.GetFolderPath(Environment.SpecialFolder.UserProfile):origin;
                string path=Path.GetFullPath(raw,basis);
                var attributes=File.GetAttributes(path);
                return(Path:path,Folder:(attributes&FileAttributes.Directory)!=0);
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException){return(Path:(string)null,Folder:false);}
        });
        if(generation!=navVersion||revision!=suggestVersion||text!=FilterBox.Text.Trim())return;
        if(resolved.Path!=null)
        {
            if(resolved.Folder)Navigate(resolved.Path);
            else Navigate(Path.GetDirectoryName(resolved.Path),Path.GetFileName(resolved.Path));
            EndEditPath();
        }
        else{Search(text);await SearchCompletion;}
        if(IsKeyboardFocusWithin)FocusList();
    }

    public void Search(string query)
    {
#if DEBUG
        VerificationQueryEvents?.Add("SEARCH " + query + "\n" + Environment.StackTrace);
#endif
        if (string.IsNullOrWhiteSpace(query))
        {
            if(IsWidget)
            {
                StopWidgetWork();LeaveSearch();searchQuery="";
                indexedResults=new();indexedTotal=indexedOffset=0;
                ShowItems(indexedResults,null,animate:false);
                FilterBox.Visibility=Visibility.Visible;Crumbs.Visibility=Visibility.Collapsed;FilterHint.Visibility=Visibility.Visible;
                SearchCompletion=Task.CompletedTask;
            }
            return;
        }
        ++navVersion;
        searching = false;
        indexedCancellation?.Cancel();
        indexedCancellation?.Dispose();
        indexedCancellation = new();
        indexedCanLoadMore=false;
        if (SearchScopeIndex == 2)
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
            if (SearchScopeIndex == 0 && IsVirtual(Dir))
                throw new ArgumentException("Everything cannot search inside this virtual location. Search from its containing folder or This PC.");
            indexedExpression = IndexedQuery.Compose(query, SearchScopeIndex == 0 ? Dir : null,MatchOptions);
        }
        catch (ArgumentException ex) { if(IsWidget){indexedSearch=false;searchQuery=query;ShowItems(new List<Entry>(),null,animate:false);} SearchCoverage.Text = ex.Message; SearchCoverage.Visibility = Visibility.Visible; return; }
        indexedSearch = true;
        ArchiveRoot = null; // indexed hits are real paths, even when the origin tab is inside a ZIP
        searchQuery = query;
        FilterBox.Text = query;
        FilterBox.Visibility=Visibility.Visible;Crumbs.Visibility=Visibility.Hidden;FilterHint.Visibility=Visibility.Collapsed;
        if (sortKey == "Version") ShowSort("Name", false);
        ++navVersion;
        sizing?.Cancel(); icons?.Cancel(); thumbs?.Cancel();
        StopFolderWatch();
        indexedResults = new(); indexedTotal = indexedOffset = 0;
        indexedShowHidden = ShowHidden; visibilityParents = new(StringComparer.OrdinalIgnoreCase);
        searchNotice = "Querying the selected Everything index…";
        SearchCoverage.Visibility = IsWidget?Visibility.Collapsed:Visibility.Visible;
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
                indexedOffset, SearchPageSize, sortKey, sortDesc, token);
            if (token.IsCancellationRequested || generation != navVersion) return;
            // Inspect only the requested page. A separate raw cursor ensures a
            // fully hidden page cannot repeat or make paging stop prematurely.
            string visibilityRoot = SearchScopeIndex == 0 ? Dir : null;
            var parents = visibilityParents;
            var visibility = indexedShowHidden ? null : await Task.Run(() => page.Hits.Select(hit =>
                FileVisibility.PathVisible(hit.Path, visibilityRoot, parents, token)).ToArray(), token);
            if (token.IsCancellationRequested || generation != navVersion) return;
            indexedOffset += (uint)page.Hits.Count;
            indexedTotal = page.Total;
            // The index can change between pages. Refresh is explicit; never
            // present a changing, deduplicated page set as a stable snapshot.
            var known = indexedResults.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            for (int index = 0; index < page.Hits.Count; index++)
            {
                var hit = page.Hits[index];
                if (!known.Add(hit.Path)) { changed = true; continue; }
                if (visibility != null && !visibility[index]) continue;
                indexedResults.Add(new Entry
                {
                    Name = System.IO.Path.GetFileName(hit.Path), Path = hit.Path, IsDir = hit.IsFolder,
                    Size = hit.Size, Modified = hit.Modified,
                    Location = System.IO.Path.GetDirectoryName(hit.Path) ?? "", IsCut = CutPaths.Contains(hit.Path),
                });
            }
            searchNotice = "Everything index · Unindexed locations may be absent.";
            if (changed) searchNotice += " Index changed during paging; refresh to restart.";
            // A duplicate page must not repeatedly request the same offset.
            indexedCanLoadMore=!changed&&page.Hits.Count>0&&indexedOffset<indexedTotal;
            MoreResults.Visibility = !IsWidget&&indexedCanLoadMore ? Visibility.Visible : Visibility.Collapsed;
            QueueVisibleSearchAssets();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != navVersion || token.IsCancellationRequested) return;
            searchNotice = ex.Message;
            indexedCanLoadMore=false;SearchCoverage.Visibility=Visibility.Visible;
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
                if(IsWidget&&indexedCanLoadMore&&indexedResults.Count==0)await LoadIndexedPage(token,generation);
            }
        }
    }

    void MoreResults_Click(object sender, RoutedEventArgs e)
    {
        if (indexedSearch && !searching && indexedOffset < indexedTotal)
            SearchCompletion = LoadIndexedPage(indexedCancellation.Token, navVersion);
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
