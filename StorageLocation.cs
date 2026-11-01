using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileExplorer;

// A user-selected profile move. The worker waits for the exact old app to exit,
// verifies all copied bytes, then switches one small startup pointer atomically.
static class StorageLocation
{
    internal sealed record Request(string Settings, string Bulk, string Destination, int Pid, long Started);
    internal sealed record Member(string Source, string Destination, string Hash);
    internal static string ResultPath => Path.Combine(Settings.Dir, "storage-result.json");
    internal sealed record Result(bool Success, string Message);
    internal static string Blocker(MainWindow window) => Settings.Profile != null || Settings.Portable != null
        ? "Portable and test copies keep their data beside the application."
        : window.UpdateInstallationBlocker ?? (ArchiveContent.Retained().Any() ? "Close and release archive working copies before moving their location." : null);

    internal static void Validate(string settings, string bulk, string destination)
    {
        destination=Path.GetFullPath(destination).TrimEnd('\\');
        if(!Path.IsPathFullyQualified(destination)||destination.Length<=3)throw new IOException("Choose a folder on a data drive.");
        if(Path.GetPathRoot(destination).Equals(Path.GetPathRoot(Environment.SystemDirectory),StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a data drive rather than the Windows drive.");
        foreach(string source in new[]{settings,bulk})
        {
            string path=Path.GetFullPath(source).TrimEnd('\\');
            if(destination.Equals(path,StringComparison.OrdinalIgnoreCase)||destination.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase)||path.StartsWith(destination+"\\",StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a location outside the current data folders.");
        }
        if(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any())throw new IOException("Choose an empty folder so existing files are preserved.");
        for(var parent=new DirectoryInfo(destination);parent!=null;parent=parent.Parent)
            if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a direct folder rather than a linked location.");
    }

    internal static void Start(MainWindow window,string destination)
    {
        if(Blocker(window) is { } blocker)throw new IOException(blocker);
        Validate(Settings.Dir,DataFolder.Bulk,destination);
        using var caller=Process.GetCurrentProcess();
        var request=new Request(Settings.Dir,DataFolder.Bulk,Path.GetFullPath(destination),caller.Id,caller.StartTime.ToUniversalTime().Ticks);
        string path=Path.Combine(Settings.Dir,"storage-move-"+Guid.NewGuid().ToString("N")+".json");
        Directory.CreateDirectory(Settings.Dir);File.WriteAllText(path,JsonSerializer.Serialize(request));
        var start=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        start.ArgumentList.Add("--relocate-profile="+path);
        using var worker=Process.Start(start)??throw new IOException("The storage move could not start.");
        window.Quit();
    }

    internal static int Run(string requestPath)
    {
        string source=Settings.Dir;
        bool trial=false;string pointer=DataFolder.LocationFile;
#if DEBUG
        trial=Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_STORAGE_WORKER")=="1"&&Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s8-delivery\appearance-",StringComparison.OrdinalIgnoreCase)==true;
        if(trial)pointer=Path.Combine(Path.GetDirectoryName(source),"location.txt");
#endif
        if((Settings.Profile!=null||Settings.Portable!=null)&&!trial)return 1;
        try
        {
            string full=Path.GetFullPath(requestPath);
            if(Path.GetDirectoryName(full)!=source||!Path.GetFileName(full).StartsWith("storage-move-",StringComparison.Ordinal)||Path.GetExtension(full)!=".json")throw new IOException("Invalid storage move request.");
            var request=JsonSerializer.Deserialize<Request>(File.ReadAllText(full))??throw new IOException("Missing storage move request.");
            if(!request.Settings.Equals(source,StringComparison.OrdinalIgnoreCase)||!request.Bulk.Equals(DataFolder.Bulk,StringComparison.OrdinalIgnoreCase))throw new IOException("The current storage binding changed.");
            try
            {
                using var parent=Process.GetProcessById(request.Pid);
                if(parent.StartTime.ToUniversalTime().Ticks==request.Started)parent.WaitForExit();
            }
            catch(ArgumentException) { } // The exact requesting process already exited.
            if(Program.Running)throw new IOException("Another Pathweft window is using this profile.");
            if(!trial)Agent.StopForUpdate();
            Move(request,pointer,full);
            if(trial)return 0;
            if(DesktopLaunch.FromSetup(true)!=0)throw new IOException("Data moved, but Windows integration could not restart. Check Windows integration settings.");
            DesktopLaunch.Start("--front --storage-result");return 0;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or JsonException)
        {
            string current=Settings.Profile!=null||Settings.Portable!=null?Settings.Dir:DataFolder.Current;
            Directory.CreateDirectory(current);File.WriteAllText(Path.Combine(current,"storage-result.json"),JsonSerializer.Serialize(new Result(false,error.Message)));
            if(trial)return 1;
            try{_ = DesktopLaunch.FromSetup(true);DesktopLaunch.Start("--front --storage-result");}catch(Exception restart)when(restart is IOException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception){ }
            return 1;
        }
    }
#if DEBUG
    internal static void StartVerificationParent(string path,string pipeName)
    {
        var request=JsonSerializer.Deserialize<Request>(File.ReadAllText(path));
        if(!Path.GetFullPath(path).StartsWith(@"D:\Temp\pathweft-s8-delivery\appearance-",StringComparison.OrdinalIgnoreCase)||!pipeName.StartsWith("Pathweft-StorageTrial-",StringComparison.Ordinal))throw new ArgumentException("Owned storage fixture required.");
        using var self=Process.GetCurrentProcess();request=request with{Pid=self.Id,Started=self.StartTime.ToUniversalTime().Ticks};File.WriteAllText(path,JsonSerializer.Serialize(request));
        var start=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        start.ArgumentList.Add("--relocate-profile="+path);start.Environment["PATHWEFT_VERIFY_STORAGE_WORKER"]="1";
        using var worker=Process.Start(start);
        using var pipe=new System.IO.Pipes.NamedPipeClientStream(".",pipeName,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.CurrentUserOnly);pipe.Connect();
        using var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),1024,true){AutoFlush=true};writer.WriteLine(worker.Id);
        using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,1024,true);if(reader.ReadLine()!="exit")throw new IOException("Fixture release missing.");
    }
