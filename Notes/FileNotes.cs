using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

/// BROWSE-07: tags, a rating and a comment for files and folders. Stored only in Pathweft's own
/// database (notes.sqlite in the data folder), never in the files, alternate streams or sidecars.
/// A record is keyed by its path and, on NTFS/ReFS, by volume serial + file ID, so a rename or move
/// on the same volume is followed. On FAT, network and cloud roots only the path is known: a rename
/// there loses the record (it shows as stale and can be pruned).
internal sealed record FileNote(string[] Tags,int Rating,string Comment)
{
    internal bool Empty=>Tags.Length==0&&Rating==0&&string.IsNullOrWhiteSpace(Comment);
    internal string TagText=>string.Join(", ",Tags);
    internal string Stars=>Rating<=0?"":new string('★',Rating)+new string('☆',5-Rating);
    internal static string[] SplitTags(string text)=>(text??"").Split(new[]{',',';'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

internal static class FileNotes
{
    static string FilePath=>Path.Combine(Settings.Dir,"notes.sqlite");
    static readonly object gate=new();
    static Dictionary<string,(FileNote Note,ulong Volume,string Id)> notes;
    static List<string> stale=new();
    /// A note changed (path), or null after a reload/heal: panes refresh the cells they show.
    internal static event Action<string> Changed;

    static SqliteConnection Open()
    {
        SqliteRuntime.Ensure();Directory.CreateDirectory(Settings.Dir);
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=FilePath,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());db.Open();
        using var setup=db.CreateCommand();
        setup.CommandText="CREATE TABLE IF NOT EXISTS notes(path TEXT PRIMARY KEY COLLATE NOCASE,volume INTEGER NOT NULL DEFAULT 0,fileid TEXT,tags TEXT NOT NULL DEFAULT '',rating INTEGER NOT NULL DEFAULT 0,comment TEXT NOT NULL DEFAULT ''); CREATE INDEX IF NOT EXISTS notes_identity ON notes(volume,fileid);";
        setup.ExecuteNonQuery();return db;
    }
    static void Load()
    {
        if(notes!=null)return;
        notes=new(StringComparer.OrdinalIgnoreCase);
        if(!File.Exists(FilePath))return;
        try
        {
            using var db=Open();using var read=db.CreateCommand();read.CommandText="SELECT path,volume,fileid,tags,rating,comment FROM notes";
            using var r=read.ExecuteReader();
            while(r.Read())notes[r.GetString(0)]=(new FileNote(FileNote.SplitTags(r.GetString(3)),r.GetInt32(4),r.GetString(5)),(ulong)r.GetInt64(1),r.IsDBNull(2)?null:r.GetString(2));
        }
        catch(SqliteException){}
    }
    internal static bool Any{get{lock(gate){Load();return notes.Count>0;}}}
    /// In memory only: cheap enough for every cell and every live-filter keystroke.
    internal static FileNote Find(string path)
    {
        if(path==null)return null;
        lock(gate){Load();return notes.Count>0&&notes.TryGetValue(path,out var hit)?hit.Note:null;}
    }
    internal static void Set(string path,FileNote note)
    {
        var (volume,id)=Identity(path);
        lock(gate)
        {
            Load();
            using var db=Open();using var write=db.CreateCommand();
            write.Parameters.AddWithValue("$path",path);
            if(note==null||note.Empty){write.CommandText="DELETE FROM notes WHERE path=$path";notes.Remove(path);}
            else
            {
                write.CommandText="INSERT INTO notes(path,volume,fileid,tags,rating,comment) VALUES($path,$volume,$id,$tags,$rating,$comment) ON CONFLICT(path) DO UPDATE SET volume=$volume,fileid=$id,tags=$tags,rating=$rating,comment=$comment";
                write.Parameters.AddWithValue("$volume",(long)volume);write.Parameters.AddWithValue("$id",(object)id??DBNull.Value);
                write.Parameters.AddWithValue("$tags",note.TagText);write.Parameters.AddWithValue("$rating",Math.Clamp(note.Rating,0,5));write.Parameters.AddWithValue("$comment",note.Comment??"");
                notes[path]=(note with{Rating=Math.Clamp(note.Rating,0,5)},volume,id);
            }
            write.ExecuteNonQuery();stale.Remove(path);
        }
        Changed?.Invoke(path);
    }
    /// Every tag in use, for the editor's suggestions.
    internal static string[] AllTags(){lock(gate){Load();return notes.Values.SelectMany(x=>x.Note.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();}}
    internal static string[] Stale(){lock(gate)return stale.ToArray();}

    /// Follows records whose path is gone to their file ID's current path (same volume), and lists the
    /// rest as stale. Runs off the UI thread at start; touches only local NTFS/ReFS volumes by ID.
    internal static void Heal()
    {
        List<(string Path,ulong Volume,string Id)> missing;
        lock(gate){Load();missing=notes.Where(x=>!Exists(x.Key)).Select(x=>(x.Key,x.Value.Volume,x.Value.Id)).ToList();}
        if(missing.Count==0){lock(gate)stale=new();return;}
        var moved=new List<(string From,string To)>();var gone=new List<string>();
        foreach(var item in missing)
        {
            string now=item.Id!=null?PathOfId(item.Volume,item.Id):null;
            if(now!=null&&!now.Equals(item.Path,StringComparison.OrdinalIgnoreCase))moved.Add((item.Path,now));else gone.Add(item.Path);
        }
        lock(gate)
        {
            if(moved.Count>0)
            {
                using var db=Open();
                foreach(var (from,to) in moved)
                {
                    if(!notes.Remove(from,out var record)||notes.ContainsKey(to))continue;notes[to]=record;
                    using var update=db.CreateCommand();update.CommandText="UPDATE notes SET path=$to WHERE path=$from";
                    update.Parameters.AddWithValue("$to",to);update.Parameters.AddWithValue("$from",from);update.ExecuteNonQuery();
                }
            }
            stale=gone;
        }
        if(moved.Count>0)Changed?.Invoke(null);
    }
    /// Settings > Notes: forget records whose file is gone.
    internal static int Prune()
    {
        int removed=0;
        lock(gate)
        {
            Load();using var db=Open();
            foreach(var path in stale.Where(path=>!Exists(path)))
            {
                using var delete=db.CreateCommand();delete.CommandText="DELETE FROM notes WHERE path=$path";delete.Parameters.AddWithValue("$path",path);
                removed+=delete.ExecuteNonQuery();notes.Remove(path);
            }
            stale=new();
        }
        Changed?.Invoke(null);return removed;
    }
    static bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);

    // Volume serial + 128-bit file ID through a handle that reads only attributes (no cloud recall).
    static (ulong Volume,string Id) Identity(string path)
    {
        try
        {
            if(path.StartsWith(@"\\")||Path.GetPathRoot(path) is not {Length:>0} root||new DriveInfo(root) is not {DriveType:DriveType.Fixed or DriveType.Removable,DriveFormat:"NTFS" or "ReFS"})return(0,null);
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException){return(0,null);}
        using var handle=CreateFileW(@"\\?\"+path,0x80,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if(handle.IsInvalid||!GetFileInformationByHandleEx(handle,18,out var id,Marshal.SizeOf<FileIdInfo>()))return(0,null);
        return(id.Volume,id.High.ToString("X16")+id.Low.ToString("X16"));
    }
    static string PathOfId(ulong volume,string id)
    {
        if(id is not {Length:32})return null;
        foreach(var drive in DriveInfo.GetDrives())
        {
            try
            {
                if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable)||!drive.IsReady||drive.DriveFormat is not ("NTFS" or "ReFS"))continue;
                var root=drive.RootDirectory.FullName;
                // FileIdInfo reports the 64-bit serial; its low half is the 32-bit serial Windows shows.
                if(!GetVolumeInformationW(root,null,0,out uint serial,out _,out _,null,0)||(uint)volume!=serial)continue;
                using var hint=CreateFileW(root,0x80,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);if(hint.IsInvalid)continue;
                var descriptor=new FileIdDescriptor{Size=(uint)Marshal.SizeOf<FileIdDescriptor>(),Type=2,High=Convert.ToUInt64(id[..16],16),Low=Convert.ToUInt64(id[16..],16)};
                using var file=OpenFileById(hint,ref descriptor,0x80,7,IntPtr.Zero,0x02000000);if(file.IsInvalid)continue;
                var name=new StringBuilder(32768);uint length=GetFinalPathNameByHandleW(file,name,(uint)name.Capacity,0);
                if(length is >0 and <32768){var text=name.ToString();return text.StartsWith(@"\\?\UNC\")?@"\\"+text[8..]:text.StartsWith(@"\\?\")?text[4..]:text;}
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or FormatException){}
        }
        return null;
    }
    [StructLayout(LayoutKind.Sequential)] struct FileIdInfo{public ulong Volume;public ulong Low,High;}
    [StructLayout(LayoutKind.Sequential)] struct FileIdDescriptor{public uint Size;public int Type;public ulong Low,High;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int type,out FileIdInfo info,int size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern SafeFileHandle OpenFileById(SafeFileHandle hint,ref FileIdDescriptor id,uint access,uint share,IntPtr security,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandleW(SafeFileHandle file,StringBuilder name,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetVolumeInformationW(string root,StringBuilder name,int nameSize,out uint serial,out uint maxComponent,out uint flags,StringBuilder fileSystem,int fileSystemSize);
}
