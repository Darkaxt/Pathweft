using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FileExplorer;

internal sealed class PerformanceProcessRow : INotifyPropertyChanged
{
    internal ProcessMetric Sample;
    public int Pid=>Sample.Pid;
    public long Started=>Sample.Started;
    public string Process=>Sample.Process;
    public double Usage=>Sample.Usage;
    public double SortUsage=>Math.Round(Sample.Usage*2);
    public long Memory=>Sample.Memory;
    public string Percent=>Observed?Sample.Percent:"—";
    public string MemoryText=>Observed?Sample.MemoryText:"—";
    public bool Pinned {get;internal set;}
    public string Pin=>Pinned?"\uE718":"";
    public bool Observed {get;internal set;}=true;
    public string State=>Observed?Pinned?"Pinned · live":"Live":"No current observation";
    public string ExecutablePath {get;internal set;}="Reading…";
    public string CommandLine {get;internal set;}="Reading…";
    public string MetadataIssue {get;internal set;}
    public string PathTooltip=>MetadataIssue??ExecutablePath;
    public string CommandTooltip=>MetadataIssue??CommandLine;
    internal bool MetadataRequested;
    public event PropertyChangedEventHandler PropertyChanged;
    internal PerformanceProcessRow(ProcessMetric sample)=>Sample=sample;
    internal void Notify()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
}

internal sealed record PerformanceProcessMetadata(string ExecutablePath,string CommandLine,string Issue=null);
internal static class PerformanceProcessMetadataReader
{
    // Documented local Win32_Process properties, queried only for visible
    // optional columns. Nothing is persisted and no privilege is requested.
    internal static Dictionary<(int Pid,long Started),PerformanceProcessMetadata> Read((int Pid,long Started)[] identities,CancellationToken cancel)
    {
        var result=new Dictionary<(int,long),PerformanceProcessMetadata>();
        var wanted=identities.ToLookup(x=>x.Pid);
        object locator=null,services=null,rows=null;
        try
        {
            locator=Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator",true));
            services=((dynamic)locator).ConnectServer(".",@"root\cimv2");
            cancel.ThrowIfCancellationRequested();
            rows=((dynamic)services).ExecQuery("SELECT ProcessId,CreationDate,ExecutablePath,CommandLine FROM Win32_Process WHERE "+string.Join(" OR ",wanted.Select(x=>"ProcessId="+x.Key.ToString(CultureInfo.InvariantCulture))),"WQL",0);
            // SWbemObjectSet exposes ItemIndex, not a managed IEnumerable;
            // process properties belong to SWbemObject.Properties_.
            int count=Convert.ToInt32(((dynamic)rows).Count,CultureInfo.InvariantCulture);
            for(int index=0;index<count;index++)
            {
                cancel.ThrowIfCancellationRequested();object row=((dynamic)rows).ItemIndex(index);
                try
                {
                    int pid=Convert.ToInt32(Property(row,"ProcessId"),CultureInfo.InvariantCulture);
                    if(!wanted.Contains(pid))continue;
                    long? started=CreationTicks((string)Property(row,"CreationDate"));
                    foreach(var identity in wanted[pid])
                        if(started.HasValue&&started.Value/10==identity.Started/10)
                            result[identity]=new((string)Property(row,"ExecutablePath")??"Unavailable",(string)Property(row,"CommandLine")??"Unavailable");
                }
                finally{if(Marshal.IsComObject(row))Marshal.FinalReleaseComObject(row);}
            }
        }
        catch(Exception error)when(error is COMException or UnauthorizedAccessException or TypeLoadException)
        {foreach(var identity in identities)result.TryAdd(identity,new("Unavailable","Unavailable","Windows process metadata unavailable (0x"+error.HResult.ToString("X8")+"): "+error.Message));}
        finally
        {
            foreach(var value in new[]{rows,services,locator})if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);
        }
        return result;
    }
    static object Property(object row,string name)
    {
        object properties=null,property=null;
        try{properties=((dynamic)row).Properties_;property=((dynamic)properties).Item(name,0);return ((dynamic)property).Value;}
        finally{foreach(var value in new[]{property,properties})if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
    }
    static long? CreationTicks(string value)
    {
        if(value?.Length!=25||!DateTime.TryParseExact(value[..21],"yyyyMMddHHmmss.ffffff",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)||!int.TryParse(value[22..],out int minutes))return null;
        return new DateTimeOffset(date,TimeSpan.FromMinutes(value[21]=='-'?-minutes:minutes)).UtcTicks;
    }
}