#endif

    internal static void Move(Request request,string pointer,string requestPath=null)
    {
        Validate(request.Settings,request.Bulk,request.Destination);
        string target=Path.GetFullPath(request.Destination),runtime=Path.Combine(target,"Runtime");
        var members=new List<Member>();var created=new List<Member>();bool committed=false;
        static bool Under(string path,string root)=>path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
        string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
        var routing=new[]{Path.Combine(request.Settings,"data-root.txt"),requestPath}.Where(p=>p!=null&&File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>(Path:p,Hash:Hash(p))).ToArray();
        try
        {
            Directory.CreateDirectory(target);
            var acl=new DirectoryInfo(request.Settings).GetAccessControl();acl.SetAccessRuleProtection(true,true);new DirectoryInfo(target).SetAccessControl(acl);
            void Copy(string from,string to,bool settings)
            {
                if(!Directory.Exists(from))return;
                if((File.GetAttributes(from)&FileAttributes.ReparsePoint)!=0)throw new IOException("The current data folder is linked; its binding was preserved.");
                foreach(string path in Directory.EnumerateFileSystemEntries(from))
                {
                    if(settings&&(Under(path,request.Bulk)||path.Equals(pointer,StringComparison.OrdinalIgnoreCase)||path.Equals(requestPath,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)=="data-root.txt"))continue;
                    if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked data was preserved; choose a direct profile folder.");
                    string destination=Path.Combine(to,Path.GetFileName(path));
                    if(Directory.Exists(path)){Directory.CreateDirectory(destination);Copy(path,destination,settings);continue;}
                    using(var input=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                    {
                        string digest=Convert.ToHexString(SHA256.HashData(input));input.Position=0;
                        bool ownsOutput=false;
                        try{using(var output=File.Open(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){ownsOutput=true;input.CopyTo(output);}}
                        catch{if(ownsOutput&&File.Exists(destination))File.Delete(destination);throw;}
                        var member=new Member(path,destination,digest);created.Add(member);
                        if(Hash(destination)!=digest)throw new IOException("Copied data verification failed; the original location was preserved.");
                        members.Add(member);
                    }
                }
            }
            Copy(request.Settings,target,true);Directory.CreateDirectory(runtime);Copy(request.Bulk,runtime,false);
            foreach(var file in members)if(Hash(file.Source)!=file.Hash)throw new IOException("Source data changed during the move; the original location was preserved.");
            File.WriteAllText(Path.Combine(target,"data-root.txt"),runtime);
            created.Add(new(null,Path.Combine(target,"data-root.txt"),Hash(Path.Combine(target,"data-root.txt"))));
            Settings.WriteAllLines(pointer,new[]{target});committed=true;
            int residuals=0;
            foreach(var file in members)
            {
                try{if(Hash(file.Source)==file.Hash)File.Delete(file.Source);else residuals++;}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException){residuals++;}
            }
            // Generated routing/request files are replaced by the committed pointer.
            foreach(var file in routing)
                try{if(File.Exists(file.Path)){if(Hash(file.Path)==file.Hash)File.Delete(file.Path);else residuals++;}}catch(Exception error)when(error is IOException or UnauthorizedAccessException){residuals++;}
            foreach(string root in new[]{request.Bulk,request.Settings}.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!Directory.Exists(root))continue;
                foreach(string directory in Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length).Append(root))
                    try{if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){residuals++;}
            }
            File.WriteAllText(Path.Combine(target,"storage-result.json"),JsonSerializer.Serialize(new Result(true,residuals==0?"Data moved successfully.":"Data moved. Some old files are still in use and were preserved.")));
        }
        catch
        {
            if(!committed)
            {
                foreach(var file in created)if(File.Exists(file.Destination)&&Hash(file.Destination)==file.Hash)File.Delete(file.Destination);
                foreach(string directory in (Directory.Exists(target)?Directory.GetDirectories(target,"*",SearchOption.AllDirectories):Array.Empty<string>()).OrderByDescending(p=>p.Length).Append(target).Where(Directory.Exists))
                    if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
            }
            throw;
        }
    }
}
