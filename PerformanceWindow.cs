using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FileExplorer;

public partial class MainWindow
{
    PerformancePanel performance;
    MaintenancePanel maintenance;
    bool maintenanceVisible;
    bool maintenanceClosing,maintenanceClosed;
    bool changingPaneMode;
    Task paneModeWork=Task.CompletedTask;
    Task maintenanceRetiring=Task.CompletedTask;
    readonly Dictionary<PaneView,bool> performanceModes=new();
    void InitializePerformance()
    {
        InitializeAdministrator();
        performance=new PerformancePanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,10,0,0)};
        Grid.SetRow(performance,1);((Grid)StatsRow.Parent).Children.Add(performance);
        Grid.SetRow(performance.Analysis,0);((Grid)StatsRow.Parent).Children.Add(performance.Analysis);
        performance.AnalysisChanged+=()=>{ShowAnalysis();if(performance.Selected<0&&!maintenanceVisible)active?.FocusList();};
        IsVisibleChanged+=(_,_)=>RefreshPerformance();
        StateChanged+=(_,_)=>RefreshPerformance();
        Activated+=(_,_)=>performance.OwnerFocusChanged();Deactivated+=(_,_)=>performance.OwnerFocusChanged();
        Closed+=(_,_)=>{performance.DisposeAsync().AsTask().GetAwaiter().GetResult();};
        Closing+=async(_,e)=>
        {
            if(tray!=null&&!quitting){if(maintenanceVisible)await CloseMaintenance();return;}
            if(maintenance==null||maintenanceClosed)return;
            e.Cancel=true;if(maintenanceClosing)return;maintenanceClosing=true;
            await maintenance.DisposeAsync();maintenanceClosed=true;
            // A synchronously completed disposal is still inside WPF Closing.
            // Dispatch the second close after that cancelled event unwinds.
            _=Dispatcher.BeginInvoke(new Action(Close));
        };
    }
    async void Performance_Click(object sender,RoutedEventArgs e)
    {
        if(changingPaneMode)return;paneModeWork=TogglePerformance();await paneModeWork;
    }
    async Task TogglePerformance()
    {
        if(changingPaneMode)return;changingPaneMode=true;
        Task draining=Task.CompletedTask;
        try
        {
        if(maintenanceVisible)draining=CloseMaintenance();
        if(active==null)return;
        performanceModes[active]=!performanceModes.GetValueOrDefault(active);
        if(!performanceModes[active])performance.Select(-1);
        RefreshPerformance();ArrangeStats();
        }
        finally{changingPaneMode=false;}
        await draining;
    }
    bool PerformanceMode=>!maintenanceVisible&&active!=null&&performanceModes.GetValueOrDefault(active);
    void RefreshPerformance()
    {
        if(performance==null)return;
        performance.SetFolder(active?.Dir);
        performance.Visibility=!maintenanceVisible&&PerformanceMode&&IsVisible&&WindowState!=WindowState.Minimized?Visibility.Visible:Visibility.Collapsed;
        ShowAnalysis();
        PerformanceCommand.Tag=PerformanceMode?"on":null;
        AutomationProperties.SetItemStatus(PerformanceCommand,PerformanceMode?"On":"Off");
        string label=PerformanceMode?"Show folder statistics":"Show performance";
        PerformanceCommand.ToolTip=label;AutomationProperties.SetName(PerformanceCommand,label);
    }
    bool AnalysisVisible=>maintenanceVisible&&maintenance?.Expanded==true||performance?.Selected>=0&&PerformanceMode;
    void ShowAnalysis()
    {
        if(performance==null)return;
        bool show=AnalysisVisible&&!maintenanceVisible;
        performance.Analysis.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        PaneArea.Visibility=AnalysisVisible?Visibility.Collapsed:Visibility.Visible;
        HiddenToggle.Visibility=Visibility.Visible;
        HiddenToggle.Tag=!maintenanceVisible&&!PerformanceMode&&Settings.ShowHidden?"on":null;
        // File actions must never target a selection hidden behind analysis.
        if(PerformanceCommand.Parent is Panel commands)foreach(var child in commands.Children)
            if(child is Button button&&button!=PerformanceCommand&&button!=TransfersButton&&button!=MaintenanceCommand&&button!=AdministratorCommand&&button!=AnalyzeSpaceCommand)button.IsEnabled=!AnalysisVisible;
        UpdateSpaceCommandState();
        MaintenanceCommand.Tag=maintenanceVisible?"on":null;
        AutomationProperties.SetItemStatus(MaintenanceCommand,maintenanceVisible?"On":"Off");
        MaintenanceCommand.ToolTip=maintenanceVisible?"Show folder statistics":"Show maintenance";
    }
    async void Maintenance_Click(object sender,RoutedEventArgs e)
    {
        if(changingPaneMode)return;paneModeWork=ToggleMaintenance();await paneModeWork;
    }
    async Task ToggleMaintenance()
    {
        if(changingPaneMode)return;changingPaneMode=true;
        Task loading=Task.CompletedTask;
        try
        {
        if(maintenanceVisible){loading=CloseMaintenance();}
        else
        {
        if(maintenance==null)
        {
            maintenance=new();Grid.SetRow(maintenance,0);((Grid)StatsRow.Parent).Children.Add(maintenance);
            Grid.SetRow(maintenance.Cards,1);((Grid)StatsRow.Parent).Children.Add(maintenance.Cards);
            maintenance.AnalysisChanged+=ShowAnalysis;
            maintenance.Back+=()=>{ShowAnalysis();active?.FocusList();};
        }
        if(active!=null)performanceModes[active]=false;
        performance.Select(-1);maintenanceVisible=true;ArrangeStats();loading=OpenMaintenance();
        }
        }
        finally{changingPaneMode=false;}
        await loading;
    }
    async Task OpenMaintenance()
    {
        await maintenanceRetiring;
        if(maintenanceVisible)await maintenance.Show();
    }
    async Task CloseMaintenance()
    {
        maintenanceVisible=false;maintenanceRetiring=maintenance?.Hide()??Task.CompletedTask;ArrangeStats();active?.FocusList();await maintenanceRetiring;
    }
}
