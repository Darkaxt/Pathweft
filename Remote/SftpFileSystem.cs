using System.IO;
using System.Text.Json;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace FileExplorer;

// BROWSE-09: SFTP through SSH.NET (MIT). Host identity: the first connection shows the SHA-256
// fingerprint and asks; a changed key is refused, never silently accepted.
internal sealed class SftpFileSystem : IRemoteFileSystem
{
    readonly RemoteConnection connection;readonly string secret;readonly Func<string,string,Task<bool>> askHostKey;
    SftpClient client;
    public RemoteConnection Connection=>connection;
    internal SftpFileSystem(RemoteConnection connection,string secret,Func<string,string,Task<bool>> askHostKey)
    {this.connection=connection;this.secret=secret;this.askHostKey=askHostKey;}

    public async Task Connect(CancellationToken cancel)
    {
        if(client?.IsConnected==true)return;
        AuthenticationMethod method=connection.Auth=="key"
            ?new PrivateKeyAuthenticationMethod(connection.User,string.IsNullOrEmpty(secret)?new PrivateKeyFile(connection.KeyFile):new PrivateKeyFile(connection.KeyFile,secret))
            :new PasswordAuthenticationMethod(connection.User,secret??"");
        var info=new ConnectionInfo(connection.Host,connection.EffectivePort,connection.User,method){Timeout=TimeSpan.FromSeconds(20)};
        string fingerprint=null;
        var sftp=new SftpClient(info);
        // The event runs on SSH.NET's thread during the handshake: record the key and trust only a known one.
        sftp.HostKeyReceived+=(_,e)=>{fingerprint=e.FingerPrintSHA256;e.CanTrust=KnownHosts.Find(connection.Host,connection.EffectivePort)==fingerprint;};
        try{await sftp.ConnectAsync(cancel);}
        catch(Renci.SshNet.Common.SshConnectionException) when(fingerprint!=null&&KnownHosts.Find(connection.Host,connection.EffectivePort) is var known&&known!=fingerprint)
        {
            sftp.Dispose();
            if(known!=null)throw new IOException($"The SSH host key of {connection.Host} changed (now SHA256:{fingerprint}). The connection was refused; remove the saved key only if the server's key was replaced on purpose.");
            // First connection: ask, remember on Yes, connect again with the key now known.
            if(askHostKey==null||!await askHostKey(connection.Host+":"+connection.EffectivePort,"SHA256:"+fingerprint))throw new OperationCanceledException("The host key was not trusted.");
            KnownHosts.Add(connection.Host,connection.EffectivePort,fingerprint);
            sftp=new SftpClient(info);
            sftp.HostKeyReceived+=(_,e)=>e.CanTrust=KnownHosts.Find(connection.Host,connection.EffectivePort)==e.FingerPrintSHA256;
            await sftp.ConnectAsync(cancel);
        }
        client=sftp;
    }

    public async Task<List<RemoteItem>> List(string path,CancellationToken cancel)
    {
        await Connect(cancel);var items=new List<RemoteItem>();
        await foreach(var file in client.ListDirectoryAsync(RemotePath.Normalize(path),cancel))
        {
            if(file.Name is "." or "..")continue;
            items.Add(new(file.Name,RemotePath.Combine(path,file.Name),file.IsDirectory,file.IsDirectory?-1:file.Length,file.LastWriteTimeUtc));
        }
        return items;
    }

    public async Task Download(string remotePath,string localFile,IProgress<long> progress,CancellationToken cancel)
    {
        await Connect(cancel);string partial=localFile+".pathweft-part";
        try
        {
            await using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1<<16,true))
            {
                var started=(SftpDownloadAsyncResult)client.BeginDownloadFile(RemotePath.Normalize(remotePath),output,null,null,done=>progress?.Report((long)done));
                using(cancel.Register(()=>started.IsDownloadCanceled=true))await Task.Factory.FromAsync(started,client.EndDownloadFile);
                cancel.ThrowIfCancellationRequested();
            }
            File.Move(partial,localFile,true);
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }

    public async Task Upload(string localFile,string remotePath,bool replace,IProgress<long> progress,CancellationToken cancel)
    {
        await Connect(cancel);remotePath=RemotePath.Normalize(remotePath);
        if(!replace&&client.Exists(remotePath))throw new IOException($"{remotePath} already exists on {connection.Host}.");
        await using var input=new FileStream(localFile,FileMode.Open,FileAccess.Read,FileShare.Read,1<<16,true);
        var started=(SftpUploadAsyncResult)client.BeginUploadFile(input,remotePath,replace,null,null,done=>progress?.Report((long)done));
        using(cancel.Register(()=>started.IsUploadCanceled=true))await Task.Factory.FromAsync(started,client.EndUploadFile);
        cancel.ThrowIfCancellationRequested();
    }

    public async Task CreateFolder(string path,CancellationToken cancel)
    {
        await Connect(cancel);path=RemotePath.Normalize(path);
        if(!client.Exists(path))await client.CreateDirectoryAsync(path,cancel);
    }

    public async Task Delete(string path,bool isDir,bool recursive,CancellationToken cancel)
    {
        await Connect(cancel);path=RemotePath.Normalize(path);
        if(!isDir){await client.DeleteFileAsync(path,cancel);return;}
        if(recursive)foreach(var item in await List(path,cancel)){cancel.ThrowIfCancellationRequested();await Delete(item.Path,item.IsDir,true,cancel);}
        await client.DeleteDirectoryAsync(path,cancel);
    }

    public async Task Move(string from,string to,CancellationToken cancel)
    {
        await Connect(cancel);to=RemotePath.Normalize(to);
        if(client.Exists(to))throw new IOException($"{to} already exists on {connection.Host}.");
        await client.RenameFileAsync(RemotePath.Normalize(from),to,cancel);
    }

    public void Dispose(){try{client?.Disconnect();}catch(Exception ex) when(ex is ObjectDisposedException or Renci.SshNet.Common.SshException or IOException){}client?.Dispose();client=null;}
}

/// known_hosts.json in the settings folder: "host:port" → SHA-256 fingerprint (base64, as SSH.NET reports it).
internal static class KnownHosts
{
    static string FilePath=>Path.Combine(Settings.Dir,"known_hosts.json");
    static readonly object gate=new();
    static Dictionary<string,string> Read()
    {
        try{return File.Exists(FilePath)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(FilePath))??new():new();}
        catch(JsonException){return new();}
    }
    internal static string Find(string host,int port){lock(gate)return Read().GetValueOrDefault(host.ToLowerInvariant()+":"+port);}
    internal static void Add(string host,int port,string fingerprint)
    {
        lock(gate)
        {
            var all=Read();all[host.ToLowerInvariant()+":"+port]=fingerprint;Directory.CreateDirectory(Settings.Dir);
            string temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(all,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,FilePath,true);
        }
    }
    /// Forget a host's key (Settings), so the next connection asks again.
    internal static void Remove(string host,int port)
    {
        lock(gate){var all=Read();if(!all.Remove(host.ToLowerInvariant()+":"+port))return;string temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(all));File.Move(temp,FilePath,true);}
    }
}
