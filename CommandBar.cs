using System.Windows.Controls;
using System.Windows;

namespace FileExplorer;

public partial class MainWindow
{
    void UpdateCommandStates()
    {
        UpdateSpaceCommandState();
        HiddenToggle.Tag=Settings.ShowHidden?"on":null;
        PanesCommand.Tag=Settings.SinglePane?null:"on";
        PreviewCommand.Tag=Settings.Preview?"on":null;
        System.Windows.Automation.AutomationProperties.SetItemStatus(HiddenToggle,Settings.ShowHidden?"On":"Off");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PanesCommand,Settings.SinglePane?"One pane":"Two panes");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PreviewCommand,Settings.Preview?"On":"Off");
        HiddenToggle.ToolTip=Settings.ShowHidden?"Hide Windows hidden and dot-prefixed files (Ctrl+H)":"Show Windows hidden and dot-prefixed files (Ctrl+H)";
        PreviewCommand.ToolTip=Settings.Preview?"Hide preview (Alt+P)":"Show preview (Alt+P)";
        var eye=(System.Windows.Media.DrawingImage)FindResource("CommandIcon.Hidden");
        ((Image)HiddenToggle.Content).Source=Settings.ShowHidden?eye:ExplorerIcons.Crossed(eye);
        PanesCommand.ToolTip=Settings.SinglePane?"Show two panes (Ctrl+P)":"Show one pane (Ctrl+P)";
        ((Image)PanesCommand.Content).Source=ExplorerIcons.PaneLayout(!Settings.SinglePane);
        UpdateCommandSpacing();
    }
    void UpdateSpaceCommandState()
    {
        if(AnalyzeSpaceCommand!=null)
        {
            AnalyzeSpaceCommand.IsEnabled=active?.CanAnalyzeSpace==true;
            AnalyzeSpaceCommand.Tag=active?.SpaceAnalyzing==true?"on":null;
            AnalyzeSpaceCommand.ToolTip=active?.SpaceAnalyzing==true?"Restore the previous view":"Analyze space in this folder";
            System.Windows.Automation.AutomationProperties.SetItemStatus(AnalyzeSpaceCommand,active?.SpaceAnalyzing==true?"On":"Off");
        }
    }
    void CommandBar_SizeChanged(object sender,SizeChangedEventArgs args)=>UpdateCommandSpacing();
    async void AnalyzeSpace_Click(object sender,RoutedEventArgs args)
    {
        try{await active.ToggleSpaceAsync();}
        catch(Exception error){Status.Text="Space analysis: "+error.Message;}
        finally{UpdateCommandStates();}
    }
    void UpdateCommandSpacing()
    {
        if(CommandBar.ActualWidth<=0)return;
        CommandButtons.MaxWidth=CommandBar.ActualWidth;
        var visible=CommandButtons.Children.OfType<Button>().Where(button=>button.Visibility==Visibility.Visible).ToArray();
        if(visible.Length==0)return;
        double width=Math.Clamp((CommandBar.ActualWidth-36)/visible.Length,22,30);
        foreach(var button in CommandButtons.Children.OfType<Button>())if(button.Width!=width)button.Width=width;
    }
}
