using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;

sealed partial class PackageProbe
{
    async Task VerifyUpdate138()
    {
        var downloads=Type("UpdateDownloads");
        string target=(string)Call(downloads,null,"Create",new Version(0,0,138));
        File.WriteAllText(target,"delivered installer ownership fixture");
        string record=Path.Combine(Path.GetDirectoryName(target),"download.json");
        var receipt=JsonNode.Parse(File.ReadAllText(record));receipt["Owner"]=0;File.WriteAllText(record,receipt.ToJsonString());
        var start=new ProcessStartInfo("cmd.exe","/d /q"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var setup=Process.Start(start))
        {
            try
            {
                Call(downloads,null,"Handoff",target,setup);
                var retiring=(Task)Call(downloads,null,"RetireAsync",CancellationToken.None);
                await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                Check(!retiring.IsCompleted&&File.Exists(target),"Delivered update retirement retains its exact live installer");
                await setup.StandardInput.WriteLineAsync("exit");await setup.WaitForExitAsync();await retiring;
                Check(!Directory.Exists(Path.GetDirectoryName(target)),"Delivered reopened app removes the setup payload and receipt after actual process exit");
            }
            finally{if(!setup.HasExited){await setup.StandardInput.WriteLineAsync("exit");await setup.WaitForExitAsync();}}
        }
        target=(string)Call(downloads,null,"Create",new Version(0,0,139));
        string original=target+".original";File.Move(target,original);File.WriteAllText(target,"replacement");
        Check(!(bool)Call(downloads,null,"Discard",target)&&File.Exists(target),"Delivered retirement preserves a replaced file identity");
        File.Delete(target);File.Move(original,target);Call(downloads,null,"Discard",target);
        var entry=Activator.CreateInstance(Type("SpaceEntry"));
        entry.GetType().GetField("State",Flags).SetValue(entry,Enum.Parse(Type("SpaceState"),"Changed"));
        entry.GetType().GetField("Refreshing",Flags).SetValue(entry,true);
        Check((string)Get(entry,"SpaceStatus")=="Updating…","Delivered queued changed snapshot says Updating rather than Incomplete");
        entry.GetType().GetField("State",Flags).SetValue(entry,Enum.Parse(Type("SpaceState"),"Incomplete"));
        Check((string)Get(entry,"SpaceStatus")=="Partially measured","Delivered terminal partial coverage remains distinct from active work");
    }
}
