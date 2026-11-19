using System.IO;
using System.Text.Json;

namespace FileExplorer;

/// BROWSE-05: a folder's saved view: view mode, columns (order and widths), sort, date grouping and
/// hidden files. Folders are always listed before files in Pathweft, so that is not a profile choice.
/// Unset fields fall back to the user's current defaults.
internal sealed class FolderProfile
{
    public string Mode { get; set; }                       // PaneView.ViewMode name
    public List<string> Columns { get; set; }              // null: the defaults (chosen or automatic)
    public Dictionary<string, double> Widths { get; set; } = new();
    public string Sort { get; set; }
    public bool Descending { get; set; }
    public bool GroupByDate { get; set; }
    public bool Hidden { get; set; }
    public bool Subfolders { get; set; }                    // applies to the folders below too
    internal FolderProfile Copy() => new()
    {
        Mode = Mode, Columns = Columns?.ToList(), Widths = new(Widths ?? new()), Sort = Sort, Descending = Descending,
        GroupByDate = GroupByDate, Hidden = Hidden, Subfolders = Subfolders,
    };
}

/// folder-profiles.json in the data folder. The folder's own profile wins; otherwise the nearest
/// ancestor saved for its subfolders; otherwise none (the defaults). Each pane and tab applies them
/// on its own as it navigates.
internal static class FolderProfiles
{
    static string FilePath => Path.Combine(Settings.Dir, "folder-profiles.json");
    static readonly JsonSerializerOptions json = new() { WriteIndented = true };
    static Dictionary<string, FolderProfile> all;
    internal static event Action Changed;
    static Dictionary<string, FolderProfile> All
    {
        get
        {
            if (all != null) return all;
            try { all = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, FolderProfile>>(File.ReadAllText(FilePath), json) : null; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { all = null; }
            all = new Dictionary<string, FolderProfile>(all ?? new(), StringComparer.OrdinalIgnoreCase);
            return all;
        }
    }
    internal static int Count => All.Count;
    static string Key(string folder) => folder.Length > 3 ? folder.TrimEnd('\\') : folder;

    /// The profile that applies to this folder and the folder it was saved for, or (null, null).
    internal static (string Folder, FolderProfile Profile) Find(string folder)
    {
        if (string.IsNullOrEmpty(folder) || All.Count == 0) return (null, null);
        var key = Key(folder);
        if (All.TryGetValue(key, out var own)) return (key, own);
        for (var parent = Path.GetDirectoryName(key); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            if (All.TryGetValue(Key(parent), out var above) && above.Subfolders) return (Key(parent), above);
        return (null, null);
    }
    internal static void Set(string folder, FolderProfile profile) { All[Key(folder)] = profile; Save(); }
    internal static bool Remove(string folder) { bool removed = All.Remove(Key(folder)); if (removed) Save(); return removed; }
    internal static void Save(bool notify = true)
    {
        quiet?.Stop();
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(All, json)); File.Move(temp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (notify) Changed?.Invoke();
    }
    /// A column width dragged in a profiled folder: written once the drag settles, without reapplying.
    static System.Windows.Threading.DispatcherTimer quiet;
    internal static void SaveSoon()
    {
        quiet ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        quiet.Tick -= Quiet; quiet.Tick += Quiet; quiet.Stop(); quiet.Start();
    }
    static void Quiet(object sender, EventArgs e) => Save(notify: false);
}
