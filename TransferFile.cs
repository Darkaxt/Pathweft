using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// Native metadata/stream inspection and short final removal operations.
// Copying and verification do not retain an exclusive source handle.
internal sealed class TransferFile : IDisposable
{
    internal readonly record struct Stamp(uint Volume, ulong Id, long Size, long Created, long Modified, long Accessed, FileAttributes Attributes, uint Links)
    {
        internal bool Matches(Stamp other) => Volume == other.Volume && Id == other.Id && Size == other.Size
            && Created == other.Created && Modified == other.Modified && Attributes == other.Attributes;
    }
    internal readonly record struct StreamEntry(string Name, long Size);
    internal SafeFileHandle Handle { get; }
    internal Stamp Initial { get; }
    internal string Path { get; }
    const uint Read = 0x80000000, Delete = 0x10000, OpenReparse = 0x00200000, Backup = 0x02000000;
    [ThreadStatic] static StringBuilder volumePathBuffer;

    internal TransferFile(string path, bool removal)
    {
        Path = path;
        Handle = CreateFileW(Long(path), Read | (removal ? Delete : 0), 1, IntPtr.Zero, 3, OpenReparse, IntPtr.Zero);
        if (Handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); Handle.Dispose(); throw Error(path, error); }
        try
        {
            Initial = ReadStamp(Handle, path);
            if ((Initial.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("File transfers do not follow reparse points: " + path);
        }
        catch { Handle.Dispose(); throw; }
    }

    internal static Stamp Inspect(string path)
    {
        using var handle = CreateFileW(Long(path), 0, 7, IntPtr.Zero, 3, OpenReparse | Backup, IntPtr.Zero);
        if (handle.IsInvalid) throw Error(path, Marshal.GetLastWin32Error());
        return ReadStamp(handle, path);
    }

    internal static SafeFileHandle OpenCopySource(string path, Stamp expected)
    {
        var handle = CreateFileW(Long(path), Read, 7, IntPtr.Zero, 3, OpenReparse | 0x08000000 /* SEQUENTIAL_SCAN */, IntPtr.Zero);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw Error(path, error); }
        try
        {
            if (!expected.Matches(ReadStamp(handle, path)))
                throw new IOException("A source changed after planning; it was retained: " + path);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static int ReadCopySource(SafeFileHandle handle, string path, byte[] buffer, int count)
    {
        if (!ReadFile(handle, buffer, count, out int read, IntPtr.Zero)) throw Error(path, Marshal.GetLastWin32Error());
        return read;
    }

    internal void Require(Stamp expected)
    {
        if (!expected.Matches(ReadStamp(Handle, Path))) throw new IOException("A source changed after planning; it was retained: " + Path);
    }
    internal static void RequireCurrent(string path, Stamp expected)
    {
        if (!expected.Matches(Inspect(path))) throw new IOException("A source changed after planning; it was retained: " + path);
    }

    internal void RemoveOnClose()
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 1);
            if (!SetFileInformationByHandle(Handle, 4, buffer, 4)) throw Error(Path + " (could not remove this file)", Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static IReadOnlyList<StreamEntry> Streams(string path, bool directory = false)
    {
        var entries = new List<StreamEntry>();
        IntPtr find = FindFirstStreamW(Long(path), 0, out var data, 0);
        if (find == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 38) return entries;
            // FindFirstStreamW reports ERROR_INVALID_PARAMETER for a filesystem
            // without streams. Successful inventories need no volume discovery.
            if (error == 87) return directory ? Array.Empty<StreamEntry>() : new[] { new StreamEntry("::$DATA", new FileInfo(path).Length) };
            throw Error(path + " (stream inventory)", error);
        }
        try
        {
            do { entries.Add(new(data.Name, data.Size)); } while (FindNextStreamW(find, out data));
            int error = Marshal.GetLastWin32Error();
            if (error != 38) throw Error(path + " (stream inventory)", error);
        }
        finally { FindClose(find); }
        return entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
    }

    internal static string StreamPath(string path, string name) => name == "::$DATA" ? path : path + name;

    internal static bool NamedStreams(string path)
    {
        // Reuse scratch storage on this synchronous worker. Volume paths and
        // capabilities are still queried afresh: removable/mounted volumes must
        // never inherit a cached filesystem result from an earlier operation.
        var root = volumePathBuffer ??= new StringBuilder(32768);
        root.Clear();
        if (!GetVolumePathNameW(System.IO.Path.GetFullPath(path), root, root.Capacity)) throw Error(path + " (volume)", Marshal.GetLastWin32Error());
        if (!GetVolumeInformationW(root.ToString(), null, 0, out _, out _, out uint flags, null, 0)) throw Error(path + " (filesystem capabilities)", Marshal.GetLastWin32Error());
        return (flags & 0x40000) != 0;
    }

    internal static void RequirePlainDestination(string path)
    {
        string current = System.IO.Path.GetFullPath(path);
        while (current != null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Transfer destinations do not traverse reparse points: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = System.IO.Path.GetDirectoryName(current);
        }
    }

    internal static bool ApplyMetadata(string target, Stamp source, out FileAttributes storageChanges, bool requireSize = false)
    {
        using (var handle = CreateFileW(Long(target), 0x100 /* FILE_WRITE_ATTRIBUTES */, 7, IntPtr.Zero, 3, OpenReparse | Backup, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw Error(target, Marshal.GetLastWin32Error());
            long created = source.Created, accessed = source.Accessed, modified = source.Modified;
            if (!SetFileTime(handle, ref created, ref accessed, ref modified)) throw Error(target, Marshal.GetLastWin32Error());
        }
        const FileAttributes writable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed | FileAttributes.Temporary;
        var current = File.GetAttributes(target);
        var desired = (current & ~writable) | (source.Attributes & writable);
        if (current != desired) File.SetAttributes(target, desired);
        var actual = Inspect(target);
        if (requireSize && actual.Size != source.Size) throw new IOException("The destination length changed during verification: " + target);
        if ((actual.Attributes & writable) != (source.Attributes & writable)) throw new IOException("The destination could not preserve basic file attributes: " + target);
        if (source.Attributes.HasFlag(FileAttributes.Encrypted) && !actual.Attributes.HasFlag(FileAttributes.Encrypted))
            throw new IOException("The destination could not preserve encryption: " + target);
        storageChanges = (actual.Attributes ^ source.Attributes) & (FileAttributes.Compressed | FileAttributes.SparseFile);
        if (source.Attributes.HasFlag(FileAttributes.SparseFile)) storageChanges |= FileAttributes.SparseFile;
        return actual.Created != source.Created || actual.Modified != source.Modified || actual.Accessed != source.Accessed;
    }

    internal static void RemoveCopiedSource(string path, Stamp expected, IReadOnlyList<StreamEntry> streams, Job job)
    {
        job.WaitUntilResumed(); if (job.IsCancelled) throw new OperationCanceledException();
        using var source = new TransferFile(path, removal: true);
        source.Require(expected);
        if (!Streams(path).SequenceEqual(streams)) throw new IOException("A moved source gained or lost streams; it was retained: " + path);
        if (job.IsCancelled) throw new OperationCanceledException();
        source.RemoveOnClose();
    }

    internal static void RemoveIncompleteCopy(string path, Stamp expected)
    {
        try
        {
            using var output = new TransferFile(path, removal: true);
            if (output.Initial.Volume != expected.Volume || output.Initial.Id != expected.Id)
                throw new IOException("The destination was replaced by another owner; its replacement was left alone.");
            if (output.Initial.Attributes.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, output.Initial.Attributes & ~FileAttributes.ReadOnly);
            output.RemoveOnClose();
        }
        catch (IOException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 2 or 3 }) { } // Native copy cancellation already removed its output.
    }

    internal static void RemoveVerifiedSource(string path, string expectedSha256, Job job)
    {
        var initial = Inspect(path);
        if (Streams(path).Any(stream => stream.Name != "::$DATA"))
            throw new IOException("Archive transfer cannot remove a source with alternate streams: " + path);
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] buffer = new byte[1024 * 1024]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                job.WaitUntilResumed(); if (job.IsCancelled) throw new OperationCanceledException();
                hash.AppendData(buffer, 0, count);
            }
            if (Convert.ToHexString(hash.GetHashAndReset()) != expectedSha256)
                throw new IOException("A moved source changed; it was retained: " + path);
        }
        job.WaitUntilResumed(); if (job.IsCancelled) throw new OperationCanceledException();
        using var source = new TransferFile(path, removal: true);
        source.Require(initial);
        if (Streams(path).Any(stream => stream.Name != "::$DATA")) throw new IOException("A moved source gained alternate streams; it was retained: " + path);
        source.RemoveOnClose();
    }

    internal static IOException Error(string path, int error) => new(path + ": " + new Win32Exception(error).Message, new Win32Exception(error));
    internal static string Long(string path) => path.StartsWith(@"\\?\") ? path : path.StartsWith(@"\\") ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    public void Dispose() => Handle.Dispose();

    internal static Stamp ReadStamp(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw Error(path, Marshal.GetLastWin32Error());
        return new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow, ((long)info.SizeHigh << 32) | info.SizeLow,
            info.Created.Value, info.Modified.Value, info.Accessed.Value, (FileAttributes)info.Attributes, info.Links);
    }
    [StructLayout(LayoutKind.Sequential)] struct FileTime { public uint Low, High; public long Value => ((long)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] struct FileInfoNative
    {
        public uint Attributes; public FileTime Created, Accessed, Modified;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StreamData
    {
        public long Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle handle, [Out] byte[] buffer, int count, out int read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFileTime(SafeFileHandle handle, ref long created, ref long accessed, ref long modified);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr data, uint bytes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstStreamW(string path, int level, out StreamData data, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool FindNextStreamW(IntPtr find, out StreamData data);
    [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr find);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumePathNameW(string path, StringBuilder root, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetVolumeInformationW(string root, StringBuilder name, int nameLength, out uint serial, out uint componentLength, out uint flags, StringBuilder filesystem, int filesystemLength);
}
