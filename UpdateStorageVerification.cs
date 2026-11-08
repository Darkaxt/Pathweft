#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyUpdateStorage38(string root,Action<bool,string> check)
    {
        await VerifyDownload114(root,check);
        string target=UpdateDownloads.Create(new Version(0,0,138));File.WriteAllText(target,"owned installer fixture");
        string record=Path.Combine(Path.GetDirectoryName(target),"download.json");
        var receipt=JsonSerializer.Deserialize<UpdateDownloads.Receipt>(File.ReadAllText(record));
        File.WriteAllText(record,JsonSerializer.Serialize(receipt with{Owner=0}));
        var start=new ProcessStartInfo("cmd.exe","/d /q"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var setup=Process.Start(start))
        {
            UpdateDownloads.Handoff(target,setup);
            var retiring=UpdateDownloads.RetireAsync(CancellationToken.None);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            check(!retiring.IsCompleted&&File.Exists(target),"The restarted owner retains a live exact installer instead of deleting it by age or a deadline");
            await setup.StandardInput.WriteLineAsync("exit");await setup.WaitForExitAsync();await retiring;
            check(!File.Exists(target)&&!Directory.Exists(Path.GetDirectoryName(target)),"Actual installer exit retires its payload/receipt/directory without a timer");
        }
        target=UpdateDownloads.Create(new Version(0,0,139));
        await UpdateDownloads.RetireAsync(CancellationToken.None);
        check(File.Exists(target),"Startup retirement preserves a download owned by the current live process");
        string saved=target+".original";File.Move(target,saved);File.WriteAllText(target,"unrelated replacement");
        check(!UpdateDownloads.Discard(target)&&File.ReadAllText(target)=="unrelated replacement","Identity-bound retirement preserves a replaced path and unrelated members");
        File.Delete(target);File.Move(saved,target);UpdateDownloads.Discard(target);
        await VerifySpaceLive38(root,check);
    }
    internal async Task VerifySpaceLive38(string root,Action<bool,string> check)
    {
        string folder=Directory.CreateDirectory(Path.Combine(root,"live disclosure")).FullName;
        string branch=Directory.CreateDirectory(Path.Combine(folder,"downloads")).FullName;
        Directory.CreateDirectory(Path.Combine(branch,"WPF"));File.WriteAllBytes(Path.Combine(branch,"installer.exe"),new byte[4096]);
        File.WriteAllText(Path.Combine(folder,"direct.txt"),"root file");
        await S1Verification.Navigate(Left,folder);await Left.StopSizingAsync();SetActive(Left);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int visits=0;
        SpaceScan.BeforeEnumeration=(path,token)=>
        {if(path==folder&&Interlocked.Increment(ref visits)==2){entered.TrySetResult();release.Task.WaitAsync(token).GetAwaiter().GetResult();}};
        try
        {
            await Left.ToggleSpaceAsync();await entered.Task;
            check(Left.Items.Any(row=>row.Name=="direct.txt"),"Direct root files appear while recursive analysis is still running");
            var row=Left.Items.Single(item=>item.Path==branch);
            var populated=Sizes(Left,()=>Left.Items.Any(item=>item.Name=="installer.exe"));
            Left.SpaceExpandForVerification((SpaceEntry)row);await populated;
            check(!release.Task.IsCompleted&&Left.Items.Any(item=>item.Name=="WPF")&&Left.Items.Single(item=>item.Name=="installer.exe").DisplaySize==4096,
                "Expanding a folder shows real file children and bytes before the held whole-root scan finishes");
            await S1Verification.Snapshot(this,root,"live-analysis-children");
        }
        finally{release.TrySetResult();await Left.SpaceCompletion;SpaceScan.BeforeEnumeration=null;await Left.EndSpaceAsync();}
        var updates=new Dictionary<string,SpaceUpdate>(StringComparer.OrdinalIgnoreCase);
        bool invalidated=false;
        await SpaceScan.RunAsync(folder,false,"",update=>
        {updates[update.Path]=update;if(!invalidated&&update.Path==folder&&update.State==SpaceState.Scanning){invalidated=true;SizeCache.Invalidate(Path.Combine(folder,"direct.txt"));}},CancellationToken.None);
        check(updates[folder].State==SpaceState.Changed&&new SpaceEntry{State=SpaceState.Changed,Refreshing=true}.SpaceStatus=="Updating…",
            "A changed active snapshot is distinct from terminal partial coverage and presents a queued refresh as Updating");
    }
}
public partial class PaneView
{
    internal void SpaceExpandForVerification(SpaceEntry row)=>ToggleSpaceRow(row);
}
#endif
