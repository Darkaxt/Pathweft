using System.IO;
using System.Text;
using System.Windows;

namespace FileExplorer;

/// OPUS-06: the listing as a table, in the order and with the filter shown and the pane's columns. Copy as
/// table puts the selected rows (or every row) on the clipboard as tab-separated text with a header, for a
/// spreadsheet; Export listing… writes every shown row to a CSV or TSV file. Only what the columns already
/// hold is written: no file is read, no cloud file is fetched and no folder is measured for it.
public partial class MainWindow
{
    /// Name, the full path where rows come from more than one folder (results, flat view, a collection,
    /// expanded folders), then the columns shown.
    static List<string> TableKeys(PaneView p, IEnumerable<Entry> rows)
    {
        var keys = new List<string> { "Name" };
        if (p.InSearch || rows.Any(e => e.NestDepth > 0)) keys.Add("Path");
        keys.AddRange(p.CopyColumns.Where(k => !keys.Contains(k)));
        return keys;
    }

    internal static string ListingTable(PaneView p, IReadOnlyList<Entry> rows, string format) =>
        CopyText.Render(rows, new CopyChoice(format, true, true, "columns", TableKeys(p, rows)), p.InSearch ? null : p.Dir);

    /// The selected rows when there is a selection, else the whole listing as shown.
    internal void CopyAsTable(PaneView p)
    {
        var shown = p.ListingRows;
        var picked = p.List.SelectedItems.Cast<Entry>().Where(e => !e.IsUp).ToHashSet();
        var rows = picked.Count > 0 ? shown.Where(picked.Contains).ToList() : shown.ToList();
        if (rows.Count == 0) { Status.Text = "Nothing to copy"; return; }
        var text = ListingTable(p, rows, "tsv");
#if DEBUG
        if (CopySink is { } sink) { sink(text); return; } // verification never takes the clipboard
#endif
        PutText(text, $"Copied {rows.Count:N0} row(s) as a table");
    }

    internal void ExportListing(PaneView p)
    {
        var rows = p.ListingRows;
        if (rows.Count == 0) { Status.Text = "Nothing to export"; return; }
        string stem = p.InSearch ? "Listing" : Path.GetFileName(p.Dir.TrimEnd('\\')) is { Length: > 0 } n ? n : "Listing";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export listing", FileName = stem + ".csv", DefaultExt = ".csv", AddExtension = true, OverwritePrompt = true,
            Filter = "CSV (comma-separated)|*.csv|TSV (tab-separated)|*.tsv",
        };
        if (dialog.ShowDialog(this) != true) return;
        string format = dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? "tsv" : "csv";
        var error = WriteListing(dialog.FileName, ListingTable(p, rows, format));
        Status.Text = error == null ? $"Exported {rows.Count:N0} row(s) to {dialog.FileName}" : "Not exported: " + error;
    }

    /// UTF-8 with a byte order mark, so Excel reads names in any script; returns the error or null.
    internal static string WriteListing(string file, string text)
    {
        try { File.WriteAllText(file, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return ex.Message; }
    }
}
