using System.IO;

namespace FileExplorer;

// BROWSE-09: preview and open work on a downloaded copy in this run's own temp folder; the
// remote item never gets a local-path claim. The folder is removed when Pathweft exits.
internal static class RemoteTempCopies
{
    static readonly string Root=Path.Combine(Path.GetTempPath(),"Pathweft-remote",Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    static int counter;

    /// Downloads the remote file into a fresh subfolder (keeping its name, so its type and app still apply).
    internal static async Task<string> Fetch(IRemoteFileSystem fs,RemoteItem item,IProgress<long> progress,CancellationToken cancel)
    {
        string folder=Path.Combine(Root,Interlocked.Increment(ref counter).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        string local=Path.Combine(folder,string.Concat(item.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)));
        await fs.Download(item.Path,local,progress,cancel);
        return local;
    }

    /// At exit: this run's copies go. Files still open in another app stay until the next run's sweep.
    internal static void Cleanup()
    {
        try{if(Directory.Exists(Root))Directory.Delete(Root,true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        // copies of earlier runs whose process is gone
        try
        {
            string parent=Path.GetDirectoryName(Root);if(!Directory.Exists(parent))return;
            foreach(var folder in Directory.EnumerateDirectories(parent))
            {
                if(!int.TryParse(Path.GetFileName(folder),out int pid)||pid==Environment.ProcessId)continue;
                bool alive;try{using var process=System.Diagnostics.Process.GetProcessById(pid);alive=process.ProcessName.Equals("Pathweft",StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){alive=false;}
                if(!alive)try{Directory.Delete(folder,true);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
    }
}
