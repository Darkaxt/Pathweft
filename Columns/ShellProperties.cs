using System.Runtime.InteropServices;

namespace FileExplorer;

// COLS-02: Windows' property system, as Explorer's Choose Details reads it. The list is every property
// description registered for columns; values come from each file's property store, read the way Explorer's
// Details view reads them (a cloud placeholder: cached properties only, so it is never hydrated).
internal static class ShellProperties
{
    internal sealed record Description(string Name, string Label, string Group, int Width, PropertyKey Key);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey { public Guid Format; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Type; ushort r1, r2, r3; public IntPtr A, B; }

    static readonly object gate = new();
    static List<Description> all;
    static readonly Dictionary<string, Description> byName = new(StringComparer.OrdinalIgnoreCase);

    /// Every property Windows offers as a column, grouped by its namespace (System.Music.Artist: Music), labelled.
    internal static IReadOnlyList<Description> All()
    {
        lock (gate)
        {
            if (all != null) return all;
            var list = new List<Description>();
            try
            {
                var iid = typeof(IPropertyDescriptionList).GUID;
                if (PSEnumeratePropertyDescriptions(6 /*PDEF_COLUMN*/, ref iid, out var descriptions) == 0)
                {
                    descriptions.GetCount(out uint count);
                    var descriptionId = typeof(IPropertyDescription).GUID;
                    for (uint i = 0; i < count; i++)
                    {
                        if (descriptions.GetAt(i, ref descriptionId, out var d) != 0 || d == null) continue;
                        try { if (Describe(d) is { } item) list.Add(item); }
                        finally { Marshal.ReleaseComObject(d); }
                    }
                    Marshal.ReleaseComObject(descriptions);
                }
            }
            catch (Exception error) when (error is COMException or InvalidCastException) { }
            all = list.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                      .OrderBy(d => d.Group, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var d in all) byName[d.Name] = d;
            return all;
        }
    }

    /// One property by its canonical name (System.Author), or null when Windows here has no such property.
    internal static Description Find(string name)
    {
        lock (gate) if (byName.TryGetValue(name, out var known)) return known;
        try
        {
            if (PSGetPropertyKeyFromName(name, out var key) != 0) return null;
            var iid = typeof(IPropertyDescription).GUID;
            if (PSGetPropertyDescription(ref key, ref iid, out var d) != 0 || d == null) return null;
            try { var item = Describe(d, allowHidden: true); if (item != null) lock (gate) byName[name] = item; return item; }
            finally { Marshal.ReleaseComObject(d); }
        }
        catch (Exception error) when (error is COMException or InvalidCastException) { return null; }
    }

    static Description Describe(IPropertyDescription d, bool allowHidden = false)
    {
        d.GetPropertyKey(out var key);
        if (d.GetCanonicalName(out var namePtr) != 0) return null;
        string name = Take(namePtr);
        string label = d.GetDisplayName(out var labelPtr) == 0 ? Take(labelPtr) : "";
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrEmpty(name)) return null;
        d.GetViewFlags(out uint view);
        if (!allowHidden && (view & 0x800) != 0) return null; // PDVF_HIDDEN
        d.GetDefaultColumnWidth(out uint chars);
        var parts = name.Split('.');
        string group = parts.Length >= 3 ? parts[1] switch { "GPS" => "GPS", "DRM" => "DRM", var g => g } : "General";
        return new(name, label, group, (int)Math.Clamp(chars == 0 ? 20 : chars, 8, 60) * 7, key);
    }

    static string Take(IntPtr p) { try { return Marshal.PtrToStringUni(p) ?? ""; } finally { Marshal.FreeCoTaskMem(p); } }

    /// The named properties of one file: display text and a value to sort by (a number, a date or the text).
    /// `cachedOnly`: a cloud placeholder, read from the cached properties without opening its content.
    internal static Dictionary<string, (string Text, IComparable Sort)> Read(string path, IReadOnlyCollection<Description> wanted, bool cachedOnly)
    {
        var result = new Dictionary<string, (string, IComparable)>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return result;
        var iid = typeof(IPropertyStore).GUID;
        // GPS_DEFAULT | GPS_BESTEFFORT (0x40); a placeholder: GPS_FASTPROPERTIESONLY (0x8; 0x2 would be GPS_READWRITE)
        if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, cachedOnly ? 0x8 : 0x40, ref iid, out var store) != 0 || store == null) return result;
        try
        {
            foreach (var d in wanted)
            {
                var key = d.Key;
                if (store.GetValue(ref key, out var value) != 0) continue;
                try
                {
                    if (value.Type == 0) continue; // VT_EMPTY
                    string text = PSFormatForDisplayAlloc(ref key, ref value, 0, out var shown) == 0 ? Take(shown) : "";
                    IComparable sort = text;
                    if (value.Type is 64 /*VT_FILETIME*/ && PropVariantToFileTime(ref value, 1 /*PSTF_LOCAL*/, out long ft) == 0) sort = ft;
                    else if (value.Type is >= 2 and <= 5 or >= 16 and <= 23 or 11 && PropVariantToDouble(ref value, out double number) == 0) sort = number;
                    result[d.Name] = (text, sort);
                }
                finally { PropVariantClear(ref value); }
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException) { }
        finally { Marshal.ReleaseComObject(store); }
        return result;
    }

    [ComImport, Guid("1F9FC1D0-C39B-4B26-817F-011967D3440E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyDescriptionList
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyDescription description);
    }

    [ComImport, Guid("6F79D558-3E96-4549-A1D1-7D75D2288814"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyDescription
    {
        [PreserveSig] int GetPropertyKey(out PropertyKey key);
        [PreserveSig] int GetCanonicalName(out IntPtr name);
        [PreserveSig] int GetPropertyType(out ushort type);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int GetEditInvitation(out IntPtr invitation);
        [PreserveSig] int GetTypeFlags(uint mask, out uint flags);
        [PreserveSig] int GetViewFlags(out uint flags);
        [PreserveSig] int GetDefaultColumnWidth(out uint chars);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [DllImport("propsys.dll")] static extern int PSEnumeratePropertyDescriptions(int filter, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyDescriptionList list);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);
    [DllImport("propsys.dll")] static extern int PSGetPropertyDescription(ref PropertyKey key, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyDescription description);
    [DllImport("propsys.dll")] static extern int PSFormatForDisplayAlloc(ref PropertyKey key, ref PropVariant value, int flags, out IntPtr text);
    [DllImport("propsys.dll")] static extern int PropVariantToDouble(ref PropVariant value, out double number);
    [DllImport("propsys.dll")] static extern int PropVariantToFileTime(ref PropVariant value, int flags, out long fileTime);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bind, int flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
}
