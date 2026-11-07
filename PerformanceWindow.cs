using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FileExplorer;

public partial class MainWindow
{
    PerformancePanel performance;
    readonly Dictionary<PaneView,bool> performanceModes=new();
    void InitializePerformance()
    {
        performance=new PerformancePanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,10,0,0)};
        Grid.SetRow(performance,1);((Grid)StatsRow.Parent).Children.Add(performance);
        Grid.SetRow(performance.Analysis,0);((Grid)StatsRow.Parent).Children.Add(performance.Analysis);
        performance.AnalysisChanged+=()=>{ShowAnalysis();if(performance.Selected<0)active?.FocusList();};
        IsVisibleChanged+=(_,_)=>RefreshPerformance();
        StateChanged+=(_,_)=>RefreshPerformance();
        Activated+=(_,_)=>performance.OwnerFocusChanged();Deactivated+=(_,_)=>performance.OwnerFocusChanged();
        Closed+=(_,_)=>performance.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    void Performance_Click(object sender,RoutedEventArgs e)
    {
        if(active==null)return;
        performanceModes[active]=!performanceModes.GetValueOrDefault(active);
        if(!performanceModes[active])performance.Select(-1);
        RefreshPerformance();ArrangeStats();
    }
    bool PerformanceMode=>active!=null&&performanceModes.GetValueOrDefault(active);
    void RefreshPerformance()
    {
        if(performance==null)return;
        performance.SetFolder(active?.Dir);
        performance.Visibility=PerformanceMode&&IsVisible&&WindowState!=WindowState.Minimized?Visibility.Visible:Visibility.Collapsed;
        ShowAnalysis();
        PerformanceCommand.Tag=PerformanceMode?"on":null;
        AutomationProperties.SetItemStatus(PerformanceCommand,PerformanceMode?"On":"Off");
        string label=PerformanceMode?"Show folder statistics":"Show performance";
        PerformanceCommand.ToolTip=label;AutomationProperties.SetName(PerformanceCommand,label);
    }
    bool AnalysisVisible=>performance?.Selected>=0&&PerformanceMode;
    void ShowAnalysis()
    {
        if(performance==null)return;
        bool show=AnalysisVisible;
        performance.Analysis.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        PaneArea.Visibility=show?Visibility.Collapsed:Visibility.Visible;
        // File actions must never target a selection hidden behind analysis.
        if(PerformanceCommand.Parent is Panel commands)foreach(var child in commands.Children)
            if(child is Button button&&button!=PerformanceCommand&&button!=TransfersButton)button.IsEnabled=!show&&(button!=AnalyzeSpaceCommand||active?.CanAnalyzeSpace==true);
    }
}
