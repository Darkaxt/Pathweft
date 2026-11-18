using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;

// COLS-01..04, HASH-01..03: the Details columns. Each view kind (folders, archives, search, space analyzer,
// This PC) keeps its own chosen columns, order and widths; until the owner chooses, a kind shows the columns
// its data has, as before. The column header's menu chooses them. Hash columns are the hashing switch: only
// panes on screen read files, through the app's one queue (HashQueue), and every value lands in the cache.
public partial class PaneView
{
    GridViewColumn[] fileColumns,driveColumns;
    readonly Dictionary<string,GridViewColumn> extraColumns=new(),spaceColumns=new();
    readonly Dictionary<GridViewColumn,double> baseWidths=new();
    INotifyCollectionChanged columnCollection;
    readonly HashSet<Entry> columnEntries=new();
    DispatcherOperation columnUpdate;
    DataTemplate fileNameCell;
    GridView spaceGrid;
    string widthKind;
    bool applyingColumns,fittingColumns;
    /// The columns shown now besides Name (and the automatic Message), in order: what hashing, filling and Copy follow.
    internal string[] ShownColumns { get; private set; } = Array.Empty<string>();
    static readonly DependencyPropertyDescriptor ColumnWidth=DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty,typeof(GridViewColumn));

    internal string ColumnKind=>SpaceAnalyzing?ColumnCatalog.Space:Dir==ThisPC&&!InSearch?ColumnCatalog.Drives:InSearch?ColumnCatalog.Search:ArchiveRoot!=null?ColumnCatalog.Archive:ColumnCatalog.Folder;
    // Folders keep the names widths always had; This PC its "Drive." ones; the other kinds their own, starting from the folders'.
    static string WidthKey(string kind,string key)=>kind==ColumnCatalog.Folder?key:kind==ColumnCatalog.Drives?"Drive."+key:kind+"."+key;
    static double? SavedWidth(string kind,string key)=>Settings.ColumnWidths.TryGetValue(WidthKey(kind,key),out var w)||kind!=ColumnCatalog.Drives&&Settings.ColumnWidths.TryGetValue(key,out w)?w:null;

    void InitializeColumns()
    {
        var grid=(GridView)details;
        fileColumns=grid.Columns.ToArray();fileNameCell=NameColumn.CellTemplate;
        foreach(var column in fileColumns)baseWidths[column]=column.Width;
        GridViewColumn Column(string name,string property,double width)
        {
            var column=new GridViewColumn{Header=name,DisplayMemberBinding=new Binding(property),Width=Settings.ColumnWidths.GetValueOrDefault("Drive."+name,width)};
            colKey[column]=name;baseWidths[column]=width;
            ColumnWidth.AddValueChanged(column,(_,_)=>{SaveWidth(column);if(!ResizingColumn&&List.View is GridView)FitName();});
            return column;
        }
        driveColumns=new[]{NameColumn,Column("Capacity",nameof(Entry.TotalText),115),Column("Free space",nameof(Entry.FreeText),115),Column("Filesystem",nameof(Entry.DriveFormat),90),Column("Connection",nameof(Entry.DriveConnection),160),Column("Location",nameof(Entry.DriveLocation),220)};
        grid.ColumnHeaderContextMenu=HeaderMenu();
        // a column dragged to a new place: that order is the kind's from now on, in every pane
        grid.Columns.CollectionChanged+=(_,e)=>
        {
            if(applyingColumns||IsWidget||e.Action!=NotifyCollectionChangedAction.Move)return;
            ColumnCatalog.SetLayout(ColumnKind,grid.Columns.Where(c=>c!=NameColumn).Select(c=>colKey.GetValueOrDefault(c)).Where(k=>k is not null and not "Message"));
        };
        ColumnCatalog.LayoutChanged+=kind=>
        {
            if(kind==null){foreach(var e in items)e.RefreshCells();if(space!=null)foreach(var e in space.Visible)e.RefreshCells();return;}
            if(kind==ColumnKind){QueueColumns();QueueHashes();}
        };
        IsVisibleChanged+=(_,_)=>QueueHashes();
    }

    /// A column's width, kept for the kind on screen (not while Pathweft itself places the columns).
    void SaveWidth(GridViewColumn column)
    {
        if(IsWidget||applyingColumns||fittingColumns||double.IsNaN(column.Width)||!colKey.TryGetValue(column,out var key))return;
        Settings.ColumnWidths[WidthKey(ColumnKind,key)]=column.Width;
    }

    GridViewColumn Extra(string key,bool forSpace)
    {
        var set=forSpace?spaceColumns:extraColumns;
        if(set.TryGetValue(key,out var known))return known;
        var column=new GridViewColumn{Header=ColumnCatalog.Header(key),CellTemplate=CellFor(key),Width=ColumnCatalog.DefaultWidth(key)};
        colKey[column]=key;set[key]=column;baseWidths[column]=column.Width;
        ColumnWidth.AddValueChanged(column,(_,_)=>
        {
            if(forSpace){if(!applyingColumns&&!fittingColumns&&!double.IsNaN(column.Width))Settings.ColumnWidths[WidthKey(ColumnCatalog.Space,key)]=column.Width;return;}
            SaveWidth(column);if(!ResizingColumn&&List.View is GridView)FitName();
        });
        return column;
    }

    // Muted text like the other detail cells; a hash cell not computed by itself also offers Hash.
    DataTemplate CellFor(string key)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty,new Binding("["+key+"]"));
        text.SetValue(OpacityProperty,0.55);
        text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        text.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty,FontNumeralAlignment.Tabular);
        if(!ColumnCatalog.IsHash(key))return new DataTemplate{VisualTree=text};
        var row=new FrameworkElementFactory(typeof(DockPanel));
        var link=new FrameworkElementFactory(typeof(TextBlock));
        link.SetValue(TextBlock.TextProperty,"Hash");
        link.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        link.SetValue(CursorProperty,Cursors.Hand);
        link.SetValue(MarginProperty,new Thickness(0,0,6,0));
        link.SetValue(ToolTipProperty,"Hash this file now");
        link.SetValue(DockPanel.DockProperty,Dock.Left);
        link.SetBinding(VisibilityProperty,new Binding("[?"+key+"]"));
        link.AddHandler(MouseLeftButtonDownEvent,new MouseButtonEventHandler((s,e)=>
        {
            if(((FrameworkElement)s).DataContext is not Entry entry)return;
            e.Handled=true;HashNow(new[]{entry});
        }));
        row.AppendChild(link);row.AppendChild(text);
        return new DataTemplate{VisualTree=row};
    }

    void TrackColumns()
    {
        StopColumns();
        if(fileColumns==null)return;
        foreach(var entry in items)ObserveColumnEntry(entry);
        columnCollection=items as INotifyCollectionChanged;
        if(columnCollection!=null)columnCollection.CollectionChanged+=ColumnsChanged;
        ReconcileColumns();
    }
    void ObserveColumnEntry(Entry entry)
    {if(columnEntries.Add(entry))entry.PropertyChanged+=ColumnDataChanged;}
    void ColumnDataChanged(object sender,PropertyChangedEventArgs e)
    {
        if(IsWidget&&e.PropertyName!=nameof(Entry.Message))return;
        if(e.PropertyName is nameof(Entry.Size) or nameof(Entry.Version) or nameof(Entry.Modified) or nameof(Entry.Message))QueueColumns();
        if(e.PropertyName is nameof(Entry.Size) or nameof(Entry.Modified))QueueHashes();
    }
    void ColumnsChanged(object sender,NotifyCollectionChangedEventArgs e)
    {
        if(e.Action==NotifyCollectionChangedAction.Reset)
        {foreach(var item in columnEntries)item.PropertyChanged-=ColumnDataChanged;columnEntries.Clear();foreach(var item in items)ObserveColumnEntry(item);}
        else
        {
            if(e.OldItems!=null)foreach(Entry item in e.OldItems){item.PropertyChanged-=ColumnDataChanged;columnEntries.Remove(item);}
            if(e.NewItems!=null)foreach(Entry item in e.NewItems)ObserveColumnEntry(item);
        }
        QueueColumns();QueueHashes();
    }
    void QueueColumns()
    {
        if(columnUpdate is {Status:DispatcherOperationStatus.Pending})return;
        columnUpdate=Dispatcher.BeginInvoke(ReconcileColumns,DispatcherPriority.Background);
    }

    /// What a kind shows before the owner chose: the columns its data fills, as Pathweft always did.
    List<string> AutoColumns(string kind,Entry[] data)
    {
        var keys=new List<string>();
        void Add(string key,bool has){if(has)keys.Add(key);}
        if(kind==ColumnCatalog.Drives)
        {
            Add("Capacity",data.Any(e=>e.HasDriveCapacity));Add("Free space",data.Any(e=>e.HasDriveCapacity));Add("Filesystem",data.Any(e=>e.DriveFormat.Length>0));
            Add("Connection",data.Any(e=>e.DriveConnection.Length>0));Add("Location",data.Any(e=>e.DriveLocation.Length>0));
            return keys;
        }
        if(kind==ColumnCatalog.Space)return keys;
        Add("Version",data.Any(e=>e.Version.Length>0));Add("Type",data.Any(e=>e.Ext.Length>0));
        Add("Size",data.Any(e=>e.DisplaySize>=0));Add("Modified",data.Any(e=>e.Modified!=default));
        // the CRC32 column of earlier versions: stored values in archives by default, folders when it was turned on
        Add("#CRC-32",kind==ColumnCatalog.Archive?Settings.ArchiveCrc:kind==ColumnCatalog.Folder&&Settings.FolderCrc&&!IsVirtual(Dir)&&!IsServer(Dir));
        return keys;
    }

    GridViewColumn ColumnFor(string key,bool drives)
    {
        if(drives)return driveColumns.FirstOrDefault(c=>colKey[c]==key);
        if(ColumnCatalog.Common(ColumnCatalog.Drives).Contains(key))return null;
        return fileColumns.FirstOrDefault(c=>colKey[c]==key&&key!="Message")??Extra(key,false);
    }

    void ReconcileColumns()
    {
        if(SpaceAnalyzing){ReconcileSpaceColumns();return;}
        if(fileColumns==null||details is not GridView grid)return;
        if(IsWidget)
        {
            var message=fileColumns.Single(column=>colKey[column]=="Message");
            bool show=items.Any(entry=>entry.Message.Length>0);
            if(show&&!grid.Columns.Contains(message))grid.Columns.Add(message);
            else if(!show)grid.Columns.Remove(message);
            FitName();return;
        }
        string kind=ColumnKind;bool drives=kind==ColumnCatalog.Drives;
        var data=items.Where(e=>!e.IsUp).ToArray();
        var keys=ColumnCatalog.Layout(kind)??AutoColumns(kind,data);
        var wanted=new List<GridViewColumn>{NameColumn};
        var shown=new List<string>();
        foreach(var key in keys)if(ColumnFor(key,drives) is {} column&&!wanted.Contains(column)){wanted.Add(column);shown.Add(key);}
        if(!drives&&data.Any(e=>e.Message.Length>0))wanted.Add(fileColumns.Single(c=>colKey[c]=="Message"));
        ShownColumns=shown.ToArray();
        NameColumn.CellTemplate=drives?(DataTemplate)FindResource("DriveNameCell"):fileNameCell;
        applyingColumns=true;
        try
        {
            bool kindChanged=widthKind!=kind;widthKind=kind;
            foreach(var column in wanted)
                if(column!=NameColumn&&(kindChanged||!grid.Columns.Contains(column))&&colKey.TryGetValue(column,out var key))
                    column.Width=SavedWidth(kind,key)??baseWidths.GetValueOrDefault(column,ColumnCatalog.DefaultWidth(key));
            if(!grid.Columns.SequenceEqual(wanted))
            {
                foreach(var column in grid.Columns.Except(wanted).ToArray())grid.Columns.Remove(column);
                for(int i=0;i<wanted.Count;i++)
                {if(i<grid.Columns.Count&&grid.Columns[i]==wanted[i])continue;grid.Columns.Remove(wanted[i]);grid.Columns.Insert(i,wanted[i]);}
                ShowSortArrow();
            }
        }
        finally{applyingColumns=false;}
        if(List.View is GridView)FitName();
        QueueHashes();
    }

    // ---- the space analyzer's grid: Name, Space used, Of this folder and Status stay; the chosen columns follow ----
    void AttachSpaceColumns(GridView grid)
    {
        spaceGrid=grid;spaceColumns.Clear(); // a new grid each analysis: its columns are its own
        grid.ColumnHeaderContextMenu=HeaderMenu();
        ReconcileSpaceColumns();
    }
    void ReconcileSpaceColumns()
    {
        if(spaceGrid==null||!SpaceAnalyzing)return;
        var keys=(ColumnCatalog.Layout(ColumnCatalog.Space)??new List<string>()).Where(k=>k!="Size").ToList();
        var wanted=spaceGrid.Columns.Where(c=>!spaceColumns.ContainsValue(c)).Take(4).Concat(keys.Select(k=>Extra(k,true))).ToList();
        ShownColumns=keys.ToArray();
        applyingColumns=true;
        try
        {
            foreach(var column in wanted.Skip(4))if(!spaceGrid.Columns.Contains(column))column.Width=SavedWidth(ColumnCatalog.Space,colKey[column])??ColumnCatalog.DefaultWidth(colKey[column]);
            if(!spaceGrid.Columns.SequenceEqual(wanted)){spaceGrid.Columns.Clear();foreach(var column in wanted)spaceGrid.Columns.Add(column);}
        }
        finally{applyingColumns=false;}
        QueueHashes();
    }

    // ---- COLS-01: the column header's menu ----
    ContextMenu HeaderMenu()
    {
        var menu=new ContextMenu();
        System.Windows.Automation.AutomationProperties.SetName(menu,"Columns");
        menu.Opened+=(_,_)=>FillHeaderMenu(menu);
        return menu;
    }

    static MenuItem Check(string header,bool on,Action run,bool enabled=true)
    {
        var item=new MenuItem{Header=header,IsCheckable=true,IsChecked=on,IsEnabled=enabled};
        System.Windows.Automation.AutomationProperties.SetName(item,header);
        if(run!=null)item.Click+=(_,_)=>run();
        return item;
    }

    internal void FillHeaderMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        string kind=ColumnKind;
        var grid=List.View as GridView;
        var column=(menu.PlacementTarget as GridViewColumnHeader)?.Column;
        menu.Items.Add(MainWindow.Item("Size column to fit","","",()=>FitColumns(new[]{column}),column!=null));
        menu.Items.Add(MainWindow.Item("Size all columns to fit","","",()=>FitColumns(grid?.Columns.ToArray()??Array.Empty<GridViewColumn>()),grid!=null));
        menu.Items.Add(new Separator());
        var shown=ShownColumns;
        menu.Items.Add(Check("Name",true,null,enabled:false));
        foreach(var key in ColumnCatalog.Common(kind))menu.Items.Add(Check(ColumnCatalog.Header(key),shown.Contains(key),()=>ToggleColumn(kind,key)));
        if(kind==ColumnCatalog.Drives)return;
        var hashes=MainWindow.Item("Hashes","","",()=>{});
        foreach(var name in Checksums.Names)
        {
            string why=Checksums.Unavailable(name)??(kind==ColumnCatalog.Archive&&name!="CRC-32"?"Inside archives only the stored CRC-32 is shown; the others need the files extracted (Checksum does that)":null);
            var item=Check(name,shown.Contains("#"+name),()=>ToggleColumn(kind,"#"+name),why==null);
            if(why!=null){item.ToolTip=why;ToolTipService.SetShowOnDisabled(item,true);}
            hashes.Items.Add(item);
        }
        hashes.Items.Add(new Separator());
        hashes.Items.Add(Check("Lowercase",Settings.HashLowercase,()=>{Settings.HashLowercase=!Settings.HashLowercase;Settings.Save();ColumnCatalog.CaseChanged();}));
        menu.Items.Add(hashes);
        var properties=shown.Where(ColumnCatalog.IsProperty).ToList();
        if(properties.Count>0)
        {
            menu.Items.Add(new Separator());
            foreach(var key in properties)menu.Items.Add(Check(ColumnCatalog.Header(key),true,()=>ToggleColumn(kind,key)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MainWindow.Item("More…","","",()=>ChooseColumns(kind)));
    }

    void ToggleColumn(string kind,string key)
    {
        var keys=(ColumnCatalog.Layout(kind)??ShownColumns.ToList()).ToList();
        if(!keys.Remove(key))keys.Add(key);
        ColumnCatalog.SetLayout(kind,keys);
    }

    internal void ChooseColumns(string kind)
    {
        var chosen=ChooseColumnsDialog.Show(Window.GetWindow(this),kind,ColumnCatalog.Layout(kind)??ShownColumns.ToList());
        if(chosen==null)return;
        if(chosen.Automatic)ColumnCatalog.ResetLayout(kind);else ColumnCatalog.SetLayout(kind,chosen.Keys);
    }

    /// Size to fit: as wide as what the realized rows and the header need, then kept as the column's width.
    internal void FitColumns(IReadOnlyCollection<GridViewColumn> columns)
    {
        if(columns.Count==0)return;
        fittingColumns=true;
        foreach(var column in columns)column.Width=double.NaN;
        Dispatcher.BeginInvoke(()=>
        {
            fittingColumns=false;
            foreach(var column in columns)
            {
                column.Width=Math.Ceiling(column.ActualWidth);
                if(column==NameColumn&&!IsWidget)Settings.ColumnWidths["Name"]=column.Width; // Name's width is now the owner's, as after dragging it
            }
            if(List.View is GridView&&!columns.Contains(NameColumn))FitName();
        },DispatcherPriority.Loaded);
    }

    // ---- HASH-02/03: what this pane asks the queue for ----
    readonly object hashOwner=new(),hashNowOwner=new();
    CancellationTokenSource hashLookup,cellFilling;
    Task lookupWork=Task.CompletedTask,cellWork=Task.CompletedTask;
    DispatcherTimer hashTimer;
    readonly object postGate=new();
    readonly List<Action> posts=new();
    bool postScheduled;

    internal void QueueHashes()
    {
        if(hashTimer==null)
        {
            hashTimer=new DispatcherTimer(DispatcherPriority.Background,Dispatcher){Interval=TimeSpan.FromMilliseconds(250)};
            hashTimer.Tick+=(_,_)=>{hashTimer.Stop();StartHashes();};
        }
        hashTimer.Stop();hashTimer.Start();
    }

    /// Results from the readers, handed to the rows in batches on the UI thread.
    void Post(Action action)
    {
        lock(postGate){posts.Add(action);if(postScheduled)return;postScheduled=true;}
        Dispatcher.BeginInvoke(()=>
        {
            Action[] batch;
            lock(postGate){batch=posts.ToArray();posts.Clear();postScheduled=false;}
            foreach(var a in batch)a();
        },DispatcherPriority.Background);
    }

    string[] HashNames()=>ShownColumns.Where(ColumnCatalog.IsHash).Select(ColumnCatalog.Algorithm).Where(n=>Checksums.Unavailable(n)==null).ToArray();
    IEnumerable<Entry> ShownRows()=>SpaceAnalyzing?(IEnumerable<Entry>)space?.Visible??Array.Empty<Entry>():view?.Cast<Entry>()??Enumerable.Empty<Entry>();
    bool OnScreen=>IsVisible&&Window.GetWindow(this) is {WindowState:not WindowState.Minimized};
    HashSet<Entry> RealizedRows(IEnumerable<Entry> rows)=>rows.Where(e=>List.ItemContainerGenerator.ContainerFromItem(e) is FrameworkElement{IsVisible:true}).ToHashSet();

    static readonly Dictionary<string,bool> networkRoots=new(StringComparer.OrdinalIgnoreCase);
    internal static bool OnNetwork(string path)
    {
        if(path.StartsWith(@"\\"))return true;
        string root=Path.GetPathRoot(path)??"";
        lock(networkRoots)if(networkRoots.TryGetValue(root,out var known))return known;
        bool network;
        try{network=new DriveInfo(root).DriveType==DriveType.Network;}catch(ArgumentException){network=false;}
        lock(networkRoots)networkRoots[root]=network;
        return network;
    }

    /// Why a file is not hashed by itself (HASH-03), or null.
    internal static string HashSkipReason(Entry e)
    {
        long limit=Math.Max(1,Settings.HashLimitMB)*(1L<<20);
        if(e.Size>limit)return $"Over {Settings.HashLimitMB:N0} MB";
        if(HashCache.IsPlaceholder(e.Attributes))return "Online-only";
        if(OnNetwork(e.Path))return "Network";
        return null;
    }

    HashQueue.Job HashJob(Entry e,string[] names,int priority,bool recall)=>new()
    {
        Path=e.Path,Names=names,Priority=priority,Recall=recall,
        Done=values=>Post(()=>ApplyHashes(e,values,recall)),
        Failed=_=>Post(()=>{foreach(var n in names)if(!e.HasCell("#"+n))e.Offer("#"+n,"Can't read");}),
    };

    void ApplyHashes(Entry e,IReadOnlyDictionary<string,string> values,bool requested)
    {
        foreach(var (name,value) in values)
        {
            string key="#"+name;
            if(value!=null){e.SetCell(key,value);e.SetCell("~"+key,null);e.Withdraw(key);}
            else if(!e.HasCell(key))e.Offer(key,requested?"Can't read":"Online-only"); // a placeholder found only when the file was opened
        }
    }

    /// Rebuilds this pane's requests: the active pane's visible rows first, then the rest of its folder, then
    /// the other pane's. A pane not on screen reads nothing and shows what the cache already knows.
    internal void StartHashes()
    {
        hashLookup?.Cancel();hashLookup=null;
        StartCells();
        var names=HashNames();
        if(names.Length==0||IsWidget||ArchiveRoot!=null&&!SpaceAnalyzing){HashQueue.Cancel(hashOwner);return;}
        var rows=ShownRows().Where(e=>!e.IsDir&&!e.IsUp&&names.Any(n=>!e.HasCell("#"+n))).ToList();
        bool onScreen=OnScreen;
        var visible=onScreen?RealizedRows(rows):new HashSet<Entry>();
        var jobs=new List<HashQueue.Job>();var lookups=new List<Entry>();
        foreach(var e in rows)
        {
            string why=HashSkipReason(e);
            // a Hash click being read keeps its "Hashing…"; a limit raised in Settings takes the offer back
            var open=names.Where(n=>!e.HasCell("#"+n)&&e.Cell("~#"+n)!="Hashing…").ToArray();
            foreach(var n in open)
                if(why!=null)e.Offer("#"+n,why);
                else if(e.Offered("#"+n)){e.Withdraw("#"+n);e.SetCell("~#"+n,null);}
            if(!onScreen||why!=null){if(why!="Network")lookups.Add(e);continue;}
            if(open.Length>0)jobs.Add(HashJob(e,open,(visible.Contains(e)?0:1)+(isActivePane?0:2),recall:false));
        }
        HashQueue.Submit(hashOwner,jobs);
        if(lookups.Count>0)LookUpHashes(lookups,names);
    }

    /// Cached values only: the file's identity is read, never its content.
    void LookUpHashes(List<Entry> rows,string[] names)
    {
        var cts=hashLookup=new CancellationTokenSource();
        var work=rows.Select(e=>(Entry:e,e.Path)).ToList();
        lookupWork=Task.Run(()=>
        {
            foreach(var (e,path) in work)
            {
                if(cts.IsCancellationRequested)return;
                if(HashCache.Identify(path) is not {} key)continue;
                var known=HashCache.Get(key);
                var hit=names.Where(known.ContainsKey).ToDictionary(n=>n,n=>known[n]);
                if(hit.Count>0)Post(()=>ApplyHashes(e,hit,false));
            }
        },cts.Token);
    }

    /// The owner asked: these files are hashed now, first in the queue, whatever their size or where they are.
    internal void HashNow(IEnumerable<Entry> rows)
    {
        var names=HashNames();
        if(names.Length==0)return;
        var jobs=new List<HashQueue.Job>();
        foreach(var e in rows.Where(e=>!e.IsDir&&!e.IsUp))
        {
            var missing=names.Where(n=>!e.HasCell("#"+n)).ToArray();
            if(missing.Length==0)continue;
            foreach(var n in missing){e.Withdraw("#"+n);e.SetCell("~#"+n,"Hashing…");}
            jobs.Add(HashJob(e,missing,-1,recall:true));
        }
        HashQueue.Add(hashNowOwner,jobs);
    }

    /// Requests of this pane still queued or being read (verification).
    internal int HashesPending=>HashQueue.Pending(hashOwner)+HashQueue.Pending(hashNowOwner)+(lookupWork.IsCompleted?0:1)+(cellWork.IsCompleted?0:1);

    void StopHashes()
    {
        hashTimer?.Stop();
        HashQueue.Cancel(hashOwner);HashQueue.Cancel(hashNowOwner);
        hashLookup?.Cancel();hashLookup=null;
        cellFilling?.Cancel();cellFilling=null;
    }

    /// COPY-01: the columns Copy details offers, as shown (the space analyzer's Space used first).
    internal string[] CopyColumns=>SpaceAnalyzing?ShownColumns.Prepend("Space used").ToArray():ShownColumns;

    /// What a file's extended cells need that a listing does not bring (read off the UI thread).
    internal sealed record Extras(DateTime? Created,DateTime Accessed,FileAttributes Attributes,string Owner,string Version,Dictionary<string,(string Text,IComparable Sort)> Properties,IReadOnlyList<ShellProperties.Description> Asked);

    internal static Extras ReadExtras(string path,bool isDir,FileAttributes attributes,bool basics,bool owner,bool version,IReadOnlyList<ShellProperties.Description> properties)
    {
        DateTime? created=null;DateTime accessed=default;FileAttributes attrs=attributes;string ownerText=null,versionText=null;
        Dictionary<string,(string,IComparable)> read=null;
        try
        {
            FileSystemInfo info=isDir?new DirectoryInfo(path):new FileInfo(path);
            if(basics&&info.Exists){created=info.CreationTime;accessed=info.LastAccessTime;attrs=info.Attributes;}
            if(owner)
            {
                try
                {
                    FileSystemSecurity security=isDir?((DirectoryInfo)info).GetAccessControl(AccessControlSections.Owner):((FileInfo)info).GetAccessControl(AccessControlSections.Owner);
                    ownerText=security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier sid?TryAccount(sid):"";
                }
                catch(Exception error) when(error is UnauthorizedAccessException or IOException or PrivilegeNotHeldException or InvalidOperationException){ownerText="";}
            }
            if(version)
            {
                versionText="";
                if(!isDir&&Versioned.Contains(Path.GetExtension(path)))
                {var fvi=System.Diagnostics.FileVersionInfo.GetVersionInfo(path);versionText=(fvi.FileVersion??fvi.ProductVersion??"").Split(' ')[0];}
            }
            if(properties.Count>0)
                read=ShellProperties.Read(path,properties,cachedOnly:HashCache.IsPlaceholder(attrs==0&&info.Exists?info.Attributes:attrs)); // a placeholder: cached properties only
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException){}
        return new(created,accessed,attrs,ownerText,versionText,read??new(),properties);
    }

    internal static void ApplyExtras(Entry e,Extras x)
    {
        if(x.Created is {} created){e.Created=created;e.Accessed=x.Accessed;if(e.Attributes==0)e.Attributes=x.Attributes;}
        if(x.Owner!=null)e.SetCell("Owner",x.Owner);
        if(x.Version!=null){if(x.Version!="")e.Version=x.Version;e.SetCell("~Version","");}
        foreach(var p in x.Asked)if(x.Properties.TryGetValue(p.Name,out var value))e.SetCell(p.Name,value.Text,value.Sort);else e.SetCell(p.Name,"");
        e.RefreshCells();
    }

    /// Work for the property handlers, which expect a single-threaded apartment.
    internal static Task<T> RunSta<T>(Func<T> work)
    {
        var done=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{done.SetResult(work());}catch(Exception error){done.SetException(error);}}){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="Pathweft details"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        return done.Task;
    }

    // ---- COLS-02: Owner, Windows properties and the dates a listing did not bring, filled in the background ----
    void StartCells()
    {
        cellFilling?.Cancel();cellFilling=null;
        string kind=ColumnKind;
        if(IsWidget||kind is ColumnCatalog.Drives or ColumnCatalog.Archive||!OnScreen)return;
        var shown=ShownColumns;
        bool owner=shown.Contains("Owner"),basics=shown.Any(k=>k is "Created" or "Accessed" or "Attributes"),versions=kind==ColumnCatalog.Space&&shown.Contains("Version");
        var properties=shown.Where(ColumnCatalog.IsProperty).Select(ShellProperties.Find).Where(d=>d!=null).ToList();
        if(!owner&&!basics&&!versions&&properties.Count==0)return;
        bool Needs(Entry e)=>owner&&!e.HasCell("Owner")||basics&&e.Created==default||versions&&!e.IsDir&&e.Version.Length==0&&!e.HasCell("~Version")||properties.Any(p=>!e.HasCell(p.Name));
        var rows=ShownRows().Where(e=>!e.IsUp&&!IsVirtual(e.Path)&&Needs(e)).ToList();
        if(rows.Count==0)return;
        var visible=RealizedRows(rows);
        var work=rows.Where(visible.Contains).Concat(rows.Where(e=>!visible.Contains(e))).Select(e=>(Entry:e,e.Path,e.IsDir,e.Attributes,Basics:basics&&e.Created==default,Owner:owner&&!e.HasCell("Owner"),Version:versions&&!e.IsDir&&e.Version.Length==0&&!e.HasCell("~Version"),Properties:properties.Where(p=>!e.HasCell(p.Name)).ToList())).ToList();
        var cts=cellFilling=new CancellationTokenSource();
        cellWork=RunSta(()=>
        {
            foreach(var x in work)
            {
                if(cts.IsCancellationRequested)break;
                var read=ReadExtras(x.Path,x.IsDir,x.Attributes,x.Basics,x.Owner,x.Version,x.Properties);
                Post(()=>ApplyExtras(x.Entry,read));
            }
            return true;
        });
    }

    static string TryAccount(SecurityIdentifier sid)
    {
        try{return sid.Translate(typeof(NTAccount)).Value;}
        catch(IdentityNotMappedException){return sid.Value;}
        catch(SystemException){return sid.Value;}
    }

    void StopColumns()
    {
        columnUpdate?.Abort();columnUpdate=null;
        if(columnCollection!=null)columnCollection.CollectionChanged-=ColumnsChanged;columnCollection=null;
        foreach(var entry in columnEntries)entry.PropertyChanged-=ColumnDataChanged;columnEntries.Clear();
    }
}
