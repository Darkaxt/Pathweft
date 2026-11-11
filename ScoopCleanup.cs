using System.IO;
using System.Runtime.InteropServices;
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
    internal sealed class VersionInUseException(string message):IOException(message);
    internal static void DeleteVersion(Group group,CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
#if DEBUG
        VersionDeleteStartedForVerification?.Invoke(group.Folder);
#endif
        // A running program keeps its .exe open, so recursive removal would
        // stop midway through the package. Check only the executables, before
        // changing anything. A mapped image still admits exclusive reads, but
        // Windows refuses write access with a sharing violation while it runs.
        // The probe neither truncates nor writes. A ReadOnly .exe denies write
        // access regardless of use; removal below handles that attribute.
        var pending=new Stack<string>();pending.Push(group.Folder);
        while(pending.TryPop(out string folder))
        {
            foreach(string file in Directory.EnumerateFiles(folder,"*.exe"))
            {
                try{using var probe=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);}
                catch(IOException error)when((error.HResult&0xFFFF) is 32 or 33)
                {throw new VersionInUseException("In use: "+Path.GetFileName(file)+" is running. This version was preserved.");}
                catch(UnauthorizedAccessException){}
            }
            foreach(string child in Directory.EnumerateDirectories(folder))
                if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
        }
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
                var attributes=File.GetAttributes(child);
                if((attributes&FileAttributes.ReparsePoint)!=0)
                {
                    // Scoop persists user data through junctions whose link names
                    // can be ReadOnly. Clear only that link's bit, not its target.
                    if((attributes&FileAttributes.ReadOnly)!=0)File.SetAttributes(child,attributes&~FileAttributes.ReadOnly);
                    Directory.Delete(child,false);
                }
                else folders.Push(child);
            }
        }
        try{Directory.Delete(group.Folder,true);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {
            // Native recursive deletion can stop at an ordinary ReadOnly file.
            // Only after that actual failure, inspect the remaining package
            // tree and clear ReadOnly on its own regular members. Never change
            // attributes through a reparse point into persistent user data.
            bool changed=false;
            var remaining=new Stack<string>();remaining.Push(group.Folder);
            while(remaining.TryPop(out string folder))
            {
                cancel.ThrowIfCancellationRequested();
                var folderAttributes=File.GetAttributes(folder);
                if((folderAttributes&FileAttributes.ReparsePoint)!=0)continue;
                if((folderAttributes&FileAttributes.ReadOnly)!=0)
                {
                    File.SetAttributes(folder,folderAttributes&~FileAttributes.ReadOnly);
                    changed=true;
                }
                foreach(string file in Directory.EnumerateFiles(folder))
                {
                    cancel.ThrowIfCancellationRequested();
                    var attributes=File.GetAttributes(file);
                    if((attributes&(FileAttributes.ReparsePoint|FileAttributes.ReadOnly))==FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(file,attributes&~FileAttributes.ReadOnly);
                        changed=true;
                    }
                }
                foreach(string child in Directory.EnumerateDirectories(folder))
                    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)remaining.Push(child);
            }
            if(!changed)throw;
            Directory.Delete(group.Folder,true);
        }
    }
#if DEBUG
    internal static Action<string> DeleteFailureForVerification;
    internal static Func<IReadOnlyList<Group>,CancellationToken,(Dictionary<string,long> Bytes,bool Indexed)> VersionSizesForVerification;
    internal static Action<string> VersionDeleteStartedForVerification;
#endif
    internal static IDisposable Guard(Group group,CancellationToken cancel=default)
    {
        var handles=new List<SafeFileHandle>();
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
            }
            return new Lease(handles);
        }
        catch{foreach(var h in handles)h.Dispose();throw;}
    }
    sealed class Lease(List<SafeFileHandle> handles):IDisposable
    {public void Dispose(){foreach(var h in handles)h.Dispose();}}
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
}
