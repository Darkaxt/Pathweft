using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;

namespace FileExplorer;

internal sealed class MaintenancePanel : Border,IAsyncDisposable
{
    internal event Action Back;
    internal event Action AnalysisChanged;
    internal bool Expanded {get;private set;}
    internal static readonly string[] Categories=["Cleanup","Startup","Services","Windows tweaks","Windows apps"];
    readonly System.Windows.Controls.Primitives.UniformGrid cards=new(){Columns=5,Visibility=Visibility.Collapsed,Margin=new(0,10,0,0)};
    readonly Button[] cardButtons=new Button[5];readonly TextBlock[] cardValues=new TextBlock[5],cardSubtitles=new TextBlock[5];int selectedCategory;
    readonly TextBlock[] cardLabels=new TextBlock[5];string fullDescription="";
    internal FrameworkElement Cards=>cards;
    readonly ListView table=new(){SelectionMode=SelectionMode.Single,Background=Brushes.Transparent,BorderThickness=new(0)};
    readonly TextBlock title=new(){Text="Maintenance",FontSize=18,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)},description=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,12)};
    readonly WrapPanel actions=new();
    readonly Button change,protectedReview,restore,restorePoint,export,import,cancel;
    readonly Dictionary<string,CleanupReview> reviews=new();
    readonly Dictionary<string,MaintenanceClient> elevatedReviews=new();
    readonly Dictionary<string,MaintenanceRow> analyzedRows=new();
    readonly Dictionary<string,MaintenanceRow[]> loadedRows=new();
    readonly Dictionary<string,(string Property,ListSortDirection Direction)> sorts=new();
    readonly Dictionary<string,string> selections=new();
    readonly Dictionary<string,(string Search,string Group)> filterStates=new();
    readonly HashSet<string> visitedCategories=[];
    readonly Dictionary<string,(GridViewColumnHeader Header,TextBlock Arrow)> headers=new();
    readonly WrapPanel filters=new(){Margin=new(0,0,0,8)};
    readonly ComboBox groups=new(){MinWidth=170,MaxWidth=240,Margin=new(0,0,8,6)};
    readonly TextBox search=new(){Tag="Search settings and effects",Width=240,MinHeight=32,Margin=new(0,0,8,6),Padding=new(10,5,10,5)};
    readonly Border explanation=new(){Padding=new(12),Margin=new(0,10,0,0),Visibility=Visibility.Collapsed};
    readonly TextBlock explanationTitle=new(){FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    readonly TextBox explanationText=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,0),Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(0),IsReadOnlyCaretVisible=true};
    readonly TextBlock sourceText=new(){Margin=new(0,6,0,0),TextWrapping=TextWrapping.Wrap};
    bool updatingFilters;
    MaintenanceRow[] overviewSelection;
    CancellationTokenSource reading=new();Task work=Task.CompletedTask,hiding;bool busy,inspecting,cleaning,disposed,refreshPending;
    internal Task Ready=>work;
    internal string Category=>Categories[selectedCategory];
    internal ListView Rows=>table;
    internal string State=>status.Text;
#if DEBUG
    internal string CleanupSummaryForVerification=>cardValues[0].Text;
    internal Func<int,Task> AnalyzeStepForVerification;
    internal bool CleanEnabledForVerification=>change.IsEnabled;
    internal string[] ActionLabelsForVerification=>actions.Children.OfType<Button>().Select(button=>button.Content as string).Where(label=>label!=null).ToArray();
    internal void SelectCategoryForVerification(string category)=>cardButtons[Array.IndexOf(Categories,category)].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal void CancelForVerification()=>cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal async Task CategoryForVerification(string category)
    {
        cardButtons[Array.IndexOf(Categories,category)].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Task observed;
        do{observed=work;await observed;}while(work!=observed);
    }
    internal async Task ClickForVerification(string label)
    {
        Button button=Buttons(this).Single(b=>Equals(b.Content,label));
        if(!button.IsEnabled)throw new InvalidOperationException("Disabled maintenance command: "+label);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await work;
    }
    internal void ImportForVerification(string file)=>SelectImported(file);
    internal void SearchForVerification(string text)=>search.Text=text;
    internal void GroupForVerification(string text)=>groups.SelectedItem=text;
    internal string ExplanationForVerification=>explanationText.Text;
    internal Task DoubleClickForVerification(DependencyObject source)
    {
        table.RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=source});
        return work;
    }
    static IEnumerable<Button> Buttons(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);if(child is Button button)yield return button;foreach(var nested in Buttons(child))yield return nested;}
    }
