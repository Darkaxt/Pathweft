using System.IO;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace FileExplorer.Preview.Ebook;

// EBOOK-01: FB2 description (title-info, publish-info, coverpage), bodies as HTML chapters, <binary> images.
internal static class Fb2Reader
{
    internal static EbookBook Read(byte[] data, CancellationToken cancel)
    {
        var document = EpubReader.Parse(data);
        var root = document.Root ?? throw new InvalidDataException("This FB2 file is empty.");
        if (root.Name.LocalName != "FictionBook") throw new InvalidDataException("This is not a FictionBook (FB2) file.");
        XElement Child(XElement parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        IEnumerable<XElement> Children(XElement parent, string name) => parent?.Elements().Where(e => e.Name.LocalName == name) ?? Enumerable.Empty<XElement>();
        string Text(XElement element) => element == null ? null : EpubReader.Clean(element.Value) is { Length: > 0 } text ? text : null;

        var book = new EbookBook { Format = "FB2" };
        var description = Child(root, "description");
        var title = Child(description, "title-info");
        book.Title = Text(Child(title, "book-title"));
        var authors = Children(title, "author").Select(a => Text(a.Element(a.Name.Namespace + "nickname")) is { } nick && Child(a, "last-name") == null ? nick :
            EpubReader.Clean(string.Join(' ', new[] { "first-name", "middle-name", "last-name" }.Select(n => Child(a, n)?.Value ?? "")))).Where(n => n.Length > 0).ToArray();
        book.Author = authors.Length > 0 ? string.Join(", ", authors) : null;
        book.Language = Text(Child(title, "lang"));
        book.Published = Text(Child(title, "date"));
        book.Description = Text(Child(title, "annotation"));
        book.Publisher = Text(Child(Child(description, "publish-info"), "publisher"));

        foreach (var binary in Children(root, "binary"))
        {
            cancel.ThrowIfCancellationRequested();
            if ((string)binary.Attribute("id") is not { } id) continue;
            try { book.Resources[id] = Convert.FromBase64String(binary.Value.Trim()); } catch (FormatException) { }
        }
        string Href(XElement image) => image.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value?.TrimStart('#');
        if (Child(Child(title, "coverpage"), "image") is { } coverImage && Href(coverImage) is { } coverId && book.Resources.TryGetValue(coverId, out var cover)) book.Cover = cover;

        // Each top-level section of the main body is a chapter; the notes body is one more chapter.
        int part = 0;
        foreach (var body in Children(root, "body"))
        {
            bool notes = (string)body.Attribute("name") is "notes" or "comments";
            var sections = Children(body, "section").ToArray();
            // Elements before the first section (the body title, an epigraph) open the first chapter.
            var preamble = body.Elements().TakeWhile(e => e.Name.LocalName != "section").ToArray();
            var groups = notes || sections.Length == 0 ? new[] { body.Elements().ToArray() } : sections.Select((s, i) => i == 0 ? preamble.Append(s).ToArray() : new[] { s }).ToArray();
            foreach (var group in groups)
            {
                cancel.ThrowIfCancellationRequested();
                if (group.Length == 0) continue;
                string href = $"part{++part:D4}.html";
                var html = new StringBuilder("<html><body>");
                int entries = book.Toc.Count;
                foreach (var element in group) Write(html, element, 1, book, href);
                // Notes are one contents entry, not one per note.
                if (notes) book.Toc.RemoveRange(entries, book.Toc.Count - entries);
                html.Append("</body></html>");
                book.Chapters.Add(new(href, html.ToString()));
                if (notes) book.Toc.Add(new(Text(Child(body, "title")) ?? "Notes", href, 0));
            }
        }
        if (book.Chapters.Count == 0) throw new InvalidDataException("This FB2 file has no body.");
        return book;
    }

    static void Write(StringBuilder html, XElement element, int depth, EbookBook book, string href)
    {
        string name = element.Name.LocalName;
        string id = (string)element.Attribute("id");
        string Open(string tag) => id != null ? $"<{tag} id=\"{WebUtility.HtmlEncode(id)}\">" : $"<{tag}>";
        void Inner(int level) { foreach (var node in element.Nodes()) if (node is XText text) html.Append(WebUtility.HtmlEncode(text.Value)); else if (node is XElement child) Write(html, child, level, book, href); }
        switch (name)
        {
            case "section":
                // A section title is a heading and a table-of-contents entry at the section's depth.
                if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "title") is { } heading && EpubReader.Clean(heading.Value) is { Length: > 0 } label)
                {
                    id ??= "section" + book.Toc.Count;
                    book.Toc.Add(new(label, href + "#" + id, depth - 1));
                }
                html.Append(Open("div"));
                foreach (var node in element.Nodes()) if (node is XElement child) Write(html, child, depth + 1, book, href);
                html.Append("</div>"); return;
            case "title": html.Append($"<h{Math.Min(depth, 6)}>"); foreach (var p in element.Elements()) { if (p.Name.LocalName == "empty-line") continue; foreach (var node in p.Nodes()) if (node is XText t) html.Append(WebUtility.HtmlEncode(t.Value)); else if (node is XElement c) Write(html, c, depth, book, href); html.Append("<br/>"); } html.Append($"</h{Math.Min(depth, 6)}>"); return;
            case "subtitle": html.Append(Open("h5")); Inner(depth); html.Append("</h5>"); return;
            case "p": case "v": case "text-author": html.Append(Open("p")); if (name == "text-author") html.Append("<i>"); Inner(depth); if (name == "text-author") html.Append("</i>"); html.Append("</p>"); return;
            case "empty-line": html.Append("<p>&#160;</p>"); return;
            case "epigraph": case "cite": case "annotation": html.Append(Open("blockquote")); Inner(depth); html.Append("</blockquote>"); return;
            case "poem": case "stanza": html.Append(Open("div")); Inner(depth); html.Append("</div>"); return;
            case "emphasis": html.Append("<em>"); Inner(depth); html.Append("</em>"); return;
            case "strong": html.Append("<strong>"); Inner(depth); html.Append("</strong>"); return;
            case "strikethrough": html.Append("<s>"); Inner(depth); html.Append("</s>"); return;
            case "sub": case "sup": case "code": html.Append('<' + name + '>'); Inner(depth); html.Append("</" + name + '>'); return;
            case "a":
                string target = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value ?? "";
                // Note links (#n1) point into the notes body, which is a separate chapter; the viewer finds the id in any chapter.
                html.Append($"<a href=\"{WebUtility.HtmlEncode(target)}\">"); Inner(depth); html.Append("</a>"); return;
            case "image":
                string image = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value?.TrimStart('#');
                if (image != null) html.Append($"<img src=\"{WebUtility.HtmlEncode(image)}\"/>"); return;
            case "table": case "tr": case "td": case "th": html.Append(Open(name)); Inner(depth); html.Append("</" + name + '>'); return;
            case "binary": return;
            default: Inner(depth); return;
        }
    }
}
