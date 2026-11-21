using System.Windows;

namespace FileExplorer;

public partial class MainWindow
{
    readonly CancellationTokenSource updateShutdown=new();
    Task<string> updateCheck;
    bool manualUpdateRequested,updateChecksStopped,fullUpdateRequested;
    Updater.Release availableUpdate; // UPDATE-03: the newer release the last check found, shown by the dot

    // UPDATE-03: a dot on the sidebar's update button, with its tooltip and accessible name, instead of a popup
    void ShowUpdateAvailable(Updater.Release release)
    {
        availableUpdate=release;
        UpdateDot.Visibility=release==null?Visibility.Collapsed:Visibility.Visible;
        UpdateButton.ToolTip=release==null?"Check for updates (it also checks by itself every hour)":$"Pathweft {release.Version} is available: click to see what\u2019s new and update";
        System.Windows.Automation.AutomationProperties.SetName(UpdateButton,release==null?"Check for updates":$"Update available: Pathweft {release.Version}");
    }

    // Establish the visible/manual state synchronously before the HTTP await.
    // Manual callers joining an automatic check share its task and get a result.
    public Task<string> CheckForUpdate(bool manual)
    {
        if(updateChecksStopped)return Task.FromResult<string>(null);
        if(updateDialog?.OfferedRelease!=null)
        {
            if(manual)Bring(updateDialog);
            return Task.FromResult<string>(null);
        }
        if(manual)
        {
            manualUpdateRequested=true;
            EnsureUpdateDialog().ShowChecking();
        }
        if(updateCheck!=null)return updateCheck;
        var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        updateCheck=completion.Task;
        _=RunUpdateCheck(completion);
        return completion.Task;
    }
    UpdateDialog EnsureUpdateDialog()
    {
        if(updateDialog is {} existing){Bring(existing);return existing;}
        var dialog=updateDialog=new UpdateDialog(this);
        dialog.Closed+=(_,_)=>
        {
            if(ReferenceEquals(updateDialog,dialog))updateDialog=null;
        };
        dialog.Show();return dialog;
    }
    async Task RunUpdateCheck(TaskCompletionSource<string> completion)
    {
        string answer=null;
        Exception failure=null;
        try
        {
            // A manual caller joining an automatic check sees the version it asked for in its window.
            var release=await Updater.Check(manual:true,updateShutdown.Token,reinstall:fullUpdateRequested);
            if(!updateChecksStopped)
            {
                // UPDATE-03: any successful check sets the dot; only a manual check shows the release in a window.
                // Closing a manual checker does not resurrect it on response.
                ShowUpdateAvailable(release);
                if(release!=null)
                {
                    if(manualUpdateRequested&&updateDialog!=null)updateDialog.ShowRelease(release,continueFull:fullUpdateRequested);
                }
                else if(manualUpdateRequested)
                {
                    answer=$"Pathweft {Updater.Current} is the latest version.";
                    updateDialog?.ShowCheckResult(failed:false,answer);
                }
            }
        }
        catch(OperationCanceledException)when(updateChecksStopped){}
        catch(Exception error)when(Updater.Failed(error))
        {
            if(manualUpdateRequested&&!updateChecksStopped)
            {
                answer=Updater.CheckFailed;
                updateDialog?.ShowCheckResult(failed:true,Updater.CheckFailed+"\n"+error.Message);
            }
        }
        catch(Exception error){failure=error;}
        finally{updateCheck=null;manualUpdateRequested=fullUpdateRequested=false;}
        if(failure!=null)completion.TrySetException(failure);else completion.TrySetResult(answer);
    }
    async Task StopUpdateChecks()
    {
        updateChecksStopped=true;firstUpdateTimer.Stop();updateTimer.Stop();
        updateShutdown.Cancel();
        if(updateCheck is {} work)await work;
    }
}
