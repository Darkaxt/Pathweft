using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FileExplorer.StartMenu;

/// SAB-02 (S6b): the Shell work of the owned menu on one STA thread of its own: enumerations, icons, shortcut and
/// jump-list reads and launches. The helper's UI thread also runs the low-level hooks, so it never waits on the Shell.
internal static class ShellWorker
{
    static readonly BlockingCollection<Action> work = new();

    static ShellWorker()
    {
        var thread = new Thread(() => { foreach (var job in work.GetConsumingEnumerable()) job(); }) { IsBackground = true, Name = "Start menu shell" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal static Task<T> Run<T>(Func<T> job)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() =>
        {
            try { done.SetResult(job()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    internal static Task Run(Action job) => Run(() => { job(); return true; });
}

/// What the menu shows and opens: a program, a folder, a document, a Shell location or URI, or a command.
internal enum EntryKind { Program, Folder, Document, Shell, Command }

internal sealed record Entry(string Name, EntryKind Kind, string Target)
{
    internal string Arguments { get; init; }
    internal string WorkingDirectory { get; init; }
    /// Where the icon comes from: a parsing name for the Shell (Target when null), or "file,index" for a jump-list task.
    internal string Icon { get; init; }
    /// A submenu: All Programs folders, Computer's drives, Recent items, a folder shown as a menu.
    internal Func<IReadOnlyList<Entry>> Children { get; init; }
    internal bool New { get; init; }
    internal string Tip { get; init; }
    internal Action Invoke { get; init; }
    /// A jump-list link without a file path (a Shell item), as its ITEMIDLIST.
    internal byte[] IdList { get; init; }
    /// A program's AppUserModelID (whose jump list it has); for a jump-list item, the program that opens it.
    internal string Owner { get; init; }
}

internal static class StartMenuShell
{
    internal const string ThisPC = PaneView.ThisPC;

    // ---- opening: SAB-06 folder routing to Pathweft, everything else as Windows' menu opens it ----

    /// Folders, drives and This PC open in Pathweft (a new tab in the running window, or a new window); a file
    /// path opens its folder with the file selected. Never through Explorer.
    internal static void Route(string path)
    {
        AllowSetForegroundWindow(-1);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (Program.Running && Program.HandOff(path)) return;
            try { using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath) { ArgumentList = { "--front", path }, UseShellExecute = false }); }
            catch (System.ComponentModel.Win32Exception) { }
        });
    }

    /// Opens what an entry stands for. Returns a message when it cannot be opened (the item is gone), else null.
    internal static Task<string> Open(Entry e)
    {
        if (e.Kind == EntryKind.Command) { e.Invoke?.Invoke(); return Task.FromResult<string>(null); }
        if (e.Kind == EntryKind.Folder && (e.Target == ThisPC || PaneView.IsRemote(e.Target))) { Route(e.Target); return Task.FromResult<string>(null); }
        AllowSetForegroundWindow(-1);
        return ShellWorker.Run(() =>
        {
            string target = e.Target;
            if (e.IdList == null && IsFilePath(target))
            {
                if (!PaneView.IsRemote(target))
                {
                    if (Directory.Exists(target)) { Route(target); return null; }
                    if (!File.Exists(target)) return $"{e.Name} is no longer there.";
                }
                if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && MainWindow.ShortcutFolder(target) is { } folder) { Route(folder); return null; }
            }
            int error = Execute(target, e.Arguments, e.WorkingDirectory, e.IdList, e.Kind == EntryKind.Program);
            return error is 0 or 1223 ? null : $"{e.Name} could not be opened ({new System.ComponentModel.Win32Exception(error).Message})";
        });
    }

    static bool IsFilePath(string s) => s != null && (s.StartsWith(@"\\") || s.Length > 2 && s[1] == ':' && s[2] == '\\');

