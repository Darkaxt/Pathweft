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
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
    public string State=>(Observed?Pinned?"Pinned · live":"Live":"No current observation")+(Sample.ParentEnded==true?" · parent ended":"");
    // MONITOR-23: unknown handle counts sort below every observed count.
    public int Handles=>Sample.Handles??-1;
    public string HandlesText=>Observed&&Sample.Handles is {} held?held.ToString("N0",CultureInfo.CurrentCulture):"—";
    public string ParentMark=>Sample.ParentEnded==true?"parent ended":"";
    public Visibility ParentMarkVisibility=>Sample.ParentEnded==true?Visibility.Visible:Visibility.Collapsed;
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
    // WMI reports an empty property (a process that exited, or one whose path is hidden) as DBNull.
    internal static object Property(object row,string name)
    {
        object properties=null,property=null;
        try{properties=((dynamic)row).Properties_;property=((dynamic)properties).Item(name,0);object value=((dynamic)property).Value;return value is DBNull?null:value;}
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
    // CPU and RAM keep their own column choices.
    static Dictionary<string,bool> DefaultProcessColumns()=>new(){["Process"]=true,["Pid"]=true,["Percent"]=true,["MemoryText"]=true,["ExecutablePath"]=false,["CommandLine"]=false,["State"]=false,["HandlesText"]=false};
    readonly Dictionary<string,bool> cpuColumns=SavedProcessColumns(Settings.CpuProcessColumns),ramColumns=SavedProcessColumns(Settings.RamProcessColumns);
    static Dictionary<string,bool> SavedProcessColumns(string[] shown)
    {
        var columns=DefaultProcessColumns();if(shown==null)return columns;
        foreach(var key in columns.Keys.ToArray())columns[key]=key=="Process"||shown.Contains(key);
        return columns;
    }
    Dictionary<string,bool> processColumns=>selected==Ram?ramColumns:cpuColumns;
    // MONITOR-23: CPU and RAM share the live process table; CPU% is CPU-only and Handles RAM-only.
    static readonly (string Label,string Property,double Width)[] processColumnSet=[("Process","Process",170d),("PID","Pid",70d),("CPU","Percent",90d),("Working set","MemoryText",120d),("Executable path","ExecutablePath",340d),("Command line","CommandLine",480d),("Status","State",170d),("Handles","HandlesText",100d)];
    bool Offered(string property)=>property switch{"Percent"=>selected!=Ram,"HandlesText"=>selected==Ram,_=>true};
    readonly Button handleTypesButton=new(){Content="Handle types",MinHeight=32,Visibility=Visibility.Collapsed},endProcessButton=new(){Content="End process…",MinHeight=32,Visibility=Visibility.Collapsed},openDriveButton=new(){Content="Open drive I/O",Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left};
    readonly StackPanel processPanel=new(){Visibility=Visibility.Collapsed,Margin=new(0,9,0,1)};
    readonly TextBlock ramFacts=new(){TextWrapping=TextWrapping.Wrap,FontSize=11},processActionText=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,3,0,0)};
    PerformanceHandleTypes handleTypes;bool handleReading;Task handleWork=Task.CompletedTask;
    PerformanceProcessRow PinnedTarget=>ReferenceEquals(table.ItemsSource,processItems)&&table.SelectedItem is PerformanceProcessRow {Pinned:true,Observed:true} row?row:null;
    readonly Dictionary<(int Pid,long Started),PerformanceProcessRow> processRows=new();
    readonly ObservableCollection<PerformanceProcessRow> processItems=new();
    readonly TextBox processFilter=new(){MinWidth=120,MaxWidth=300,ToolTip="Filter processes, PIDs and visible metadata"};
    readonly CancellationTokenSource metadataStopped=new();
    Task metadataWork=Task.CompletedTask,metadataRead=Task.CompletedTask;
    void BuildProcessControls()
    {
        columnsButton.SetResourceReference(StyleProperty,"AuxiliaryAction");
        AutomationProperties.SetName(columnsButton,"Choose process columns");
        AutomationProperties.SetName(processFilter,"Filter processes in the graph and table");
        processFilter.SetResourceReference(StyleProperty,"PerformanceFilter");
        processFilter.TextChanged+=(_,_)=>ProcessFilterChanged();
        var rowStyle=new Style(typeof(ListViewItem),table.TryFindResource(typeof(ListViewItem)) as Style);
        rowStyle.Setters.Add(new Setter(AutomationProperties.ItemStatusProperty,new Binding("State")));
        table.ItemContainerStyle=rowStyle;
        foreach(var (label,property,_) in processColumnSet)
        {
            var item=new MenuItem{Header=label,Tag=property,IsCheckable=true,IsChecked=processColumns[property],IsEnabled=property!="Process",StaysOpenOnClick=true};
            // The application's menu template presents Icon, not the native
            // check gutter. Supply the checked state through that same slot.
            var mark=new TextBlock{Text="\uE73E",FontFamily=new("Segoe Fluent Icons"),FontSize=12};
            mark.SetBinding(VisibilityProperty,new Binding(nameof(MenuItem.IsChecked)){Source=item,Converter=(BooleanToVisibilityConverter)FindResource("Vis")});item.Icon=mark;
            item.Click+=(_,_)=>
            {
                processColumns[property]=item.IsChecked;
                var shown=processColumns.Where(x=>x.Value).Select(x=>x.Key).ToArray();
                if(selected==Ram)Settings.RamProcessColumns=shown;else Settings.CpuProcessColumns=shown;Settings.Save();
                ConfigureTable();RefreshProcessRows(monitor.Details);
            };
            processColumnsMenu.Items.Add(item);
        }
        columnsButton.Click+=(_,_)=>{processColumnsMenu.PlacementTarget=columnsButton;processColumnsMenu.Placement=PlacementMode.Bottom;processColumnsMenu.IsOpen=true;};
        foreach(var button in new[]{handleTypesButton,endProcessButton,openDriveButton})button.SetResourceReference(StyleProperty,"AuxiliaryAction");
        handleTypesButton.ToolTip="Count the pinned process's handles by type (read once, on demand)";
        endProcessButton.ToolTip="End the pinned process after confirmation; its PID and start time are rechecked first";
        openDriveButton.ToolTip="Most handles are files: open Drive I/O for the current folder's drive to see which files are read or written";
        handleTypesButton.Click+=(_,_)=>ShowHandleTypes();endProcessButton.Click+=(_,_)=>EndPinnedProcess();openDriveButton.Click+=(_,_)=>Select(Drive);
        table.SelectionChanged+=(_,_)=>UpdateProcessActions();
        var rowMenu=new ContextMenu();var pin=new MenuItem{Header="Pin / unpin process"};rowMenu.Items.Add(pin);
        var history=new MenuItem{Header="Inspect app history"};rowMenu.Items.Add(history);
        history.Click+=(_,_)=>
        {
            if(table.SelectedItem is not PerformanceProcessRow row)return;
            appFilter.Text=row.Process;analysisKind.SelectedIndex=4;
        };
        pin.Click+=(_,_)=>ToggleProcessPin(table.SelectedItem as PerformanceProcessRow);
        rowMenu.Opened+=(_,_)=>pin.IsEnabled=history.IsEnabled=table.SelectedItem is PerformanceProcessRow;
        table.ContextMenu=rowMenu;
        // As in Task Manager: right-clicking the header row chooses columns; blank space offers nothing.
        table.ContextMenuOpening+=(_,e)=>
        {
            if(!ReferenceEquals(table.ItemsSource,processItems)||e.OriginalSource is not DependencyObject source)return;
            for(var node=source;node!=null&&!ReferenceEquals(node,table);node=VisualTreeHelper.GetParent(node))
            {
                if(node is GridViewHeaderRowPresenter or GridViewColumnHeader)
                {
                    e.Handled=true;processColumnsMenu.PlacementTarget=table;processColumnsMenu.Placement=PlacementMode.MousePoint;processColumnsMenu.IsOpen=true;return;
                }
                if(node is ListViewItem)return;
            }
            e.Handled=true;
        };
        table.AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler((_,e)=>
        {
            if(e.ChangedButton==MouseButton.Left&&ItemsControl.ContainerFromElement(table,e.OriginalSource as DependencyObject) is ListViewItem {Content:PerformanceProcessRow row})ToggleProcessPin(row);
        }),true);
        table.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space&&table.SelectedItem is PerformanceProcessRow row){ToggleProcessPin(row);e.Handled=true;}};
        table.ToolTip="Click a process or press Space to pin/unpin it above the live list.";
    }
    void BuildProcessHeader(StackPanel title)
    {
        ramFacts.SetResourceReference(TextBlock.ForegroundProperty,"Muted");processActionText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        AutomationProperties.SetName(processPanel,"Memory and process action details");
        processPanel.Children.Add(ramFacts);processPanel.Children.Add(processActionText);processPanel.Children.Add(openDriveButton);title.Children.Add(processPanel);
    }
    void UpdateProcessActions()
    {
        bool live=!Historical&&selected is Cpu or Ram;var target=PinnedTarget;
        processPanel.Visibility=endProcessButton.Visibility=live?Visibility.Visible:Visibility.Collapsed;
        handleTypesButton.Visibility=live&&selected==Ram?Visibility.Visible:Visibility.Collapsed;
        ramFacts.Visibility=selected==Ram?Visibility.Visible:Visibility.Collapsed;
        handleTypesButton.IsEnabled=endProcessButton.IsEnabled=target!=null&&!handleReading;
        // The breakdown belongs to one exact identity; drop it once another process is pinned.
        if(handleTypes!=null&&!handleReading&&(target==null||target.Pid!=handleTypes.Pid||target.Started!=handleTypes.Started))
        {handleTypes=null;openDriveButton.Visibility=Visibility.Collapsed;}
        string actions=selected==Ram?"inspect its handle types or end it":"end it";
        if(!handleReading&&handleTypes==null)processActionText.Text=target==null?$"Click a process to pin it, then {actions}.":$"Pinned {target.Process} (PID {target.Pid}) · actions apply to this exact process start.";
        var ram=monitor.Latest?.Ram;
        ramFacts.Text=ram==null?"Memory counters unavailable":$"Physical {MainWindow.Fmt(ram.Used)} of {MainWindow.Fmt(ram.Total)}"+
            (ram is {KernelPaged:{} paged,KernelNonpaged:{} nonpaged}?$" · kernel paged pool {MainWindow.Fmt(paged)} · non-paged pool {MainWindow.Fmt(nonpaged)}":" · kernel pool unavailable")+
            (ram.Handles is {} handles?$" · {handles:N0} handles system-wide":"");
    }
    async void ShowHandleTypes()
    {
        if(PinnedTarget is not {} row||handleReading)return;
        handleReading=true;openDriveButton.Visibility=Visibility.Collapsed;processActionText.Text=$"Reading handle types for {row.Process} (PID {row.Pid})…";UpdateProcessActions();
        try
        {
            var work=Task.Run(()=>PerformanceProcessControl.HandleTypes(row.Pid,row.Started,metadataStopped.Token),metadataStopped.Token);handleWork=work;
            var types=await work;if(disposed)return;
            handleTypes=types;processActionText.Text=$"{row.Process} (PID {row.Pid}) · {types}";
            openDriveButton.Visibility=types.Dominant=="File"?Visibility.Visible:Visibility.Collapsed;
        }
        catch(OperationCanceledException)when(metadataStopped.IsCancellationRequested){}
        catch(OutOfMemoryException){handleTypes=new(row.Pid,row.Started,[],0,"Not enough memory to read the system handle table");if(!disposed)processActionText.Text=$"{row.Process} (PID {row.Pid}) · {handleTypes}";}
        finally{handleReading=false;if(!disposed)UpdateProcessActions();}
    }
    void EndPinnedProcess()
    {
        if(PinnedTarget is not {} row)return;
        string question=$"End {row.Process} (PID {row.Pid})?\n\nUnsaved work in that process is lost. Pathweft first rechecks the PID and start time and refuses if the PID now belongs to another process.";
        if(MessageBox.Show(Window.GetWindow(this),question,"End process",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        // Explicit result: a refusal is shown; an ended process becomes "No current observation" on the next sample.
        if(PerformanceProcessControl.End(row.Pid,row.Started) is {} issue)MessageBox.Show(Window.GetWindow(this),issue,"End process",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    // MONITOR-23 parent-ended marker: a small badge at the end of the Process cell, apart from the name
    // (which trims first); the Status column and item status repeat it.
    internal static DataTemplate ProcessCell(string pin)
    {
        var body=new FrameworkElementFactory(typeof(DockPanel));body.SetValue(DockPanel.LastChildFillProperty,true);
        if(pin!=null)
        {
            var glyph=new FrameworkElementFactory(typeof(TextBlock));glyph.SetValue(TextBlock.FontFamilyProperty,new System.Windows.Media.FontFamily("Segoe Fluent Icons"));glyph.SetValue(TextBlock.FontSizeProperty,11d);glyph.SetValue(FrameworkElement.WidthProperty,18d);glyph.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);glyph.SetBinding(TextBlock.TextProperty,new Binding(pin));glyph.SetValue(DockPanel.DockProperty,Dock.Left);body.AppendChild(glyph);
        }
        var badge=new FrameworkElementFactory(typeof(Border));badge.SetValue(DockPanel.DockProperty,Dock.Right);badge.SetValue(Border.CornerRadiusProperty,new CornerRadius(3));badge.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        badge.SetResourceReference(Border.BackgroundProperty,Appearance.ResourceKey("#14FFFFFF"));badge.SetResourceReference(Border.BorderBrushProperty,Appearance.ResourceKey("#2EFFFFFF"));
        badge.SetValue(Border.PaddingProperty,new Thickness(5,0,5,1));badge.SetValue(FrameworkElement.MarginProperty,new Thickness(8,0,0,0));badge.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        badge.SetBinding(UIElement.VisibilityProperty,new Binding("ParentMarkVisibility"));badge.SetValue(FrameworkElement.ToolTipProperty,"The process that started this one has exited (PID reuse checked by start time)");
        var mark=new FrameworkElementFactory(typeof(TextBlock));mark.SetBinding(TextBlock.TextProperty,new Binding("ParentMark"));mark.SetValue(TextBlock.FontSizeProperty,10.5d);mark.SetResourceReference(TextBlock.ForegroundProperty,"Muted");badge.AppendChild(mark);body.AppendChild(badge);
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("Process"));text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);body.AppendChild(text);
        return new DataTemplate{VisualTree=body};
    }
    void ConfigureProcessTable(GridView view)
    {
        foreach(MenuItem item in processColumnsMenu.Items){item.Visibility=Offered((string)item.Tag)?Visibility.Visible:Visibility.Collapsed;item.IsChecked=processColumns[(string)item.Tag];}
        foreach(var (label,property,width) in processColumnSet)
        {
            if(!processColumns[property]||!Offered(property))continue;
            var column=new GridViewColumn{Header=new GridViewColumnHeader{Content=label,Tag=property},Width=width};
            if(property=="Process")
            {
                column.CellTemplate=ProcessCell("Pin");
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
        if(row==null)return;row.Pinned=!row.Pinned;row.Notify();SortRows();UpdateProcessActions();
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
        if(disposed||!Showing||selected is not (Cpu or Ram)||Historical||(!processColumns["ExecutablePath"]&&!processColumns["CommandLine"])||!metadataWork.IsCompleted)return;
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
