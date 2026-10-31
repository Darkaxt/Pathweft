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
        driveColumns=new[]{NameColumn,Column("Capacity",nameof(Entry.TotalText),115),Column("Free space",nameof(Entry.FreeText),115),Column("Filesystem",nameof(Entry.DriveFormat),90),Column("Connection",nameof(Entry.DriveConnection),160),Column("Location",nameof(Entry.DriveLocation),220)};
    }
    void TrackColumns()
    {
        StopColumns();
        if(IsWidget||fileColumns==null)return;
        foreach(var entry in items)ObserveColumnEntry(entry);
        columnCollection=items as INotifyCollectionChanged;
        if(columnCollection!=null)columnCollection.CollectionChanged+=ColumnsChanged;
        ReconcileColumns();
    }
    void ObserveColumnEntry(Entry entry)
    {if(columnEntries.Add(entry))entry.PropertyChanged+=ColumnDataChanged;}
    void ColumnDataChanged(object sender,PropertyChangedEventArgs e)
    {if(e.PropertyName is nameof(Entry.Size) or nameof(Entry.Version) or nameof(Entry.Modified))QueueColumns();}
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
        if(IsWidget||fileColumns==null||details is not GridView grid)return;
        bool drives=Dir==ThisPC&&!InSearch;
        var data=items.Where(e=>!e.IsUp).ToArray();
        bool Has(string key)=>key switch
        {
            "Name"=>true,"Version"=>data.Any(e=>e.Version.Length>0),"Type"=>data.Any(e=>e.Ext.Length>0),
            "Size"=>data.Any(e=>e.Size>=0),"Modified"=>data.Any(e=>e.Modified!=default),
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
    void StopColumns()
    {
        columnUpdate?.Abort();columnUpdate=null;
        if(columnCollection!=null)columnCollection.CollectionChanged-=ColumnsChanged;columnCollection=null;
        foreach(var entry in columnEntries)entry.PropertyChanged-=ColumnDataChanged;columnEntries.Clear();
    }
}
