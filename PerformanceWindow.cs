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
        IsVisibleChanged+=(_,_)=>RefreshPerformance();
        StateChanged+=(_,_)=>RefreshPerformance();
        Activated+=(_,_)=>performance.OwnerFocusChanged();Deactivated+=(_,_)=>performance.OwnerFocusChanged();
        Closed+=(_,_)=>performance.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    void Performance_Click(object sender,RoutedEventArgs e)
    {
        if(active==null)return;
        performanceModes[active]=!performanceModes.GetValueOrDefault(active);
        RefreshPerformance();ArrangeStats();
    }
    bool PerformanceMode=>active!=null&&performanceModes.GetValueOrDefault(active);
    void RefreshPerformance()
    {
        if(performance==null)return;
        performance.SetFolder(active?.Dir);
        performance.Visibility=PerformanceMode&&IsVisible&&WindowState!=WindowState.Minimized?Visibility.Visible:Visibility.Collapsed;
        PerformanceCommand.Tag=PerformanceMode?"on":null;
        AutomationProperties.SetItemStatus(PerformanceCommand,PerformanceMode?"On":"Off");
        string label=PerformanceMode?"Show folder statistics":"Show performance";
        PerformanceCommand.ToolTip=label;AutomationProperties.SetName(PerformanceCommand,label);
    }
}
