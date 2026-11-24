// EBOOK-01: the reader-neutral book model that EPUB, FB2 and MOBI/AZW3 readers produce and EbookViewer lays out.
namespace FileExplorer.Preview.Ebook;

// One spine document. Html is (X)HTML markup; Href is its book-internal path, used to resolve relative
// links and images (an EPUB "OEBPS/text/ch1.xhtml", or a synthetic "part0001.html" for MOBI and FB2).
internal sealed record EbookChapter(string Href, string Html);

// A table-of-contents entry: Href is a book-internal path with an optional #fragment; Depth starts at 0.
internal sealed record EbookTocEntry(string Title, string Href, int Depth);

internal sealed class EbookBook
{
    internal string Format = "";            // "EPUB", "FB2", "MOBI", "AZW3 (KF8)"
    internal string Title, Author, Publisher, Language, Description, Published;
    internal byte[] Cover;                   // cover image bytes when the book names one
    internal readonly List<EbookChapter> Chapters = new();
    internal readonly List<EbookTocEntry> Toc = new();
    // Images and other resources by book-internal path (case-insensitive), resolved against a chapter's Href.
    internal readonly Dictionary<string, byte[]> Resources = new(StringComparer.OrdinalIgnoreCase);
    internal string Notice;                  // e.g. "DRM-protected book: only the metadata can be shown."

    // Resolves a reference written in the document at `from` (relative, ../, #fragment) to a book-internal path.
    internal static string Resolve(string from, string reference)
    {
        if (string.IsNullOrEmpty(reference)) return from;
        reference = Uri.UnescapeDataString(reference.Split('#')[0]);
        if (reference.Length == 0) return from;
        if (reference.StartsWith('/')) return reference.TrimStart('/');
        var parts = new List<string>(from.Replace('\\', '/').Split('/'));
        parts.RemoveAt(parts.Count - 1);
        foreach (var segment in reference.Replace('\\', '/').Split('/'))
        {
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (segment != "." && segment.Length > 0) parts.Add(segment);
        }
        return string.Join('/', parts);
    }
}
