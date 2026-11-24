using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FileExplorer.Preview.Ebook;

// EBOOK-01: META-INF/container.xml -> OPF (metadata, manifest, spine, cover) -> EPUB 3 nav or EPUB 2 NCX.
internal static class EpubReader
{
    const long MaxEntry = 64L << 20, MaxTotal = 512L << 20;
    // Font obfuscation (IDPF and Adobe) only scrambles embedded fonts; anything else in encryption.xml is DRM.
    static readonly string[] FontObfuscation = { "http://www.idpf.org/2008/embedding", "http://ns.adobe.com/pdf/enc#RC" };

    internal static EbookBook Read(string path, CancellationToken cancel)
    {
        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).GroupBy(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        long total = 0;
        byte[] Bytes(string name)
        {
            if (!entries.TryGetValue(name, out var entry)) return null;
            if (entry.Length > MaxEntry || (total += entry.Length) > MaxTotal) throw new InvalidDataException("The book is larger than the preview reads.");
            using var stream = entry.Open(); var buffer = new MemoryStream((int)entry.Length);
            stream.CopyTo(buffer); return buffer.ToArray();
        }
        XDocument Xml(string name) => Bytes(name) is { } data ? Parse(data) : null;

        var book = new EbookBook { Format = "EPUB" };
        var container = Xml("META-INF/container.xml") ?? throw new InvalidDataException("This EPUB has no META-INF/container.xml.");
        string opfPath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile" && (string)e.Attribute("media-type") is null or "application/oebps-package+xml")
            ?.Attribute("full-path")?.Value ?? throw new InvalidDataException("This EPUB names no package document.");
        var opf = Xml(opfPath) ?? throw new InvalidDataException("The package document " + opfPath + " is missing.");
        var package = opf.Root;
        var metadata = Child(package, "metadata");
        string Meta(string name) => metadata?.Elements().Where(e => e.Name.LocalName == name).Select(e => Clean(e.Value)).FirstOrDefault(v => v.Length > 0);
        book.Title = Meta("title");
        book.Author = metadata == null ? null : string.Join(", ", metadata.Elements().Where(e => e.Name.LocalName == "creator").Select(e => Clean(e.Value)).Where(v => v.Length > 0).Distinct());
        if (book.Author?.Length == 0) book.Author = null;
        book.Publisher = Meta("publisher"); book.Language = Meta("language"); book.Published = Meta("date");
        book.Description = Meta("description") is { } description ? Markup.PlainText(description) : null;
        if (package.Attribute("version")?.Value is { } version && version.StartsWith('3')) book.Format = "EPUB 3";

        var manifest = new Dictionary<string, (string Href, string Type, string Properties)>(StringComparer.Ordinal);
        foreach (var item in Child(package, "manifest")?.Elements().Where(e => e.Name.LocalName == "item") ?? Enumerable.Empty<XElement>())
            if (item.Attribute("id")?.Value is { } id && item.Attribute("href")?.Value is { } href)
                manifest[id] = (EbookBook.Resolve(opfPath, href), (string)item.Attribute("media-type") ?? "", (string)item.Attribute("properties") ?? "");

        if (Xml("META-INF/encryption.xml") is { } encryption &&
            encryption.Descendants().Where(e => e.Name.LocalName == "EncryptionMethod").Any(e => !FontObfuscation.Contains((string)e.Attribute("Algorithm"))))
            book.Notice = "This book is DRM-protected; only its details and cover can be shown.";

        // Cover: EPUB 3 cover-image property, the EPUB 2 <meta name="cover">, then a manifest item named cover.
        string coverId = manifest.FirstOrDefault(m => m.Value.Properties.Split(' ').Contains("cover-image")).Key
            ?? metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "meta" && (string)e.Attribute("name") == "cover")?.Attribute("content")?.Value;
        if (coverId == null || !manifest.ContainsKey(coverId))
            coverId = manifest.FirstOrDefault(m => m.Value.Type.StartsWith("image/") && m.Key.Contains("cover", StringComparison.OrdinalIgnoreCase)).Key;
        if (coverId != null && manifest.TryGetValue(coverId, out var cover) && cover.Type.StartsWith("image/") && !cover.Type.Contains("svg")) book.Cover = Bytes(cover.Href);
        if (book.Notice != null) return book;

