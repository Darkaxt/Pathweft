#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyStorageLocation1111(string root,Action<bool,string> check)
    {
        string trial=Directory.CreateDirectory(Path.Combine(root,"location-move")).FullName;
        string settings=Directory.CreateDirectory(Path.Combine(trial,"settings")).FullName;
        string bulk=Directory.CreateDirectory(Path.Combine(trial,"bulk")).FullName;
        string anchor=Directory.CreateDirectory(Path.Combine(trial,"bootstrap")).FullName;
        string pointer=Path.Combine(anchor,"location.txt"),target=Path.Combine(trial,"selected");
        File.WriteAllText(pointer,settings);File.WriteAllText(Path.Combine(settings,"settings.txt"),"theme.native=1\nwine=0\n");
        File.WriteAllText(Path.Combine(settings,"quickaccess.txt"),"D:\\Projects\n");
        Directory.CreateDirectory(Path.Combine(bulk,"Cache"));
        byte[] payload=Enumerable.Range(0,131072).Select(i=>(byte)(i%251)).ToArray();
        File.WriteAllBytes(Path.Combine(bulk,"Cache","payload.bin"),payload);
        var request=new StorageLocation.Request(settings,bulk,target,Environment.ProcessId,0);
        string busy=Path.Combine(bulk,"locked.bin");File.WriteAllText(busy,"Must survive failed migration");
        using(var held=File.Open(busy,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            bool refused=false;try{StorageLocation.Move(request,pointer);}catch(IOException){refused=true;}
            check(refused&&File.ReadAllText(pointer)==settings&&File.Exists(Path.Combine(settings,"settings.txt"))&&File.ReadAllBytes(Path.Combine(bulk,"Cache","payload.bin")).SequenceEqual(payload),"Failed relocation preserves its startup binding and source settings/cache");
        }
        check(!Directory.Exists(target)||!Directory.EnumerateFileSystemEntries(target).Any(),"Failed relocation discards only the newly created destination copy");
        StorageLocation.Move(request,pointer);
        check(File.ReadAllLines(pointer).Single()==target&&File.ReadAllText(Path.Combine(target,"quickaccess.txt"))=="D:\\Projects\n"&&File.ReadAllBytes(Path.Combine(target,"Runtime","Cache","payload.bin")).SequenceEqual(payload),"Verified relocation preserves real settings and cache bytes before switching the startup pointer");
        check(!Directory.Exists(settings)&&!Directory.Exists(bulk)&&File.ReadAllText(Path.Combine(target,"data-root.txt"))==Path.Combine(target,"Runtime"),"Successful relocation removes the verified old members and binds bulk data below the chosen profile");
        string again=Path.Combine(trial,"selected-again");
        StorageLocation.Move(new(target,Path.Combine(target,"Runtime"),again,0,0),pointer);
        check(File.ReadAllBytes(Path.Combine(again,"Runtime","Cache","payload.bin")).SequenceEqual(payload)&&!Directory.Exists(target),"A subsequent move handles nested runtime data once and preserves its content");
        string occupied=Directory.CreateDirectory(Path.Combine(trial,"occupied")).FullName;File.WriteAllText(Path.Combine(occupied,"existing.txt"),"Keep me");
        bool overlap=false,nonempty=false;
        try{StorageLocation.Validate(again,Path.Combine(again,"Runtime"),Path.Combine(again,"child"));}catch(IOException){overlap=true;}
        try{StorageLocation.Validate(again,Path.Combine(again,"Runtime"),occupied);}catch(IOException){nonempty=true;}
        check(overlap&&nonempty&&File.ReadAllText(Path.Combine(occupied,"existing.txt"))=="Keep me","Overlapping and nonempty destinations are refused without changing user files");
        string workerRoot=Directory.CreateDirectory(Path.Combine(root,"storage-worker")).FullName;
        string workerProfile=Directory.CreateDirectory(Path.Combine(workerRoot,"profile")).FullName;
        string workerBulk=Directory.CreateDirectory(Path.Combine(workerProfile,"Runtime")).FullName;
        File.WriteAllText(Path.Combine(workerProfile,"settings.txt"),"wine=0\ntray=0\n");File.WriteAllText(Path.Combine(workerBulk,"cache.txt"),"Worker fixture");
        string workerTarget=Path.Combine(workerRoot,"moved"),requestPath=Path.Combine(workerProfile,"storage-move-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(requestPath,System.Text.Json.JsonSerializer.Serialize(new StorageLocation.Request(workerProfile,workerBulk,workerTarget,0,0)));
        string channel="Pathweft-StorageTrial-"+Guid.NewGuid().ToString("N");
        using(var pipe=new System.IO.Pipes.NamedPipeServerStream(channel,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous|System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden};
            start.Environment["PATHWEFT_PROFILE"]=workerProfile;start.ArgumentList.Add("--storage-parent="+requestPath);start.ArgumentList.Add("--storage-parent-pipe="+channel);
            using var parent=System.Diagnostics.Process.Start(start);await pipe.WaitForConnectionAsync();
            using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,1024,true);using var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),1024,true){AutoFlush=true};
            using var worker=System.Diagnostics.Process.GetProcessById(int.Parse(await reader.ReadLineAsync()));
            check(!worker.HasExited&&!File.Exists(Path.Combine(workerRoot,"location.txt")),"The real relocation worker waits while its exact requesting parent remains alive");
            await writer.WriteLineAsync("exit");await parent.WaitForExitAsync();await worker.WaitForExitAsync();
            check(parent.ExitCode==0&&worker.ExitCode==0&&File.ReadAllLines(Path.Combine(workerRoot,"location.txt")).Single()==workerTarget&&File.ReadAllText(Path.Combine(workerTarget,"Runtime","cache.txt"))=="Worker fixture","Actual parent exit releases the original worker, which verifies/moves data and commits its pointer successfully");
        }
        double priorScale=Settings.TextScale;string mode=Settings.NativeMode;
        try
        {
            foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
            {
                Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();
                var window=new SettingsWindow(this);window.Show();
                try
                {
                    window.SelectStorage();await Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ApplicationIdle);
                    var button=(Button)window.FindName("ChangeDataLocation");
                    check(button.IsVisible&&button.ActualWidth>=100&&((TextBlock)window.FindName("DataLocation")).Text==DataFolder.Bulk,"Storage location and Change location action render in the selected category: "+theme+scale);
                    string selected=null;window.ChooseStorageForVerification=()=>Path.Combine(trial,"ui-selected");window.MoveStorageForVerification=path=>selected=path;
                    check(!button.IsEnabled,"The isolated profile prevents live location changes");button.IsEnabled=true;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(selected==Path.Combine(trial,"ui-selected"),"The actual Change location click validates and submits the chosen directory: "+theme+scale);
                    var scroll=(ScrollViewer)window.FindName("CategoryScroll");
                    check(scroll.ViewportWidth>=300&&button.ActualHeight>20,"Storage controls remain within the content viewport: "+theme+scale);
                    await S1Verification.Snapshot(window,root,$"storage-{theme}-{scale*100:0}");
                }
                finally{window.Close();}
            }
        }
        finally{Settings.TextScale=priorScale;Settings.NativeMode=mode;ApplyAppearance();}
    }
}
#endif
