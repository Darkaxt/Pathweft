using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace FileExplorer;

// CRC32 of a folder's files for the opt-in column. In memory only, keyed by path + size + last write:
// a changed file is a new key. Cloud placeholders are left blank, never hydrated.
static class FileCrc
{
    const FileAttributes InCloud=FileAttributes.Offline|(FileAttributes)0x40000|(FileAttributes)0x400000; // RECALL_ON_OPEN, RECALL_ON_DATA_ACCESS
    const int Buffer=1<<20,Limit=50_000;
    static readonly ConcurrentDictionary<(string,long,long),uint> cache=new();

    internal static uint? Get(string path,CancellationToken token)
    {
        try
        {
            var file=new FileInfo(path);
            if(!file.Exists||(file.Attributes&InCloud)!=0)return null;
            var key=(path.ToUpperInvariant(),file.Length,file.LastWriteTimeUtc.Ticks);
            if(cache.TryGetValue(key,out uint known))return known;
            var crc=new Crc32();byte[] buffer=ArrayPool<byte>.Shared.Rent(Buffer);
            try
            {
                using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,0,FileOptions.SequentialScan);
                for(int read;(read=stream.Read(buffer,0,Buffer))>0;){token.ThrowIfCancellationRequested();crc.Append(buffer.AsSpan(0,read));}
            }
            finally{ArrayPool<byte>.Shared.Return(buffer);}
            file.Refresh();
            if(file.Length!=key.Item2||file.LastWriteTimeUtc.Ticks!=key.Item3)return null; // written while read: try again next time
            uint value=crc.GetCurrentHashAsUInt32();
            if(cache.Count>=Limit)cache.Clear();
            cache[key]=value;return value;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return null;}
    }

    // Background I/O and CPU priority for the calling thread (THREAD_MODE_BACKGROUND_BEGIN/END).
    internal static void LowPriority(Action work)
    {
        bool lowered=SetThreadPriority(GetCurrentThread(),0x00010000);
        try{work();}finally{if(lowered)SetThreadPriority(GetCurrentThread(),0x00020000);}
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool SetThreadPriority(IntPtr thread,int priority);
}
