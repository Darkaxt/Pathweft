using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileExplorer;

/// OPUS-09: details stored inside files (title, tags, artist, rating, ...) edited for many files at once, written
/// through Windows' own property handlers, the ones Explorer's Details tab writes with, so each format keeps its
/// own layout (EXIF/XMP, ID3, Vorbis comments, MP4 and ASF atoms, Office document properties). Only the formats
/// and properties in Families are offered, and of those only what the file's handler says it can write. Nothing
/// is kept beside the file (no alternate streams); read-only and online-only cloud files are left alone.
internal static class EmbeddedMetadata
{
    internal enum Kind { Text, List, Rating, Number }
    internal sealed record Field(string Name, string Label, Kind Kind);

    internal static readonly Field Title = new("System.Title", "Title", Kind.Text), Subject = new("System.Subject", "Subject", Kind.Text),
        Artist = new("System.Music.Artist", "Contributing artists", Kind.List), Album = new("System.Music.AlbumTitle", "Album", Kind.Text),
        AlbumArtist = new("System.Music.AlbumArtist", "Album artist", Kind.Text), Year = new("System.Media.Year", "Year", Kind.Number),
        Track = new("System.Music.TrackNumber", "Track number", Kind.Number), Genre = new("System.Music.Genre", "Genre", Kind.List),
        Authors = new("System.Author", "Authors", Kind.List), Tags = new("System.Keywords", "Tags", Kind.List),
        Rating = new("System.Rating", "Rating", Kind.Rating), Comment = new("System.Comment", "Comments", Kind.Text),
        Copyright = new("System.Copyright", "Copyright", Kind.Text);
    /// In the order the dialog shows them.
    internal static readonly Field[] All = { Title, Subject, Artist, Album, AlbumArtist, Year, Track, Genre, Authors, Tags, Rating, Comment, Copyright };
    internal const string DateTaken = "System.Photo.DateTaken";

