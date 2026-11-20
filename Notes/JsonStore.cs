using System.IO;
using System.Text.Json;

namespace FileExplorer;

/// S22: the side files the workflow features keep (workspaces.json, collections.json, aliases.json,
/// history.json), read once and written whole to a temporary file that then replaces the old one, as
/// saved-searches.json is. A file that can't be read is set aside as .bad rather than overwritten.
internal sealed class JsonStore<T> where T : class
{
    static readonly JsonSerializerOptions json = new() { WriteIndented = true };
    readonly Func<string> path;
    readonly Func<List<T>, List<T>> clean;
    List<T> all;
    internal event Action Changed;
    internal JsonStore(Func<string> path, Func<List<T>, List<T>> clean = null) { this.path = path; this.clean = clean ?? (list => list); }
    internal string FilePath => path();

    internal IReadOnlyList<T> All
    {
        get
        {
            if (all != null) return all;
            var file = FilePath;
            try { all = File.Exists(file) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file), json) : null; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                all = null;
                try { File.Copy(file, file + ".bad", overwrite: true); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            }
            all = clean((all ?? new()).Where(x => x != null).ToList());
            return all;
        }
    }

    internal void Put(T item, T replacing = null)
    {
        var list = All.ToList();
        int at = replacing != null ? list.IndexOf(replacing) : -1;
        if (at >= 0) list[at] = item; else list.Add(item);
        Write(list);
    }
    internal void Remove(T item) { var list = All.ToList(); if (list.Remove(item)) Write(list); }
    internal void Replace(List<T> list) => Write(list);

    /// Returns the error, or null once the file is in place. Listeners hear of it either way: the list
    /// they show is the one in memory.
    internal string Write(List<T> list)
    {
        string error = null;
        try
        {
            var file = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list, json)); File.Move(temp, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; }
        all = list; Changed?.Invoke();
        return error;
    }

    /// Forgets the copy in memory (a test profile switched, or the file was cleared from Settings).
    internal void Reload() { all = null; Changed?.Invoke(); }
}
