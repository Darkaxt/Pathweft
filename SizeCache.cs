using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

// Fallback measurements only. Everything owns its index; Pathweft retains at
// most HotLimit recent records, never a second disk-wide in-memory dictionary.
public static class SizeCache
{
    record Entry(string Key,long Size,long Stamp);
    static readonly object state=new(),gate=new(),opening=new();
    static readonly Dictionary<string,LinkedListNode<Entry>> hot=new(StringComparer.Ordinal);
    static readonly LinkedList<Entry> recent=new();
    static readonly HashSet<Measurement> measuring=new();
    internal const int HotLimit=2048;
    internal static int HotCount{get{lock(state)return hot.Count;}}
    internal static event Action<string> Invalidated;
    internal static string FilePath=>Path.Combine(DataFolder.Cache,"folder-sizes.sqlite");
    internal static string LegacyPath=>Path.Combine(DataFolder.Cache,"folder-sizes.txt");
    internal static bool OwnPersistence(string path)=>path.Equals(FilePath,StringComparison.OrdinalIgnoreCase)||path.Equals(FilePath+"-wal",StringComparison.OrdinalIgnoreCase)||path.Equals(FilePath+"-shm",StringComparison.OrdinalIgnoreCase);
    static SqliteConnection database;
    static Task loading;
    public static void StartLoading(){lock(gate)loading??=Task.Run(Load);}
    static void Ready(){StartLoading();loading.GetAwaiter().GetResult();}
    static string Key(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();

    // Listing reads only the hot cache: no WPF wait for import or database I/O.
    public static long Peek(string path,DateTime lastWriteUtc)
    {
        string key=Key(path);if(!Monitor.TryEnter(state))return -1;
        try{return hot.TryGetValue(key,out var node)&&node.Value.Stamp==lastWriteUtc.Ticks?node.Value.Size:-1;}
        finally{Monitor.Exit(state);}
    }
    public static long Get(string path,DateTime lastWriteUtc)
    {
        Ready();string key=Key(path);bool stale=false;
        lock(state)
        {
            if(hot.TryGetValue(key,out var node))
            {
                if(node.Value.Stamp==lastWriteUtc.Ticks){recent.Remove(node);recent.AddFirst(node);return node.Value.Size;}
                stale=true;
            }
            else
            {
                using var command=database.CreateCommand();command.CommandText="SELECT bytes,stamp FROM sizes WHERE path=$path";
                command.Parameters.AddWithValue("$path",key);using var row=command.ExecuteReader();
                if(row.Read())
                {
                    if(row.GetInt64(1)==lastWriteUtc.Ticks){long size=row.GetInt64(0);Remember(new(key,size,lastWriteUtc.Ticks));return size;}
                    stale=true;
                }
            }
        }
        if(stale)Invalidate(path);return -1;
    }
    static void Remember(Entry entry)
    {
        if(hot.Remove(entry.Key,out var previous))recent.Remove(previous);
        hot[entry.Key]=recent.AddFirst(entry);
        if(hot.Count>HotLimit){hot.Remove(recent.Last.Value.Key);recent.RemoveLast();}
    }
    internal static bool Within(string path,string folder)
    {
        if(path.Equals(folder,StringComparison.OrdinalIgnoreCase))return true;
        var prefix=Path.TrimEndingDirectorySeparator(folder);
        return path.Length>prefix.Length&&path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&path[prefix.Length]==Path.DirectorySeparatorChar;
    }
    internal static void Invalidate(string path,bool subtree=false)
    {
        Ready();string key=Key(path),prefix=key.TrimEnd('\\')+"\\";
        lock(state)
        {
            using var transaction=database.BeginTransaction();using var command=database.CreateCommand();command.Transaction=transaction;
            // The separator range uses the BINARY primary-key index. No map.Keys
            // snapshot or prefix reconstruction per record during watcher events.
            if(subtree)
            {
                command.CommandText="DELETE FROM sizes WHERE path >= $lo AND path < $hi";
                command.Parameters.AddWithValue("$lo",prefix);command.Parameters.AddWithValue("$hi",prefix[..^1]+"]");
                command.ExecuteNonQuery();command.Parameters.Clear();
                foreach(var entry in recent.Where(e=>Within(e.Key,key)).ToArray())RemoveHot(entry.Key);
            }
            command.CommandText="DELETE FROM sizes WHERE path=$path";var parameter=command.Parameters.Add("$path",SqliteType.Text);
            for(string p=key;p!=null;p=Path.GetDirectoryName(p)){string ancestor=Key(p);parameter.Value=ancestor;command.ExecuteNonQuery();RemoveHot(ancestor);}
            transaction.Commit();
            foreach(var scan in measuring)if(Within(path,scan.Path)||subtree&&Within(scan.Path,path))scan.Invalid=true;
        }
        Invalidated?.Invoke(path);
    }
    static void RemoveHot(string key){if(hot.Remove(key,out var node))recent.Remove(node);}
    static void Put(string key,DateTime stamp,long bytes)
    {
        using var command=database.CreateCommand();command.CommandText="INSERT OR REPLACE INTO sizes(path,bytes,stamp) VALUES($path,$bytes,$stamp)";
        command.Parameters.AddWithValue("$path",key);command.Parameters.AddWithValue("$bytes",bytes);command.Parameters.AddWithValue("$stamp",stamp.Ticks);
        command.ExecuteNonQuery();Remember(new(key,bytes,stamp.Ticks));
    }
    public static void Set(string path,DateTime lastWriteUtc,long size){Ready();lock(state)Put(Key(path),lastWriteUtc,size);}
    public static void Clear()
    {
        Ready();lock(state)
        {
            using var command=database.CreateCommand();command.CommandText="DELETE FROM sizes";command.ExecuteNonQuery();
            hot.Clear();recent.Clear();foreach(var scan in measuring)scan.Invalid=true;
        }
        Invalidated?.Invoke(null);
    }
    internal sealed class Measurement:IDisposable
    {
        internal string Path{get;}
        internal bool Invalid;
        internal Measurement(string path){Ready();Path=path;lock(state)measuring.Add(this);}
        internal bool Complete(DateTime stamp,long bytes){lock(state){if(Invalid)return false;Put(Key(Path),stamp,bytes);return true;}}
        internal void Discard()
        {
            // Measurement failure is not another filesystem change. Remove its
            // old validity without starting an endless denied-folder rescan.
            lock(state)
            {
                Invalid=true;string key=Key(Path);using var command=database.CreateCommand();
                command.CommandText="DELETE FROM sizes WHERE path=$path";command.Parameters.AddWithValue("$path",key);command.ExecuteNonQuery();RemoveHot(key);
            }
        }
        public void Dispose(){lock(state)measuring.Remove(this);}
    }
    public static void Load()
    {
        lock(opening)
        {
            if(database!=null)return;
            SqliteRuntime.Ensure();Directory.CreateDirectory(DataFolder.Cache);
            var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=FilePath,Pooling=false}.ToString());connection.Open();
            try
            {
                using var command=connection.CreateCommand();
                command.CommandText="PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-4096; CREATE TABLE IF NOT EXISTS sizes(path TEXT PRIMARY KEY COLLATE BINARY,bytes INTEGER NOT NULL,stamp INTEGER NOT NULL) WITHOUT ROWID; CREATE TABLE IF NOT EXISTS metadata(name TEXT PRIMARY KEY,value TEXT NOT NULL) WITHOUT ROWID;";command.ExecuteNonQuery();
                command.CommandText="SELECT value FROM metadata WHERE name='legacy-import'";bool imported=command.ExecuteScalar()!=null;
                string source=File.Exists(LegacyPath)?LegacyPath:Path.Combine(Settings.Dir,"folder-sizes.txt");
                if(!imported)
                {
                    // Interrupted imports roll back; original remains until the
                    // migration marker commits with all streamed records.
                    using var transaction=connection.BeginTransaction();command.Transaction=transaction;
                    command.CommandText="INSERT OR REPLACE INTO sizes(path,bytes,stamp) VALUES($path,$bytes,$stamp)";
                    var path=command.Parameters.Add("$path",SqliteType.Text);var bytes=command.Parameters.Add("$bytes",SqliteType.Integer);var stamp=command.Parameters.Add("$stamp",SqliteType.Integer);
                    if(File.Exists(source))foreach(string line in File.ReadLines(source))
                    {
                        var parts=line.Split('|',4);
                        if(parts.Length!=4||!long.TryParse(parts[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out long size)||size<0||!long.TryParse(parts[1],out long ticks)||Loops(parts[3])||!Path.IsPathFullyQualified(parts[3]))continue;
                        path.Value=Key(parts[3]);bytes.Value=size;stamp.Value=ticks;command.ExecuteNonQuery();
                    }
                    command.Parameters.Clear();command.CommandText="INSERT INTO metadata VALUES('legacy-import','1')";command.ExecuteNonQuery();transaction.Commit();command.Transaction=null;
                }
                database=connection;
                // Old application cache, removed only after the import commit.
                try{if(File.Exists(source))File.Delete(source);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            catch{connection.Dispose();throw;}
        }
    }
    internal static bool Loops(string path)
    {
        if(path.Length<=200)return false;var parts=path.Split('\\');var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i+3<=parts.Length;i++)if(!seen.Add(string.Join('\\',parts,i,3)))return true;return false;
    }
    // Measurements are individually committed. Only checkpoint the WAL: no
    // dictionary snapshot, sort, or complete-file rewrite on timer/shutdown.
    public static void SaveIfChanged()=>Save();
    public static void Save()
    {
        if(loading==null)return;Ready();lock(state)
        {using var command=database.CreateCommand();command.CommandText="PRAGMA wal_checkpoint(PASSIVE)";command.ExecuteNonQuery();}
    }
}
