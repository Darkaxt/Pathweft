using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;

namespace FileExplorer;

public partial class PaneView
{
    internal event Action AdministratorRequested;
    bool folderDenied;
    internal static bool IsAccessDenied(Exception error)=>error is UnauthorizedAccessException or System.Security.SecurityException
        ||error is Win32Exception{NativeErrorCode:5};

    // ACCESS-09: administrator access cannot open a network folder, so it is not offered there.
    bool OffersAdministrator=>folderDenied&&!AdministratorSession.Browsing&&!FileExplorer.Search.NetworkDrives.IsNetwork(Dir);
    void ShowFolderFailure(Exception error)
    {
        folderDenied=IsAccessDenied(error);
        FolderFailureTitle.Text=folderDenied?"Access denied":"Folder unavailable";
        FolderFailureDetails.Text=folderDenied
            ? FileExplorer.Search.NetworkDrives.IsNetwork(Dir)?"The share denies access to this folder. Administrator access does not apply to network folders."
            : AdministratorSession.Browsing?"Windows still denies access to this folder with administrator access.":"Use administrator access to open this folder."
            : error.Message;
        FolderFailureDetails.ToolTip=error.Message;
        FolderFailurePanel.Visibility=Visibility.Visible;
        if(!SpaceAnalyzing)Footer.Text=FolderFailureTitle.Text;
        DeniedWatermark.Visibility=folderDenied?Visibility.Visible:Visibility.Collapsed;
        UpdateFolderAccessAction(true);
        AutomationProperties.SetName(FolderFailurePanel,FolderFailureTitle.Text+". "+FolderFailureDetails.Text);
    }
    void ClearFolderFailure()
    {
        folderDenied=false;
        FolderFailurePanel.Visibility=DeniedWatermark.Visibility=Visibility.Collapsed;
    }
    internal void UpdateFolderAccessAction(bool enabled)
    {
        FolderAccessAction.Content=OffersAdministrator?"Use administrator access":"Try again";
        FolderAccessAction.IsEnabled=enabled;
    }
    void FolderAccess_Click(object sender,RoutedEventArgs args)
    {
        if(OffersAdministrator)AdministratorRequested?.Invoke();
        else if(SpaceAnalyzing)RefreshSpace(force:true,measure:true);
        else Refresh();
    }
}
