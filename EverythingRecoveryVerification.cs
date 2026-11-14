#if DEBUG
using FileExplorer.Search;

namespace FileExplorer;

public partial class MainWindow
{
    // SEARCH-05: a native send held on the IPC dispatcher stands in for an Everything
    // instance that stopped answering; status and restart are substituted, so the
    // user's real Everything is never restarted by verification.
    internal async Task VerifyEverythingRecovery66(Action<bool,string> check)
    {
        var replyTimeout=EverythingRecovery.ReplyTimeout;var restartAfter=EverythingRecovery.RestartAfter;
        var state=EverythingRecovery.State.Ready;int restarts=0;
        using var release=new ManualResetEventSlim();
        // While waiting for the dispatcher, a timeout must never reach the real status or restart.
        EverythingRecovery.ProbeOverride=()=>EverythingRecovery.State.Loading;
        EverythingRecovery.RestartOverride=()=>Task.FromResult(false);
        EverythingRecovery.ReplyTimeout=TimeSpan.FromMinutes(4);
        // Earlier real queries may still occupy the IPC dispatcher (a slow real Everything). Wait until it
        // reaches this fixture's held send, then start the unanswered clock from that held request.
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EverythingSearchClient.BeforeNativeSend=_=>{entered.TrySetResult();release.Wait();};
        using(var hold=new CancellationTokenSource())
        {
            var held=EverythingSearchClient.Shared.QueryAsync("folder:","",0,1,"Name",false,hold.Token);
            await Task.WhenAny(entered.Task,Task.Delay(TimeSpan.FromMinutes(3)));
            check(entered.Task.IsCompleted,"The Everything IPC dispatcher reaches the held fixture send");
            hold.Cancel();try{await held;}catch(Exception error)when(error is OperationCanceledException or InvalidOperationException){}
        }
        EverythingRecovery.ProbeOverride=()=>state;
        EverythingRecovery.RestartOverride=()=>{Interlocked.Increment(ref restarts);return Task.FromResult(true);};
        EverythingRecovery.ResetForVerification();EverythingSearchClient.Shared.StartUnansweredForVerification();
        EverythingRecovery.ReplyTimeout=TimeSpan.FromSeconds(1);EverythingRecovery.RestartAfter=TimeSpan.FromSeconds(3.5);
        async Task<string> Unanswered(CancellationToken token=default)
        {
            try{await EverythingSearchClient.Shared.QueryAsync("folder:","",0,1,"Name",false,token);return "answered";}
            catch(InvalidOperationException error)when(error.InnerException is TimeoutException){return error.Message;}
        }
        try
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            string ready=await Unanswered();
            check(ready.Contains("didn't answer within 1 seconds. Search again")&&restarts==0&&watch.Elapsed<TimeSpan.FromSeconds(3),"An unanswered query ends after its bound; a short silence does not restart Everything: "+ready);
            state=EverythingRecovery.State.Loading;string loading=await Unanswered();
            check(loading.Contains("still loading its index")&&restarts==0,"While Everything loads its index the query says so and nothing is restarted: "+loading);
            state=EverythingRecovery.State.Busy;string busy=await Unanswered();
            check(busy.Contains("busy updating its index")&&restarts==0,"While Everything updates its index the query says so and nothing is restarted: "+busy);
            state=EverythingRecovery.State.Hung;string hung=await Unanswered();
            check(hung.Contains("Pathweft restarted it")&&restarts==1,"After the restart threshold a hung Everything is restarted once: "+hung);
            string again=await Unanswered();
            check(again.Contains("still isn't answering")&&restarts==1,"A second hang in the same session is reported, not restarted again: "+again);
            using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            bool cancelled=false;try{await Unanswered(cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            check(cancelled&&restarts==1,"A caller's own cancellation stays a cancellation and does not count as Everything failing");
        }
        finally
        {
            EverythingSearchClient.BeforeNativeSend=null;release.Set();
            EverythingRecovery.ProbeOverride=null;EverythingRecovery.RestartOverride=null;
            EverythingRecovery.ReplyTimeout=replyTimeout;EverythingRecovery.RestartAfter=restartAfter;
            // The once-per-session restart stays spent, so nothing later in this verification process can restart the real Everything.
        }
    }
}
#endif
