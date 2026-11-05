using System.Windows;

namespace FileExplorer;

public partial class MainWindow
{
    readonly CancellationTokenSource updateShutdown=new();
    Task<string> updateCheck;
    bool manualUpdateRequested,updateChecksStopped;

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
            if(dialog.OfferedRelease is {} release)Updater.Dismiss(release);
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
            // Dismissal is evaluated after completion, so a manual caller joining
            // an automatic check can still see the version it explicitly asked for.
            var release=await Updater.Check(manual:true,updateShutdown.Token);
            if(!updateChecksStopped)
            {
                if(release!=null&&(manualUpdateRequested||!Updater.IsDismissed(release)))
                {
                    // Closing a manual checker does not resurrect it on response.
                    if(!manualUpdateRequested||updateDialog!=null)EnsureUpdateDialog().ShowRelease(release);
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
        finally{updateCheck=null;manualUpdateRequested=false;}
        if(failure!=null)completion.TrySetException(failure);else completion.TrySetResult(answer);
    }
    async Task StopUpdateChecks()
    {
        updateChecksStopped=true;firstUpdateTimer.Stop();updateTimer.Stop();
        updateShutdown.Cancel();
        if(updateCheck is {} work)await work;
    }
}
