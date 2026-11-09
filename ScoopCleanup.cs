using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using FileExplorer.Search;

namespace FileExplorer;

// Eligibility follows Scoop cleanup/Select-CurrentVersion, pinned in
// vendor/Scoop/source-manifest.json. Never execute package scripts or infer
// obsolescence from age. Missing/ambiguous installation metadata is preserved.
internal static class ScoopCleanup
{
    internal sealed record Group(string Folder,string Current,string Active,string Version,bool Cache);
    internal static (string Root,string Cache) Locations()
    {
        string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configRoot=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")??Path.Combine(profile,".config");
        string config=Path.Combine(configRoot,"scoop","config.json");
        string root=Environment.GetEnvironmentVariable("SCOOP"),cache=Environment.GetEnvironmentVariable("SCOOP_CACHE");
        try
        {
            if(File.Exists(config))
            {
                using var data=JsonDocument.Parse(File.ReadAllText(config));
                foreach(var value in data.RootElement.EnumerateObject())
                {
                    if(value.Value.ValueKind!=JsonValueKind.String)continue;
                    if(string.IsNullOrWhiteSpace(root)&&value.Name.Equals("root_path",StringComparison.OrdinalIgnoreCase))root=value.Value.GetString();
                    if(string.IsNullOrWhiteSpace(cache)&&value.Name.Equals("cache_path",StringComparison.OrdinalIgnoreCase))cache=value.Value.GetString();
                }
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){ }
        root=Full(string.IsNullOrWhiteSpace(root)?Path.Combine(profile,"scoop"):root);
        return(root,Full(string.IsNullOrWhiteSpace(cache)?Path.Combine(root,"cache"):cache));
    }
    static string Full(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
    internal static bool IsScoop(CleanupTarget target)=>target.Id is "scoop-versions" or "scoop-cache";
    internal static (Dictionary<string,long> Bytes,bool Indexed) VersionSizes(IReadOnlyList<Group> groups,CancellationToken cancel)
    {
#if DEBUG
        if(VersionSizesForVerification!=null)return VersionSizesForVerification(groups,cancel);
#endif
        var sizes=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        if(groups.Count==0)return(sizes,false);
        try
        {
            sizes=EverythingSearchClient.Shared.FolderSizesAsync(groups.Select(g=>g.Folder),"",cancel).GetAwaiter().GetResult();
        }
        catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception){ }
        cancel.ThrowIfCancellationRequested();
        // Eligibility is metadata-based, independent of index coverage. A size
        // estimate must never turn into a recursive package scan.
        return(sizes,sizes.Count==groups.Count);
    }
    internal static IEnumerable<Group> Candidates(CleanupTarget target,CancellationToken cancel)
    {
        string apps=target.Id=="scoop-versions"?target.Root:Path.Combine(Locations().Root,"apps");
        if(!Directory.Exists(apps))yield break;
        foreach(string app in Directory.EnumerateDirectories(apps))
        {
            cancel.ThrowIfCancellationRequested();
            if(Path.GetFileName(app).Equals("scoop",StringComparison.OrdinalIgnoreCase)||Linked(app))continue;
            string current=Path.Combine(app,"current"),active;
            try{active=Active(current,app);}catch(Exception e)when(Expected(e)){continue;}
            if(active==null)continue;
            if(target.Id=="scoop-versions")foreach(string version in Directory.EnumerateDirectories(app))
            {
                if(Same(version,active)||Same(version,current)||Linked(version)||!Installed(version))continue;
                yield return new(version,current,active,Path.GetFileName(version),false);
            }
            if(target.Id=="scoop-cache")
                yield return new(target.Root,current,active,Path.GetFileName(active),true);
        }
    }
    internal static IEnumerable<string> CacheFiles(Group group)
    {
        string app=Path.GetFileName(Path.GetDirectoryName(group.Current));
        if(!Directory.Exists(group.Folder))yield break;
        foreach(string file in Directory.EnumerateFiles(group.Folder,app+"#*"))
        {
            string[] parts=Path.GetFileName(file).Split('#');
            if(parts.Length!=3||!parts[0].Equals(app,StringComparison.OrdinalIgnoreCase)||!Version.TryParse(parts[1],out var cached)||!Version.TryParse(group.Version,out var active)||cached>=active||
                parts.Any(string.IsNullOrWhiteSpace)||file.EndsWith(".download",StringComparison.OrdinalIgnoreCase))continue;
            yield return file;
        }
    }
    static bool Installed(string folder)
    {
        string install=Path.Combine(folder,"install.json"),manifest=Path.Combine(folder,"manifest.json");
        if(!File.Exists(install)){install=Path.Combine(folder,"scoop-install.json");manifest=Path.Combine(folder,"scoop-manifest.json");}
        try
        {
            if(!File.Exists(install)||!File.Exists(manifest)||new FileInfo(install).Length>1024*1024||new FileInfo(manifest).Length>1024*1024)return false;
            using var installation=JsonDocument.Parse(File.ReadAllText(install));using var definition=JsonDocument.Parse(File.ReadAllText(manifest));
            return installation.RootElement.ValueKind==JsonValueKind.Object&&installation.RootElement.EnumerateObject().Any()&&
                definition.RootElement.ValueKind==JsonValueKind.Object&&definition.RootElement.TryGetProperty("version",out var version)&&
                version.ValueKind==JsonValueKind.String&&string.Equals(version.GetString(),Path.GetFileName(folder),StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception e)when(Expected(e)){return false;}
    }
    static string Active(string current,string app)
    {
        if(!Linked(current))return null;
        string target=new DirectoryInfo(current).ResolveLinkTarget(false)?.FullName;
        if(target==null||!Same(Path.GetDirectoryName(target),app)||Linked(target)||!Installed(target))return null;
        return Full(target);
    }
    internal static bool PersistentLink(string path,Group group)
    {
        if(!Directory.Exists(path)||!Linked(path))return false;
        string app=Path.GetDirectoryName(group.Current),root=Path.GetDirectoryName(Path.GetDirectoryName(app));
        string persist=Path.Combine(root,"persist",Path.GetFileName(app));
        string destination=new DirectoryInfo(path).ResolveLinkTarget(false)?.FullName;
        return destination!=null&&(Same(destination,persist)||Below(destination,persist));
    }
    internal static void DeleteVersion(Group group,CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
#if DEBUG
        VersionDeleteStartedForVerification?.Invoke(group.Folder);
#endif
        // Unlink directory junction names before recursive removal. .NET's
        // recursive path calls DeleteVolumeMountPoint for junctions, requiring
        // volume-management access even for an ordinary user-owned Scoop link.
        // Non-recursive deletion removes the link itself, never its target.
        // Once this version starts, finish it before honoring cancellation;
        // stopping after unlinking a child would leave a partial version.
        var folders=new Stack<string>();folders.Push(group.Folder);
        while(folders.TryPop(out string folder))
        {
            foreach(string child in Directory.EnumerateDirectories(folder))
            {
                if(Linked(child))Directory.Delete(child,false);else folders.Push(child);
            }
        }
        Directory.Delete(group.Folder,true);
    }
#if DEBUG
    internal static Action ResourcesRegisteredForVerification;
    internal static Action<string> DeleteFailureForVerification;
    internal static Func<IReadOnlyList<Group>,CancellationToken,(Dictionary<string,long> Bytes,bool Indexed)> VersionSizesForVerification;
    internal static Action ConsumerQueryForVerification;
    internal static Action<string> VersionDeleteStartedForVerification;
#endif
    internal static IDisposable Guard(Group group,CancellationToken cancel=default,bool checkConsumers=true)
    {
        var handles=new List<SafeFileHandle>();uint session=0;bool started=false;
        try
        {
            cancel.ThrowIfCancellationRequested();
            // Hold the binding and all normal ancestors against rename/reparse
            // substitution through the entire reviewed group. Do not lock contents.
            string app=Path.GetDirectoryName(group.Current);
            for(string parent=app;parent!=null;parent=Path.GetDirectoryName(parent))
            {
                cancel.ThrowIfCancellationRequested();
                var handle=OpenDirectory(parent);handles.Add(handle);
                if(Linked(parent))throw new IOException("Scoop installation ancestor is a link");
            }
            handles.Add(OpenDirectory(group.Current));
            if(!Same(Active(group.Current,app),group.Active))throw new IOException("Scoop current version changed; analyze again");
            if(!group.Cache)
            {
                if(Linked(group.Folder)||!Installed(group.Folder))throw new IOException("Scoop version changed");
                if(!checkConsumers)return new Lease(handles,session,started);
                int code=RmStartSession(out session,0,new StringBuilder(33));
                if(code!=0)throw new IOException("Cannot check package consumers: "+code);started=true;
                var files=new List<string>(128);var folders=new Stack<string>();folders.Push(group.Folder);
                while(folders.TryPop(out var folder))
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach(string file in Directory.EnumerateFiles(folder))
                    {
                        cancel.ThrowIfCancellationRequested();
                        files.Add(file);
                        if(files.Count==128){Register();files.Clear();}
                    }
                    foreach(string child in Directory.EnumerateDirectories(folder)){cancel.ThrowIfCancellationRequested();if(!Linked(child))folders.Push(child);}
                }
                if(files.Count>0)Register();
                cancel.ThrowIfCancellationRequested();
                uint needed=0,count=0,reasons=0;code=RmGetList(session,out needed,ref count,IntPtr.Zero,ref reasons);
                if(code!=0||needed!=0)throw new IOException("Package is in use; preserved");
                void Register()
                {
                    cancel.ThrowIfCancellationRequested();
                    int result=RmRegisterResources(session,(uint)files.Count,files.ToArray(),0,IntPtr.Zero,0,IntPtr.Zero);if(result!=0)throw new IOException("Cannot check package consumers: "+result);
#if DEBUG
                    ResourcesRegisteredForVerification?.Invoke();
#endif
                    cancel.ThrowIfCancellationRequested();
                }
            }
            return new Lease(handles,session,started);
        }
        catch{foreach(var h in handles)h.Dispose();if(started)RmEndSession(session);throw;}
    }
    // One native consumer query for the usual case (no obsolete package in use).
    // Only a positive/error result is narrowed to individual groups. Reuse the
    // enumerated paths during narrowing, not a second recursive filesystem scan.
    internal static Dictionary<Group,string> Consumers(IReadOnlyList<Group> groups,CancellationToken cancel,Action<string> progress=null)
    {
        var blocked=new Dictionary<Group,string>();var resources=new List<(Group Group,List<string> Files)>();
        foreach(var group in groups)
        {
            cancel.ThrowIfCancellationRequested();progress?.Invoke(group.Folder);
            try
            {
                using var binding=Guard(group,cancel,checkConsumers:false);
                var files=new List<string>();var folders=new Stack<string>();folders.Push(group.Folder);
                while(folders.TryPop(out string folder))
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach(string file in Directory.EnumerateFiles(folder)){cancel.ThrowIfCancellationRequested();files.Add(file);}
                    foreach(string child in Directory.EnumerateDirectories(folder)){cancel.ThrowIfCancellationRequested();if(!Linked(child))folders.Push(child);}
                }
                resources.Add((group,files));
            }
            catch(Exception error)when(Expected(error)){blocked[group]=error.Message;}
        }
        Inspect(0,resources.Count);return blocked;
        void Inspect(int first,int length)
        {
            if(length==0)return;cancel.ThrowIfCancellationRequested();
            uint session=0;bool started=false;string reason=null;
            try
            {
                int code=RmStartSession(out session,0,new StringBuilder(33));
                if(code!=0)throw new IOException("Cannot check package consumers: "+code);started=true;
                var batch=new List<string>(4096);
                for(int i=first;i<first+length;i++)foreach(string file in resources[i].Files)
                {cancel.ThrowIfCancellationRequested();batch.Add(file);if(batch.Count==4096)Register();}
                if(batch.Count>0)Register();
                progress?.Invoke(length==1?resources[first].Group.Folder:"Checking Windows package consumers…");
#if DEBUG
                ConsumerQueryForVerification?.Invoke();
#endif
                uint count=0,reasons=0;code=RmGetList(session,out uint needed,ref count,IntPtr.Zero,ref reasons);
                cancel.ThrowIfCancellationRequested();
                if(code==0&&needed==0)return;
                reason=code is 0 or 234?"Package is in use; preserved":"Cannot check package consumers: "+code;
                void Register()
                {
                    int result=RmRegisterResources(session,(uint)batch.Count,batch.ToArray(),0,IntPtr.Zero,0,IntPtr.Zero);
                    if(result!=0)throw new IOException("Cannot check package consumers: "+result);batch.Clear();
#if DEBUG
                    ResourcesRegisteredForVerification?.Invoke();
#endif
                    cancel.ThrowIfCancellationRequested();
                }
            }
            catch(Exception error)when(Expected(error)){reason=error.Message;}
            finally{if(started)RmEndSession(session);}
            if(length==1){blocked[resources[first].Group]=reason;return;}
            int half=length/2;Inspect(first,half);Inspect(first+half,length-half);
        }
    }
    sealed class Lease(List<SafeFileHandle> handles,uint session,bool started):IDisposable
    {public void Dispose(){foreach(var h in handles)h.Dispose();if(started)RmEndSession(session);}}
    static SafeFileHandle OpenDirectory(string path)
    {
        var h=CreateFile(path,0x80,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        if(h.IsInvalid){h.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),path);}return h;
    }
    internal static bool Same(string a,string b)=>a!=null&&b!=null&&Full(a).Equals(Full(b),StringComparison.OrdinalIgnoreCase);
    internal static bool Below(string path,string root)=>Full(path).StartsWith(Full(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    static bool Linked(string path)=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    internal static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]static extern int RmStartSession(out uint session,uint flags,StringBuilder key);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]static extern int RmRegisterResources(uint session,uint count,string[] files,uint applications,IntPtr processes,uint services,IntPtr names);
    [DllImport("rstrtmgr.dll")]static extern int RmGetList(uint session,out uint needed,ref uint count,IntPtr info,ref uint reasons);
    [DllImport("rstrtmgr.dll")]static extern int RmEndSession(uint session);
}