internal sealed partial class PerformancePanel
{
    readonly Button columnsButton=new(){Content="Columns",MinHeight=32};
    readonly ContextMenu processColumnsMenu=new();
    readonly Dictionary<string,bool> processColumns=new(){["Process"]=true,["Pid"]=true,["Percent"]=true,["MemoryText"]=true,["ExecutablePath"]=false,["CommandLine"]=false,["State"]=false};
    readonly Dictionary<(int Pid,long Started),PerformanceProcessRow> processRows=new();
    readonly ObservableCollection<PerformanceProcessRow> processItems=new();
    readonly TextBox processFilter=new(){MinWidth=150,MaxWidth=220,ToolTip="Filter processes, PIDs and visible metadata"};
    readonly CancellationTokenSource metadataStopped=new();
    Task metadataWork=Task.CompletedTask,metadataRead=Task.CompletedTask;
    void BuildProcessControls()
    {
        columnsButton.SetResourceReference(StyleProperty,"AuxiliaryAction");
        AutomationProperties.SetName(columnsButton,"Choose process columns");
        AutomationProperties.SetName(processFilter,"Filter CPU processes");
        processFilter.SetResourceReference(StyleProperty,"PerformanceFilter");
        processFilter.TextChanged+=(_,_)=>SortRows();
        var rowStyle=new Style(typeof(ListViewItem),table.TryFindResource(typeof(ListViewItem)) as Style);
        rowStyle.Setters.Add(new Setter(AutomationProperties.ItemStatusProperty,new Binding("State")));
        table.ItemContainerStyle=rowStyle;
        foreach(var (label,property) in new[]{("Process","Process"),("PID","Pid"),("CPU","Percent"),("Working set","MemoryText"),("Executable path","ExecutablePath"),("Command line","CommandLine"),("Status","State")})
        {
            var item=new MenuItem{Header=label,IsCheckable=true,IsChecked=processColumns[property],IsEnabled=property!="Process",StaysOpenOnClick=true};
            // The application's menu template presents Icon, not the native
            // check gutter. Supply the checked state through that same slot.
            var mark=new TextBlock{Text="\uE73E",FontFamily=new("Segoe Fluent Icons"),FontSize=12};
            mark.SetBinding(VisibilityProperty,new Binding(nameof(MenuItem.IsChecked)){Source=item,Converter=(BooleanToVisibilityConverter)FindResource("Vis")});item.Icon=mark;
            item.Click+=(_,_)=>{processColumns[property]=item.IsChecked;ConfigureTable();RefreshProcessRows(monitor.Details);};
            processColumnsMenu.Items.Add(item);
        }
        columnsButton.Click+=(_,_)=>{processColumnsMenu.PlacementTarget=columnsButton;processColumnsMenu.IsOpen=true;};
        var rowMenu=new ContextMenu();var pin=new MenuItem{Header="Pin / unpin process"};rowMenu.Items.Add(pin);
        pin.Click+=(_,_)=>ToggleProcessPin(table.SelectedItem as PerformanceProcessRow);
        rowMenu.Opened+=(_,_)=>pin.IsEnabled=table.SelectedItem is PerformanceProcessRow;
        table.ContextMenu=rowMenu;
        table.AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler((_,e)=>
        {
            if(e.ChangedButton==MouseButton.Left&&ItemsControl.ContainerFromElement(table,e.OriginalSource as DependencyObject) is ListViewItem {Content:PerformanceProcessRow row})ToggleProcessPin(row);
        }),true);
        table.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space&&table.SelectedItem is PerformanceProcessRow row){ToggleProcessPin(row);e.Handled=true;}};
        table.ToolTip="Click a process or press Space to pin/unpin it above the live list.";
    }
    void ConfigureProcessTable(GridView view)
    {
        foreach(var (label,property,width) in new[]{("Process","Process",170d),("PID","Pid",70d),("CPU","Percent",90d),("Working set","MemoryText",120d),("Executable path","ExecutablePath",340d),("Command line","CommandLine",480d),("Status","State",170d)})
        {
            if(!processColumns[property])continue;
            var column=new GridViewColumn{Header=new GridViewColumnHeader{Content=label,Tag=property},Width=width};
            if(property=="Process")
            {
                var body=new FrameworkElementFactory(typeof(StackPanel));body.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
                var glyph=new FrameworkElementFactory(typeof(TextBlock));glyph.SetValue(TextBlock.FontFamilyProperty,new System.Windows.Media.FontFamily("Segoe Fluent Icons"));glyph.SetValue(TextBlock.FontSizeProperty,11d);glyph.SetValue(FrameworkElement.WidthProperty,18d);glyph.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);glyph.SetBinding(TextBlock.TextProperty,new Binding("Pin"));body.AppendChild(glyph);
                var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(property));body.AppendChild(text);column.CellTemplate=new DataTemplate{VisualTree=body};
            }
            else column.DisplayMemberBinding=new Binding(property);
            if(property is "ExecutablePath" or "CommandLine")
            {
                column.DisplayMemberBinding=null;var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(property));text.SetBinding(FrameworkElement.ToolTipProperty,new Binding(property=="ExecutablePath"?"PathTooltip":"CommandTooltip"));text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);column.CellTemplate=new DataTemplate{VisualTree=text};
            }
            view.Columns.Add(column);
        }
    }
    void ToggleProcessPin(PerformanceProcessRow row)
    {
        if(row==null)return;row.Pinned=!row.Pinned;row.Notify();SortRows();
        if(row.Pinned)table.ScrollIntoView(row);
    }
    void RefreshProcessRows(ProcessMetric[] samples)
    {
        var observed=samples.Select(x=>(x.Pid,x.Started)).ToHashSet();
        foreach(var key in processRows.Keys.Where(key=>!observed.Contains(key)).ToArray())
        {
            var row=processRows[key];if(row.Pinned){row.Observed=false;row.Notify();}else processRows.Remove(key);
        }
        foreach(var sample in samples)
        {
            var key=(sample.Pid,sample.Started);
            if(!processRows.TryGetValue(key,out var row))processRows[key]=row=new(sample);
            else{row.Sample=sample;row.Observed=true;row.Notify();}
        }
        // Keep stable row objects and the selected identity across sampler ticks.
        var selection=table.SelectedItem as PerformanceProcessRow;
        var rows=processRows.Values.ToArray();var wanted=rows.ToHashSet();
        // SysOptimizer UpdateProcessRows: preserve containers while membership
        // changes instead of replacing the entire ItemsSource every tick.
        for(int i=processItems.Count-1;i>=0;i--)if(!wanted.Contains(processItems[i]))processItems.RemoveAt(i);
        foreach(var row in rows)if(!processItems.Contains(row))processItems.Add(row);
        if(!ReferenceEquals(table.ItemsSource,processItems))table.ItemsSource=processItems;
        if(selection!=null&&wanted.Contains(selection))table.SelectedItem=selection;
        SortRows();LoadProcessMetadata();
    }
    void LoadProcessMetadata()
    {
        if(disposed||!Showing||selected!=Cpu||Historical||(!processColumns["ExecutablePath"]&&!processColumns["CommandLine"])||!metadataWork.IsCompleted)return;
        var pending=processRows.Where(x=>x.Value.Observed&&!x.Value.MetadataRequested).Select(x=>x.Key).ToArray();
        if(pending.Length==0)return;
        foreach(var key in pending)processRows[key].MetadataRequested=true;
        metadataWork=ResolveProcessMetadata(pending);
    }
    async Task ResolveProcessMetadata((int Pid,long Started)[] identities)
    {
        try
        {
            var read=Task.Run(()=>PerformanceProcessMetadataReader.Read(identities,metadataStopped.Token),metadataStopped.Token);metadataRead=read;
            var result=await read;
            if(disposed)return;
            foreach(var key in identities)if(processRows.TryGetValue(key,out var row))
            {
                var value=result.GetValueOrDefault(key,new("Process exited or identity unavailable","Process exited or identity unavailable"));
                row.ExecutablePath=value.ExecutablePath;row.CommandLine=value.CommandLine;row.MetadataIssue=value.Issue;row.Notify();
            }
        }
        catch(OperationCanceledException)when(metadataStopped.IsCancellationRequested){}
    }
}
