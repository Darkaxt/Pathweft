#if DEBUG
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyAdministrator30(string root,Action<bool,string> check)
    {
        performance.AutomaticIo=false;SetSinglePane(true);SetActive(Left);
        using var identity=WindowsIdentity.GetCurrent();using var self=Process.GetCurrentProcess();
        check(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),"Main browser remains a standard-integrity process");
        File.WriteAllText(Path.Combine(root,"keep.txt"),"Retain selection across access modes");
        await S1Verification.Navigate(Left,root);Left.List.SelectedItem=Left.Items.Single(x=>x.Name=="keep.txt");
        string profile=Settings.Profile;
        check(!AdministratorSession.Browsing&&!AdministratorSession.Available&&AdministratorCommand.Tag==null,"Initial browser mode is Standard with no elevated helper");
        var controls=CommandButtons.Children.OfType<Button>().ToArray();
        check(Array.IndexOf(controls,AdministratorCommand)==Array.IndexOf(controls,HiddenToggle)+1,"Shield sits immediately beside hidden-files toggle");
        AdministratorSession.LaunchForVerification=_=>throw new Win32Exception(1223);
        AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;
        check(!AdministratorSession.Browsing&&!AdministratorSession.Available&&Left.Selected.Any(x=>x.Name=="keep.txt"),"Native UAC-cancel result leaves Standard mode, original path and selection intact");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1400;Height=850;UpdateLayout();
            check(AdministratorCommand.Content is Image{Source:DrawingImage,Width:16,Height:16}&&AdministratorCommand.ActualWidth<=30.1&&AdministratorCommand.ActualHeight<=24.1,"Shield retains existing compact vector proportions at "+theme+scale);
            await S1Verification.Snapshot(this,root,$"administrator-{theme}-{scale:0.0}");
        }
        string stagedFolder=Path.Combine(DataFolder.Cache,"EditorSaves");Directory.CreateDirectory(stagedFolder);
        string staged=Path.Combine(stagedFolder,Guid.NewGuid().ToString("N")),editorFile=Path.Combine(root,"editor-save.txt");
        byte[] contents=System.Text.Encoding.UTF8.GetBytes("Exact editor contents é 子");File.WriteAllBytes(staged,contents);File.WriteAllText(editorFile,"Original");
        try
        {
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contents));
            await MaintenanceWorker.Apply(new("save-editor",editorFile,hash,staged));
            check(File.ReadAllBytes(editorFile).SequenceEqual(contents),"Fixed editor-save protocol atomically replaces the destination with the exact staged bytes");
            File.WriteAllText(staged,"Changed source");bool rejected=false;
            try{await MaintenanceWorker.Apply(new("save-editor",editorFile,hash,staged));}catch(IOException){rejected=true;}
            check(rejected&&File.ReadAllBytes(editorFile).SequenceEqual(contents)&&!Directory.EnumerateFiles(root,"editor-save.txt.pathweft-*.tmp").Any(),"Changed editor staging is rejected without replacing the original or leaving adjacent temporary files");
        }
        finally{File.Delete(staged);}
        if(Environment.GetEnvironmentVariable("PATHWEFT_ADMIN_NATIVE")!="1"){AdministratorSession.LaunchForVerification=null;return;}
        string handoff="Pathweft-Admin-Trial-"+Guid.NewGuid().ToString("N");
        using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,handoff);
        using var cancel=new EventWaitHandle(false,EventResetMode.ManualReset,handoff+"-cancel");
        using var nextOwner=new EventWaitHandle(false,EventResetMode.ManualReset,handoff+"-next");
        using var ownerRetired=new EventWaitHandle(false,EventResetMode.ManualReset,handoff+"-retired");
        int launches=0;Process helper=null;
        AdministratorSession.LaunchForVerification=async launch=>
        {
            launches++;
            File.WriteAllText(Path.Combine(root,"request.json"),JsonSerializer.Serialize(new{pid=self.Id,started=self.StartTime.ToUniversalTime().Ticks,executable=launch.FileName,args=launch.ArgumentList.ToArray(),handoff}));
            int signaled=await Task.Run(()=>WaitHandle.WaitAny(new WaitHandle[]{ready,cancel}));
            if(signaled==1)throw new Win32Exception(1223);
            int pid=int.Parse(File.ReadAllText(Path.Combine(root,"worker.pid")));
            long handle=long.Parse(File.ReadAllText(Path.Combine(root,"worker.handle")));
            helper=new Process{StartInfo=launch};
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Process).GetMethod("SetProcessId",flags).Invoke(helper,new object[]{pid});
            typeof(Process).GetMethod("SetProcessHandle",flags).Invoke(helper,new object[]{new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(handle),true)});
            return helper;
        };
        try
        {
            AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;
            check(AdministratorSession.Available&&AdministratorSession.Browsing&&Equals(AdministratorCommand.Tag,"on")&&launches==1,"Actual approved helper activates administrator browsing through the real shield handler");
            var concurrent=await Task.WhenAll(Enumerable.Range(0,16).Select(_=>MaintenanceClient.ReadFolder(root,default)));
            check(concurrent.All(rows=>rows.Any(x=>x.Name=="keep.txt"))&&launches==1,"Overlapping folder reads queue within the shared session capacity and all retire without busy failures");
            var drive=await Task.Run(()=>WindowsPerformanceCounters.MapPath(root));
            await using(var client=new MaintenanceClient())
            {
                var reply=await client.Start(new("disk-health",drive.Disk.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),null,null),true);
                check(reply.Ok&&reply.Health?.Disk==drive.Disk&&reply.Health.Name.Length>0&&launches==1,"Read-only native drive health reuses the same approved helper and exact physical identity");
                File.WriteAllText(Path.Combine(root,"drive-admin-health.json"),JsonSerializer.Serialize(reply.Health,new JsonSerializerOptions{WriteIndented=true}));
            }
            await S1Verification.Navigate(Left,root);
            Left.List.SelectedItem=Left.Items.Single(x=>x.Name=="keep.txt");
            AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;
            await S1Verification.Navigate(Left,root);
            check(!AdministratorSession.Browsing&&AdministratorSession.Available&&launches==1&&AutomationProperties.GetItemStatus(AdministratorCommand).Contains("session ready"),"Standard toggle retains approved session for other features without another launch");
            string protectedFolder=Path.Combine(root,"protected");bool denied=false;
            try{Directory.GetFileSystemEntries(protectedFolder);}catch(UnauthorizedAccessException){denied=true;}
            check(denied,"Task-owned protected fixture is genuinely inaccessible to the standard GUI");
            var rows=await MaintenanceClient.ReadFolder(protectedFolder,default);
            check(rows.Any(x=>x.Name=="protected.txt"&&x.Bytes==23),"The same approved helper reads protected metadata while standard process cannot");
            async Task DeniedFolder()
            {
                var failed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Failed(string message)=>failed.TrySetResult();Left.Error+=Failed;
                try{Left.Navigate(protectedFolder);await failed.Task;}finally{Left.Error-=Failed;}
                check(Left.FolderFailurePanel.Visibility==Visibility.Visible&&Left.DeniedWatermark.Visibility==Visibility.Visible&&Left.FolderFailureTitle.Text=="Access denied"&&Left.FolderAccessAction.Content.ToString()=="Use administrator access"&&!Left.Items.Any(),"Real Standard denial has pane explanation, administrator action and red ribbon instead of an empty-looking folder");
            }
            await DeniedFolder();
            foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
            {
                Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1400;Height=850;UpdateLayout();
                check(Left.FolderAccessAction.IsEnabled&&Left.FolderFailurePanel.ActualWidth>100&&Left.DeniedWatermark.Opacity<=0.4,"Denied pane action and faint watermark remain visible and themed at "+theme+scale);
                await S1Verification.Snapshot(this,root,$"access-denied-{theme}-{scale:0.0}");
            }
            Width=900;UpdateLayout();await S1Verification.Snapshot(this,root,"access-denied-narrow");Width=1400;
            var listing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Listed(){if(Left.Listed&&Left.Items.Any(x=>x.Name=="protected.txt"))listing.TrySetResult();}
            Left.StatsChanged+=Listed;
            try{Left.FolderAccessAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;await listing.Task;}finally{Left.StatsChanged-=Listed;}
            check(Left.Items.Any(x=>x.Name=="protected.txt"),"Actual administrator browser shows the protected folder without reopening Pathweft");
            check(Left.FolderFailurePanel.Visibility==Visibility.Collapsed&&Left.DeniedWatermark.Visibility==Visibility.Collapsed&&launches==1,"Pane administrator action reuses the approved helper and clears denial after actual successful listing");
            await Left.ToggleSpaceAsync();await Left.SpaceCompletion;
            check(Left.SpaceAnalyzing&&Left.SpaceSummary.Bytes==23&&Left.Items.Any(x=>x.Name=="protected.txt"),"Actual protected disk-analysis pane measures and lists through administrator access");
            AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;await Left.SpaceCompletion;
            check(Left.SpaceAnalyzing&&Left.Dir==protectedFolder&&Left.SpaceSummary.Bytes==0&&!Left.Items.Any()&&Left.DeniedWatermark.Visibility==Visibility.Visible,"Switching actual analysis to Standard removes privileged rows/totals and presents denied root without reopening the view");
            await Left.EndSpaceAsync();
            // EndSpace refresh is asynchronous. Observe the owned error result,
            // rather than assuming an empty visible list is successful enumeration.
            await DeniedFolder();
            check(!Left.Items.Any(x=>x.Name=="protected.txt"),"Leaving Standard analysis cannot restore its saved administrator-only listing");
            AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;
            await S1Verification.Navigate(Left,protectedFolder);await Left.ToggleSpaceAsync();await Left.SpaceCompletion;
            check(Left.SpaceSummary.Bytes==23&&Left.DeniedWatermark.Visibility==Visibility.Collapsed&&launches==1,"Switching the same root back to administrator remeasures successfully with one helper launch");
            await Left.EndSpaceAsync();await S1Verification.Navigate(Left,root);
            string empty=Path.Combine(root,"empty");Directory.CreateDirectory(empty);await S1Verification.Navigate(Left,empty);
            check(!Left.Items.Any()&&Left.FolderFailurePanel.Visibility==Visibility.Collapsed&&Left.DeniedWatermark.Visibility==Visibility.Collapsed,"A genuinely empty accessible folder has no denial UI after navigation");
            await S1Verification.Navigate(Left,root);
            await Left.ToggleSpaceAsync();await Left.SpaceCompletion;
            var protectedBranch=(SpaceEntry)Left.Items.Single(x=>x.Path==protectedFolder);
            Left.List.SelectedItem=protectedBranch;Left.List.ScrollIntoView(protectedBranch);UpdateLayout();
            var expander=PreviewChildren<Button>(Left.List).Single(button=>ReferenceEquals(button.DataContext,protectedBranch)&&AutomationProperties.GetName(button)=="Expand or collapse folder");
            expander.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Left.SpaceCompletion;
            check(Left.Items.Any(x=>x.Path==Path.Combine(protectedFolder,"protected.txt")),"Actual analyzer expansion exposes the protected file only in administrator mode");
            AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;await Left.SpaceCompletion;
            check(Left.Items.OfType<SpaceEntry>().Any(x=>x.Path==protectedFolder&&x.State==SpaceState.Denied&&x.Expanded)&&Left.Selected.Any(x=>x.Path==protectedFolder)&&!Left.Items.Any(x=>x.Path==Path.Combine(protectedFolder,"protected.txt")),"Standard parent analysis retains selected expanded branch while removing its privileged descendants");
            AdministratorCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await accessWork;await Left.SpaceCompletion;
            check(Left.Items.Any(x=>x.Path==Path.Combine(protectedFolder,"protected.txt"))&&Left.Selected.Any(x=>x.Path==protectedFolder)&&launches==1,"Administrator parent analysis restores expansion and selection using the same helper");
            await Left.EndSpaceAsync();await S1Verification.Navigate(Left,root);
            var measured=new List<SpaceUpdate>();await MaintenanceClient.ScanSpace(protectedFolder,measured.Add,default);
            check(measured.Any(x=>x.Path==protectedFolder&&x.Bytes==23&&x.State==SpaceState.Complete),"Disk analysis uses the approved helper and returns actual protected-file size");
            string protectedFile=Path.Combine(protectedFolder,"protected.txt");
            await MaintenanceClient.SaveEditor(protectedFile,new byte[31]);
            await MaintenanceClient.SaveEditor(protectedFile,new byte[52]);
            bool stillProtected=false;try{File.ReadAllBytes(protectedFile);}catch(UnauthorizedAccessException){stillProtected=true;}
            var saved=await MaintenanceClient.ReadFolder(protectedFolder,default);
            check(stillProtected&&saved.Single(x=>x.Name=="protected.txt").Bytes==52&&launches==1&&!Directory.EnumerateFiles(Path.Combine(DataFolder.Cache,"EditorSaves")).Any(),"Repeated protected editor saves reuse the same helper, preserve ACLs and retire staged contents");
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();bool stopped=false;
                try{await MaintenanceClient.ReadFolder(protectedFolder,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
                check(stopped&&AdministratorSession.Available,"Cancelled enumeration retires without ending or relaunching shared session");
            }
            await using(var client=new MaintenanceClient())
            {
                var reviewed=await client.Start(new("analyze","windows-temp",null,Path.Combine(DataFolder.Cache,"Maintenance")),true);
                check(reviewed.Ok&&reviewed.Row?.Bytes.HasValue==true&&launches==1,"Protected read-only Cleanup review reuses the same approved session");
            }
            bool withinBudget=true;var costs=new List<object>();
            await using(var io=new PerformanceIoClient())
            {
                await io.Enable();await io.SetScope(root,true);
                check(io.Connected&&io.Latest.Running&&launches==1,"Real detailed file-I/O trace shares the already approved helper");
                foreach(bool active in new[]{true,false})
                {
                    helper.Refresh();var cpu=helper.TotalProcessorTime;long memory=helper.PrivateMemorySize64;var elapsed=Stopwatch.StartNew();
                    // Match the existing native I/O acceptance workload:
                    // thirty published observations, with and without owned I/O.
                    for(int i=0;i<30;i++)
                    {
                        var next=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void Changed()=>next.TrySetResult();io.Changed+=Changed;
                        try{if(active)await File.WriteAllBytesAsync(Path.Combine(root,"known.bin"),new byte[65536]);await next.Task;}finally{io.Changed-=Changed;}
                    }
                    helper.Refresh();double use=(helper.TotalProcessorTime-cpu).TotalSeconds/elapsed.Elapsed.TotalSeconds/Environment.ProcessorCount*100;
                    costs.Add(new{active,seconds=elapsed.Elapsed.TotalSeconds,cpuPercent=use,privateBytes=helper.PrivateMemorySize64,initialPrivateBytes=memory,launches});
                    withinBudget&=use<=1&&helper.PrivateMemorySize64<=96L*1024*1024;
                    File.WriteAllText(Path.Combine(root,"administrator-cost.json"),JsonSerializer.Serialize(costs));
                }
                await io.SetScope(root,false);
            }
            await S1Verification.Navigate(Left,root);Left.List.SelectedItem=Left.Items.Single(x=>x.Name=="keep.txt");
            int helperPid=helper.Id;
            await EndAdministrator();await S1Verification.Navigate(Left,root);
            check(!AdministratorSession.Available&&!AdministratorSession.Browsing&&Settings.Profile==profile&&launches==1,"Explicit session end retires helper naturally and restores standard browsing/profile");
            var launchOwner=new ProcessStartInfo(Program.Executable){UseShellExecute=false,CreateNoWindow=true};
            foreach(string arg in new[]{"--verify-administrator-owner",root,handoff})launchOwner.ArgumentList.Add(arg);
            using var crashOwner=Process.Start(launchOwner);await crashOwner.WaitForExitAsync();
            await Task.Run(()=>ownerRetired.WaitOne());
            using var lifecycle=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"owner-retired.json")));
            check(File.Exists(Path.Combine(root,"owner-accepted.json"))&&lifecycle.RootElement.GetProperty("helperExit").GetInt32()==0&&lifecycle.RootElement.GetProperty("ownerExited").GetBoolean(),"An approved helper exits naturally after its exact standard owner is abruptly terminated");
            check(File.ReadAllText(Path.Combine(root,"non-owner-result.txt"))=="2","A real elevated helper rejects a control pipe hosted by a different process");
            check(withinBudget,"Shared administrator helper meets existing1% CPU/96MiB native I/O budgets during active and idle observation windows");
            File.WriteAllText(Path.Combine(root,"administrator-native.json"),JsonSerializer.Serialize(new{pid=self.Id,helperPid,launches,ended=true}));
        }
        finally{nextOwner.Set();AdministratorSession.LaunchForVerification=null;await AdministratorSession.End();}
    }
}
internal static class AdministratorOwnerVerification
{
    internal static async Task<int> Run(string root,string handoff)
    {
        root=Path.GetFullPath(root);
        if(!System.Text.RegularExpressions.Regex.IsMatch(root,@"^D:\\Temp\\pathweft-s18\\specialists-[a-f0-9]{32}$")||!handoff.StartsWith("Pathweft-Admin-Trial-",StringComparison.Ordinal))return 2;
        using var self=Process.GetCurrentProcess();using var identity=WindowsIdentity.GetCurrent();
        if(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 2;
        using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,handoff+"-owner");
        using var next=EventWaitHandle.OpenExisting(handoff+"-next");
        AdministratorSession.LaunchForVerification=async launch=>
        {
            File.WriteAllText(Path.Combine(root,"request-owner.json"),JsonSerializer.Serialize(new{pid=self.Id,started=self.StartTime.ToUniversalTime().Ticks,executable=launch.FileName,args=launch.ArgumentList.ToArray(),handoff=handoff+"-owner"}));next.Set();
            await Task.Run(()=>ready.WaitOne());
            using var receipt=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"owner-handle.json")));
            var child=new Process{StartInfo=launch};const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Process).GetMethod("SetProcessId",flags).Invoke(child,new object[]{receipt.RootElement.GetProperty("pid").GetInt32()});
            typeof(Process).GetMethod("SetProcessHandle",flags).Invoke(child,new object[]{new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(receipt.RootElement.GetProperty("handle").GetInt64()),true)});
            return child;
        };
        if(!await AdministratorSession.Enable())return 2;
        await MaintenanceClient.ReadFolder(root,default);
        File.WriteAllText(Path.Combine(root,"owner-accepted.json"),JsonSerializer.Serialize(new{pid=self.Id,ready=AdministratorSession.Available}));
        // Deliberately terminate only this disposable owner without OnExit or
        // session disposal: the elevated helper must observe real owner loss.
        self.Kill();return 1;
    }
}
#endif
