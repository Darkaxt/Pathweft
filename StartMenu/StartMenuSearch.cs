using System.IO;
using System.Threading;
using FileExplorer.Search;

namespace FileExplorer.StartMenu;

/// SAB-03 (S6b): the menu's search. Programs (All Programs and the packaged apps) and Settings (PC settings pages and
/// Control Panel's items) are matched here; files and folders come from the Pathweft index service with read-only
/// queries, one per group so each group's count is the index's own. A newer keystroke cancels the earlier queries.
internal static class StartMenuSearch
{
    internal sealed record Group(string Name, IReadOnlyList<Entry> Items, ulong Total, string Filter, string Notice = null);

    /// The groups the Windows 7 menu shows, with the extensions that file each result.
    internal static readonly (string Name, string Filter)[] FileGroups =
    [
        ("Folders", "folder:"),
        ("Documents", "ext:doc;docx;docm;dot;dotx;odt;rtf;txt;md;pdf;xps;epub;xls;xlsx;xlsm;ods;csv;ppt;pptx;odp;one;msg;eml"),
        ("Pictures", "ext:jpg;jpeg;png;gif;bmp;tif;tiff;webp;heic;heif;avif;svg;ico;raw;cr2;cr3;nef;arw;dng;psd"),
        ("Music", "ext:mp3;flac;m4a;aac;wav;ogg;opus;wma;alac;aiff;ape"),
        ("Videos", "ext:mp4;mkv;avi;mov;wmv;webm;m4v;mpg;mpeg;ts;flv;3gp"),
    ];
    internal const int PerGroup = 4;

    static List<Entry> programs, settings;
    static Task loading;

    /// Programs and Settings are gathered once on the Shell thread, again when the Programs folders change.
    internal static Task Load(StartMenuOptions options) => loading ??= ShellWorker.Run(() =>
    {
        programs = StartMenuItems.Flatten(StartMenuItems.AllPrograms(options)).GroupBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
        var panel = StartMenuItems.ShellMenu(@"::{26EE0668-A00A-44D7-9371-BEB064C98683}\0").Where(e => e.Kind != EntryKind.Command);
        settings = StartMenuItems.SettingsPages.Select(p => p.Entry).Concat(panel).GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
    });

    internal static void Forget() => loading = null;

    /// Each typed word must start a word of the name (Windows 7's rule); names starting with the whole text first.
    internal static List<Entry> Match(IEnumerable<Entry> list, string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        const System.Globalization.CompareOptions ignore = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
        bool Starts(string name, string word)
        {
            for (int at = 0; at < name.Length; at++)
                if ((at == 0 || !char.IsLetterOrDigit(name[at - 1])) && compare.IsPrefix(name.AsSpan(at), word, ignore)) return true;
            return false;
        }
        return list.Where(e => words.All(w => Starts(e.Name, w)))
            .OrderBy(e => compare.IsPrefix(e.Name, text.Trim(), ignore) ? 0 : 1).ThenBy(e => e.Name.Length).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static Group Programs(string text) => programs == null ? null : Total("Programs", Match(programs, text));
    internal static Group Settings(string text) => settings == null ? null : Total("Settings", Match(settings, text));

    static Group Total(string name, List<Entry> all) => new(name, all.Take(PerGroup + 1).ToList(), (ulong)all.Count, null);

    /// The words quoted for the index (spaces separate words; quotes, | ! < > are taken literally).
    internal static string Expression(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(w => w.Replace("\"", "")).Where(w => w.Length > 0).Select(w => "\"" + w + "\""));

    /// One query per file group, visible items only, newest first; without the public folders when StartAllBack's
    /// "Search public folders" is off. Null groups: the index did not answer.
    internal static async Task<List<Group>> FilesAsync(string text, bool publicFolders, CancellationToken token)
    {
        string words = Expression(text);
        if (words.Length == 0) return [];
        if (!publicFolders && Environment.GetEnvironmentVariable("PUBLIC") is { Length: > 2 } shared && !shared.Contains('"'))
            words += " !path:\"" + shared.TrimEnd('\\') + "\\\"";
        var other = "file: !" + string.Join(" !", FileGroups.Skip(1).Select(g => g.Filter));
        var queries = FileGroups.Append(("Files", other)).Select(async g =>
        {
            var page = await IndexServiceClient.Shared.QueryAsync(words + " " + g.Item2, 0, PerGroup, "modified", true, null, true, token).ConfigureAwait(false);
            if (page == null) return null;
            var items = page.Hits.Select(h => new Entry(Path.GetFileName(h.Path.TrimEnd('\\')) is { Length: > 0 } n ? n : h.Path, h.IsFolder ? EntryKind.Folder : EntryKind.Document, h.Path) { Tip = h.Path }).ToList();
            return new Group(g.Item1, items, page.Total, g.Item2, page.Updating ? "The index is still updating: more may be found." : null);
        }).ToList();
        var groups = await Task.WhenAll(queries).ConfigureAwait(false);
        if (groups.Any(g => g == null)) return null;
        return groups.Where(g => g.Total > 0).ToList();
    }
}
