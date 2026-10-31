#if DEBUG
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace FileExplorer;
public partial class MainWindow
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern uint DragQueryFileW(IntPtr drop,uint index,StringBuilder path,uint size);
    [DllImport("ole32.dll")]static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    async Task VerifyArchiveDragRegression(string root,Action<bool,string> check)
    {
        string downloads=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders").GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
        downloads=Environment.ExpandEnvironmentVariables(downloads);
        check(DownloadsFolder().Equals(downloads,StringComparison.OrdinalIgnoreCase)&&Favorites.Children.OfType<Button>().Any(b=>b.ToolTip as string==downloads),"The real Favorites panel includes the Windows-redirected Downloads known folder");
        string registryPath=@"Software\Darkaxt\Pathweft\S8Verification\canonical-"+Guid.NewGuid().ToString("N");
        using(var isolated=Registry.CurrentUser.CreateSubKey(registryPath))
        {
            WindowsIntegration.VerificationRoot=isolated;
            try
            {
                WindowsIntegration.UpgradeOwnedRegistration();
                check(!WindowsIntegration.Enabled&&!File.Exists(WindowsIntegration.RecoveryPath),"Updating a disabled registration leaves folder integration off");
                using(var before=isolated.CreateSubKey(@"Software\Classes\Directory\shell"))before.SetValue("","previous-manager");
                WindowsIntegration.SetEnabled(true);
                using(var current=isolated.OpenSubKey(@"Software\Classes\Directory\shell"))check((string)current.GetValue("")=="open"&&WindowsIntegration.Enabled,"Opt-in routing preserves the canonical Open verb used by Explorer navigation");
                var registration=System.Text.Json.JsonSerializer.Deserialize<WindowsIntegration.Registration>(File.ReadAllText(WindowsIntegration.RecoveryPath));
                WindowsIntegration.SetEnabled(false);
                foreach(var oldDefault in new[]{"pathweft","open"})
                {
                    var legacy=registration with{Version=1,Values=registration.Values.Where(v=>!v.Key.Contains(@"\CLSID\")).Select(v=>v.Name=="DelegateExecute"?v with{Written=""}:v.Name==""&&v.Key.EndsWith(@"\shell")?v with{Written=oldDefault}:v).ToList(),CreatedKeys=registration.CreatedKeys.Where(k=>!k.Contains("CLSID")&&!k.EndsWith("WOW6432Node")).ToList()};
                    foreach(var value in legacy.Values)using(var key=isolated.CreateSubKey(value.Key))key.SetValue(value.Name,value.Written);
                    File.WriteAllText(WindowsIntegration.RecoveryPath,System.Text.Json.JsonSerializer.Serialize(legacy));
                    WindowsIntegration.UpgradeOwnedRegistration();
                    using(var command=isolated.OpenSubKey(@"Software\Classes\Directory\shell\open\command"))
                        check(WindowsIntegration.Enabled&&(string)command.GetValue("DelegateExecute")=="{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}","A wholly owned "+oldDefault+" legacy registration upgrades to browser-aware routing");
                    WindowsIntegration.SetEnabled(false);
                }
                WindowsIntegration.SetEnabled(true);
                var nativePrior=System.Text.Json.JsonSerializer.Deserialize<WindowsIntegration.Registration>(File.ReadAllText(WindowsIntegration.RecoveryPath));
                nativePrior=nativePrior with{Values=nativePrior.Values.Select(v=>v.Name==""&&v.Key.EndsWith(@"\InprocServer32")?v with{Written=Path.Combine(AppContext.BaseDirectory,"PathweftShell-1.0.0-"+(v.Key.Contains("WOW6432Node")?"x86":"x64")+".dll")}:v).ToList()};
                foreach(var value in nativePrior.Values)using(var key=isolated.OpenSubKey(value.Key,true))key.SetValue(value.Name,value.Written);
                File.WriteAllText(WindowsIntegration.RecoveryPath,System.Text.Json.JsonSerializer.Serialize(nativePrior));
                WindowsIntegration.UpgradeOwnedRegistration();
                check(WindowsIntegration.Enabled,"A wholly owned versioned native registration updates both architecture paths");
                WindowsIntegration.SetEnabled(false);
                using(var current=isolated.OpenSubKey(@"Software\Classes\Directory\shell"))check((string)current.GetValue("")=="previous-manager"&&!File.Exists(WindowsIntegration.RecoveryPath),"Disabling after upgrade restores the real original manager");
                WindowsIntegration.SetEnabled(true);
                using(var current=isolated.OpenSubKey(@"Software\Classes\Directory\shell",true))current.SetValue("","another-manager");
                WindowsIntegration.UpgradeOwnedRegistration();
                using(var current=isolated.OpenSubKey(@"Software\Classes\Directory\shell"))check((string)current.GetValue("")=="another-manager","An update never overwrites a later default manager");
                WindowsIntegration.SetEnabled(false);
            }
            finally{WindowsIntegration.VerificationRoot=null;}
        }
        Registry.CurrentUser.DeleteSubKeyTree(registryPath);
        string payload=Directory.CreateDirectory(Path.Combine(root,"source","payload")).FullName;
        Directory.CreateDirectory(Path.Combine(payload,"nested"));
        File.WriteAllText(Path.Combine(payload,"one.txt"),"first archive payload");File.WriteAllText(Path.Combine(payload,"nested","two.txt"),"nested archive payload");
        foreach(string extension in new[]{".zip",".7z"})
        {
            string archive=Path.Combine(root,"drag"+extension);
            await Task.Run(()=>Archive.Create(archive,Archive.Files(new[]{payload},"")));
            string original=await Task.Run(()=>Archive.Fingerprint(archive));
            string target=Directory.CreateDirectory(Path.Combine(root,"destination"+extension)).FullName;
            await S1Verification.Navigate(Left,target);await S1Verification.Navigate(Right,archive);
            Right.ListForVerification.SelectedItem=Right.Items.Single(e=>e.Name=="payload");
            int leases=ArchiveContent.Retained().Count();
            var data=Right.CreateDragData();
            string virtualSource=Path.Combine(archive,"payload");
            check(ArchiveDragData.Files(data).SequenceEqual(new[]{virtualSource})&&ArchiveContent.Retained().Count()==leases,"Creating "+extension+" drag data exposes virtual identities without extracting on the UI thread");
            using var release=new ManualResetEventSlim(false);
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocker=Ferry.Submit(new[]{Path.Combine(payload,"one.txt")},target,false,_=>{entered.TrySetResult();release.Wait();});
            await entered.Task;
            Job job=null;
            try
            {
                var constructor=typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single();
                var drop=(DragEventArgs)constructor.Invoke(new object[]{data,DragDropKeyStates.None,DragDropEffects.Copy,Left.ListForVerification,new Point(5,5)});
                drop.RoutedEvent=DragDrop.DropEvent;Left.ListForVerification.RaiseEvent(drop);
                job=jobs.First();
                await Dispatcher.InvokeAsync(()=>UpdateLayout(),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                check(job.ArchiveOperation&&job.Sources.SequenceEqual(new[]{virtualSource})&&!job.IsMove&&!job.Completion.IsCompleted&&ArchiveContent.Retained().Count()==leases,"The actual "+extension+" pane Drop returns to the dispatcher and queues archive work without temporary export or a delayed native drop");
            }
            finally{release.Set();await blocker.Completion;}
            await job.Completion;
            check(job.Successful&&File.ReadAllText(Path.Combine(target,"payload","one.txt"))=="first archive payload"&&File.ReadAllText(Path.Combine(target,"payload","nested","two.txt"))=="nested archive payload"&&Archive.Fingerprint(archive)==original,"The real "+extension+" drag-to-folder transfer completes through the shared queue with exact contents and an unchanged archive");
            // Exercise the actual native data adapter without clipboard writes,
            // a desktop drag loop, pointer input or foreground activation.
            var format=new FORMATETC{cfFormat=(short)DataFormats.GetDataFormat(DataFormats.FileDrop).Id,dwAspect=DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=TYMED.TYMED_HGLOBAL};
            ((System.Runtime.InteropServices.ComTypes.IDataObject)data).GetData(ref format,out var medium);
            try
            {
                var path=new StringBuilder(32768);DragQueryFileW(medium.unionmember,0,path,(uint)path.Capacity);
                check(DragQueryFileW(medium.unionmember,uint.MaxValue,null,0)==1&&File.ReadAllText(Path.Combine(path.ToString(),"one.txt"))=="first archive payload","An external OLE consumer can still obtain real CF_HDROP "+extension+" content on demand");
                _=data.GetData(DataFormats.FileDrop);
                check(ArchiveContent.Retained().Count()==leases+1,"Repeated external data requests reuse the same materialized archive export");
            }
            finally{ReleaseStgMedium(ref medium);}
            foreach(var record in ArchiveContent.Retained().Where(r=>r.Source==archive&&r.Purpose=="drag export").ToArray())ArchiveContent.Release(record);
        }
        check(HiddenTrialPresentation.ForegroundViolations==0,"The complete archive drag and redirected Favorites regression uses zero desktop activation or input");
    }
}
#endif
