using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FileExplorer;

public partial class MainWindow
{
    bool changingAccess,lastAdministratorMode;
    Task accessWork=Task.CompletedTask;
    void InitializeAdministrator()
    {
        var menu=new ContextMenu();var end=new MenuItem{Header="End administrator session"};
        end.Click+=async(_,_)=>{accessWork=EndAdministrator();await accessWork;};menu.Items.Add(end);
        menu.Opened+=(_,_)=>end.IsEnabled=AdministratorSession.Available&&!changingAccess;
        AdministratorCommand.ContextMenu=menu;
        Left.AdministratorRequested+=RequestAdministrator;Right.AdministratorRequested+=RequestAdministrator;
        AdministratorSession.Changed+=AdministratorChanged;
        Closed+=(_,_)=>AdministratorSession.Changed-=AdministratorChanged;
        UpdateAdministrator();
    }
    void UpdateAdministrator()
    {
        bool admin=AdministratorSession.Browsing,ready=AdministratorSession.Available;
        AdministratorCommand.Tag=admin&&!maintenanceVisible?"on":null;
        string caption=maintenanceVisible?"Explorer administrator access is available in file mode":admin?"Administrator access — switch to standard":ready?"Standard access — administrator session ready":"Standard access — switch to administrator";
        AdministratorCommand.ToolTip=caption;AutomationProperties.SetName(AdministratorCommand,caption);
        AutomationProperties.SetItemStatus(AdministratorCommand,maintenanceVisible?"Explorer only":admin?"Administrator":ready?"Standard; administrator session ready":"Standard");
        AdministratorCommand.IsEnabled=!changingAccess&&!maintenanceVisible;
        AdministratorCommand.Opacity=changingAccess||maintenanceVisible?0.4:1;
        Left.UpdateFolderAccessAction(!changingAccess);Right.UpdateFolderAccessAction(!changingAccess);
    }
    void AdministratorChanged()
    {
        if(Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.InvokeAsync(async()=>
        {
            try
            {
            UpdateAdministrator();
            if(changingAccess)return;
            if(!AdministratorSession.Available)await performance.EndAdministratorIo();
            else performance.AdministratorReady();
            if(!changingAccess&&lastAdministratorMode!=AdministratorSession.Browsing)await RefreshAdministratorPanes();
            }
            catch(Exception error)when(error is System.IO.IOException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception){Status.Text=error.Message;}
        });
    }
    async Task RefreshAdministratorPanes()
    {
        lastAdministratorMode=AdministratorSession.Browsing;
        await Left.RefreshAccessMode();await Right.RefreshAccessMode();
    }
    async void Administrator_Click(object sender,RoutedEventArgs e)
    {if(changingAccess||maintenanceVisible)return;accessWork=ToggleAdministrator();await accessWork;}
    async void RequestAdministrator()
    {if(changingAccess||maintenanceVisible||AdministratorSession.Browsing)return;accessWork=ToggleAdministrator();await accessWork;}
    async Task ToggleAdministrator()
    {
        changingAccess=true;UpdateAdministrator();
        try
        {
            if(!AdministratorSession.Browsing&&!await AdministratorSession.Enable())return;
            AdministratorSession.SetBrowsing(!AdministratorSession.Browsing);await RefreshAdministratorPanes();
        }
        catch(Exception error)when(error is System.IO.IOException or System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {Status.Text=error.Message;}
        finally{changingAccess=false;UpdateAdministrator();if(AdministratorSession.Available)performance.AdministratorReady();}
    }
    async Task EndAdministrator()
    {
        if(changingAccess)return;changingAccess=true;UpdateAdministrator();
        try{await AdministratorSession.End();await performance.EndAdministratorIo();await RefreshAdministratorPanes();Status.Text="Administrator session ended";}
        catch(Exception error)when(error is System.IO.IOException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception){Status.Text=error.Message;}
        finally{changingAccess=false;UpdateAdministrator();}
    }
}
