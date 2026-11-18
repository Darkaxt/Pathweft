using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Controls;
using Clipboard = System.Windows.Forms.Clipboard;

namespace FileExplorer;

// COPY-02: what Copy… writes, remembered between uses (Settings.CopyDetails).
internal sealed record CopyChoice(string Format, bool Header, bool FullPaths, string Case, List<string> Keys)
{
    internal static readonly string[] Formats = { "pairs", "lines", "tsv", "csv", "json", "markdown", "checksum" };
    internal static readonly string[] Cases = { "columns", "upper", "lower" };

    internal static string FormatLabel(string format) => format switch
    {
        "pairs" => "Name: value, a block per file",
        "lines" => "Values only, one per line",
        "tsv" => "Tab-separated (spreadsheets)",
        "csv" => "Comma-separated (CSV)",
        "json" => "JSON",
        "markdown" => "Markdown table",
        "checksum" => "Checksum file (hash *name)",
        _ => format,
    };

    internal static string CaseLabel(string value) => value switch { "upper" => "Upper case", "lower" => "Lower case", _ => "As the columns show them" };

    internal string Serialize() => string.Join(";", Format, Header ? 1 : 0, FullPaths ? 1 : 0, Case, string.Join(",", Keys));

    internal static CopyChoice Parse(string text)
    {
        var parts = (text ?? "").Split(';');
        string format = parts.Length > 0 && Formats.Contains(parts[0]) ? parts[0] : "pairs";
        bool header = parts.Length <= 1 || parts[1] != "0";
        bool full = parts.Length > 2 && parts[2] == "1";
        string hashCase = parts.Length > 3 && Cases.Contains(parts[3]) ? parts[3] : "columns";
        var keys = parts.Length > 4 ? parts[4].Split(',').Where(k => k != "").Distinct().ToList() : new List<string>();
        return new(format, header, full, hashCase, keys);
    }
}

internal static class CopyText
{
    static string NL => Environment.NewLine;

    static string Relative(string root, string path)
    {
        if (string.IsNullOrEmpty(root)) return path;
        string prefix = root.EndsWith('\\') ? root : root + "\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : path;
    }

    /// The file's name for a checksum line or a Path value: relative to the folder shown, or the full path.
    internal static string Name(Entry e, bool full, string root) => full ? e.Path : Relative(root, e.Path);

    internal static string Value(Entry e, string key, CopyChoice choice, string root)
    {
        if (key == "Path") return Name(e, choice.FullPaths, root);
        if (ColumnCatalog.IsHash(key))
        {
            var value = ColumnCatalog.HashValue(e, key) ?? "";
            return choice.Case switch { "upper" => value.ToUpperInvariant(), "lower" => value.ToLowerInvariant(), _ => ColumnCatalog.Case(value) };
        }
        return ColumnCatalog.Value(e, key);
    }

