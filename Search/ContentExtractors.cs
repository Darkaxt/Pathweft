using System.IO;
using System.Text;
using System.Text.Json;

namespace FileExplorer.Search;

// CONTENT-02: the formats Pathweft can actually read text from. Support is these extractors, not a guess
// from previews: plain-text formats are read directly; Word, Excel, PowerPoint and PDF go through the
// Craft cores' text extraction in the isolated Office worker (the same API the preview uses).
internal static class ContentExtractors
{
    // Raise when an extractor changes, so failed and unsupported files are tried again.
    internal const int Version = 1;
    internal const long TextLimit = 512L << 20, DocumentLimit = 64L << 20; // the Office worker's own input limit
    const int ChunkCharacters = 2048, ChunkLines = 8;

    static readonly HashSet<string> text = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".rst", ".adoc", ".tex", ".log", ".nfo", ".srt", ".vtt",
        ".json", ".jsonl", ".ndjson", ".json5", ".geojson", ".csv", ".tsv", ".xml", ".xaml", ".html", ".htm", ".xhtml", ".svg",
        ".ini", ".cfg", ".conf", ".config", ".properties", ".env", ".yaml", ".yml", ".toml", ".reg", ".editorconfig", ".gitignore", ".gitattributes",
        ".cs", ".csproj", ".props", ".targets", ".sln", ".vb", ".fs", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".sh", ".bash", ".zsh",
        ".py", ".pyw", ".ipynb", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".css", ".scss", ".less",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".rs", ".go", ".java", ".kt", ".kts", ".gradle", ".swift", ".m", ".mm",
        ".rb", ".php", ".pl", ".lua", ".r", ".sql", ".dart", ".scala", ".clj", ".ex", ".exs", ".erl", ".hs", ".nim", ".zig",
        ".cmake", ".mk", ".diff", ".patch", ".lock", ".toml", ".tf", ".hcl", ".proto", ".graphql", ".gql",
    };
    static readonly Dictionary<string, string> documents = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = "Word", [".docm"] = "Word", [".doc"] = "Word", [".odt"] = "Word",
        [".xlsx"] = "Excel", [".xlsm"] = "Excel", [".xlsb"] = "Excel", [".xls"] = "Excel", [".ods"] = "Excel",
        [".pptx"] = "PowerPoint", [".pdf"] = "PDF",
    };
    static readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) { "Dockerfile", "Makefile", "LICENSE", "README", "CHANGELOG", ".gitignore", ".editorconfig" };

    internal static bool Supports(string path) => Kind(path) != null;
    internal static string Kind(string path)
    {
        string extension = Path.GetExtension(path);
        if (text.Contains(extension) || extension.Length == 0 && names.Contains(Path.GetFileName(path))) return "text";
        return documents.GetValueOrDefault(extension);
    }
    internal static IEnumerable<string> Extensions => text.Concat(documents.Keys).Select(e => e.TrimStart('.'));
    internal static string Coverage => "Text, Markdown, JSON/JSONL, CSV, logs, XML/HTML, configuration and source code; Word, Excel, PowerPoint and PDF";

    // Text: chunks of up to eight lines or 2,048 characters, each located by its first line, column and byte offset.
    // Reads at most `length` bytes (the size when the read began), so a file being appended to is read as it was.
    internal static IEnumerable<(string Location, string Text)> ReadText(Stream stream, long length, CancellationToken token)
    {
        var head = new byte[Math.Min(8192, length)];
        int got = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        Encoding encoding; int bom;
        if (got >= 2 && head[0] == 0xFF && head[1] == 0xFE) (encoding, bom) = (Encoding.Unicode, 2);
        else if (got >= 2 && head[0] == 0xFE && head[1] == 0xFF) (encoding, bom) = (Encoding.BigEndianUnicode, 2);
        else
        {
            if (Array.IndexOf(head, (byte)0, 0, got) >= 0) throw new ContentUnsupportedException("Binary data, not text.");
            (encoding, bom) = (new UTF8Encoding(false, false), got >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0);
        }
        int unit = encoding is UTF8Encoding ? 1 : 2;
        stream.Position = bom;
        var input = new BufferedStream(stream, 1 << 16);
        long offset = bom, chunkOffset = bom; int line = 1, column = 1, chunkLine = 1, chunkColumn = 1, lines = 0;
        var chunk = new StringBuilder(ChunkCharacters + 256);
        var bytes = new List<byte>(1024);
        string Location() => JsonSerializer.Serialize(new { line = chunkLine, column = chunkColumn, offset = chunkOffset });
        while (offset < length)
        {
            token.ThrowIfCancellationRequested();
            // One line (or a long line's next piece) as bytes, so offsets stay exact.
            long start = offset; bytes.Clear(); bool newline = false;
            while (offset < length)
            {
                int b = input.ReadByte();
                if (b < 0) { length = offset; break; }
                int c = 0;
                if (unit == 2) { c = input.ReadByte(); if (c < 0) { length = offset; break; } }
                offset += unit;
                if (unit == 1 ? b == '\n' : encoding == Encoding.Unicode ? b == '\n' && c == 0 : b == 0 && c == '\n') { newline = true; break; }
                bytes.Add((byte)b); if (unit == 2) bytes.Add((byte)c);
                // A very long line continues in the next piece, cut after whitespace and never inside a character.
                if (bytes.Count >= ChunkCharacters * unit && (unit == 1 ? b is ' ' or '\t' or ',' : b == 0 && c is ' ' or ',' || c == 0 && b is ' ' or ',')) break;
                if (bytes.Count >= ChunkCharacters * unit * 4 && (unit == 2 || b < 0x80)) break;
            }
            string piece = encoding.GetString(bytes.ToArray()).TrimEnd('\r');
            if (chunk.Length > 0 && (chunk.Length + piece.Length > ChunkCharacters || lines >= ChunkLines))
            { yield return (Location(), chunk.ToString()); chunk.Clear(); lines = 0; }
            if (chunk.Length == 0) { chunkLine = line; chunkColumn = column; chunkOffset = start; }
            chunk.Append(piece).Append(newline ? '\n' : ' '); lines++;
            if (newline) { line++; column = 1; } else column += piece.Length;
        }
        if (chunk.Length > 0) yield return (Location(), chunk.ToString());
    }

    // Only the coordinates a result needs; the Craft cores' rectangles and glyph ranges are left out.
    static readonly string[] kept = { "page", "slide", "sheet", "cell", "paragraph", "shape", "part", "line" };
    internal static string Compact(JsonElement location)
    {
        var compact = new Dictionary<string, JsonElement>();
        foreach (var name in kept) if (location.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.String) compact[name] = value;
        if (compact.Count == 0) return location.GetRawText();
        compact["engine"] = JsonDocument.Parse("1").RootElement; // marks Craft coordinates, which count from 0
        return JsonSerializer.Serialize(compact);
    }

    // How a stored location reads in results and tooltips: "line 12", "page 3", "Sheet1!B4", "slide 2".
    internal static string Describe(string location)
    {
        try
        {
            using var json = JsonDocument.Parse(location);
            var root = json.RootElement;
            string Value(string name) => root.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ToString() : null;
            bool craft = root.TryGetProperty("engine", out _);
            string Display(string index) => craft && int.TryParse(index, out int n) ? (n + 1).ToString() : index;
            if (Value("page") is { } page) return "page " + Display(page);
            if (!craft && Value("line") is { } line) return "line " + line;
            if (Value("slide") is { } slide) return "slide " + Display(slide);
            if (Value("sheet") is { } sheet) return Value("cell") is { } cell ? sheet + "!" + cell : "sheet " + sheet;
            if (Value("cell") is { } only) return "cell " + only;
            if (Value("paragraph") is { } paragraph) return "paragraph " + Display(paragraph);
            if (Value("part") is { } part) return part;
            return string.Join(", ", root.EnumerateObject().Select(p => p.Name + " " + p.Value));
        }
        catch (JsonException) { return location; }
    }

    internal static int? Line(string location)
    {
        try { using var json = JsonDocument.Parse(location); return !json.RootElement.TryGetProperty("engine", out _) && json.RootElement.TryGetProperty("line", out var line) && line.TryGetInt32(out int n) ? n : null; }
        catch (JsonException) { return null; }
    }
    internal static int? Page(string location)
    {
        try
        {
            using var json = JsonDocument.Parse(location);
            foreach (var name in new[] { "page", "slide" })
                if (json.RootElement.TryGetProperty(name, out var page) && page.TryGetInt32(out int n)) return n;
            return null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed class ContentUnsupportedException(string message) : Exception(message);