    internal enum Family { Photo, Music, Video, Document }
    /// The supported matrix (spec OPUS-09a): format → the properties Pathweft offers for it.
    internal static readonly IReadOnlyDictionary<Family, Field[]> Families = new Dictionary<Family, Field[]>
    {
        [Family.Photo] = new[] { Title, Subject, Rating, Tags, Comment, Authors, Copyright },
        [Family.Music] = new[] { Title, Artist, Album, AlbumArtist, Year, Track, Genre, Rating, Comment },
        [Family.Video] = new[] { Title, Artist, Year, Genre, Rating, Tags, Comment },
        [Family.Document] = new[] { Title, Subject, Tags, Authors, Comment },
    };
    static readonly Dictionary<string, Family> extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = Family.Photo, [".jpeg"] = Family.Photo, [".jfif"] = Family.Photo, [".tif"] = Family.Photo, [".tiff"] = Family.Photo,
        [".mp3"] = Family.Music, [".flac"] = Family.Music, [".m4a"] = Family.Music, [".wma"] = Family.Music,
        [".mp4"] = Family.Video, [".m4v"] = Family.Video, [".wmv"] = Family.Video,
        [".docx"] = Family.Document, [".xlsx"] = Family.Document, [".pptx"] = Family.Document,
    };
    /// What a format's handler reports writable but doesn't keep (found by writing and reading back): not offered.
    static readonly Dictionary<string, string[]> notKept = new(StringComparer.OrdinalIgnoreCase) { [".flac"] = new[] { "System.Rating" } };
    internal static Family? FamilyOf(string path) => extensions.TryGetValue(Path.GetExtension(path), out var f) ? f : null;
    internal static string FamilyName(Family f) => f switch { Family.Photo => "photo", Family.Music => "song", Family.Video => "video", _ => "document" };
    internal const int Limit = 2000; // files per edit: each one is opened through its handler when the dialog opens
    internal static int Stars(uint value) => value == 0 ? 0 : value < 13 ? 1 : value < 38 ? 2 : value < 63 ? 3 : value < 88 ? 4 : 5;
    internal static uint FromStars(int stars) => stars switch { <= 0 => 0, 1 => 1, 2 => 25, 3 => 50, 4 => 75, _ => 99 };

    /// One file as the dialog found it. Problem: why it can't be changed (then it is only listed).
    internal sealed class Item
    {
        public string Path { get; init; }
        public Family? Family { get; init; }
        public string Problem { get; set; }
        public HashSet<string> Writable { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string[]> Values { get; } = new(StringComparer.Ordinal);
        public bool TakenWritable { get; set; }
        public DateTime? Taken { get; set; } // UTC
        public IEnumerable<Field> Fields => Family is { } f ? Families[f].Where(x => Writable.Contains(x.Name)) : Enumerable.Empty<Field>();
    }

    internal enum DateMode { Keep, Set, Shift }
    /// What to change. Fields: only the touched ones (an empty array clears the property). Set: a local time.
    internal sealed record Change(IReadOnlyDictionary<string, string[]> Fields, DateMode Dates, DateTime Set, TimeSpan Shift,
                                  bool Modified, bool Created, bool Taken, bool KeepModified)
    {
        internal bool Empty => Fields.Count == 0 && (Dates == DateMode.Keep || !(Modified || Created || Taken));
    }

    /// Changed: Undo holds the file's earlier values. Error: why it wasn't. Neither: skipped (Note says why).
    internal sealed record Outcome(string Path, bool Changed, string Error, string Note, string Undo, long Stamp);

    // ---- reading ----

    internal static Task<List<Item>> InspectAsync(IReadOnlyList<string> paths) => Sta(() => paths.Select(Inspect).ToList());

    internal static Item Inspect(string path)
    {
        var item = new Item { Path = path, Family = FamilyOf(path) };
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { item.Problem = ex.Message; return item; }
        if ((attributes & FileAttributes.Directory) != 0) { item.Problem = "a folder: folders have no details of their own"; return item; }
        if (item.Family == null) { item.Problem = "Windows can't store these details in a " + Path.GetExtension(path) + " file"; return item; }
        if (((int)attributes & 0x441000) != 0) { item.Problem = "online-only cloud file: left alone, so it isn't downloaded"; return item; } // RECALL_ON_DATA_ACCESS | RECALL_ON_OPEN | OFFLINE
        if ((attributes & FileAttributes.ReadOnly) != 0) { item.Problem = "read-only"; return item; }
        int hr = Open(path, out var store);
        if (hr != 0) { item.Problem = Why(hr); return item; }
        try
        {
            var capabilities = store as IPropertyStoreCapabilities;
            foreach (var field in Families[item.Family.Value])
            {
                var key = Key(field.Name);
                if ((capabilities == null || capabilities.IsPropertyWritable(ref key) == 0) && !(notKept.TryGetValue(Path.GetExtension(path), out var dropped) && dropped.Contains(field.Name))) item.Writable.Add(field.Name);
                if (Get(store, field.Name, field.Kind) is { } value) item.Values[field.Name] = value;
            }
            if (item.Family == Family.Photo)
            {
                var key = Key(DateTaken);
                item.TakenWritable = capabilities == null || capabilities.IsPropertyWritable(ref key) == 0;
                item.Taken = GetTime(store, DateTaken);
            }
            if (item.Writable.Count == 0 && !item.TakenWritable) item.Problem = "Windows can't change the details of this file";
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { item.Problem = ex.Message; }
        finally { Marshal.ReleaseComObject(store); }
        return item;
    }

    // ---- writing ----

    /// Applies the change to each editable item in turn; a stop takes effect between files.
    internal static Task<List<Outcome>> ApplyAsync(IReadOnlyList<Item> items, Change change, Action<int, string> progress, CancellationToken stop) => Sta(() =>
    {
        var outcomes = new List<Outcome>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Problem != null) { outcomes.Add(new(item.Path, false, null, item.Problem, null, 0)); continue; }
            if (stop.IsCancellationRequested) { outcomes.Add(new(item.Path, false, null, "not started: stopped", null, 0)); continue; }
            progress?.Invoke(i, item.Path);
            outcomes.Add(ApplyOne(item, change));
        }
        return outcomes;
    });

    static Outcome ApplyOne(Item item, Change change)
    {
        string path = item.Path;
        var fields = item.Fields.Where(f => change.Fields.ContainsKey(f.Name)).ToList();
        bool dates = change.Dates != DateMode.Keep;
        bool taken = dates && change.Taken && item.TakenWritable && (change.Dates == DateMode.Set || item.Taken != null);
        bool modified = dates && change.Modified, created = dates && change.Created;
        if (fields.Count == 0 && !taken && !modified && !created) return new(path, false, null, "nothing chosen applies to this file", null, 0);
        var undo = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var others = new Dictionary<Field, string[]>();
        DateTime oldModified, oldCreated;
        try { oldModified = File.GetLastWriteTimeUtc(path); oldCreated = File.GetCreationTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(path, false, ex.Message, null, null, 0); }
        undo["@modified"] = new[] { oldModified.ToFileTimeUtc().ToString() };
        undo["@created"] = new[] { oldCreated.ToFileTimeUtc().ToString() };
        if (fields.Count > 0 || taken)
        {
            int hr = Open(path, out var store);
            if (hr != 0) return new(path, false, Why(hr), null, null, 0);
            try
            {
                foreach (var field in item.Fields.Except(fields)) others[field] = Get(store, field.Name, field.Kind) ?? Array.Empty<string>();
                foreach (var field in fields)
                {
                    undo[field.Name] = Get(store, field.Name, field.Kind) ?? Array.Empty<string>();
                    if ((hr = Set(store, field.Name, field.Kind, change.Fields[field.Name])) != 0) return new(path, false, $"{field.Label}: {Why(hr)}", null, null, 0);
                }
                if (taken)
                {
                    var old = GetTime(store, DateTaken);
                    undo["@taken"] = old is { } t ? new[] { t.ToFileTimeUtc().ToString() } : Array.Empty<string>();
                    var value = change.Dates == DateMode.Set ? change.Set.ToUniversalTime() : old.Value + change.Shift;
                    if ((hr = SetTime(store, DateTaken, value)) != 0) return new(path, false, "Date taken: " + Why(hr), null, null, 0);
                }
                if ((hr = store.Commit()) != 0) return new(path, false, Why(hr), null, null, 0);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or FormatException or OverflowException) { return new(path, false, ex.Message, null, null, 0); }
            finally { Marshal.ReleaseComObject(store); }
        }
        // read back: a detail the format's handler accepted but didn't keep is said, not counted as written
        string note = null;
        if (fields.Count > 0 && Open(path, out var check, readOnly: true) == 0)
            try
            {
                var lost = fields.Where(f => !Same(Get(check, f.Name, f.Kind), change.Fields[f.Name], f.Kind)).Select(f => f.Label).ToList();
                if (lost.Count > 0) note = string.Join(", ", lost) + " not kept by this file's format";
                // what the handler changed besides: Undo puts it back as well
                foreach (var (field, was) in others) if (!Same(Get(check, field.Name, field.Kind), was, field.Kind)) undo[field.Name] = was;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { }
            finally { Marshal.ReleaseComObject(check); }
        // the handler rewrote the file, which updates its modified date as in Explorer (unless asked to keep it)
        try
        {
            if (modified) File.SetLastWriteTimeUtc(path, change.Dates == DateMode.Set ? change.Set.ToUniversalTime() : oldModified + change.Shift);
            else if (change.KeepModified) File.SetLastWriteTimeUtc(path, oldModified);
            if (created) File.SetCreationTimeUtc(path, change.Dates == DateMode.Set ? change.Set.ToUniversalTime() : oldCreated + change.Shift);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException) { note = (note == null ? "" : note + "; ") + "dates not changed: " + ex.Message; }
        if (dates && change.Taken && !taken && item.Family == Family.Photo) note ??= "no date taken to shift";
        int skipped = item.Family is { } family ? change.Fields.Keys.Count(name => Families[family].Any(f => f.Name == name) && !item.Writable.Contains(name)) : 0;
        if (skipped > 0) note ??= $"{skipped} of the details can't be stored in this file";
        return new(path, true, null, note, JsonSerializer.Serialize(undo), Ferry.Stamp(path));
    }

    /// Undo: puts back the values and dates a change found. Null when done, else why not.
    internal static string Restore(string path, string undo) => Sta(() =>
    {
        Dictionary<string, string[]> values;
        try { values = JsonSerializer.Deserialize<Dictionary<string, string[]>>(undo ?? "") ?? new(); }
        catch (JsonException) { return "the earlier details weren't recorded readably"; }
        var fields = All.Where(f => values.ContainsKey(f.Name)).ToList();
        if (fields.Count > 0 || values.ContainsKey("@taken"))
        {
            int hr = Open(path, out var store);
            if (hr != 0) return Why(hr);
            try
            {
                foreach (var field in fields) if ((hr = Set(store, field.Name, field.Kind, values[field.Name])) != 0) return $"{field.Label}: {Why(hr)}";
                if (values.TryGetValue("@taken", out var taken))
                    if ((hr = taken.Length == 0 ? Clear(store, DateTaken) : SetTime(store, DateTaken, DateTime.FromFileTimeUtc(long.Parse(taken[0])))) != 0) return "Date taken: " + Why(hr);
                if ((hr = store.Commit()) != 0) return Why(hr);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or FormatException) { return ex.Message; }
            finally { Marshal.ReleaseComObject(store); }
            // some handlers (FLAC) drop part of several removals saved together, never one saved alone
            var notBack = Differing(path, fields, values);
            foreach (var field in notBack)
            {
                if (Open(path, out var one) != 0) break;
                try { if (Set(one, field.Name, field.Kind, values[field.Name]) == 0) one.Commit(); }
                catch (Exception ex) when (ex is COMException or InvalidCastException) { }
                finally { Marshal.ReleaseComObject(one); }
            }
            if (notBack.Count > 0 && Differing(path, notBack, values) is { Count: > 0 } still) return string.Join(", ", still.Select(f => f.Label)) + " not put back";
        }
        try
        {
            if (values.TryGetValue("@modified", out var m)) File.SetLastWriteTimeUtc(path, DateTime.FromFileTimeUtc(long.Parse(m[0])));
            if (values.TryGetValue("@created", out var c)) File.SetCreationTimeUtc(path, DateTime.FromFileTimeUtc(long.Parse(c[0])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return "dates: " + ex.Message; }
        return null;
    }).GetAwaiter().GetResult();

    // ---- the property system ----

    static readonly Dictionary<string, ShellProperties.PropertyKey> keys = new(StringComparer.Ordinal);
    static ShellProperties.PropertyKey Key(string name)
    {
        lock (keys)
        {
            if (keys.TryGetValue(name, out var known)) return known;
            Marshal.ThrowExceptionForHR(PSGetPropertyKeyFromName(name, out var key));
            return keys[name] = key;
        }
    }

    static List<Field> Differing(string path, List<Field> fields, Dictionary<string, string[]> values)
    {
        if (fields.Count == 0 || Open(path, out var check, readOnly: true) != 0) return new();
        try { return fields.Where(f => !Same(Get(check, f.Name, f.Kind), values[f.Name], f.Kind)).ToList(); }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return new(); }
        finally { Marshal.ReleaseComObject(check); }
    }

    static bool Same(string[] stored, string[] wanted, Kind kind)
    {
        static string Norm(IEnumerable<string> v) => string.Join("\u001F", (v ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0));
        if (kind is Kind.Rating) return Stars(uint.TryParse(stored?.FirstOrDefault(), out uint a) ? a : 0) == Stars(uint.TryParse(wanted.FirstOrDefault(), out uint b) ? b : 0);
        if (kind is Kind.Text) return Norm(new[] { string.Join("; ", stored ?? Array.Empty<string>()) }) == Norm(new[] { string.Join("; ", wanted) });
        return Norm(stored) == Norm(wanted);
    }

    static int Open(string path, out IWritablePropertyStore store, bool readOnly = false)
    {
        var iid = typeof(IWritablePropertyStore).GUID;
        int hr = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, readOnly ? 0x1 /*GPS_HANDLERPROPERTIESONLY*/ : 0x2 /*GPS_READWRITE*/, ref iid, out store);
        return hr == 0 && store == null ? unchecked((int)0x80004005) : hr;
    }

    static string Why(int hr) => unchecked((uint)hr) switch
    {
        0x80070020 or 0x80070021 or 0x80030020 or 0x80030021 => "in use by another program",
        0x80070005 or 0x80030005 => "access denied",
        0x80070002 or 0x80070003 => "not found",
        0x80070070 => "the disk is full",
        _ => Marshal.GetExceptionForHR(hr)?.Message is { Length: > 0 } m ? m.TrimEnd('.', ' ', '\r', '\n') : $"error 0x{hr:X8}",
    };

    static string[] Get(IWritablePropertyStore store, string name, Kind kind)
    {
        var key = Key(name);
        if (store.GetValue(ref key, out var value) != 0) return null;
        try
        {
            if (value.Type == 0 || value.Type == 1) return null; // VT_EMPTY, VT_NULL
            switch (kind)
            {
                case Kind.Number:
                case Kind.Rating:
                    return PropVariantToUInt32(ref value, out uint number) == 0 ? new[] { number.ToString() } : null;
                case Kind.List:
                    if (PropVariantToStringVectorAlloc(ref value, out var array, out uint count) != 0) return null;
                    var list = new string[count];
                    for (int i = 0; i < count; i++) { var p = Marshal.ReadIntPtr(array, i * IntPtr.Size); list[i] = Marshal.PtrToStringUni(p) ?? ""; Marshal.FreeCoTaskMem(p); }
                    Marshal.FreeCoTaskMem(array);
                    return list;
                default:
                    if (PropVariantToStringAlloc(ref value, out var text) != 0) return null;
                    try { return new[] { Marshal.PtrToStringUni(text) ?? "" }; } finally { Marshal.FreeCoTaskMem(text); }
            }
        }
        finally { PropVariantClear(ref value); }
    }

    static DateTime? GetTime(IWritablePropertyStore store, string name)
    {
        var key = Key(name);
        if (store.GetValue(ref key, out var value) != 0) return null;
        try { return value.Type == 64 && PropVariantToFileTime(ref value, 0 /*PSTF_UTC*/, out long ft) == 0 ? DateTime.FromFileTimeUtc(ft) : null; }
        finally { PropVariantClear(ref value); }
    }

    static int Set(IWritablePropertyStore store, string name, Kind kind, string[] values)
    {
        values = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
        if (values.Length == 0) return Clear(store, name, kind);
        var key = Key(name);
        PropVariant value = default;
        try
        {
            switch (kind)
            {
                case Kind.List:
                    int hr = InitPropVariantFromStringVector(values, (uint)values.Length, out value);
                    if (hr != 0) return hr;
                    break;
                case Kind.Number:
                case Kind.Rating:
                    value.Type = 19; value.A = (IntPtr)(long)uint.Parse(values[0]); break; // VT_UI4
                default:
                    value.Type = 31; value.A = Marshal.StringToCoTaskMemUni(string.Join("; ", values)); break; // VT_LPWSTR
            }
            return store.SetValue(ref key, ref value);
        }
        finally { PropVariantClear(ref value); }
    }

    static int SetTime(IWritablePropertyStore store, string name, DateTime utc)
    {
        var key = Key(name);
        var value = new PropVariant { Type = 64, A = (IntPtr)utc.ToFileTimeUtc() }; // VT_FILETIME
        return store.SetValue(ref key, ref value);
    }

    /// VT_EMPTY removes the property; a handler that refuses it gets an empty text or list, or no stars, instead.
    static int Clear(IWritablePropertyStore store, string name, Kind? kind = null)
    {
        var key = Key(name);
        var value = new PropVariant();
        int hr = store.SetValue(ref key, ref value);
        if (hr == 0 || kind is not (Kind.Text or Kind.List or Kind.Rating)) return hr;
        try
        {
            if (kind == Kind.Rating) { value.Type = 19; value.A = IntPtr.Zero; } // unrated
            else if (kind == Kind.List) { if (InitPropVariantFromStringVector(Array.Empty<string>(), 0, out value) != 0) return hr; }
            else { value.Type = 31; value.A = Marshal.StringToCoTaskMemUni(""); }
            return store.SetValue(ref key, ref value);
        }
        finally { PropVariantClear(ref value); }
    }

    /// Property handlers are written for the shell's apartment: every read and write runs on one STA thread of its own.
    static Task<T> Sta<T>(Func<T> run)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { done.SetResult(run()); } catch (Exception ex) { done.SetException(ex); } }) { IsBackground = true, Name = "Pathweft metadata" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Type; ushort r1, r2, r3; public IntPtr A, B; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IWritablePropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out ShellProperties.PropertyKey key);
        [PreserveSig] int GetValue(ref ShellProperties.PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref ShellProperties.PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("C8E2D566-186E-4D49-BF41-6909EAD56ACC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStoreCapabilities
    {
        [PreserveSig] int IsPropertyWritable(ref ShellProperties.PropertyKey key);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bind, int flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IWritablePropertyStore store);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int PSGetPropertyKeyFromName(string name, out ShellProperties.PropertyKey key);
    [DllImport("propsys.dll")] static extern int PropVariantToUInt32(ref PropVariant value, out uint number);
    [DllImport("propsys.dll")] static extern int PropVariantToFileTime(ref PropVariant value, int flags, out long fileTime);
    [DllImport("propsys.dll")] static extern int PropVariantToStringAlloc(ref PropVariant value, out IntPtr text);
    [DllImport("propsys.dll")] static extern int PropVariantToStringVectorAlloc(ref PropVariant value, out IntPtr array, out uint count);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int InitPropVariantFromStringVector([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values, uint count, out PropVariant value);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
}
