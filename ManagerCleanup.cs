using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace FileExplorer;

// Cache actions use the manager's own command. Package stores, installed
// versions and WinGet's user-selected download directory are not cache targets.
internal static class ManagerCleanup
{
    internal sealed record Review(string Id,string Executable,string Root,string[] Prefix,int Count,string Details,bool Supported,long? Bytes=null);
    internal static readonly (string Id,string Name,string Description)[] Managers=
    [
        ("winget","WinGet","No generic cache cleanup: installer downloads may be user files"),
        ("chocolatey","Chocolatey expired HTTP cache","Remove only expired user HTTP-cache entries through Chocolatey"),
        ("npm","Npm package cache","Clear Npm's regenerable package cache; future installs may download again"),
        ("pip","Pip download and wheel cache","Clear Pip's regenerable HTTP and wheel caches; future installs may download again"),
        ("cargo","Cargo","Cargo owns global-cache aging; no stable manual global cleanup command is available"),
        ("vcpkg","vcpkg","Shared downloads and binary archives require project consumer review"),
        ("dotnet-tool","NuGet HTTP cache","Clear only NuGet's HTTP cache; installed tools and global packages remain"),
        ("winps","PowerShell 5.x","Installed modules are packages, not a disposable shared cache"),
        ("pwsh","PowerShell 7.x","Installed modules are packages, not a disposable shared cache"),
        ("installer","Installer packages","Windows Installer's managed package cache must remain available for repair and uninstall"),
        ("bun","Bun","Bun's cache can contain a linked global store used by active projects")
    ];
    internal static bool IsManager(CleanupTarget target)=>target.Id.StartsWith("manager-",StringComparison.Ordinal);
    internal static IEnumerable<(string Id,string Name,string Description)> AvailableActions()
    {
        foreach(var manager in Managers)
        {
            string executable=manager.Id switch
            {
                "chocolatey"=>"choco.exe", "npm"=>"node.exe",
                "pip"=>"python.exe", "dotnet-tool"=>"dotnet.exe", _=>""
            };
            if(executable.Length==0)continue;
            if(manager.Id=="chocolatey"&&Elevated())continue;
            if(!CanFind(executable))continue;
            if(manager.Id=="npm"&&!CanFindNpmCli())continue;
            yield return manager;
        }
    }
    static bool CanFind(string executable)
    {
        try{Find(executable);return true;}
        catch(IOException){return false;}
    }
    static bool CanFindNpmCli()
    {
        try{NpmCli(Find("node.exe"));return true;}
        catch(IOException){return false;}
    }
    static bool Elevated()=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
#if DEBUG
    internal static Func<string,string> FindForVerification;
    internal static Func<string,string,string,string> RunForVerification;
    internal static string OwnedRootForVerification;
#endif
    static string Find(string name)
    {
#if DEBUG
        if(FindForVerification is {} find)return find(name);
#endif
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            if(string.IsNullOrWhiteSpace(folder))continue;
            try{string path=Path.GetFullPath(Path.Combine(folder.Trim('"'),name));if(File.Exists(path))return path;}
            catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException){}
        }
        if(name.Equals("python.exe",StringComparison.OrdinalIgnoreCase))
        {
            string scoopPython=Path.Combine(ScoopCleanup.Locations().Root,"apps","python","current","python.exe");
            if(File.Exists(scoopPython))return Path.GetFullPath(scoopPython);
        }
        throw new IOException(name+" is not installed on the application PATH");
    }
    static string Run(string manager,string action,CancellationToken cancel,string root="")
    {
        cancel.ThrowIfCancellationRequested();
#if DEBUG
        if(RunForVerification is {} run)return run(manager,action,root);
#endif
        var launch=new ProcessStartInfo(Program.Executable)
        {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true
        };
        foreach(string arg in new[]{"--manager-clean-worker",manager,action})launch.ArgumentList.Add(arg);
        if(root.Length>0)launch.ArgumentList.Add(root);
        var result=MaintenanceProcess.Run(launch,cancel).GetAwaiter().GetResult();
        if(result.ExitCode!=0)throw new IOException(manager+" cache command failed: "+(string.IsNullOrWhiteSpace(result.Error)?result.Output:result.Error).Trim());
        return result.Output.Trim();
    }
    internal static async Task<int> RunWorker(string manager,string action,string root)
    {
        if(Console.ReadLine()!="owned")return 2;
        try
        {
            string executable;
            string[] args;
            switch((manager,action))
            {
                case("chocolatey","list"):
                    executable=Find("choco.exe");args=["cache","list","--limit-output","--no-color"];break;
                case("chocolatey","clean"):
                    if(Elevated())throw new InvalidDataException("Chocolatey's elevated cache command also changes the system cache");
                    executable=Find("choco.exe");args=["cache","remove","--expired","--limit-output","--no-color"];break;
                case("pip","location"):
                    executable=Find("python.exe");args=["-m","pip","cache","dir"];break;
                case("pip","info"):
                    executable=Find("python.exe");args=["-m","pip","cache","info"];break;
                case("pip","clean"):
                    executable=Find("python.exe");args=["-m","pip","--cache-dir",Root(root),"cache","purge"];break;
                case("dotnet-tool","location"):
                    executable=Find("dotnet.exe");args=["nuget","locals","http-cache","--list","--force-english-output"];break;
                case("dotnet-tool","clean"):
                    executable=Find("dotnet.exe");args=["nuget","locals","http-cache","--clear","--force-english-output"];break;
                case("npm","location"):
                case("npm","list"):
                case("npm","clean"):
                {
                    executable=Find("node.exe");string cli=NpmCli(executable);
                    args=action switch{"location"=>[cli,"config","get","cache"],"list"=>[cli,"cache","ls"],_=>[cli,"cache","clean","--force","--cache",Root(root)]};break;
                }
                default:throw new InvalidDataException("Unsupported package-manager cache action");
            }
            var launch=new ProcessStartInfo(executable)
            {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in args)launch.ArgumentList.Add(arg);
            using var process=Process.Start(launch)??throw new IOException("Package-manager command did not start");
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Console.Out.Write(await output);Console.Error.Write(await error);
            return process.ExitCode;
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {Console.Error.WriteLine(error.Message);return 1;}
    }
    static string NpmCli(string node)
    {
        string cli=Path.Combine(Path.GetDirectoryName(node)!,"node_modules","npm","bin","npm-cli.js");
        if(!File.Exists(cli))throw new IOException("Npm's CLI is not beside the selected Node executable");
        return cli;
    }
    static string Root(string value)
    {
        string supplied=value.Trim().Trim('"');
        if(!Path.IsPathFullyQualified(supplied))throw new InvalidDataException("Package cache root is not absolute");
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(supplied));
        string user=Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        string[] protectedRoots=[
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"\\Downloads"];
        bool privateUserCache=path.StartsWith(user+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
#if DEBUG
        if(OwnedRootForVerification is {} owned&&path.StartsWith(Path.TrimEndingDirectorySeparator(owned)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))privateUserCache=true;
#endif
        bool protectedPath=protectedRoots.Any(value=>
        {
            string root=Path.TrimEndingDirectorySeparator(value);
            return path.Equals(root,StringComparison.OrdinalIgnoreCase)||
                path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
                root.StartsWith(path+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        });
        if(!privateUserCache||protectedPath)
            throw new InvalidDataException("Package cache is outside a private user cache folder or is under a protected folder");
        for(string current=path;current!=Path.GetPathRoot(current);current=Path.GetDirectoryName(current)!)
            if(Directory.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Package cache path contains a link: "+current);
        return path;
    }
    static (int Files,long Bytes) CacheFiles(string root,CancellationToken cancel)
    {
        if(!Directory.Exists(root))return(0,0);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Package cache root is a link: "+root);
        int files=0;long bytes=0;
        var pending=new Stack<string>();pending.Push(root);
        while(pending.TryPop(out string folder))
        {
            cancel.ThrowIfCancellationRequested();
            foreach(string entry in Directory.EnumerateFileSystemEntries(folder))
            {
                cancel.ThrowIfCancellationRequested();
                var attributes=File.GetAttributes(entry);
                if((attributes&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Package cache contains a link: "+entry);
                if((attributes&FileAttributes.Directory)!=0)pending.Push(entry);
                else{files=checked(files+1);bytes=checked(bytes+new FileInfo(entry).Length);}
            }
        }
        return(files,bytes);
    }
    internal static Review Inspect(CleanupTarget target,CancellationToken cancel)
    {
        string id=target.Id["manager-".Length..];
        var manager=Managers.Single(m=>m.Id==id);
        if(id is not ("chocolatey" or "npm" or "pip" or "dotnet-tool"))
            return new(id,"","",[],0,manager.Description,false);
        cancel.ThrowIfCancellationRequested();
        if(id=="chocolatey")
        {
            if(Elevated())return new(id,"","",[],0,"Run Pathweft without elevation to review Chocolatey's user HTTP cache; elevated Chocolatey also touches the system cache",false);
            string exe=Find("choco.exe"),output=Run(id,"list",cancel);
            var match=Regex.Match(output,@"where\s+(\d+)\s+items\s+have\s+expired",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if(!match.Success)throw new InvalidDataException("Chocolatey did not report an expired-cache count");
            int count=int.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
            return new(id,exe,"user HTTP cache",[],count,"Expired downloads; installed packages stay.\nChocolatey selects the expired user-cache entries; future installs may download again.",true);
        }
        if(id=="pip")
        {
            string exe=Find("python.exe"),root=Root(Run(id,"location",cancel));
            var cache=CacheFiles(root,cancel);
            string info=Run(id,"info",cancel);
            var http=Regex.Match(info,@"Number of HTTP files:\s*(\d+)",RegexOptions.CultureInvariant);
            var wheels=Regex.Match(info,@"Number of locally built wheels:\s*(\d+)",RegexOptions.CultureInvariant);
            if(!http.Success||!wheels.Success)throw new InvalidDataException("Pip did not report cache entry counts");
            int count=checked(int.Parse(http.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)+int.Parse(wheels.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture));
            return new(id,exe,root,["-m","pip"],count,"Cached downloads and wheels; future installs may download again.\n"+root,true,cache.Bytes);
        }
        if(id=="dotnet-tool")
        {
            string exe=Find("dotnet.exe"),output=Run(id,"location",cancel);
            var match=Regex.Match(output,@"(?m)^http-cache:\s*(.+)$",RegexOptions.CultureInvariant);
            if(!match.Success)throw new InvalidDataException("NuGet did not report its HTTP-cache location");
            string root=Root(match.Groups[1].Value);var cache=CacheFiles(root,cancel);
            return new(id,exe,root,[],cache.Files,"Cached HTTP responses; installed tools and packages stay.\n"+root,true,cache.Bytes);
        }
        string node=Find("node.exe");
        string cli=NpmCli(node);
        string npmRoot=Root(Run(id,"location",cancel));
        // Npm's flat cache option and `cache clean --force` target _cacache.
        // Its own read commands write sibling _logs, and _npx is a separate
        // store. Neither belongs in this review's size or change comparison.
        var npmCache=CacheFiles(Path.Combine(npmRoot,"_cacache"),cancel);
        int entries=Run(id,"list",cancel).Split('\n',StringSplitOptions.RemoveEmptyEntries).Length;
        return new(id,node,npmRoot,[cli],entries,"Cached packages; future installs may download again.\n"+npmRoot,true,npmCache.Bytes);
    }
    internal static CleanupOutcome Clean(Review reviewed,CleanupTarget target,CancellationToken cancel,Action<CleanupProgress> progress)
    {
        if(!reviewed.Supported||reviewed.Count==0)throw new InvalidOperationException("No reviewed manager cache is available");
        progress?.Invoke(new("Checking package cache",reviewed.Root,0,reviewed.Count,0,0,0));
        bool started=false;
        try
        {
            var current=Inspect(target,cancel);
            if(!Same(current,reviewed))throw new IOException("Package-manager cache changed; Cleanup will review it again");
            progress?.Invoke(new("Cleaning package cache",reviewed.Root,0,reviewed.Count,0,0,0));
            started=true;
            string result;
            try{result=Run(reviewed.Id,"clean",cancel,reviewed.Root is "user HTTP cache"?"":reviewed.Root);}
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {return new(0,reviewed.Count,0,false,"The manager command failed and the cache may be partially cleared. Cleanup will review it again. "+error.Message,true);}
            try
            {
                var remaining=Inspect(target,cancel);
                int removed=Math.Max(0,reviewed.Count-remaining.Count),skipped=remaining.Count;
                long bytes=Math.Max(0,reviewed.Bytes.GetValueOrDefault()-remaining.Bytes.GetValueOrDefault());
                progress?.Invoke(new("Cleaning package cache",reviewed.Root,reviewed.Count,reviewed.Count,removed,skipped,bytes));
                return new(removed,skipped,bytes,false,result+(skipped>0?"\nCache entries remain; Cleanup will review them again.":""));
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                return new(0,reviewed.Count,0,false,"The manager command completed, but cache readback failed: "+error.Message+"\n"+result,true);
            }
        }
        catch(OperationCanceledException)when(cancel.IsCancellationRequested)
        {return new(0,0,0,true,started?"The manager cache may be partially cleared; Cleanup will review it when reopened.":"");}
    }
    static bool Same(Review a,Review b)=>a.Id==b.Id&&a.Executable==b.Executable&&a.Root==b.Root&&a.Count==b.Count&&a.Bytes==b.Bytes&&a.Supported==b.Supported&&a.Prefix.SequenceEqual(b.Prefix);
}
