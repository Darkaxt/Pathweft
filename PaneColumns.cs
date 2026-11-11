using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FileExplorer;

public partial class PaneView
{
    GridViewColumn[] fileColumns,driveColumns;
    INotifyCollectionChanged columnCollection;
    readonly HashSet<Entry> columnEntries=new();
    DispatcherOperation columnUpdate;
    DataTemplate fileNameCell;
    void InitializeColumns()
    {
        fileColumns=((GridView)details).Columns.ToArray();fileNameCell=NameColumn.CellTemplate;
        GridViewColumn Column(string name,string property,double width)
        {
            var column=new GridViewColumn{Header=name,DisplayMemberBinding=new Binding(property),Width=Settings.ColumnWidths.GetValueOrDefault("Drive."+name,width)};
            colKey[column]=name;
            var descriptor=DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty,typeof(GridViewColumn));
            descriptor.AddValueChanged(column,(_,_)=>{Settings.ColumnWidths["Drive."+name]=column.Width;if(!ResizingColumn&&List.View is GridView)FitName();});
            return column;
        }
        var crcItem=new MenuItem{Header="CRC32",IsCheckable=true,ToolTip="Stored CRC32 inside archives; in folders, hashed in the background"};
        var headerMenu=new ContextMenu();headerMenu.Items.Add(crcItem);
        headerMenu.Opened+=(_,_)=>crcItem.IsChecked=ArchiveRoot!=null?Settings.ArchiveCrc:Settings.FolderCrc;
        crcItem.Click+=(_,_)=>ShowCrc(crcItem.IsChecked);
        ((GridView)details).ColumnHeaderContextMenu=headerMenu;
        driveColumns=new[]{NameColumn,Column("Capacity",nameof(Entry.TotalText),115),Column("Free space",nameof(Entry.FreeText),115),Column("Filesystem",nameof(Entry.DriveFormat),90),Column("Connection",nameof(Entry.DriveConnection),160),Column("Location",nameof(Entry.DriveLocation),220)};
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
        QueueColumns();
    }
    void QueueColumns()
    {
        if(columnUpdate is {Status:DispatcherOperationStatus.Pending})return;
        columnUpdate=Dispatcher.BeginInvoke(ReconcileColumns,DispatcherPriority.Background);
    }
    void ReconcileColumns()
    {
        if(SpaceAnalyzing)return;
        if(fileColumns==null||details is not GridView grid)return;
        if(IsWidget)
        {
            var message=fileColumns.Single(column=>colKey[column]=="Message");
            bool show=items.Any(entry=>entry.Message.Length>0);
            if(show&&!grid.Columns.Contains(message))grid.Columns.Add(message);
            else if(!show)grid.Columns.Remove(message);
            FitName();return;
        }
        bool drives=Dir==ThisPC&&!InSearch;
        var data=items.Where(e=>!e.IsUp).ToArray();
        bool Has(string key)=>key switch
        {
            "Name"=>true,"Version"=>data.Any(e=>e.Version.Length>0),"Type"=>data.Any(e=>e.Ext.Length>0),
            "Size"=>data.Any(e=>e.DisplaySize>=0),"Modified"=>data.Any(e=>e.Modified!=default),
            "Message"=>data.Any(e=>e.Message.Length>0),"CRC32"=>CrcShown,
            "Capacity" or "Free space"=>data.Any(e=>e.HasDriveCapacity),"Filesystem"=>data.Any(e=>e.DriveFormat.Length>0),
            "Connection"=>data.Any(e=>e.DriveConnection.Length>0),"Location"=>data.Any(e=>e.DriveLocation.Length>0),_=>false
        };
        var wanted=(drives?driveColumns:fileColumns).Where(column=>Has(colKey[column])).ToArray();
        NameColumn.CellTemplate=drives?(DataTemplate)FindResource("DriveNameCell"):fileNameCell;
        if(!grid.Columns.SequenceEqual(wanted))
        {
            foreach(var column in grid.Columns.Except(wanted).ToArray())grid.Columns.Remove(column);
            for(int i=0;i<wanted.Length;i++)
            {if(i<grid.Columns.Count&&grid.Columns[i]==wanted[i])continue;grid.Columns.Remove(wanted[i]);grid.Columns.Insert(i,wanted[i]);}
            ShowSortArrow();
            if(List.View is GridView)FitName();
        }
    }
    // CRC32: an archive's stored values, or (opt-in) a folder's files hashed lazily, visible rows first.
    bool CrcShown=>!InSearch&&!IsWidget&&(ArchiveRoot!=null?Settings.ArchiveCrc:Settings.FolderCrc&&!IsVirtual(Dir)&&!IsServer(Dir));
    CancellationTokenSource crcWork;
    internal Task CrcCompletion=Task.CompletedTask;
    internal void ShowCrc(bool show)
    {
        if(ArchiveRoot!=null)Settings.ArchiveCrc=show;else Settings.FolderCrc=show;
        Settings.Save();ReconcileColumns();StartCrc();
    }
    void StartCrc()
    {
        crcWork?.Cancel();crcWork=null;
        if(ArchiveRoot!=null||!CrcShown||indexedSearch||SpaceAnalyzing||view==null)return;
        var all=view.Cast<Entry>().Where(e=>!e.IsDir&&!e.IsUp&&e.Crc==null).ToList();
        if(all.Count==0)return;
        var shown=all.Where(e=>List.ItemContainerGenerator.ContainerFromItem(e) is FrameworkElement{IsVisible:true}).ToHashSet();
        var todo=all.Where(shown.Contains).Concat(all.Where(e=>!shown.Contains(e))).ToList();
        var cts=crcWork=new CancellationTokenSource();var done=new TaskCompletionSource();CrcCompletion=done.Task;
        new Thread(()=>
        {
            var ui=new UiBatch(Dispatcher);
            try
            {
                FileCrc.LowPriority(()=>
                {
                    foreach(var e in todo)
                    {
                        if(cts.IsCancellationRequested)return;
                        if(e.Size>4<<20)ui.Flush(); // a long read must not hold back finished rows
                        if(FileCrc.Get(e.Path,cts.Token) is {} crc)ui.Add(()=>{if(crcWork==cts&&!cts.IsCancellationRequested)e.Crc=crc;});
                    }
                });
            }
            catch(OperationCanceledException){}
            finally{ui.Flush();done.TrySetResult();}
        }){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="Pathweft CRC32"}.Start();
    }
    void StopColumns()
    {
        columnUpdate?.Abort();columnUpdate=null;
        if(columnCollection!=null)columnCollection.CollectionChanged-=ColumnsChanged;columnCollection=null;
        foreach(var entry in columnEntries)entry.PropertyChanged-=ColumnDataChanged;columnEntries.Clear();
    }
}
