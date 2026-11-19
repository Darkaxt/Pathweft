using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FileExplorer;

// BROWSE-09: saved SFTP/WebDAV connections. connections.json holds only where and who;
// passwords and key passphrases live in Windows Credential Manager ("Pathweft:remote:<id>").
internal sealed class RemoteConnection
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Name {get;set;}=""; // shown in the sidebar
    public string Kind {get;set;}="sftp"; // sftp | webdav | webdavs
    public string Host {get;set;}="";
    public int Port {get;set;}
    public string User {get;set;}="";
    public string Root {get;set;}="/"; // the folder opened first
    public string Auth {get;set;}="password"; // password | key
    public string KeyFile {get;set;}=""; // private key path when Auth is key
    internal int DefaultPort=>Kind switch{"sftp"=>22,"webdavs"=>443,_=>80};
    internal int EffectivePort=>Port>0?Port:DefaultPort;
    internal string Scheme=>Kind;
    /// The pane address of a remote path on this connection: sftp://user@host:port/path
    internal string Address(string path="/")
    {
        string user=User.Length>0?Uri.EscapeDataString(User)+"@":"";
        string port=Port>0&&Port!=DefaultPort?":"+Port:"";
        return $"{Kind}://{user}{Host}{port}{RemotePath.Normalize(path)}";
    }
    internal string CredentialTarget=>"Pathweft:remote:"+Id;
}

internal static class RemoteConnections
{
    static string FilePath=>Path.Combine(Settings.Dir,"connections.json");
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    static readonly object gate=new();
    /// The saved list changed (the sidebar's Connections section follows).
    internal static event Action Changed;

    internal static List<RemoteConnection> Load()
    {
        lock(gate)
        {
            try{return File.Exists(FilePath)?JsonSerializer.Deserialize<List<RemoteConnection>>(File.ReadAllText(FilePath))??new():new();}
            catch(JsonException){return new();}
        }
    }
    internal static void Save(List<RemoteConnection> connections)
    {
        lock(gate)
        {
            Directory.CreateDirectory(Settings.Dir);
            string temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(connections,Json),new UTF8Encoding(false));
            File.Move(temp,FilePath,true);
        }
        Changed?.Invoke();
    }
    internal static void Upsert(RemoteConnection connection)
    {
        var all=Load();int at=all.FindIndex(x=>x.Id==connection.Id);
        if(at>=0)all[at]=connection;else all.Add(connection);Save(all);
    }
    /// Removes the saved connection and its stored secret; remote files are untouched.
    internal static void Remove(string id)
    {
        var all=Load();var gone=all.FirstOrDefault(x=>x.Id==id);if(gone==null)return;
        all.Remove(gone);Save(all);RemoteCredentials.Delete(gone.CredentialTarget);
    }
    /// The saved connection an address belongs to (kind, host, port and user match), or a transient one.
    internal static RemoteConnection Resolve(RemoteAddress address)
    {
        return Load().FirstOrDefault(x=>x.Kind==address.Kind&&x.Host.Equals(address.Host,StringComparison.OrdinalIgnoreCase)&&x.EffectivePort==address.Port
            &&(address.User.Length==0||x.User==address.User))
            ??new RemoteConnection{Id="transient-"+address.Kind+"-"+address.Host+"-"+address.Port+"-"+address.User,Name=address.Host,Kind=address.Kind,Host=address.Host,Port=address.Port,User=address.User};
    }
}

/// Windows Credential Manager (generic credentials), the only store for remote secrets.
internal static class RemoteCredentials
{
    const int Generic=1,PersistLocalMachine=2,NotFound=1168;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Credential
    {
        public int Flags,Type;public string TargetName,Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;public IntPtr CredentialBlob;public int Persist,AttributeCount;public IntPtr Attributes;public string TargetAlias,UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CredWrite(ref Credential credential,int flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CredRead(string target,int type,int flags,out IntPtr credential);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CredDelete(string target,int type,int flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr buffer);

    internal static void Write(string target,string user,string secret)
    {
        var bytes=Encoding.Unicode.GetBytes(secret??"");var blob=Marshal.AllocHGlobal(Math.Max(1,bytes.Length));
        try
        {
            Marshal.Copy(bytes,0,blob,bytes.Length);
            var credential=new Credential{Type=Generic,TargetName=target,UserName=user??"",CredentialBlob=blob,CredentialBlobSize=bytes.Length,Persist=PersistLocalMachine};
            if(!CredWrite(ref credential,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally{Array.Clear(bytes);Marshal.FreeHGlobal(blob);}
    }
    /// The stored secret, or null when none is saved.
    internal static string Read(string target)
    {
        if(!CredRead(target,Generic,0,out var pointer))
        {
            int error=Marshal.GetLastWin32Error();if(error==NotFound)return null;
            throw new System.ComponentModel.Win32Exception(error);
        }
        try
        {
            var credential=Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlobSize==0?"":Marshal.PtrToStringUni(credential.CredentialBlob,credential.CredentialBlobSize/2);
        }
        finally{CredFree(pointer);}
    }
    internal static void Delete(string target)
    {
        if(!CredDelete(target,Generic,0)&&Marshal.GetLastWin32Error() is var error&&error!=NotFound)throw new System.ComponentModel.Win32Exception(error);
    }
}
