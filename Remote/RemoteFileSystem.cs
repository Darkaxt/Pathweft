using System.IO;

namespace FileExplorer;

// BROWSE-09: what a pane needs from a remote folder. Remote items have remote paths only;
// nothing here pretends they are local files, and no operation offers local Undo.
internal sealed record RemoteItem(string Name,string Path,bool IsDir,long Size,DateTime Modified);

internal interface IRemoteFileSystem : IDisposable
{
    RemoteConnection Connection {get;}
    Task Connect(CancellationToken cancel);
    Task<List<RemoteItem>> List(string path,CancellationToken cancel);
    Task Download(string remotePath,string localFile,IProgress<long> progress,CancellationToken cancel);
    /// Fails when the destination exists and `replace` is false.
    Task Upload(string localFile,string remotePath,bool replace,IProgress<long> progress,CancellationToken cancel);
    Task CreateFolder(string path,CancellationToken cancel);
    Task Delete(string path,bool isDir,bool recursive,CancellationToken cancel);
    /// Rename or move within the same connection; refuses to replace an existing destination.
    Task Move(string from,string to,CancellationToken cancel);
}

/// sftp://user@host:port/path, webdav://host/path, webdavs://host/path
internal sealed record RemoteAddress(string Kind,string User,string Host,int Port,string Path)
{
    internal static bool IsRemote(string address)=>address!=null&&(address.StartsWith("sftp://",StringComparison.OrdinalIgnoreCase)
        ||address.StartsWith("webdav://",StringComparison.OrdinalIgnoreCase)||address.StartsWith("webdavs://",StringComparison.OrdinalIgnoreCase));
    internal static bool TryParse(string address,out RemoteAddress parsed)
    {
        parsed=null;if(!IsRemote(address))return false;
        int scheme=address.IndexOf("://",StringComparison.Ordinal);string kind=address[..scheme].ToLowerInvariant();
        // Uri does not know these schemes' ports; parse through http for the authority part.
        if(!Uri.TryCreate("http://"+address[(scheme+3)..],UriKind.Absolute,out var uri)||uri.Host.Length==0)return false;
        int port=uri.IsDefaultPort?kind switch{"sftp"=>22,"webdavs"=>443,_=>80}:uri.Port;
        string user=Uri.UnescapeDataString(uri.UserInfo.Split(':')[0]);
        parsed=new(kind,user,uri.Host,port,RemotePath.Normalize(Uri.UnescapeDataString(uri.AbsolutePath)));
        return true;
    }
}

internal static class RemotePath
{
    internal static string Normalize(string path)
    {
        path=(path??"/").Replace('\\','/');if(!path.StartsWith('/'))path="/"+path;
        while(path.Contains("//"))path=path.Replace("//","/");
        return path.Length>1?path.TrimEnd('/'):path;
    }
    internal static string Combine(string folder,string name)=>Normalize(Normalize(folder).TrimEnd('/')+"/"+name);
    internal static string Parent(string path){path=Normalize(path);int cut=path.LastIndexOf('/');return cut<=0?"/":path[..cut];}
    internal static string Name(string path){path=Normalize(path);return path=="/"?"":path[(path.LastIndexOf('/')+1)..];}
}

internal static class RemoteFileSystems
{
    /// Opens (not yet connected) the client for a connection. `askHostKey` decides a first-seen SSH host key;
    /// `secret` is the password or key passphrase (from Credential Manager or asked from the user by the caller).
    internal static IRemoteFileSystem Create(RemoteConnection connection,string secret,Func<string,string,Task<bool>> askHostKey)=>connection.Kind switch
    {
        "sftp"=>new SftpFileSystem(connection,secret,askHostKey),
        "webdav" or "webdavs"=>new WebDavFileSystem(connection,secret),
        _=>throw new NotSupportedException("Unsupported remote kind: "+connection.Kind),
    };
    /// Downloads a remote folder tree below `localFolder` (used by copy to a local pane).
    internal static async Task DownloadTree(IRemoteFileSystem fs,string remoteFolder,string localFolder,IProgress<long> progress,CancellationToken cancel)
    {
        Directory.CreateDirectory(localFolder);
        foreach(var item in await fs.List(remoteFolder,cancel))
        {
            cancel.ThrowIfCancellationRequested();string target=Path.Combine(localFolder,item.Name);
            if(item.IsDir)await DownloadTree(fs,item.Path,target,progress,cancel);
            else await fs.Download(item.Path,target,progress,cancel);
        }
    }
    /// Uploads a local folder tree as `remoteFolder` (used by copy from a local pane).
    internal static async Task UploadTree(IRemoteFileSystem fs,string localFolder,string remoteFolder,bool replace,IProgress<long> progress,CancellationToken cancel)
    {
        await fs.CreateFolder(remoteFolder,cancel);
        foreach(var directory in Directory.EnumerateDirectories(localFolder)){cancel.ThrowIfCancellationRequested();await UploadTree(fs,directory,RemotePath.Combine(remoteFolder,Path.GetFileName(directory)),replace,progress,cancel);}
        foreach(var file in Directory.EnumerateFiles(localFolder)){cancel.ThrowIfCancellationRequested();await fs.Upload(file,RemotePath.Combine(remoteFolder,Path.GetFileName(file)),replace,progress,cancel);}
    }
}