    /// ShellExecuteEx as the Windows menu launches: programs with usage logging, so they count for the recent
    /// programs list; Shell locations (::{...}, shell:...) through their ITEMIDLIST.
    internal static int Execute(string target, string arguments = null, string directory = null, byte[] idList = null, bool logUsage = false, string verb = null)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (idList != null) { pidl = Marshal.AllocCoTaskMem(idList.Length); Marshal.Copy(idList, 0, pidl, idList.Length); }
            else if (target.StartsWith("::") || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                if (SHParseDisplayName(target, IntPtr.Zero, out pidl, 0, out _) != 0) pidl = IntPtr.Zero;
            }
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), fMask = (pidl != IntPtr.Zero ? 0xCu : 0u) | (logUsage ? 0x04000000u : 0u) | 0x400u /* no error dialog: the menu says it */,
                lpVerb = verb, lpFile = pidl != IntPtr.Zero ? null : target, lpParameters = arguments, lpDirectory = directory, lpIDList = pidl, nShow = 1,
            };
            return ShellExecuteEx(ref info) ? 0 : Marshal.GetLastWin32Error();
        }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }

    // ---- icons ----

    /// A frozen icon at the given device-pixel size, from the Shell or from a "file,index" icon location.
    internal static Task<BitmapSource> Icon(Entry e, int pixels) => ShellWorker.Run(() =>
    {
        string source = e.Icon ?? e.Target;
        if (source == null) return null;
        if (source.LastIndexOf(',') is var comma and > 2 && int.TryParse(source[(comma + 1)..], out int index) && !source.StartsWith("::"))
        {
            var file = Environment.ExpandEnvironmentVariables(source[..comma].Trim('"'));
            var icons = new IntPtr[1];
            if (PrivateExtractIcons(file, index, pixels, pixels, icons, null, 1, 0) == 1 && icons[0] != IntPtr.Zero)
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHIcon(icons[0], System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    return image;
                }
                finally { DestroyIcon(icons[0]); }
            source = file;
        }
        return Thumbnails.Get(source, pixels, iconOnly: true);
    });

    // ---- Shell items ----

    /// The display name the Shell gives a file or a Shell location (localized shortcut names, "File Explorer").
    internal static string DisplayName(string parsing)
    {
        if (SHCreateItemFromParsingName(parsing, IntPtr.Zero, typeof(IShellItem).GUID, out var item) != 0) return null;
        try { return Name(item, 0); }
        finally { Marshal.ReleaseComObject(item); }
    }

    /// The children of a Shell folder (shell:AppsFolder, All Control Panel Items...), as display name and absolute
    /// parsing name, and the name relative to the folder (an app's AppUserModelID).
    internal static List<(string Name, string Parsing, string Relative)> Children(string parsing)
    {
        var list = new List<(string, string, string)>();
        if (SHCreateItemFromParsingName(parsing, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) != 0) return list;
        try
        {
            var bhid = new Guid("94f60519-2850-4924-aa5a-d15e84868039"); var iid = typeof(IEnumShellItems).GUID;
            if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var e) != 0) return list;
            var items = (IEnumShellItems)e;
            try
            {
                var one = new IShellItem[1];
                while (items.Next(1, one, out uint got) == 0 && got == 1)
                {
                    try { list.Add((Name(one[0], 0), Name(one[0], unchecked((int)0x80028000)), Name(one[0], unchecked((int)0x80018001)))); }
                    finally { Marshal.ReleaseComObject(one[0]); }
                }
            }
            finally { Marshal.ReleaseComObject(items); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        return list;
    }

    static string Name(IShellItem item, int sigdn)
    {
        if (item.GetDisplayName(sigdn, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    internal static string KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    internal static string IndirectString(string s)
    {
        if (string.IsNullOrEmpty(s) || s[0] != '@') return s;
        var buffer = new StringBuilder(1024);
        return SHLoadIndirectString(s, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : s;
    }

    // ---- shortcuts ----

    internal sealed record Link(string Path, string Arguments, string Directory, string Title, string Description, string Icon, byte[] IdList, string AppId);

    internal static Link ReadShortcut(string file)
    {
        var link = (MainWindow.IShellLinkW)new MainWindow.ShellLink();
        try
        {
            ((IPersistFile)link).Load(file, 0);
            return Read(link);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
        finally { Marshal.ReleaseComObject(link); }
    }

    static Link Read(MainWindow.IShellLinkW link)
    {
        var buffer = Marshal.AllocHGlobal(2 * 2048);
        try
        {
            string Get(Action<IntPtr> read) { Marshal.WriteInt16(buffer, 0); try { read(buffer); } catch (COMException) { } return Marshal.PtrToStringUni(buffer); }
            string path = Get(b => link.GetPath(b, 2048, IntPtr.Zero, 0));
            string args = Get(b => link.GetArguments(b, 2048));
            string dir = Get(b => link.GetWorkingDirectory(b, 2048));
            string description = Get(b => link.GetDescription(b, 2048));
            int index = 0;
            string icon = Get(b => link.GetIconLocation(b, 2048, out index));
            byte[] idList = null;
            link.GetIDList(out var pidl);
            if (pidl != IntPtr.Zero)
                try { int size = ILGetSize(pidl); idList = new byte[size]; Marshal.Copy(pidl, idList, 0, size); }
                finally { Marshal.FreeCoTaskMem(pidl); }
            string title = null, appId = null;
            if (link is IPropertyStore store)
            {
                title = StringProperty(store, new PROPERTYKEY(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2));
                appId = StringProperty(store, new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5));
            }
            return new Link(Empty(path), Empty(args), Empty(dir), Empty(IndirectString(title)), Empty(IndirectString(description)),
                string.IsNullOrEmpty(icon) ? null : icon + "," + index, idList, Empty(appId));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    static string StringProperty(IPropertyStore store, PROPERTYKEY key)
    {
        var value = new PROPVARIANT();
        try
        {
            if (store.GetValue(ref key, out value) != 0 || value.vt != 31) return null;
            return Marshal.PtrToStringUni(value.p);
        }
        finally { PropVariantClear(ref value); }
    }

    // ---- jump lists (SAB-02): the program's pinned and recent items and its own tasks and categories ----

    internal sealed record JumpCategory(string Name, IReadOnlyList<Entry> Items);

    /// The AppUserModelID Windows files a program's jump list under: the shortcut's own, or the implicit one made
    /// from its program's path with the known folder replaced by its id.
    internal static string AppIdOf(Link link)
    {
        if (link == null) return null;
        if (link.AppId != null) return link.AppId;
        if (link.Path == null || !link.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var (id, folder) in ImplicitFolders.Value)
            if (folder != null && link.Path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
                return "{" + id.ToString().ToUpperInvariant() + "}" + link.Path[folder.Length..];
        return link.Path;
    }

    static readonly Lazy<List<(Guid Id, string Folder)>> ImplicitFolders = new(() => new[]
    {
        "1AC14E77-02E7-4E5D-B744-2EB1AE5198B7", // System
        "D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27", // SystemX86
        "6365D5A7-0F0D-45E5-87F6-0DA56B6A4F7D", // ProgramFilesCommonX64
        "DE974D24-D9C6-4D3E-BF91-F4455120B917", // ProgramFilesCommonX86
        "6D809377-6AF0-444B-8957-A3773F02200E", // ProgramFilesX64
        "7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E", // ProgramFilesX86
        "5CD7AEE2-2219-4A67-B85D-6C9CE15660CB", // UserProgramFiles
        "F38BF404-1D43-42F2-9305-67DE0B28FC23", // Windows
    }.Select(g => (new Guid(g), KnownFolder(new Guid(g))?.TrimEnd('\\'))).OrderByDescending(x => x.Item2?.Length ?? 0).ToList());

    /// The jump-list files' name: a CRC-64 of the upper-case AppUserModelID in UTF-16 (table polynomial
    /// 0x92C64265D32139A4, initial value all ones, no final xor), checked against the files Windows writes.
    internal static string AppIdHash(string appId)
    {
        ulong crc = ulong.MaxValue;
        foreach (byte b in Encoding.Unicode.GetBytes(appId.ToUpperInvariant()))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x92C64265D32139A4 : crc >> 1;
        }
        return crc.ToString("x16");
    }

    static string Recent => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    internal static bool HasJumpList(string appId)
    {
        if (appId == null) return false;
        string hash = AppIdHash(appId);
        return File.Exists(Path.Combine(Recent, "AutomaticDestinations", hash + ".automaticDestinations-ms"))
            || File.Exists(Path.Combine(Recent, "CustomDestinations", hash + ".customDestinations-ms"));
    }

    /// Pinned, then the program's custom categories and tasks, then Recent (or Frequent when the program asks for
    /// it), the way the Windows 7 menu shows a jump list.
    internal static List<JumpCategory> JumpList(string appId, string owner)
    {
        var result = new List<JumpCategory>();
        if (appId == null) return result;
        string hash = AppIdHash(appId);
        var (pinned, recent) = Automatic(Path.Combine(Recent, "AutomaticDestinations", hash + ".automaticDestinations-ms"), owner);
        var custom = Custom(Path.Combine(Recent, "CustomDestinations", hash + ".customDestinations-ms"), owner);
        int max = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "JumpListItems_Maximum", 10) is int m and > 0 ? m : 10;
        if (pinned.Count > 0) result.Add(new("Pinned", pinned));
        bool knownShown = false;
        foreach (var (name, known, items) in custom)
        {
            if (known != 0)
            {
                var list = (known == 1 ? recent.OrderByDescending(x => x.Score).Select(x => x.Entry) : recent.Select(x => x.Entry))
                    .Where(x => !pinned.Any(p => SameTarget(p, x))).Take(max).ToList();
                if (list.Count > 0) result.Add(new(known == 1 ? "Frequent" : "Recent", list));
                knownShown = true;
            }
            else if (items.Count > 0) result.Add(new(name, items));
        }
        if (!knownShown)
        {
            var list = recent.Select(x => x.Entry).Where(x => !pinned.Any(p => SameTarget(p, x))).Take(max).ToList();
            if (list.Count > 0) result.Insert(pinned.Count > 0 ? 1 : 0, new("Recent", list));
        }
        // Tasks come last, as in Windows
        int tasks = result.FindIndex(c => c.Name == "Tasks");
        if (tasks >= 0 && tasks != result.Count - 1) { var t = result[tasks]; result.RemoveAt(tasks); result.Add(t); }
        return result;
    }

    static bool SameTarget(Entry a, Entry b) => a.Target != null && string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase) && a.Arguments == b.Arguments;

    /// An automaticDestinations-ms file is a compound file: its DestList stream lists the entries most recent first
    /// (Windows 10 layout, version 3 and later: 130 fixed bytes, the path, 4 more), each with its pin position and the
    /// name of the stream holding its shortcut. Read from a copy in memory, so Windows' file is never locked.
    static (List<Entry> Pinned, List<(Entry Entry, float Score)> Recent) Automatic(string file, string owner)
    {
        var pinned = new List<(int Order, Entry Entry)>(); var recent = new List<(Entry, float)>();
        if (!File.Exists(file)) return ([], recent);
        IStorage storage = null;
        try
        {
            byte[] bytes;
            using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { bytes = new byte[s.Length]; s.ReadExactly(bytes); }
            var global = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, global, bytes.Length);
            if (CreateILockBytesOnHGlobal(global, true, out var lockBytes) != 0) { Marshal.FreeHGlobal(global); return ([], recent); }
            if (StgOpenStorageOnILockBytes(lockBytes, null, 0x10 /* read, share exclusive */, IntPtr.Zero, 0, out storage) != 0) return ([], recent);
            var list = ReadStream(storage, "DestList");
            if (list == null || list.Length < 32 || BitConverter.ToInt32(list, 0) < 3) return ([], recent);
            int pos = 32;
            while (pos + 130 <= list.Length)
            {
                uint id = BitConverter.ToUInt32(list, pos + 88);
                float score = BitConverter.ToSingle(list, pos + 96);
                int pin = BitConverter.ToInt32(list, pos + 108);
                int length = BitConverter.ToUInt16(list, pos + 128);
                pos += 130 + length * 2 + 4;
                var data = ReadStream(storage, id.ToString("x"));
                if (data == null) continue;
                var link = LinkFrom(data);
                if (link == null) continue;
                var entry = JumpEntry(link, owner);
                if (entry == null) continue;
                if (pin >= 0) pinned.Add((pin, entry)); else recent.Add((entry, score));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException) { }
        finally { if (storage != null) Marshal.ReleaseComObject(storage); }
        return (pinned.OrderBy(p => p.Order).Select(p => p.Entry).ToList(), recent);
    }

    static byte[] ReadStream(IStorage storage, string name)
    {
        if (storage.OpenStream(name, IntPtr.Zero, 0x10, 0, out var stream) != 0) return null;
        try
        {
            stream.Stat(out var stat, 1);
            var data = new byte[stat.cbSize];
            stream.Read(data, data.Length, IntPtr.Zero);
            return data;
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    static Link LinkFrom(byte[] data)
    {
        var stream = SHCreateMemStream(data, data.Length);
        if (stream == null) return null;
        try
        {
            var link = (MainWindow.IShellLinkW)new MainWindow.ShellLink();
            try { ((IPersistStream)link).Load(stream); return Read(link); }
            catch (COMException) { return null; }
            finally { Marshal.ReleaseComObject(link); }
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    /// A customDestinations-ms file: version 2, the category count, then each category (0 = the program's own,
    /// named; 1 = a known one, 1 Frequent or 2 Recent; 2 = Tasks), its objects as CLSID + persisted stream, and
    /// the 0xBABFFBAB end mark.
    static List<(string Name, int Known, List<Entry> Items)> Custom(string file, string owner)
    {
        var result = new List<(string, int, List<Entry>)>();
        if (!File.Exists(file)) return result;
        IStream stream = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            stream = SHCreateMemStream(bytes, bytes.Length);
            if (stream == null) return result;
            var four = new byte[4];
            uint U32() { stream.Read(four, 4, IntPtr.Zero); return BitConverter.ToUInt32(four, 0); }
            if (U32() != 2) return result;
            uint categories = U32(); U32();
            for (uint c = 0; c < categories; c++)
            {
                uint type = U32();
                string name = null; int known = 0; uint count = 0;
                if (type == 0)
                {
                    stream.Read(four, 2, IntPtr.Zero);
                    int chars = BitConverter.ToUInt16(four, 0);
                    var text = new byte[chars * 2];
                    stream.Read(text, text.Length, IntPtr.Zero);
                    name = IndirectString(Encoding.Unicode.GetString(text));
                    count = U32();
                }
                else if (type == 1) { known = (int)U32(); }
                else if (type == 2) { name = "Tasks"; count = U32(); }
                else return result;
                var items = new List<Entry>();
                for (uint i = 0; i < count; i++)
                {
                    var clsid = new byte[16];
                    stream.Read(clsid, 16, IntPtr.Zero);
                    var type2 = Type.GetTypeFromCLSID(new Guid(clsid));
                    object obj = type2 == null ? null : Activator.CreateInstance(type2);
                    if (obj is not IPersistStream persist) return result;
                    try
                    {
                        persist.Load(stream);
                        if (obj is MainWindow.IShellLinkW link) { if (JumpEntry(Read(link), owner) is { } entry) items.Add(entry); }
                        else if (obj is IShellItem item && Name(item, unchecked((int)0x80028000)) is { } parsing)
                            items.Add(new Entry(Name(item, 0) ?? parsing, IsFilePath(parsing) && !Path.HasExtension(parsing) ? EntryKind.Folder : EntryKind.Document, parsing) { Owner = owner });
                        // a Tasks separator is a shell link without a path or a title, left out
                    }
                    finally { Marshal.ReleaseComObject(obj); }
                }
                if (U32() != 0xBABFFBAB) return result;
                if (known is 1 or 2 || items.Count > 0 || type == 0) result.Add((name, known, items));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidCastException) { }
        finally { if (stream != null) Marshal.ReleaseComObject(stream); }
        return result;
    }

    static Entry JumpEntry(Link link, string owner)
    {
        if (link == null) return null;
        bool task = link.Arguments != null || link.Title != null;
        string path = link.Path;
        if (path == null && link.IdList == null) return null;
        string name = link.Title ?? (path != null && Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : path) ?? link.Description;
        if (string.IsNullOrEmpty(name)) return null;
        // no disk access for a network path here: a server that is off answers late
        bool folder = path != null && (PaneView.IsRemote(path) ? !Path.HasExtension(path) : Directory.Exists(path));
        var kind = task ? EntryKind.Shell : folder ? EntryKind.Folder : EntryKind.Document;
        return new Entry(name, kind, path ?? name)
        {
            Arguments = link.Arguments, WorkingDirectory = link.Directory, IdList = path == null || task ? link.IdList : null,
            Icon = task ? link.Icon ?? path : path, Tip = link.Description ?? path, Owner = owner,
        };
    }

    // ---- interop ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow; public IntPtr hInstApp, lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
        public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY(Guid fmtid, uint pid) { public Guid Fmtid = fmtid; public uint Pid = pid; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT { public ushort vt; ushort r1, r2, r3; public IntPtr p; IntPtr p2; }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    internal interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(int sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IShellItem[] items, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems copy);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000109-0000-0000-C000-000000000046")]
    interface IPersistStream
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load(IStream stream);
        void Save(IStream stream, bool clearDirty);
        void GetSizeMax(out long size);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000000b-0000-0000-C000-000000000046")]
    interface IStorage
    {
        [PreserveSig] int CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out IStream stream);
        [PreserveSig] int OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr r1, uint mode, uint r2, out IStream stream);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000000a-0000-0000-C000-000000000046")]
    interface ILockBytes { }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("shell32.dll")] static extern int ILGetSize(IntPtr pidl);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);
    [DllImport("shlwapi.dll")] static extern IStream SHCreateMemStream(byte[] data, int size);
    [DllImport("ole32.dll")] static extern int CreateILockBytesOnHGlobal(IntPtr global, bool deleteOnRelease, out ILockBytes bytes);
    [DllImport("ole32.dll")] static extern int StgOpenStorageOnILockBytes(ILockBytes bytes, IStorage priority, uint mode, IntPtr exclude, uint reserved, out IStorage storage);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
}
