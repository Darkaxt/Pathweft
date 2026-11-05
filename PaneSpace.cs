using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace FileExplorer;

internal sealed class SpaceEntry:Entry
{
    internal string ParentPath;
    internal int Depth;
    internal bool Expanded,ChildrenRead;
    internal SpaceState State;
    internal long Files,Folders;
    internal string Failure="";
    internal double RootBytes;
    public Thickness TreeIndent=>new(Math.Min(Depth,128)*16,0,0,0);
    public string ExpandGlyph=>Expanded?"\uE70D":"\uE76C";
    public Visibility ExpandVisible=>IsDir&&State!=SpaceState.Reparse?Visibility.Visible:Visibility.Hidden;
    public string SpaceStatus=>State switch{SpaceState.Pending=>"Pending",SpaceState.Scanning=>"Measuring…",SpaceState.Complete=>"Measured",SpaceState.Indexed=>"Indexed",SpaceState.Denied=>"Access denied",SpaceState.Reparse=>"Link skipped",SpaceState.Incomplete=>"Incomplete",SpaceState.Cancelled=>"Cancelled",_=>"Unavailable"};
    public string SpaceTip=>State==SpaceState.Indexed?"Everything index · excluded files may be omitted":Failure;
    public double Usage=>RootBytes>0?Math.Clamp(DisplaySize/RootBytes*100,0,100):0;
    public string UsageText=>RootBytes>0?$"{Usage:0.#}%":"";
    internal void Updated(){Changed(nameof(TreeIndent));Changed(nameof(ExpandGlyph));Changed(nameof(ExpandVisible));Changed(nameof(SpaceStatus));Changed(nameof(SpaceTip));Changed(nameof(Usage));Changed(nameof(UsageText));}
}