    static string Label(string key) => key == "Path" ? "Path" : key == "Name" ? "Name" : ColumnCatalog.Header(key);
    static string Flat(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    static string Csv(string value) => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    static string Cell(string value) => Flat(value).Replace("|", "\\|");

    /// The text Copy… writes for these rows (the preview shows the same for the first few).
    internal static string Render(IReadOnlyList<Entry> rows, CopyChoice choice, string root)
    {
        var keys = choice.Keys;
        var text = new StringBuilder();
        string V(Entry e, string key) => Value(e, key, choice, root);
        switch (choice.Format)
        {
            case "pairs":
                foreach (var e in rows)
                {
                    if (text.Length > 0) text.Append(NL);
                    foreach (var key in keys) text.Append(Label(key)).Append(": ").Append(V(e, key)).Append(NL);
                }
                break;
            case "lines":
                foreach (var e in rows)
                {
                    if (text.Length > 0 && keys.Count > 1) text.Append(NL);
                    foreach (var key in keys) text.Append(V(e, key)).Append(NL);
                }
                break;
            case "tsv":
            case "csv":
                string separator = choice.Format == "tsv" ? "\t" : ",";
                Func<string, string> field = choice.Format == "tsv" ? Flat : Csv;
                if (choice.Header) text.Append(string.Join(separator, keys.Select(k => field(Label(k))))).Append(NL);
                foreach (var e in rows) text.Append(string.Join(separator, keys.Select(k => field(V(e, k))))).Append(NL);
                break;
            case "json":
                var objects = rows.Select(e => keys.ToDictionary(Label, k => V(e, k))).ToList();
                return JsonSerializer.Serialize(objects, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            case "markdown":
                text.Append("| ").Append(string.Join(" | ", keys.Select(k => Cell(Label(k))))).Append(" |").Append(NL);
                text.Append("|").Append(string.Concat(keys.Select(_ => " --- |"))).Append(NL);
                foreach (var e in rows) text.Append("| ").Append(string.Join(" | ", keys.Select(k => Cell(V(e, k))))).Append(" |").Append(NL);
                break;
            case "checksum":
                var hashes = keys.Where(ColumnCatalog.IsHash).ToList();
                foreach (var key in hashes)
                {
                    if (hashes.Count > 1) text.Append(text.Length > 0 ? NL : "").Append("; ").Append(ColumnCatalog.Algorithm(key)).Append(NL);
                    foreach (var e in rows.Where(r => !r.IsDir))
                        if (V(e, key) is { Length: > 0 } value) text.Append(value).Append(" *").Append(Name(e, choice.FullPaths, root)).Append(NL);
                }
                break;
        }
        return text.ToString().TrimEnd('\r', '\n');
    }

    /// COPY-01: Copy <column>. One file: its value. Several: a line each; hashes as checksum lines (hash *name).
    internal static string Column(IReadOnlyList<Entry> rows, string key, string root)
    {
        var choice = new CopyChoice("lines", false, false, "columns", new() { key });
        if (rows.Count == 1) return Value(rows[0], key, choice, root);
        if (ColumnCatalog.IsHash(key)) return Render(rows, choice with { Format = "checksum" }, root);
        return string.Join(NL, rows.Select(e => Value(e, key, choice, root)));
    }
}

public partial class MainWindow
{
    /// Copy size, Copy date modified, Copy CRC-32: the label of a Copy details entry for a column.
    internal static string CopyLabel(string key)
    {
        string header = ColumnCatalog.Header(key);
        if (ColumnCatalog.IsHash(key) || header.Length < 2 || char.IsUpper(header[1])) return "Copy " + header; // CRC-32, GPS
        return "Copy " + char.ToLowerInvariant(header[0]) + header[1..];
    }

    /// Copy details' entries after Copy path and Copy name: one per column the pane shows, then Copy….
    internal List<MenuItem> CopyDetailItems(PaneView p, List<Entry> rows)
    {
        var items = new List<MenuItem>();
        string root = p.Dir;
        foreach (var key in p.CopyColumns)
        {
            var item = Item(CopyLabel(key), "", "", () => _ = CopyValues(p, rows, new[] { key }, () => CopyText.Column(rows, key, root)));
            item.Tag = "copy-detail";
            items.Add(item);
        }
        var more = Item("Copy…", "", "", () => CopyWithDialog(p, rows));
        more.Tag = "copy-detail";
        items.Add(more);
        return items;
    }

    internal void CopyWithDialog(PaneView p, List<Entry> rows)
    {
        var choice = CopyDetailsDialog.Ask(this, p, rows);
        if (choice == null) return;
        Settings.CopyDetails = choice.Serialize(); Settings.Save();
        string root = p.Dir;
        _ = CopyValues(p, rows, choice.Keys, () => CopyText.Render(rows, choice, root));
    }

    /// Settings' size limit changed: the panes decide again which files hash by themselves.
    internal void RequeueHashes()
    {
        Left.QueueHashes(); Right.QueueHashes();
        taskbarSearch?.Widget?.Pane.QueueHashes();
    }

    internal Task copyDetailsWork = Task.CompletedTask; // the last one, for verification

    /// Fills what the keys need and is not known yet, then copies. Hashes not computed are computed as a job in the
    /// Transfers window (one read per file for all of them, through the cache); owner and properties are read directly.
    internal Task CopyValues(PaneView p, List<Entry> rows, IReadOnlyCollection<string> keys, Func<string> render) => copyDetailsWork = CopyValuesAsync(p, rows, keys, render);

    async Task CopyValuesAsync(PaneView p, List<Entry> rows, IReadOnlyCollection<string> keys, Func<string> render)
    {
        bool archive = p.ArchiveRoot != null;
        if (!archive)
        {
            var names = keys.Where(ColumnCatalog.IsHash).Select(ColumnCatalog.Algorithm).Where(n => Checksums.Unavailable(n) == null).ToArray();
            var work = rows.Where(e => !e.IsDir && !e.IsUp).Select(e => (Entry: e, e.Path, Size: Math.Max(0, e.Size), Names: names.Where(n => !e.HasCell("#" + n)).ToArray()))
                           .Where(w => w.Names.Length > 0).ToList();
            if (work.Count > 0)
            {
                var results = new ConcurrentQueue<(Entry, Dictionary<string, string>)>();
                string what = names.Length == 1 ? names[0] : "hashes";
                var job = TrackSubmittedJob(Ferry.Submit(work.Select(w => w.Path), Path.GetDirectoryName(work[0].Path), false, job =>
                {
                    job.TotalFiles = work.Count; job.TotalBytes = work.Sum(w => w.Size);
                    using var cancel = new CancellationTokenSource();
                    foreach (var (e, path, size, missing) in work)
                    {
                        if (job.IsCancelled) return;
                        job.Current = path; job.CurrentSize = size; job.CurrentDone = 0;
                        long reported = 0;
                        try
                        {
                            var values = HashCache.OfFile(path, missing, cancel.Token, done =>
                            {
                                if (job.IsCancelled) cancel.Cancel();
                                job.AddBytes(done - reported); reported = done; job.CurrentDone = done;
                            }, recall: true);
                            results.Enqueue((e, values));
                        }
                        catch (OperationCanceledException) { return; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { job.Errors.Enqueue($"{Path.GetFileName(path)}: {error.Message}"); }
                        job.AddBytes(size - reported);
                        job.DoneFiles++;
                    }
                }, $"Calculate {what} to copy"));
                await job.Completion;
                foreach (var (e, values) in results)
                    foreach (var (name, value) in values)
                        if (value != null) { e.SetCell("#" + name, value); e.SetCell("~#" + name, null); e.Withdraw("#" + name); }
                if (job.IsCancelled) { Status.Text = "Copy cancelled"; return; }
            }
            var properties = keys.Where(ColumnCatalog.IsProperty).Select(ShellProperties.Find).Where(d => d != null).ToList();
            bool owner = keys.Contains("Owner"), basics = keys.Any(k => k is "Created" or "Accessed" or "Attributes"), version = keys.Contains("Version");
            var extras = rows.Where(e => !e.IsUp && (owner && !e.HasCell("Owner") || basics && e.Created == default || version && !e.IsDir && e.Version.Length == 0 && !e.HasCell("~Version") || properties.Any(d => !e.HasCell(d.Name))))
                             .Select(e => (Entry: e, e.Path, e.IsDir, e.Attributes, Basics: basics && e.Created == default, Owner: owner && !e.HasCell("Owner"), Version: version && !e.IsDir && e.Version.Length == 0, Properties: properties.Where(d => !e.HasCell(d.Name)).ToList())).ToList();
            if (extras.Count > 0)
            {
                Status.Text = "Reading details…";
                var read = await PaneView.RunSta(() => extras.Select(x => (x.Entry, PaneView.ReadExtras(x.Path, x.IsDir, x.Attributes, x.Basics, x.Owner, x.Version, x.Properties))).ToList());
                foreach (var (e, x) in read) PaneView.ApplyExtras(e, x);
                Status.Text = "";
            }
        }
        string text = render();
        if (text.Length == 0) { Status.Text = "Nothing to copy"; return; }
#if DEBUG
        if (CopySink is { } sink) { sink(text); return; } // verification never takes the clipboard
#endif
        Clipboard.SetText(text);
    }
#if DEBUG
    internal static Action<string> CopySink;
#endif
}
