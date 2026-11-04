using System.Windows.Controls;
using System.Windows;

namespace FileExplorer;

public partial class MainWindow
{
    void UpdateCommandStates()
    {
        HiddenToggle.Tag=Settings.ShowHidden?"on":null;
        PanesCommand.Tag=Settings.SinglePane?null:"on";
        PreviewCommand.Tag=Settings.Preview?"on":null;
        System.Windows.Automation.AutomationProperties.SetItemStatus(HiddenToggle,Settings.ShowHidden?"On":"Off");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PanesCommand,Settings.SinglePane?"One pane":"Two panes");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PreviewCommand,Settings.Preview?"On":"Off");
        PanesCommand.ToolTip=Settings.SinglePane?"Show two panes (Ctrl+P)":"Show one pane (Ctrl+P)";
        ((Image)PanesCommand.Content).Source=ExplorerIcons.Glyph(Settings.SinglePane?"\uE8A0":"\uE89F");
        UpdateCommandSpacing();
    }
    void CommandBar_SizeChanged(object sender,SizeChangedEventArgs args)=>UpdateCommandSpacing();
    void UpdateCommandSpacing()
    {
        if(CommandBar.ActualWidth<=0)return;
        var visible=CommandButtons.Children.OfType<Button>().Where(button=>button.Visibility==Visibility.Visible).ToArray();
        if(visible.Length==0)return;
        double width=Math.Clamp((CommandBar.ActualWidth-36)/visible.Length,22,30);
        foreach(var button in CommandButtons.Children.OfType<Button>())if(button.Width!=width)button.Width=width;
    }
}
