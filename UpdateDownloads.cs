using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FileExplorer;

// An updater-owned download, not a general Temp purge. Identity-bound receipts
// survive the requesting app so the restarted app can await the real setup exit.
internal static class UpdateDownloads
{
    internal sealed record Receipt(string File,uint Volume,ulong Id,long Created,int Owner,long OwnerCreated,int Installer=0,long InstallerCreated=0);
    internal static string Root=>Path.Combine(DataFolder.Bulk,"Updates");
    static string Record(string path)=>Path.Combine(Path.GetDirectoryName(path),"download.json");
    internal static string Create(Version version)
    {
        string folder=Directory.CreateDirectory(Path.Combine(Root,Guid.NewGuid().ToString("N"))).FullName;
        string path=Path.Combine(folder,$"Pathweft-{version}-setup.exe");
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
        var stamp=TransferFile.Inspect(path);using var owner=Process.GetCurrentProcess();
        Save(path,new(Path.GetFileName(path),stamp.Volume,stamp.Id,stamp.Created,owner.Id,owner.StartTime.ToUniversalTime().ToFileTimeUtc()));
        return path;
    }
    static void Save(string path,Receipt receipt)
    {
        string record=Record(path),pending=record+".new";
        File.WriteAllText(pending,JsonSerializer.Serialize(receipt));File.Move(pending,record,true);
    }
    static Receipt Read(string path)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(path));
        if(!Path.GetDirectoryName(folder).Equals(Root,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(folder),"N",out _))
            throw new IOException("Not an owned update download.");
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Update folder was replaced.");
        var receipt=JsonSerializer.Deserialize<Receipt>(File.ReadAllText(Record(path)));
        if(receipt==null||receipt.File!=Path.GetFileName(path)||Path.GetFileName(receipt.File)!=receipt.File)throw new IOException("Update receipt differs.");
        return receipt;
    }
    internal static void Handoff(string path,Process installer)
    {var receipt=Read(path);Save(path,receipt with{Installer=installer.Id,InstallerCreated=installer.StartTime.ToUniversalTime().ToFileTimeUtc()});}
    static Process Living(int pid,long created)
    {
        if(pid<=0)return null;
        Process process=null;
        try{process=Process.GetProcessById(pid);if(!process.HasExited&&process.StartTime.ToUniversalTime().ToFileTimeUtc()==created)return process;}
        catch(ArgumentException){}catch(InvalidOperationException){}
        process?.Dispose();return null;
    }
    internal static bool Discard(string path)
    {
        var receipt=Read(path);
        using(var setup=Living(receipt.Installer,receipt.InstallerCreated))if(setup!=null)return false;
        if(File.Exists(path))
        {
            using var file=new TransferFile(path,true);
            var stamp=file.Initial;
            if(stamp.Volume!=receipt.Volume||stamp.Id!=receipt.Id||stamp.Created!=receipt.Created)return false;
            file.RemoveOnClose();
        }
        File.Delete(Record(path));
        // No recursive removal: unrelated new members survive.
        if(!Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)).Any())Directory.Delete(Path.GetDirectoryName(path));
        return true;
    }
    internal static async Task RetireAsync(CancellationToken token)
    {
        try{await RetireOwnedAsync(token).ConfigureAwait(false);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {Trace.TraceWarning("Update downloads retained: "+error.Message);}
    }
    static async Task RetireOwnedAsync(CancellationToken token)
    {
        if(!Directory.Exists(Root))return;
        foreach(string folder in Directory.EnumerateDirectories(Root))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if(!Guid.TryParseExact(Path.GetFileName(folder),"N",out _)||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
                var receipt=JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(Path.Combine(folder,"download.json"),token).ConfigureAwait(false));
                if(receipt==null||Path.GetFileName(receipt.File)!=receipt.File)continue;
                string path=Path.Combine(folder,receipt.File);Read(path);
                using var owner=Living(receipt.Owner,receipt.OwnerCreated);
                if(owner!=null)continue;
                using var setup=Living(receipt.Installer,receipt.InstallerCreated);
                if(setup!=null)await setup.WaitForExitAsync(token).ConfigureAwait(false);
                Discard(path);
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception)
            {Trace.TraceWarning("Update download retained: "+error.Message);}
        }
    }
}
