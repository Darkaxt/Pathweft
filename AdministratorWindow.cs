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
        Left.AdministratorRequested+=RequestAdministrator;Right.AdministratorRequested+=RequestAdministrator;
        AdministratorSession.Changed+=AdministratorChanged;
        Closed+=(_,_)=>AdministratorSession.Changed-=AdministratorChanged;
        UpdateAdministrator();
    }
    // The shield manages file-browsing authority only. Performance and Maintenance views hide the
    // file panes, so it is disabled there; their elevated work uses the launch-time session.
    void UpdateAdministrator()
    {
        bool admin=AdministratorSession.Browsing,ready=AdministratorSession.Available,away=maintenanceVisible||AnalysisVisible;
        AdministratorCommand.Tag=admin&&!away?"on":null;
        string caption=away?"Explorer administrator access is available in file mode":admin?"Administrator access — switch to standard":ready?"Standard access — administrator session ready":"Standard access — switch to administrator";
        AdministratorCommand.ToolTip=caption;AutomationProperties.SetName(AdministratorCommand,caption);
        AutomationProperties.SetItemStatus(AdministratorCommand,away?"Explorer only":admin?"Administrator":ready?"Standard; administrator session ready":"Standard");
        AdministratorCommand.IsEnabled=!changingAccess&&!away;
        AdministratorCommand.Opacity=changingAccess||away?0.4:1;
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
    {if(changingAccess||maintenanceVisible||AnalysisVisible)return;accessWork=ToggleAdministrator();await accessWork;}
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
        finally{changingAccess=false;UpdateAdministrator();}
    }
    // ACCESS-04: once per run, after the first frame of an interactive window. Browsing never waits
    // for it; cancelling leaves Standard access with the explicit shield and I/O actions as retries.
    bool startupAccessAsked;
    static readonly bool automatedStart=Environment.GetCommandLineArgs().Skip(1).Any(a=>a.StartsWith("--verify")||a.StartsWith("--selftest")||a.StartsWith("--s8"));
    void RequestStartupAdministrator()
    {
        if(startupAccessAsked)return;startupAccessAsked=true;
        if(!Settings.AdminAtStart||Settings.Profile!=null||automatedStart||AdministratorSession.Available)return;
#if DEBUG
        if(HiddenTrialPresentation.Enabled)return;
#endif
        Dispatcher.BeginInvoke(async()=>
        {
            try{await AdministratorSession.Enable();}
            catch(Exception error)when(error is System.IO.IOException or System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException){Status.Text=error.Message;}
        },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    async Task EndAdministrator()
    {
        if(changingAccess)return;changingAccess=true;UpdateAdministrator();
        try{await AdministratorSession.End();await performance.EndAdministratorIo();await RefreshAdministratorPanes();Status.Text="Administrator session ended";}
        catch(Exception error)when(error is System.IO.IOException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception){Status.Text=error.Message;}
        finally{changingAccess=false;UpdateAdministrator();}
    }
}