public partial class PaneView
{
    sealed class SpaceView
    {
        internal string Root;
        internal CancellationTokenSource Cancellation=new();
        internal Task Work=Task.CompletedTask;
        internal Dictionary<string,SpaceEntry> Nodes=new(StringComparer.OrdinalIgnoreCase);
        internal readonly object Mailbox=new();
        internal Dictionary<string,SpaceUpdate> Pending=new(StringComparer.OrdinalIgnoreCase);
        internal DispatcherOperation Publication;
        internal DispatcherTimer Present;
        internal HashSet<string> Seen=new(StringComparer.OrdinalIgnoreCase);
        internal ObservableCollection<Entry> Visible=new();
        internal IList<Entry> SavedItems;
        internal ListCollectionView SavedView;
        internal ViewMode Mode;
        internal GridViewColumn[] Columns;
        internal double[] Widths;
        internal string[] Selected;
        internal double Vertical,Horizontal;
        internal bool RefreshRequested,Changed,Stopping;
        internal bool SelectFirst=true,QuietRefresh,FullRefresh;
        internal HashSet<string> Dirty=new(StringComparer.OrdinalIgnoreCase);
    }
    SpaceView space;
    internal bool SpaceAnalyzing=>space!=null;
    internal bool SpaceCountsKnown=>space!=null&&!space.Nodes.Values.Any(row=>row.State==SpaceState.Indexed);
    internal bool CanAnalyzeSpace=>space!=null||Listed&&itemsFolder==Dir&&!IsWidget&&!InSearch&&!IsVirtual(Dir)&&!IsServer(Dir)&&Path.IsPathFullyQualified(Dir);
    internal Task SpaceCompletion=>ObserveSpaceCompletion();
    async Task ObserveSpaceCompletion()
    {
        while(space is {} current)
        {var work=current.Work;await work;if(space!=current||ReferenceEquals(work,current.Work))return;}
    }
    internal void SetSizingSuspended(bool suspended)
    {if(suspendSizing==suspended)return;suspendSizing=suspended;if(suspended)sizing?.Cancel();else StartSizing();}
    internal event Action SpaceModeChanged;
    internal Func<Task> SpaceAdmission;
    Task spaceTransition=Task.CompletedTask;
    GridViewColumn spaceNameColumn;
    internal async Task ToggleSpaceAsync()
    {
        if(space!=null){await EndSpaceAsync();return;}
        if(!CanAnalyzeSpace)return;
        await StopSizingAsync();
        var scroll=FindScroller(List);
        var current=new SpaceView{Root=Dir,SavedItems=items,SavedView=view,Mode=Mode,
            Columns=((GridView)details).Columns.ToArray(),Widths=((GridView)details).Columns.Select(c=>c.Width).ToArray(),
            Selected=Selected.Select(e=>e.Path).ToArray(),Vertical=scroll?.VerticalOffset??0,Horizontal=scroll?.HorizontalOffset??0};
        ++navVersion;prefetching?.Cancel();space=current;SpaceModeChanged?.Invoke();if(SpaceAdmission!=null)await SpaceAdmission();
        StopColumns();icons?.Cancel();thumbs?.Cancel();
        current.Nodes[current.Root]=new SpaceEntry{Path=current.Root,Name=Path.GetFileName(current.Root),IsDir=true,Depth=-1,Expanded=true,State=SpaceState.Pending};
        List.ItemsSource=null;List.View=null;List.ClearValue(ItemsControl.ItemTemplateProperty);List.ClearValue(ItemsControl.ItemsPanelProperty);List.ClearValue(ItemsControl.ItemContainerStyleProperty);
        var grid=new GridView{AllowsColumnReorder=false};
        spaceNameColumn=new GridViewColumn{Header="Name",CellTemplate=(DataTemplate)FindResource("SpaceNameCell"),Width=Math.Max(170,List.ActualWidth-360)};
        grid.Columns.Add(spaceNameColumn);
        grid.Columns.Add(new GridViewColumn{Header="Space used",CellTemplate=(DataTemplate)FindResource("SpaceSizeCell"),Width=100});
        grid.Columns.Add(new GridViewColumn{Header="Of this folder",CellTemplate=(DataTemplate)FindResource("SpaceUsageCell"),Width=130});
        grid.Columns.Add(new GridViewColumn{Header="Status",CellTemplate=(DataTemplate)FindResource("SpaceStatusCell"),Width=115});
        List.View=grid;ScrollViewer.SetVerticalScrollBarVisibility(List,ScrollBarVisibility.Auto);ScrollViewer.SetHorizontalScrollBarVisibility(List,ScrollBarVisibility.Auto);
        items=current.Visible;view=new ListCollectionView((IList)current.Visible);List.ItemsSource=view;
        NameEdge.Visibility=Visibility.Collapsed;ViewButton.ToolTip="Space analysis · toggle Analyze space to restore the previous view";
        List.PreviewKeyDown+=SpaceKey;List.Focus();UpdateSpaceFooter();StatsChanged?.Invoke();
        // Presentation throttle only; terminal state is drained explicitly on
        // actual worker completion. It never decides cancellation/completion.
        current.Present=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(150)};
        current.Present.Tick+=(_,_)=>{lock(current.Mailbox){if(current.Pending.Count==0)return;}DrainSpace(current);};current.Present.Start();
        current.Work=RunSpaceAsync(current,preferIndex:true);
    }
    async Task RunSpaceAsync(SpaceView current,bool preferIndex)
    {
#if DEBUG
        SpaceScan.IndexObserved?.Invoke(current.Root,"Run: preferIndex="+preferIndex);
#endif
        try
        {
            await SpaceScan.RunAsync(current.Root,preferIndex,SearchInstance,update=>QueueSpace(current,update),current.Cancellation.Token);
            DrainSpace(current);
            // Indexed descendants are discovered lazily; retain their identities
            // until their own parent enumeration establishes actual membership.
            foreach(var row in current.Nodes.Values)row.ChildrenRead=false;
            await ReadSpaceChildren(current,current.Root);
            foreach(var row in current.Nodes.Values.Where(row=>row.Expanded&&!row.ChildrenRead).ToArray())await ReadSpaceChildren(current,row.Path);
            DrainSpace(current);
        }
        catch(OperationCanceledException)when(current.Cancellation.IsCancellationRequested){}
        catch(Exception error){if(space==current&&!current.Stopping)Error?.Invoke("Space analysis: "+error.Message);}
    }
    void QueueSpace(SpaceView current,SpaceUpdate update)
    {
        lock(current.Mailbox)
        {
            current.Pending[update.Path]=update;
            current.Seen.Add(update.Path);
        }
    }
    void DrainSpace(SpaceView current)
    {
        Dictionary<string,SpaceUpdate> updates;
        lock(current.Mailbox){updates=current.Pending;current.Pending=new(StringComparer.OrdinalIgnoreCase);current.Publication=null;}
        if(space!=current||current.Stopping)return;
        foreach(var update in updates.Values)
        {
            if(!current.Nodes.TryGetValue(update.Path,out var row))
                current.Nodes[update.Path]=row=new SpaceEntry{Path=update.Path,Name=Path.GetFileName(update.Path),IsDir=true,ParentPath=update.Parent,Size=-1};
            // A replacement scan owns a new snapshot. Keep the last completed
            // value/count/status until that branch produces its replacement;
            // initial unknown rows and terminal errors remain truthful.
            if(current.QuietRefresh&&row.DisplaySize>=0&&(row.State is not (SpaceState.Pending or SpaceState.Scanning))&&(update.State is SpaceState.Pending or SpaceState.Scanning))continue;
            row.State=update.State;row.Failure=update.Error;row.Files=update.Files;row.Folders=update.Folders;
            if(update.State is SpaceState.Complete or SpaceState.Indexed||update.State!=SpaceState.Pending&&(row.DisplaySize<0||update.Bytes>=row.DisplaySize))row.Size=update.Bytes;
            row.SizeSource=update.State==SpaceState.Indexed?"Everything index · excluded files may be omitted":"Filesystem measurement";
        }
        RebuildSpace(current);
    }
    void RebuildSpace(SpaceView current)
    {
        if(space!=current||current.Stopping)return;
        var scroll=FindScroller(List);double offset=scroll?.VerticalOffset??0;
        var anchor=current.Visible.FirstOrDefault(e=>List.ItemContainerGenerator.ContainerFromItem(e) is FrameworkElement f&&f.TranslatePoint(new Point(),List).Y>=0);
        double anchorY=anchor!=null&&List.ItemContainerGenerator.ContainerFromItem(anchor) is FrameworkElement f?f.TranslatePoint(new Point(),List).Y:0;
        var selected=Selected.Select(e=>e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var children=current.Nodes.Values.Where(row=>row.ParentPath!=null).GroupBy(row=>row.ParentPath,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.OrderByDescending(row=>row.DisplaySize).ThenBy(row=>row.Name,StringComparer.OrdinalIgnoreCase).ToArray(),StringComparer.OrdinalIgnoreCase);
        double total=Math.Max(0,current.Nodes[current.Root].DisplaySize);
        var ordered=new List<Entry>();var stack=new Stack<(SpaceEntry Row,int Depth)>();
        if(children.TryGetValue(current.Root,out var top))foreach(var row in top.Reverse())stack.Push((row,0));
        while(stack.TryPop(out var next))
        {
            next.Row.Depth=next.Depth;next.Row.RootBytes=total;next.Row.Updated();ordered.Add(next.Row);
            if(next.Row.Expanded&&children.TryGetValue(next.Row.Path,out var nested))foreach(var row in nested.Reverse())stack.Push((row,next.Depth+1));
        }
        bool previous=bulkSelect;bulkSelect=true;
        try
        {
            // Reuse row identities. Only membership/order changes reach the
            // virtualized list; no per-publication ItemsSource replacement.
            for(int i=0;i<ordered.Count;i++)
            {
                if(i<current.Visible.Count&&ReferenceEquals(current.Visible[i],ordered[i]))continue;
                int old=current.Visible.IndexOf(ordered[i]);if(old>=0)current.Visible.Move(old,i);else current.Visible.Insert(i,ordered[i]);
            }
            while(current.Visible.Count>ordered.Count)current.Visible.RemoveAt(current.Visible.Count-1);
            if(current.SelectFirst&&current.Visible.Count>0)
            {
                current.SelectFirst=false;
                selected.Clear();selected.Add(current.Visible[0].Path);
            }
            SetSelection(entry=>selected.Contains(entry.Path));
        }
        finally{bulkSelect=previous;}
        List.UpdateLayout();
        if(List.IsKeyboardFocusWithin&&Keyboard.FocusedElement==List)FocusSelectedRow();
        if(anchor!=null&&List.ItemContainerGenerator.ContainerFromItem(anchor) is FrameworkElement now)scroll?.ScrollToVerticalOffset(offset+now.TranslatePoint(new Point(),List).Y-anchorY);
        else scroll?.ScrollToVerticalOffset(offset);
        UpdateSpaceFooter();StatsChanged?.Invoke();StartIcons();
    }
    async Task<bool> ReadSpaceChildren(SpaceView current,string path,bool preferIndex=true,bool measureMissing=true)
    {
        var token=current.Cancellation.Token;
        List<SpaceMember> members;
        try{members=await Task.Run(()=>SpaceScan.Children(path,token),token);}
        catch(Exception error)when(error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {if(space==current&&current.Nodes.TryGetValue(path,out var failed)){failed.State=SpaceState.Denied;failed.Failure=error.Message;failed.Updated();RebuildSpace(current);}return false;}
        token.ThrowIfCancellationRequested();if(space!=current||current.Stopping)return false;DrainSpace(current);
        var present=members.Select(member=>member.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var absent in current.Nodes.Values.Where(row=>row.ParentPath==path&&!present.Contains(row.Path)).ToArray())
            foreach(var key in current.Nodes.Keys.Where(key=>SizeCache.Within(key,absent.Path)).ToArray())current.Nodes.Remove(key);
        var missing=members.Where(member=>member.Directory&&!member.Reparse&&!current.Nodes.ContainsKey(member.Path)).ToArray();
        Dictionary<string,long> sizes=new(StringComparer.OrdinalIgnoreCase);
        if(preferIndex&&missing.Length>0)try{sizes=await FileExplorer.Search.EverythingSearchClient.Shared.FolderSizesAsync(missing.Select(member=>member.Path),SearchInstance,token);}
        catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException){}
        foreach(var member in members)
        {
            if(current.Nodes.TryGetValue(member.Path,out var existing))
            {existing.Modified=member.Modified.ToLocalTime();if(!member.Directory)existing.Size=member.Bytes;continue;}
            bool indexed=sizes.TryGetValue(Path.TrimEndingDirectorySeparator(member.Path),out long bytes);
            current.Nodes[member.Path]=new SpaceEntry{Path=member.Path,Name=member.Name,IsDir=member.Directory,Modified=member.Modified.ToLocalTime(),ParentPath=path,
                Size=member.Reparse?0:member.Directory?indexed?bytes:-1:member.Bytes,State=member.Reparse?SpaceState.Reparse:member.Directory?indexed?SpaceState.Indexed:SpaceState.Pending:SpaceState.Complete};
        }
        // An indexed parent does not establish coverage of every child. A newly
        // expanded unindexed branch still receives a real metadata measurement.
        foreach(var member in missing.Where(member=>measureMissing&&!sizes.ContainsKey(Path.TrimEndingDirectorySeparator(member.Path))))
        {
            await SpaceScan.RunAsync(member.Path,false,SearchInstance,update=>QueueSpace(current,update),token);
            DrainSpace(current);
        }
        if(current.Nodes.TryGetValue(path,out var parent))parent.ChildrenRead=true;
        RebuildSpace(current);return true;
    }
    void SpaceExpand_Click(object sender,RoutedEventArgs args)
    {args.Handled=true;if((sender as FrameworkElement)?.DataContext is SpaceEntry row)ToggleSpaceRow(row);}
    void ToggleSpaceRow(SpaceEntry row)
    {
        if(space is not {} current||!row.IsDir||row.State==SpaceState.Reparse)return;
        row.Expanded=!row.Expanded;row.Updated();RebuildSpace(current);
        if(row.Expanded&&!row.ChildrenRead)
        {
            var previous=current.Work;current.Work=ReadAfter();
            async Task ReadAfter(){try{await previous;await ReadSpaceChildren(current,row.Path);}catch(OperationCanceledException)when(current.Cancellation.IsCancellationRequested){}}
        }
    }
    void SpaceKey(object sender,KeyEventArgs args)
    {
        if(args.Key is not (Key.Left or Key.Right)||List.SelectedItem is not SpaceEntry row)return;
        if(args.Key==Key.Right&&!row.Expanded||args.Key==Key.Left&&row.Expanded)ToggleSpaceRow(row);
        else if(args.Key==Key.Left&&space.Nodes.TryGetValue(row.ParentPath??"",out var parent)&&parent.Depth>=0)List.SelectedItem=parent;
        args.Handled=true;
    }
    void UpdateSpaceFooter()
    {
        if(space is not {} current)return;var root=current.Nodes[current.Root];
        Footer.Text=$"{MainWindow.Fmt(Math.Max(0,root.DisplaySize))} logical · {root.SpaceStatus.ToLowerInvariant()}"+(current.Nodes.Values.Any(row=>row.State==SpaceState.Indexed)?" · indexed sizes may omit excluded files":"")+(Selected.Count>0?$" · {Selected.Count} selected":"");
    }
    internal (long Bytes,long Files,long Folders,string Status) SpaceSummary
    {get{var root=space.Nodes[space.Root];return(Math.Max(0,root.DisplaySize),root.Files,root.Folders,root.SpaceStatus);}}
    internal async Task EndSpaceAsync()
    {
        if(space is not {} current)return;
        current.Stopping=true;current.Cancellation.Cancel();
        try{await current.Work;}catch(OperationCanceledException)when(current.Cancellation.IsCancellationRequested){}
        if(space!=current)return;
        current.Present?.Stop();current.Publication?.Abort();current.Cancellation.Dispose();space=null;List.PreviewKeyDown-=SpaceKey;
        List.ItemsSource=null;List.View=null;items=current.SavedItems;view=current.SavedView;SetView(current.Mode);List.ItemsSource=view;TrackColumns();
        var grid=(GridView)details;grid.Columns.Clear();for(int i=0;i<current.Columns.Length;i++){grid.Columns.Add(current.Columns[i]);current.Columns[i].Width=current.Widths[i];}
        bool previous=bulkSelect;bulkSelect=true;try{SetSelection(entry=>current.Selected.Contains(entry.Path,StringComparer.OrdinalIgnoreCase));}finally{bulkSelect=previous;}
        List.UpdateLayout();var scroll=FindScroller(List);scroll?.ScrollToVerticalOffset(current.Vertical);scroll?.ScrollToHorizontalOffset(current.Horizontal);
        UpdateFooter();StatsChanged?.Invoke();SpaceModeChanged?.Invoke();StartSizing();
        if(current.Changed)Refresh();
    }
    void AfterSpace(Action action)
    {
        var previous=spaceTransition;spaceTransition=Run();
        async Task Run(){await previous;await EndSpaceAsync();action();}
    }
    void RefreshSpace(bool force=false)
    {
        if(space is not {} current||current.Stopping)return;
#if DEBUG
        SpaceScan.IndexObserved?.Invoke(current.Root,"Refresh: "+Environment.StackTrace);
#endif
        current.Changed=true;
        foreach(var path in dirtySizes)
        {
            if(path==null||path.Equals(current.Root,StringComparison.OrdinalIgnoreCase)||!SizeCache.Within(path,current.Root))current.FullRefresh=true;
            else
            {
                string relative=Path.GetRelativePath(current.Root,path);int separator=relative.IndexOf(Path.DirectorySeparatorChar);
                current.Dirty.Add(Path.Combine(current.Root,separator<0?relative:relative[..separator]));
            }
        }
        if(force||dirtySizes.Count==0)current.FullRefresh=true;
        dirtySizes.Clear();
        if(current.RefreshRequested)return;
        current.RefreshRequested=true;
        var previous=current.Work;current.Work=RefreshAfter();
        async Task RefreshAfter()
        {
            try
            {
                // Finish the original finite pass. New notifications coalesce
                // into this owner instead of cancelling/restarting the root.
                await previous;
                while(space==current&&!current.Stopping&&(current.FullRefresh||current.Dirty.Count>0))
                {
                    bool full=current.FullRefresh;current.FullRefresh=false;
                    var dirty=current.Dirty.ToArray();current.Dirty.Clear();
                    current.QuietRefresh=!full;
                    if(full)await RunSpaceAsync(current,preferIndex:false);
                    else await RefreshSpaceBranches(current,dirty);
                }
            }
            catch(OperationCanceledException)when(current.Cancellation.IsCancellationRequested){}
            catch(Exception error){if(space==current&&!current.Stopping)Error?.Invoke("Space analysis: "+error.Message);}
            finally{current.QuietRefresh=false;current.RefreshRequested=false;}
        }
    }
    async Task RefreshSpaceBranches(SpaceView current,string[] dirty)
    {
        var token=current.Cancellation.Token;
        // Only direct membership needs rereading for rename/delete/root files.
        // A changed branch gets a fresh filesystem measurement, never a stale
        // optional index result. Unrelated sibling snapshots remain untouched.
        if(!await ReadSpaceChildren(current,current.Root,preferIndex:false,measureMissing:false))return;
        var branches=current.Nodes.Values.Where(row=>row.ParentPath==current.Root&&row.IsDir&&row.State!=SpaceState.Reparse&&
            (row.State==SpaceState.Pending||dirty.Any(path=>SizeCache.Within(path,row.Path)))).ToArray();
        foreach(var branch in branches)
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await SpaceScan.RunAsync(branch.Path,false,SearchInstance,update=>{seen.Add(update.Path);QueueSpace(current,update);},token);
            DrainSpace(current);
            if(branch.State==SpaceState.Complete)
                foreach(var key in current.Nodes.Values.Where(row=>row.IsDir&&SizeCache.Within(row.Path,branch.Path)&&!seen.Contains(row.Path)).Select(row=>row.Path).ToArray())
                    foreach(var absent in current.Nodes.Keys.Where(path=>SizeCache.Within(path,key)).ToArray())current.Nodes.Remove(absent);
            foreach(var row in current.Nodes.Values.Where(row=>row.IsDir&&SizeCache.Within(row.Path,branch.Path)))row.ChildrenRead=false;
            foreach(var row in current.Nodes.Values.Where(row=>row.Expanded&&!row.ChildrenRead&&SizeCache.Within(row.Path,branch.Path)).ToArray())
                await ReadSpaceChildren(current,row.Path,preferIndex:false,measureMissing:false);
        }
        token.ThrowIfCancellationRequested();if(space!=current||current.Stopping)return;
        var children=current.Nodes.Values.Where(row=>row.ParentPath==current.Root).ToArray();
        var directories=children.Where(row=>row.IsDir).ToArray();var files=children.Where(row=>!row.IsDir&&row.State!=SpaceState.Reparse).ToArray();
        var root=current.Nodes[current.Root];
        root.Size=children.Sum(row=>Math.Max(0,row.DisplaySize));
        root.Files=files.LongLength+directories.Sum(row=>row.Files);
        root.Folders=directories.LongLength+directories.Sum(row=>row.Folders);
        root.State=directories.Any(row=>row.State is not (SpaceState.Complete or SpaceState.Indexed or SpaceState.Reparse))?SpaceState.Incomplete:
            directories.Any(row=>row.State==SpaceState.Indexed)?SpaceState.Indexed:SpaceState.Complete;
        root.Failure=root.State==SpaceState.Incomplete?"Changed or incomplete while measuring":"";
        RebuildSpace(current);
    }
    internal async Task DrainSpaceAndSizesAsync(){await EndSpaceAsync();await StopSizingAsync();await spaceTransition;}
}