#endif
    internal MaintenancePanel()
    {
        SetResourceReference(StyleProperty,"StatCard");Padding=new Thickness(16,48,16,14);Visibility=Visibility.Collapsed;
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());
        var heading=new DockPanel();var back=new Button{Content="Back to files",MinHeight=32};back.SetResourceReference(StyleProperty,"AuxiliaryAction");back.Click+=(_,_)=>{Collapse();Back?.Invoke();};DockPanel.SetDock(back,Dock.Right);heading.Children.Add(back);heading.Children.Add(title);grid.Children.Add(heading);
        var content=new Grid{Margin=new(0,14,0,0)};Grid.SetRow(content,1);grid.Children.Add(content);
        var body=new Grid();foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})body.RowDefinitions.Add(new(){Height=height});
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=body};content.Children.Add(scroll);
        var intro=new StackPanel();description.SetResourceReference(TextBlock.ForegroundProperty,"Muted");intro.Children.Add(description);body.Children.Add(intro);
        groups.SetResourceReference(StyleProperty,"PreviewClipCombo");AutomationProperties.SetName(groups,"Maintenance category filter");
        search.SetResourceReference(StyleProperty,"PerformanceFilter");
        search.ToolTip="Search settings, effects or recommendations";AutomationProperties.SetName(search,"Search maintenance items");
        filters.Children.Add(groups);filters.Children.Add(search);var clear=new Button{Content="Clear",MinHeight=32};clear.SetResourceReference(StyleProperty,"AuxiliaryAction");clear.Click+=(_,_)=>{search.Clear();groups.SelectedIndex=0;};filters.Children.Add(clear);intro.Children.Add(filters);
        search.TextChanged+=(_,_)=>FilterRows();groups.SelectionChanged+=(_,_)=>FilterRows();
        explanation.SetResourceReference(BackgroundProperty,"Palette.0AFFFFFF");explanation.CornerRadius=new(8);
        var detail=new StackPanel();detail.Children.Add(explanationTitle);detail.Children.Add(explanationText);detail.Children.Add(sourceText);
        explanationText.SetResourceReference(Control.ForegroundProperty,"Muted");AutomationProperties.SetName(explanationText,"Selected maintenance assessment");
        explanation.Child=new ScrollViewer{Content=detail,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(explanation,2);body.Children.Add(explanation);
        table.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");VirtualizingPanel.SetIsVirtualizing(table,true);VirtualizingPanel.SetIsVirtualizingWhenGrouping(table,true);VirtualizingPanel.SetVirtualizationMode(table,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(table,true);
        Columns();Grid.SetRow(table,1);body.Children.Add(table);AutomationProperties.SetName(table,"Maintenance items");
        table.AddHandler(GridViewColumnHeader.ClickEvent,new RoutedEventHandler((_,e)=>
        {
            if(e.OriginalSource is not GridViewColumnHeader {Tag:string property})return;
            var current=sorts.GetValueOrDefault(Category,("Name",ListSortDirection.Ascending));
            sorts[Category]=(property,current.Item1==property&&current.Item2==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending);
            SortRows();
        }));
        actions.Margin=new Thickness(0,12,0,0);Grid.SetRow(actions,3);body.Children.Add(actions);
        change=Button("Apply",Change);protectedReview=Button("Use administrator access",ReviewProtected);restore=Button("Restore",()=>Apply("restore"));
        restorePoint=Button("Create restore point",()=>Apply("restore-point"));export=Button("Export selection…",Export);import=Button("Import selection…",Import);
        restorePoint.ToolTip="Create a Windows restore point. Administrator permission is required.";
        table.MouseDoubleClick+=(_,e)=>
        {
            if(busy||disposed||Category!="Startup"||e.ChangedButton!=MouseButton.Left||
                e.OriginalSource is not DependencyObject source||ItemsControl.ContainerFromElement(table,source) is not ListViewItem {Content:MaintenanceRow row}||
                table.SelectedItem is not MaintenanceRow selected||selected.Id!=row.Id)return;
            var action=row.State=="Enabled"?change:restore;
            if(action.IsEnabled){e.Handled=true;action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
        };
        cancel=new Button{Content="Cancel",MinHeight=32,Margin=new(0,0,8,6)};cancel.SetResourceReference(StyleProperty,"AuxiliaryAction");
        cancel.Click+=(_,_)=>{reading.Cancel();status.Text="Cancelling…";};
        foreach(var button in new[]{change,protectedReview,restore,restorePoint,export,import,cancel})actions.Children.Add(button);
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetRow(status,4);body.Children.Add(status);
        Child=grid;table.SelectionChanged+=(_,_)=>{if(table.SelectedItem is MaintenanceRow row)selections[Category]=row.Id;Explain();UpdateActions();};
        var groupCaption=new FrameworkElementFactory(typeof(TextBlock));groupCaption.SetBinding(TextBlock.TextProperty,new Binding("Name"));groupCaption.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);groupCaption.SetValue(MarginProperty,new Thickness(8,12,0,6));groupCaption.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        table.GroupStyle.Add(new GroupStyle{HeaderTemplate=new DataTemplate{VisualTree=groupCaption}});
        for(int i=0;i<5;i++)
        {
            int index=i;var stack=new StackPanel();var label=cardLabels[i]=new TextBlock{Text=Categories[i],FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis};label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            cardValues[i]=new TextBlock{Text="—",FontSize=20,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MinHeight=27};
            cardSubtitles[i]=new TextBlock{Text=i==0?"Review pending":"Not checked",FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis};cardSubtitles[i].SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            stack.Children.Add(label);stack.Children.Add(cardValues[i]);stack.Children.Add(cardSubtitles[i]);
            var button=cardButtons[i]=new Button{Content=stack,MinHeight=84,Padding=new Thickness(12,9,14,9),Margin=new Thickness(0,0,i==4?0:10,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};button.SetResourceReference(StyleProperty,"PerformanceCard");AutomationProperties.SetName(button,Categories[i]+" maintenance");
            button.Click+=(_,_)=>
            {
                if(disposed||busy&&(!(inspecting||cleaning)||!loadedRows.ContainsKey(Categories[index])))return;
                if(Expanded)filterStates[Category]=(search.Text,groups.SelectedItem as string);
                selectedCategory=index;Expanded=true;Visibility=Visibility.Visible;AnalysisChanged?.Invoke();
                bool revisiting=!visitedCategories.Add(Category);
                if(loadedRows.TryGetValue(Category,out var cached))
                {
                    Present(cached);if(!busy){ShowRowCount(Category,cached);if(revisiting)Queue(RefreshVisible,true);}else if(revisiting)refreshPending=true;
                    UpdateActions();
                }
                else
                {
                    Present([],false);status.Text="Loading "+Category+"…";Queue(Load,true);
                }
            };cards.Children.Add(button);
        }
        cards.SizeChanged+=(_,_)=>
        {
            bool tight=cards.ActualWidth<600,compact=cards.ActualWidth<900;
            for(int i=0;i<5;i++){cardLabels[i].Text=tight?new[]{"Clean","Start","Services","Tweaks","Apps"}[i]:Categories[i];cardValues[i].FontSize=tight?11:compact?16:20;cardValues[i].MinHeight=compact?42:27;cardSubtitles[i].Visibility=compact?Visibility.Collapsed:Visibility.Visible;cardButtons[i].Padding=tight?new Thickness(6,9,6,9):new Thickness(12,9,14,9);cardButtons[i].Margin=new(0,0,i==4?0:tight?4:10,0);}
        };
        void Fit()
        {
            body.Height=Math.Max(scroll.ActualHeight,intro.ActualHeight+explanation.ActualHeight+actions.ActualHeight+status.ActualHeight+170);
        }
        SizeChanged+=(_,_)=>{Presentation();Fit();};scroll.SizeChanged+=(_,_)=>Fit();actions.SizeChanged+=(_,_)=>Fit();explanation.SizeChanged+=(_,_)=>Fit();
        UpdateActions();
    }
    Button Button(string label,Func<Task> action,bool inspect=false)
    {
        var button=new Button{Content=label,MinHeight=32,Margin=new(0,0,8,6)};button.SetResourceReference(StyleProperty,"AuxiliaryAction");AutomationProperties.SetName(button,label);
        button.Click+=(_,_)=>Queue(action,inspect);return button;
    }
    void Queue(Func<Task> action,bool inspect=false)
    {
        if(disposed||busy)return;
        if(reading.IsCancellationRequested){reading.Dispose();reading=new();}
        work=Run(action);
        async Task Run(Func<Task> call)
        {
            busy=true;inspecting=inspect;UpdateActions();try{await call();}
            catch(OperationCanceledException){status.Text="Cancelled. Completed actions remain listed; unprocessed files are unchanged.";}
            catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException or JsonException)
            {status.Text=ex.Message;}
            finally
            {
                busy=false;inspecting=false;UpdateActions();
                if(refreshPending){refreshPending=false;if(!disposed&&Expanded&&!reading.IsCancellationRequested)Queue(RefreshVisible,true);}
            }
        }
    }
    async Task RefreshVisible()
    {
        string category=Category;
        await Load();
        if(category=="Cleanup")await AnalyzeAll();
    }
    async Task Load()
    {
        reading.Token.ThrowIfCancellationRequested();string category=Category;
        status.Text="Reading Windows state…";
        MaintenanceRow[] rows=overviewSelection;overviewSelection=null;
        if(rows!=null){}
        else if(category=="Windows apps")
        {
            var packages=JsonSerializer.Deserialize<string[]>(await MaintenanceWorker.WindowsAction("ListApps",cancel:reading.Token))?.ToHashSet(StringComparer.OrdinalIgnoreCase)??[];
            rows=Maintenance.Apps.Where(a=>packages.Contains(a.Package)).Select(a=>new MaintenanceRow(a.Package,a.Name,"Current user","Installed",a.Package)).ToArray();
        }
        else rows=await Task.Run(()=>Maintenance.Read(category,reading.Token),reading.Token);
        if(disposed)return;
        loadedRows[category]=rows;
        if(category!=Category)return;
        Present(rows);
        ShowRowCount(category,rows);
    }
    void ShowRowCount(string category,MaintenanceRow[] rows)
    {
        int assessed=rows.Count(r=>MaintenanceGuidance.Describe(category,r).Recommendation!="Not assessed");
        status.Text=rows.Length==0?"No items found.":$"{rows.Length:N0} items · no changes made"+(category=="Windows tweaks"?$" · {assessed:N0} with guidance":"");
    }
    void Present(MaintenanceRow[] rows,bool updateSummary=true)
    {
        string category=Category;
        updatingFilters=true;search.Text=filterStates.GetValueOrDefault(category).Search??"";updatingFilters=false;
        Columns();
        fullDescription=category switch
        {
            "Cleanup"=>"Categories are reviewed automatically. Select a result to clean temporary files or remove obsolete package versions. Packages in use are preserved.",
            "Startup"=>"Run and RunOnce keys, policy Run entries, startup folders and tasks triggered at startup or logon. Type identifies the source. Disable saves the exact original state for Restore.",
            "Services"=>"Review each service before changing it. Stop and disable saves the actual startup configuration. Restore returns that configuration without starting the service.",
            "Windows tweaks"=>"Apply individual Windows settings. Restore uses the exact saved original values. Some changes take effect after sign-out or restart.",
            _=>"Installed built-in apps for the current user. Removal leaves other users and Windows provisioning unchanged. Reinstall through Microsoft Store if needed."
        };
        Presentation();
        if(category=="Cleanup")rows=rows.Select(r=>analyzedRows.GetValueOrDefault(r.Id,r)).ToArray();
        SetRows(rows);if(updateSummary)Summary(category,rows);
    }
    async Task AnalyzeAll()
    {
        var rows=Maintenance.Read("Cleanup",reading.Token);
        for(int i=0;i<rows.Length;i++)
        {
            reading.Token.ThrowIfCancellationRequested();
            status.Text=$"Analyzing {i+1} of {rows.Length} · {rows[i].Name}…";
#if DEBUG
            if(AnalyzeStepForVerification!=null)await AnalyzeStepForVerification(i);
#endif
            reading.Token.ThrowIfCancellationRequested();
            try{await AnalyzeRow(rows[i],false);}
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {PublishReview(rows[i] with{State="Unavailable",Details="Could not review this category.\n"+error.Message,Bytes=null,CanChange=false});}
        }
        status.Text="All categories reviewed. Nothing has been removed.";
    }
    async Task AnalyzeRow(MaintenanceRow row,bool allowElevation)
    {
        analyzedRows.Remove(row.Id);
        if(reviews.Remove(row.Id,out var previous))previous.Dispose();
        if(elevatedReviews.Remove(row.Id,out var old))await old.DisposeAsync();
        var target=CleanupReview.Targets.Single(t=>t.Id==row.Id);status.Text="Analyzing "+row.Name+"…";
        PublishReview(row with{State="Analyzing…",CanChange=false});
        MaintenanceRow result;
        if(allowElevation&&row.Id is ("windows-temp" or "error-reports"))
        {
            var client=new MaintenanceClient(maintenanceScope:true);
            try
            {
                var reply=await client.Start(new("analyze",row.Id,Maintenance.Journal,Path.Combine(DataFolder.Cache,"Maintenance")),true,reading.Token);
                if(!reply.Ok)throw new InvalidOperationException(reply.Message);
                elevatedReviews.Add(row.Id,client);result=reply.Row;
            }
            catch{await client.DisposeAsync();PublishReview(row with{State="Administrator review unavailable",CanChange=false});throw;}
        }
        else
        {
            var review=new CleanupReview(target);
            try{await Task.Run(()=>review.Analyze(reading.Token));reviews.Add(row.Id,review);result=review.Row;}
            catch(OperationCanceledException){review.Dispose();PublishReview(row with{State="Cancelled",CanChange=false});throw;}
            catch{review.Dispose();throw;}
        }
        PublishReview(result);
    }
    void PublishReview(MaintenanceRow result)
    {
        analyzedRows[result.Id]=result;
        if(Category=="Cleanup")SetRows(((IEnumerable<MaintenanceRow>)table.ItemsSource).Select(r=>r.Id==result.Id?result:r).ToArray());
        Summary("Cleanup",Maintenance.Read("Cleanup",reading.Token).Select(r=>analyzedRows.GetValueOrDefault(r.Id,r)));
    }
    void Presentation()
    {
        bool narrow=ActualWidth<600;title.Text=narrow?Category:"Maintenance · "+Category;title.ToolTip="Maintenance · "+Category;
        description.ToolTip=fullDescription;
        description.Text=!narrow?fullDescription:Category switch
        {
            "Cleanup"=>"Automatic estimates. Select temporary files or obsolete package versions to clean.",
            "Startup"=>"Windows startup entries by source. Disable saves their original state for Restore.",
            "Services"=>"Stop and disable a reviewed service; Restore returns its startup settings.",
            "Windows tweaks"=>"Review each setting before applying. Restore returns saved values.",
            _=>"Optional Windows apps for this user. Reinstall through Microsoft Store."
        };
    }
    Task Change()=>Category=="Cleanup"?Clean():Apply(Category=="Windows apps"?"remove-app":"apply");
    async Task ReviewProtected()
    {
        if(Category!="Cleanup"||table.SelectedItem is not MaintenanceRow row||row.Id is not ("windows-temp" or "error-reports"))return;
        await AnalyzeRow(row,true);
        status.Text="Administrator review ready. Select Clean reviewed to remove unchanged files.";
    }
    async Task Clean()
    {
        if(table.SelectedItem is not MaintenanceRow row)return;
        cleaning=true;UpdateActions();string category=Category;status.Text="Preparing cleanup…";
        var progress=new CleanupProgressWindow(Window.GetWindow(this),row.Name,()=>reading.Cancel());
        progress.Show();bool completed=false;
        try
        {
        analyzedRows.Remove(row.Id);
        if(elevatedReviews.Remove(row.Id,out var client))
        {
            try{var reply=await client.Send(new("clean",row.Id,null,null),reading.Token,progress.Report);status.Text=reply.Message;bool cancelled=reply.Outcome?.Cancelled==true;progress.Finish(reply.Message,!reply.Ok||reply.Outcome is{Skipped:>0}||cancelled,reply.Outcome?.Details,cancelled);completed=true;}finally{await client.DisposeAsync();}
        }
        else if(reviews.Remove(row.Id,out var review))
        {
            try
            {
                var result=await Task.Run(()=>review.Clean(reading.Token,progress.Report));
                string size=review.Row.Bytes.HasValue?$" · {MainWindow.Fmt(result.Bytes)}":"";
                status.Text=$"{result.Deleted:N0} removed{size} · {result.Skipped:N0} skipped"+(result.Cancelled?" · cancelled":"");
                progress.Finish(status.Text,result.Skipped>0||result.Cancelled||result.Failed,result.Details,result.Cancelled,result.Failed);completed=true;
            }
            finally{review.Dispose();}
        }
        var original=CleanupReview.Targets.Single(t=>t.Id==row.Id);var reset=new MaintenanceRow(row.Id,row.Name,row.Scope,"Updating…",original.Description);
        var rows=loadedRows[category].Select(r=>analyzedRows.GetValueOrDefault(r.Id,r)).Select(r=>r.Id==row.Id?reset:r).ToArray();
        loadedRows[category]=rows;if(Category==category)SetRows(rows);Summary(category,rows);
        string outcome=status.Text;
        try{await AnalyzeRow(reset,false);status.Text=outcome;}
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {PublishReview(reset with{State="Unavailable",Details="Could not review this category.\n"+error.Message,Bytes=null,CanChange=false});status.Text=outcome;}
        }
        catch(Exception error){refreshPending=true;bool cancelled=error is OperationCanceledException;progress.Finish(cancelled?"Cleanup cancelled.":error.Message,true,"",cancelled,failed:!cancelled);completed=true;throw;}
        finally{cleaning=false;if(!completed)progress.Finish("Cleanup ended.",false);}
    }
    async Task Apply(string action)
    {
        var row=table.SelectedItem as MaintenanceRow;if(row==null&&action!="restore-point")return;
        string id=row?.Id??"";bool elevated=action=="restore-point"||id.StartsWith("service:")||row?.Scope=="All users";
        if(id.StartsWith("tweak:"))elevated=Maintenance.TweakEdits(id).Any(e=>e.Hive=="HKEY_LOCAL_MACHINE");
        await using var client=new MaintenanceClient(maintenanceScope:true);status.Text="Waiting for Windows…";
        var result=await client.Start(new(action,id,Maintenance.Journal,null),elevated,reading.Token);string message=result.Message;
        await Load();status.Text=message;
    }
    Task Export()
    {
        if(table.SelectedItem is not MaintenanceRow row)return Task.CompletedTask;
        var dialog=new SaveFileDialog{Filter="Maintenance selection|*.json",FileName="Pathweft-maintenance.json"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return Task.CompletedTask;
        WriteSelection(dialog.FileName,row.Id);status.Text="Selection exported. Original settings remain in this computer's maintenance journal.";return Task.CompletedTask;
    }
    Task Import()
    {
        var dialog=new OpenFileDialog{Filter="Maintenance selection|*.json"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return Task.CompletedTask;
        SelectImported(dialog.FileName);return Task.CompletedTask;
    }
    internal static void WriteSelection(string file,string id)
    {
        Maintenance.TweakEdits(id);
        File.WriteAllText(file,JsonSerializer.Serialize(new{format="pathweft-maintenance-1",tweaks=new[]{id}},new JsonSerializerOptions{WriteIndented=true}));
    }
    void SelectImported(string file)
    {
        if(new FileInfo(file).Length>32768)throw new InvalidDataException("Maintenance selection is too large");
        using var json=JsonDocument.Parse(File.ReadAllText(file));var root=json.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("format",out var format)||format.ValueKind!=JsonValueKind.String||format.GetString()!="pathweft-maintenance-1"||
           !root.TryGetProperty("tweaks",out var tweaks)||tweaks.ValueKind!=JsonValueKind.Array||tweaks.GetArrayLength()!=1||tweaks[0].ValueKind!=JsonValueKind.String)
            throw new InvalidDataException("Import a Pathweft maintenance selection containing one Windows tweak");
        string id=tweaks[0].GetString();Maintenance.TweakEdits(id);
        var row=((IEnumerable<MaintenanceRow>)table.ItemsSource).SingleOrDefault(r=>r.Id==id)??throw new InvalidDataException("Selection names an unsupported Windows tweak");
        table.SelectedItem=row;status.Text="Selection imported. Review and Apply to change Windows.";
    }
    void UpdateActions()
    {
        if(change==null)return;bool cleanup=Category=="Cleanup",tweaks=Category=="Windows tweaks";var row=table.SelectedItem as MaintenanceRow;
        bool idle=!busy;
        bool inspectable=idle||inspecting||cleaning;filters.IsEnabled=inspectable;
        foreach(var button in new[]{change,protectedReview,restore,restorePoint,export,import})button.IsEnabled=idle;
        for(int i=0;i<5;i++)if(cardButtons[i]!=null){cardButtons[i].IsEnabled=idle||inspectable&&loadedRows.ContainsKey(Categories[i]);cardButtons[i].Tag=Expanded&&i==selectedCategory?"on":null;AutomationProperties.SetItemStatus(cardButtons[i],Expanded&&i==selectedCategory?"Selected":"");}
        table.IsEnabled=inspectable;
        change.Content=Category switch{"Cleanup"=>"Clean reviewed","Startup"=>"Disable","Services"=>"Stop and disable","Windows apps"=>"Remove",_=>"Apply"};
        bool protectedCategory=cleanup&&row?.Id is ("windows-temp" or "error-reports");
        bool reviewed=row!=null&&(protectedCategory?elevatedReviews.ContainsKey(row.Id):reviews.ContainsKey(row.Id)||elevatedReviews.ContainsKey(row.Id));
        change.IsEnabled=idle&&row?.CanChange==true&&(!cleanup||reviewed);
        protectedReview.Visibility=protectedCategory&&!elevatedReviews.ContainsKey(row.Id)?Visibility.Visible:Visibility.Collapsed;
        protectedReview.IsEnabled=idle&&protectedCategory;
        restore.Visibility=Category is "Startup" or "Services" or "Windows tweaks"?Visibility.Visible:Visibility.Collapsed;
        try{restore.IsEnabled=idle&&row!=null&&Maintenance.Undo().ContainsKey(row.Id);}catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException){restore.IsEnabled=false;status.Text="Saved maintenance state unavailable: "+error.Message;}
        restorePoint.Visibility=export.Visibility=import.Visibility=tweaks?Visibility.Visible:Visibility.Collapsed;
        export.IsEnabled=idle&&row!=null;cancel.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
    }
    internal Task Hide()=>hiding??=Retire();
    async Task Retire()
    {
        Collapse();cards.Visibility=Visibility.Collapsed;reading.Cancel();await work;
        foreach(var review in reviews.Values)review.Dispose();reviews.Clear();
        foreach(var client in elevatedReviews.Values)await client.DisposeAsync();elevatedReviews.Clear();
        await AdministratorSession.EndMaintenance();
        analyzedRows.Clear();loadedRows.Clear();filterStates.Clear();visitedCategories.Clear();refreshPending=false;
    }
    internal void Collapse()
    {
        Expanded=false;Visibility=Visibility.Collapsed;UpdateActions();AnalysisChanged?.Invoke();
    }
    async Task Overview()
    {
        var rows=await Task.Run(()=>new[]{"Cleanup","Startup","Services","Windows tweaks"}.Select(category=>(category,rows:Maintenance.Read(category,reading.Token))).ToArray(),reading.Token);
        foreach(var item in rows){loadedRows[item.category]=item.rows;Summary(item.category,item.category=="Cleanup"?item.rows.Select(r=>analyzedRows.GetValueOrDefault(r.Id,r)):item.rows);}
        UpdateActions();
        var packages=JsonSerializer.Deserialize<string[]>(await MaintenanceWorker.WindowsAction("ListApps",cancel:reading.Token))?.ToHashSet(StringComparer.OrdinalIgnoreCase)??[];
        var apps=Maintenance.Apps.Where(a=>packages.Contains(a.Package)).Select(a=>new MaintenanceRow(a.Package,a.Name,"Current user","Installed",a.Package)).ToArray();loadedRows["Windows apps"]=apps;Summary("Windows apps",apps);overviewSelection=loadedRows[Category];
    }
    void Summary(string category,IEnumerable<MaintenanceRow> items)
    {
        int index=Array.IndexOf(Categories,category);var rows=items.ToArray();string value,sub;
        if(index==0)
        {
            var sized=rows.Where(x=>x.Bytes.HasValue).ToArray();
            int reviewed=rows.Count(x=>x.State is not ("Not analyzed" or "Analyzing…"));
            value=sized.Length>0?MainWindow.Fmt(sized.Sum(x=>x.Bytes.Value)):"—";
            sub=reviewed>0?$"Up to · {sized.Length} sized · {reviewed} of {rows.Length} reviewed":"Review pending";
        }
        else if(index==1){value=rows.Count(x=>x.State=="Enabled").ToString("N0");sub="startup entries enabled";}
        else if(index==2){value=rows.Count(x=>x.StartupMode.StartsWith("Automatic")).ToString("N0");sub=$"automatic · {rows.Length} installed services";}
        else if(index==3){value=rows.Select(x=>MaintenanceGuidance.Describe(category,x)).Count(MaintenanceGuidance.PendingRecommendation).ToString("N0");sub="recommended actions pending";}
        else{value=rows.Length.ToString("N0");sub="optional preinstalled apps";}
        cardValues[index].Text=value;cardSubtitles[index].Text=sub;cardButtons[index].ToolTip=category+" · "+value+" · "+sub;
    }
    internal Task Show(){hiding=null;Collapse();visitedCategories.Clear();cards.Visibility=Visibility.Visible;Queue(async()=>{await Overview();await Load();await AnalyzeAll();},true);return work;}
    void SetRows(MaintenanceRow[] rows)
    {
        string selected=selections.GetValueOrDefault(Category);
        rows=Category=="Cleanup"?rows:rows.Select(r=>MaintenanceGuidance.Describe(Category,r)).ToArray();
        updatingFilters=true;string previous=filterStates.GetValueOrDefault(Category).Group;
        groups.ItemsSource=new[]{"All categories"}.Concat(rows.Select(r=>r.Group).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)).ToArray();
        groups.SelectedItem=groups.Items.Cast<string>().Contains(previous)?previous:"All categories";updatingFilters=false;
        filters.Visibility=Category=="Cleanup"?Visibility.Collapsed:Visibility.Visible;
        table.ItemsSource=rows;SortRows();table.SelectedItem=rows.FirstOrDefault(r=>r.Id==selected&&Matches(r));Explain();
    }
    bool Matches(MaintenanceRow row)=>(groups.SelectedIndex<=0||row.Group==groups.SelectedItem as string)&&
        (string.IsNullOrWhiteSpace(search.Text)||string.Join(" ",row.Name,row.Type,row.State,row.Group,row.Recommendation,row.Details,row.Guidance).Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));
    void FilterRows(){if(updatingFilters||table.ItemsSource==null)return;filterStates[Category]=(search.Text,groups.SelectedItem as string);CollectionViewSource.GetDefaultView(table.ItemsSource).Refresh();Explain();}
    void Explain()
    {
        if(table.SelectedItem is not MaintenanceRow row){explanation.Visibility=Visibility.Collapsed;return;}
        explanation.Visibility=Visibility.Visible;explanationTitle.Text=Category=="Windows tweaks"?row.RecommendationLabel+" · "+row.Name:row.Name;
        string effect=Category switch
        {
            "Cleanup" when row.Id=="scoop-versions"=>"Clean removes each obsolete version folder and all its contents. Current versions, packages in use and external persistent data are preserved.",
            "Cleanup" when row.Id.StartsWith("manager-",StringComparison.Ordinal)=>"Clean asks this package manager to remove only the reviewed cache category. Cached downloads may be fetched again. Installed packages and manager configuration stay in place.",
            "Cleanup" when row.Id is "windows-temp" or "error-reports"=>"Use administrator access to review protected files through Maintenance's own backend. Review does not remove anything; Clean reviewed then removes only unchanged files in that review.",
            "Cleanup"=>"Clean reviewed removes only the reviewed, unchanged files. New, changed and busy files are skipped.",
            "Startup"=>"Disable prevents this entry from running at startup. Restore returns its exact saved original state. Double-click to Disable or Restore.",
            "Services"=>"Stop and disable stops this service and disables automatic startup. Restore returns its saved startup configuration without starting the service.",
            "Windows tweaks"=>"Apply changes this Windows setting. Restore returns its exact saved original values.",
            _=>"Remove uninstalls this app for the current user. Reinstall through Microsoft Store if needed."
        };
        explanationText.Text=effect+"\n"+(Category=="Cleanup"?"":row.Guidance+"\n")+row.Details;sourceText.Inlines.Clear();
        if(!string.IsNullOrEmpty(row.Source))
        {
            var uri=new Uri(row.Source);var link=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Source · "+uri.Host)){NavigateUri=uri};
            link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Accent");
            link.RequestNavigate+=(_,e)=>{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;};
            sourceText.Inlines.Add(link);
        }
    }
    void SortRows()
    {
        var sort=sorts.GetValueOrDefault(Category,(Property:"Name",Direction:ListSortDirection.Ascending));
        if(table.ItemsSource!=null)
        {
            var view=CollectionViewSource.GetDefaultView(table.ItemsSource);
            using(view.DeferRefresh())
            {
                view.Filter=item=>Category=="Cleanup"||item is MaintenanceRow row&&Matches(row);
                view.GroupDescriptions.Clear();if(Category=="Windows tweaks")view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
                view.SortDescriptions.Clear();view.SortDescriptions.Add(new(sort.Property,sort.Direction));
                if(sort.Property!="Name")view.SortDescriptions.Add(new("Name",ListSortDirection.Ascending));
                view.SortDescriptions.Add(new("Id",ListSortDirection.Ascending));
            }
        }
        foreach(var (property,parts) in headers)
        {
            bool active=property==sort.Property;string direction=sort.Direction==ListSortDirection.Ascending?"Ascending":"Descending";
            parts.Arrow.Text=active?(sort.Direction==ListSortDirection.Ascending?"⌃":"⌄"):"";
            AutomationProperties.SetItemStatus(parts.Header,active?direction:"");parts.Header.ToolTip="Sort by "+AutomationProperties.GetName(parts.Header)+(active?" · "+direction:"");
        }
    }
    void Columns()
    {
        headers.Clear();
        var view=new GridView();var columns=new List<(string Header,string Property,double Width)>{("Name","Name",230)};
        if(Category=="Startup")columns.Add(("Type","Type",150));
        if(Category=="Services")columns.Add(("Service name","Type",180));else columns.Add(("Scope","Scope",110));
        columns.Add(("State","State",130));
        if(Category=="Windows tweaks")columns.Add(("Recommendation","RecommendationLabel",145));
        if(Category=="Services")columns.Add(("Startup type","StartupMode",160));else if(Category=="Cleanup")columns.Add(("Size","Size",95));
        columns.Add(("Details","Details",600));
        foreach(var column in columns)
        {
            string property=column.Property=="Size"?"Bytes":column.Property;
            var arrow=new TextBlock{Width=14,Margin=new(6,0,0,0),VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center};
            var caption=new StackPanel{Orientation=Orientation.Horizontal};caption.Children.Add(new TextBlock{Text=column.Header});caption.Children.Add(arrow);
            var header=new GridViewColumnHeader{Content=caption,Tag=property,Focusable=true};AutomationProperties.SetName(header,column.Header);headers[property]=(header,arrow);
            // All categories use the same compact rows and selected detail well.
            // Service dependencies/account/path are multiline secondary content.
            view.Columns.Add(new(){Header=header,DisplayMemberBinding=new Binding(column.Property=="Details"?"Summary":column.Property),Width=column.Width});
        }
        table.View=view;SortRows();
    }
    public async ValueTask DisposeAsync(){disposed=true;await Hide();reading.Dispose();}
}
