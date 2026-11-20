using System.Windows.Controls;
using System.Windows;

namespace FileExplorer;

public partial class MainWindow
{
    void UpdateCommandStates()
    {
        UpdateSpaceCommandState();
        PanesCommand.Tag=Settings.SinglePane?null:"on";
        PreviewCommand.Tag=Settings.Preview?"on":null;
        System.Windows.Automation.AutomationProperties.SetItemStatus(HiddenToggle,(active?.ShowsHidden??Settings.ShowHidden)?"On":"Off");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PanesCommand,Settings.SinglePane?"One pane":"Two panes");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PreviewCommand,Settings.Preview?"On":"Off");
        HiddenToggle.ToolTip=(active?.ShowsHidden??Settings.ShowHidden)?"Hide Windows hidden and dot-prefixed files (Ctrl+H)":"Show Windows hidden and dot-prefixed files (Ctrl+H)";
        PreviewCommand.ToolTip=Settings.Preview?"Hide preview (Alt+P)":"Show preview (Alt+P)";
        var eye=(System.Windows.Media.DrawingImage)FindResource("CommandIcon.Hidden");
        ((Image)HiddenToggle.Content).Source=(active?.ShowsHidden??Settings.ShowHidden)?eye:ExplorerIcons.Crossed(eye);
        PanesCommand.ToolTip=Settings.SinglePane?"Show two panes (Ctrl+P)":"Show one pane (Ctrl+P)";
        ((Image)PanesCommand.Content).Source=ExplorerIcons.PaneLayout(!Settings.SinglePane);
        UpdateCommandSpacing();
        ShowAnalysis();
    }
    void UpdateSpaceCommandState()
    {
        if(AnalyzeSpaceCommand!=null)
        {
            AnalyzeSpaceCommand.IsEnabled=!AnalysisVisible&&active?.CanAnalyzeSpace==true;
            AnalyzeSpaceCommand.Tag=active?.SpaceAnalyzing==true?"on":null;
            AnalyzeSpaceCommand.ToolTip=active?.SpaceAnalyzing==true?"Restore the previous view":"Analyze space in this folder";
            System.Windows.Automation.AutomationProperties.SetItemStatus(AnalyzeSpaceCommand,active?.SpaceAnalyzing==true?"On":"Off");
        }
        UpdateContentCommandState();
    }
    // CONTENT-01: indexes the contents of the folder shown and everything below it, or stops. Shown only when the
    // index service has the content index (protocol 2). A folder inside an indexed folder is covered by it.
    void UpdateContentCommandState()
    {
        if(IndexContentsCommand==null)return;
        bool shown=Search.ContentIndexing.Available;
        if(IndexContentsCommand.Visibility!=(shown?Visibility.Visible:Visibility.Collapsed)){IndexContentsCommand.Visibility=shown?Visibility.Visible:Visibility.Collapsed;UpdateCommandSpacing();}
        if(!shown)return;
        string folder=active?.CanIndexContents==true?active.Dir:null;
        string scope=folder==null?null:Search.ContentIndexing.Covering(folder);
        bool exact=scope!=null&&scope.Equals(Search.ContentIndexing.Normalize(folder),StringComparison.OrdinalIgnoreCase);
        IndexContentsCommand.IsEnabled=!AnalysisVisible&&folder!=null&&(scope==null||exact);
        IndexContentsCommand.Tag=scope!=null?"on":null;
        IndexContentsCommand.ToolTip=folder==null?"Index contents (open a folder on a drive)"
            :scope==null?"Index contents of this folder and its subfolders"
            :exact?"Stop indexing contents of this folder"
            :"Contents are indexed with "+scope;
        System.Windows.Automation.AutomationProperties.SetItemStatus(IndexContentsCommand,scope!=null?"On":"Off");
    }
    async void IndexContents_Click(object sender,RoutedEventArgs args)
    {
        if(active?.CanIndexContents!=true)return;
        string folder=Search.ContentIndexing.Normalize(active.Dir);
        IndexContentsCommand.IsEnabled=false;
        try
        {
            if(Search.ContentIndexing.Covering(folder) is { } scope)
            {
                await Search.ContentIndexing.Disable(scope);
                Status.Text="Stopped indexing contents of "+scope+". Its content index was removed; the files are unchanged.";
            }
            else
            {
                await Search.ContentIndexing.Enable(folder);
                Status.Text="Indexing contents of "+folder+" in the background. Search them with content:word in the address bar.";
            }
        }
        catch(InvalidOperationException error){Status.Text="Content index: "+error.Message;}
        finally{UpdateCommandStates();}
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