        foreach (var itemref in Child(package, "spine")?.Elements().Where(e => e.Name.LocalName == "itemref") ?? Enumerable.Empty<XElement>())
        {
            cancel.ThrowIfCancellationRequested();
            if ((string)itemref.Attribute("idref") is not { } id || !manifest.TryGetValue(id, out var item)) continue;
            if (!item.Type.Contains("html") && !item.Href.EndsWith("htm", StringComparison.OrdinalIgnoreCase) && !item.Href.EndsWith("html", StringComparison.OrdinalIgnoreCase)) continue;
            if (Bytes(item.Href) is { } html) book.Chapters.Add(new(item.Href, Decode(html)));
        }
        if (book.Chapters.Count == 0) throw new InvalidDataException("This EPUB has no readable chapters in its spine.");
        foreach (var (_, item) in manifest)
            if (item.Type.StartsWith("image/") && !item.Type.Contains("svg") && !book.Resources.ContainsKey(item.Href))
            {
                cancel.ThrowIfCancellationRequested();
                if (Bytes(item.Href) is { } image) book.Resources[item.Href] = image;
            }

        // EPUB 3 navigation document, else the NCX named by the spine's toc attribute (or any NCX in the manifest).
        var nav = manifest.Values.FirstOrDefault(m => m.Properties.Split(' ').Contains("nav"));
        if (nav.Href != null && Bytes(nav.Href) is { } navBytes) NavToc(book, nav.Href, Decode(navBytes));
        if (book.Toc.Count == 0)
        {
            string ncxId = (string)Child(package, "spine")?.Attribute("toc");
            var ncx = ncxId != null && manifest.TryGetValue(ncxId, out var named) ? named : manifest.Values.FirstOrDefault(m => m.Type == "application/x-dtbncx+xml");
            if (ncx.Href != null && Xml(ncx.Href) is { } ncxDocument) NcxToc(book, ncx.Href, ncxDocument);
        }
        return book;
    }

    static void NavToc(EbookBook book, string href, string html)
    {
        var document = Markup.Parse(html);
        var nav = document.QuerySelectorAll("nav").FirstOrDefault(n => n.GetAttribute("epub:type") == "toc" || n.GetAttribute("type") == "toc" || n.GetAttribute("role") == "doc-toc")
            ?? document.QuerySelector("nav");
        if (nav?.QuerySelector("ol") is not { } list) return;
        void Walk(AngleSharp.Dom.IElement ol, int depth)
        {
            foreach (var li in ol.Children.Where(c => c.LocalName == "li"))
            {
                var link = li.Children.FirstOrDefault(c => c.LocalName is "a" or "span");
                string title = Clean(link?.TextContent ?? "");
                if (link?.GetAttribute("href") is { } target && title.Length > 0)
                    book.Toc.Add(new(title, EbookBook.Resolve(href, target) + Fragment(target), depth));
                if (li.Children.FirstOrDefault(c => c.LocalName == "ol") is { } child && book.Toc.Count < 5000) Walk(child, depth + 1);
            }
        }
        Walk(list, 0);
    }

    static void NcxToc(EbookBook book, string href, XDocument ncx)
    {
        void Walk(XElement parent, int depth)
        {
            foreach (var point in parent.Elements().Where(e => e.Name.LocalName == "navPoint"))
            {
                string title = Clean(point.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value ?? "");
                string target = point.Elements().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;
                if (target != null && title.Length > 0) book.Toc.Add(new(title, EbookBook.Resolve(href, target) + Fragment(target), depth));
                if (book.Toc.Count < 5000) Walk(point, depth + 1);
            }
        }
        if (ncx.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "navMap") is { } map) Walk(map, 0);
    }

    static string Fragment(string target) => target.IndexOf('#') is >= 0 and var at ? target[at..] : "";
    static XElement Child(XElement parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    internal static string Clean(string text) => string.Join(' ', (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    internal static XDocument Parse(byte[] data)
    {
        // No DTD processing or external entities; XHTML entity declarations are ignored.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersFromEntities = 1 << 20 };
        using var reader = XmlReader.Create(new MemoryStream(data), settings);
        return XDocument.Load(reader);
    }

    // UTF-8 unless a BOM or the XML declaration names another encoding.
    internal static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 200));
        var match = System.Text.RegularExpressions.Regex.Match(head, "encoding=[\"']([A-Za-z0-9_\\-]+)[\"']");
        if (match.Success && !match.Groups[1].Value.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(match.Groups[1].Value).GetString(data); } catch (ArgumentException) { }
        }
        return new UTF8Encoding(false).GetString(data).TrimStart('﻿');
    }
}
