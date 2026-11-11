using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Animation;

namespace FileExplorer;

// Modeless, like Transfers: the operation owns the lifetime, not the window.
// Close requests cancellation; it never abandons an in-flight folder deletion.
internal sealed class CleanupProgressWindow : Window
{
#if DEBUG
    internal Button CancelButtonForVerification=>cancel;
    internal string PhaseForVerification=>phase.Text;
    internal bool IndeterminateForVerification=>bar.IsIndeterminate;
    internal string CountsForVerification=>completed.Text+" | "+total.Text;
#endif
    readonly TextBlock phase=new(){FontSize=20,FontWeight=FontWeights.SemiBold,Text="Preparing cleanup…"};
    readonly TextBlock path=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,16)};
    readonly TextBlock completed=new(),total=new(){HorizontalAlignment=HorizontalAlignment.Right};
    readonly ProgressBar bar=new(){Height=7,IsIndeterminate=true,Margin=new(0,7,0,14)};
    readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,0,16,0),VerticalAlignment=VerticalAlignment.Center};
    readonly Button cancel=new(){Content="Cancel",MinHeight=36,MinWidth=92};
    readonly TextBox details=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed,Margin=new(0,14,0,0)};
    readonly Action cancelOperation;readonly object sync=new();CleanupProgress latest;bool scheduled,finished,cancelling,closeRequested;
    internal CleanupProgressWindow(Window owner,string category,Action cancelOperation)
    {
        this.cancelOperation=cancelOperation;Owner=owner;Title="Cleanup — Pathweft";Width=560;MinWidth=360;SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.CanMinimize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        // AuxiliaryWindow is transparent for acrylic hosts. Give this standalone
        // progress surface its own themed backing, including the padded edge.
        var body=new StackPanel();
        var surface=new Border{Background=MainWindow.Tint(),Padding=new Thickness(20),Child=body};Content=surface;
        var caption=new TextBlock{Text=category,Margin=new(0,0,0,8)};caption.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(caption);
        body.Children.Add(phase);body.Children.Add(path);
        var counts=new Grid();counts.Children.Add(completed);counts.Children.Add(total);body.Children.Add(counts);body.Children.Add(bar);
        var footer=new DockPanel{LastChildFill=true};DockPanel.SetDock(cancel,Dock.Right);footer.Children.Add(cancel);footer.Children.Add(result);body.Children.Add(footer);
        details.SetResourceReference(Control.BackgroundProperty,"Palette.0AFFFFFF");details.SetResourceReference(Control.ForegroundProperty,"Muted");body.Children.Add(details);
        cancel.Style=(Style)owner.FindResource("JobAction");cancel.Content="Cancel";
        cancel.Click+=(_,_)=>{if(finished)Close();else Cancel();};
        Closing+=OnClosing;AuxiliaryWindows.Prepare(this,surface);
#if DEBUG
        HiddenTrialPresentation.Prepare(this);
#endif
        BusyAnimation(true);
        AutomationProperties.SetName(bar,"Cleanup progress");AutomationProperties.SetName(path,"Current cleanup folder or file");
    }
    void OnClosing(object sender,CancelEventArgs e){if(finished)return;e.Cancel=true;closeRequested=true;Cancel();}
    void Cancel()
    {
        if(cancelling)return;cancelling=true;cancel.IsEnabled=false;phase.Text="Cancelling…";
        result.Text="Waiting for the current Windows operation to finish.";cancelOperation();
    }
    internal void Report(CleanupProgress progress)
    {
        lock(sync){latest=progress;if(scheduled||finished)return;scheduled=true;}
        Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            CleanupProgress value;lock(sync){value=latest;scheduled=false;if(finished)return;}
            if(!cancelling)phase.Text=value.Phase;
            if(!string.IsNullOrEmpty(value.Path))path.Text=value.Path;
            bool managerRunning=value.Phase=="Cleaning package cache"&&value.Completed<value.Total;
            completed.Text=managerRunning?"Manager command running":$"{value.Completed:N0} completed";
            total.Text=managerRunning?"":$"{value.Total:N0} total";
            bool checking=value.Phase.StartsWith("Checking")||managerRunning;bar.IsIndeterminate=checking;BusyAnimation(checking);bar.Maximum=Math.Max(1,value.Total);bar.Value=value.Completed;
            if(!cancelling)result.Text=$"{value.Removed:N0} removed"+(value.Skipped>0?$" · {value.Skipped:N0} skipped":"");
        }));
    }
    internal void Finish(string message,bool attention,string failureDetails="",bool wasCancelled=false,bool failed=false)
    {
        lock(sync)finished=true;
        BusyAnimation(false);
        if(!attention||wasCancelled&&(closeRequested||!cancelling)){Close();return;}
        phase.Text=wasCancelled?"Cleanup cancelled":failed?"Cleanup failed":"Cleanup finished";bar.IsIndeterminate=false;
        if(failed){completed.Text="";total.Text="";}
        result.Text=message;cancel.Content="Close";cancel.IsEnabled=true;
        if(!string.IsNullOrEmpty(failureDetails)){details.Text=failureDetails;details.Visibility=Visibility.Visible;}
    }
    bool animating;
    void BusyAnimation(bool busy)
    {
        if(animating==busy)return;animating=busy;
        bar.BeginAnimation(OpacityProperty,busy&&Motion.On?new DoubleAnimation(.35,1,TimeSpan.FromSeconds(.7)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever}:null);
    }
}
