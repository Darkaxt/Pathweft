// EBOOK-01: (X)HTML book chapters laid out as native WPF flow content. Parsed with AngleSharp without a
// loader or scripting; only structure, emphasis, images from the book and internal links are kept.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace FileExplorer.Preview.Ebook;

internal static class Markup
{
    internal static IDocument Parse(string html) => new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(html ?? "");
    internal static string PlainText(string html) => EpubReader.Clean(Parse("<body>" + html + "</body>").Body?.TextContent ?? html);
}

internal sealed class EbookFlow
{
    readonly EbookBook book;
    readonly string href;
    readonly Action<string> navigate;
    internal readonly Dictionary<string, TextElement> Anchors = new(StringComparer.Ordinal);
    TextElement pendingAnchor;
    int images;

    EbookFlow(EbookBook book, string href, Action<string> navigate) { this.book = book; this.href = href; this.navigate = navigate; }

    internal static (FlowDocument Document, Dictionary<string, TextElement> Anchors) Build(EbookBook book, EbookChapter chapter, Action<string> navigate)
    {
        var flow = new EbookFlow(book, chapter.Href, navigate);
        var document = new FlowDocument { FontFamily = new("Georgia, Cambria, Segoe UI"), FontSize = 16, LineHeight = 25, PagePadding = new(28, 20, 28, 20),
            ColumnWidth = 620, ColumnGap = 48, IsColumnWidthFlexible = true, IsOptimalParagraphEnabled = true, IsHyphenationEnabled = true, Background = Brushes.Transparent, TextAlignment = TextAlignment.Left };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Palette.E8EEF6");
        var body = Markup.Parse(chapter.Html).Body;
        if (body != null) flow.Blocks(body, document.Blocks);
        if (document.Blocks.Count == 0) document.Blocks.Add(new Paragraph(new Run("(This chapter has no text.)")) { FontStyle = FontStyles.Italic });
        return (document, flow.Anchors);
    }

    static bool IsBlock(string name) => name is "p" or "div" or "section" or "article" or "aside" or "header" or "footer" or "nav" or "main" or "figure" or "figcaption"
        or "blockquote" or "ul" or "ol" or "li" or "dl" or "dt" or "dd" or "table" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th" or "hr" or "pre" or "center"
        or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "body" or "address" or "svg";

    void Register(IElement element, TextElement target)
    {
        if (element.Id is { Length: > 0 } id) Anchors.TryAdd(id, target);
        if (element.LocalName == "a" && element.GetAttribute("name") is { Length: > 0 } name) Anchors.TryAdd(name, target);
    }

    // Block children: runs of inline content become paragraphs, block elements map to WPF blocks.
    void Blocks(IElement parent, BlockCollection target)
    {
        Paragraph loose = null;
        foreach (var node in parent.ChildNodes)
        {
            if (node is IElement element && IsBlock(element.LocalName)) { loose = null; if (Block(element) is { } block) target.Add(block); continue; }
            if (node is IText text && string.IsNullOrWhiteSpace(text.Data) && loose == null) continue;
            if (loose == null) { loose = new Paragraph { Margin = new(0, 0, 0, 10) }; target.Add(loose); }
            Inline(node, loose.Inlines);
        }
    }

    Block Block(IElement element)
    {
        string name = element.LocalName;
        switch (name)
        {
            case "hr": return Mark(element, new Paragraph(new Run("⁂")) { TextAlignment = TextAlignment.Center, Margin = new(0, 12, 0, 12) });
            case "ul": case "ol":
                var list = new List { MarkerStyle = name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new(0, 4, 0, 10), Padding = new(22, 0, 0, 0) };
                foreach (var item in element.Children)
                {
                    var listItem = new ListItem(); if (item.LocalName == "li") Blocks(item, listItem.Blocks); else if (Block(item) is { } other) listItem.Blocks.Add(other);
                    if (listItem.Blocks.Count > 0) list.ListItems.Add(listItem);
                }
                return Mark(element, list);
            case "table": return Mark(element, TableBlock(element));
            case "pre":
                var pre = new Paragraph(new Run(element.TextContent)) { FontFamily = new("Cascadia Mono, Consolas"), FontSize = 13, Margin = new(0, 6, 0, 10) };
                return Mark(element, pre);
            case "svg":
                // SVG cover pages wrap one <image>; show that image.
                var href = element.QuerySelector("image")?.Attributes.FirstOrDefault(a => a.LocalName == "href")?.Value;
                return href != null && Picture(href, true) is { } picture ? Mark(element, new BlockUIContainer(picture)) : null;
        }
        // Paragraph-like blocks whose children are all inline become one Paragraph; containers become Sections.
        bool inlineOnly = element.Children.All(c => !IsBlock(c.LocalName));
        if (inlineOnly)
        {
            var paragraph = new Paragraph { Margin = new(0, 0, 0, 10) };
            foreach (var node in element.ChildNodes) Inline(node, paragraph.Inlines);
            if (paragraph.Inlines.Count == 0 && element.Id == null) return null;
            Style(element, paragraph);
            return Mark(element, paragraph);
        }
        var section = new Section();
        Blocks(element, section.Blocks);
        Style(element, section);
        return Mark(element, section);
    }

    T Mark<T>(IElement element, T block) where T : TextElement { Register(element, block); return block; }

    static void Style(IElement element, Block block)
    {
        switch (element.LocalName)
        {
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                int level = element.LocalName[1] - '0';
                block.FontSize = level switch { 1 => 28, 2 => 24, 3 => 20, 4 => 18, _ => 16 };
                block.FontWeight = FontWeights.SemiBold; block.Margin = new(0, level <= 2 ? 18 : 12, 0, 10);
                if (block is Paragraph heading) heading.KeepWithNext = true;
                break;
            case "blockquote":
                block.Margin = new(18, 6, 12, 10); block.Padding = new(12, 0, 0, 0); block.BorderThickness = new(3, 0, 0, 0);
                block.SetResourceReference(TextElement.ForegroundProperty, "Palette.C9D3E0");
                block.BorderBrush = MainWindow.Hex("#4C8DFF");
                break;
            case "center": case "figcaption": block.TextAlignment = TextAlignment.Center; break;
            case "dt": block.FontWeight = FontWeights.SemiBold; break;
            case "dd": block.Margin = new(22, 0, 0, 8); break;
        }
        // The common book classes and inline styles for centred and right-aligned text.
        string style = (element.GetAttribute("style") ?? "") + " " + (element.GetAttribute("align") ?? "");
        if (style.Contains("center", StringComparison.OrdinalIgnoreCase)) block.TextAlignment = TextAlignment.Center;
        else if (style.Contains("right", StringComparison.OrdinalIgnoreCase)) block.TextAlignment = TextAlignment.Right;
    }

    Table TableBlock(IElement element)
    {
        var table = new Table { CellSpacing = 0, Margin = new(0, 6, 0, 12) };
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        int columns = 0;
        foreach (var row in element.QuerySelectorAll("tr").Where(r => r.Closest("table") == element).Take(2000))
        {
            var tableRow = new TableRow();
            foreach (var cell in row.Children.Where(c => c.LocalName is "td" or "th"))
            {
                var tableCell = new TableCell { Padding = new(6, 3, 6, 3), BorderThickness = new(0, 0, 0, 1), BorderBrush = MainWindow.Hex("#33FFFFFF") };
                if (int.TryParse(cell.GetAttribute("colspan"), out int span) && span is > 1 and < 100) tableCell.ColumnSpan = span;
                Blocks(cell, tableCell.Blocks);
                if (cell.LocalName == "th") tableCell.FontWeight = FontWeights.SemiBold;
                tableRow.Cells.Add(tableCell);
            }
            columns = Math.Max(columns, tableRow.Cells.Sum(c => c.ColumnSpan));
            group.Rows.Add(tableRow);
        }
        for (int i = 0; i < columns; i++) table.Columns.Add(new TableColumn());
        return table;
    }

    void Inline(INode node, InlineCollection target)
    {
        if (node is IText text) { if (text.Data.Length > 0) target.Add(new Run(Collapse(text.Data))); return; }
        if (node is not IElement element) return;
        string name = element.LocalName;
        if (name is "script" or "style" or "head" or "title" or "noscript") return;
        if (name == "br") { target.Add(new LineBreak()); return; }
        if (name == "img" || name == "image")
        {
            string source = element.GetAttribute("src") ?? element.Attributes.FirstOrDefault(a => a.LocalName == "href")?.Value;
            if (source != null && Picture(source, false) is { } picture) target.Add(Mark(element, new InlineUIContainer(picture) { BaselineAlignment = BaselineAlignment.Bottom }));
            else if (element.GetAttribute("alt") is { Length: > 0 } alt) target.Add(new Run("[" + alt + "]"));
            return;
        }
        if (IsBlock(name))
        {
            // A block inside inline content (div in span): keep its text on a new line.
            target.Add(new LineBreak()); foreach (var child in element.ChildNodes) Inline(child, target); return;
        }
        Span span = name switch
        {
            "em" or "i" or "cite" or "dfn" or "var" => new Italic(),
            "strong" or "b" => new Bold(),
            "u" or "ins" => new Underline(),
            "a" when element.GetAttribute("href") is { Length: > 0 } => Link(element.GetAttribute("href")),
            _ => new Span()
        };
        switch (name)
        {
            case "s" or "strike" or "del": span.TextDecorations = TextDecorations.Strikethrough; break;
            case "sup": span.BaselineAlignment = BaselineAlignment.Superscript; span.FontSize = 11; break;
            case "sub": span.BaselineAlignment = BaselineAlignment.Subscript; span.FontSize = 11; break;
            case "small": span.FontSize = 13; break;
            case "code" or "tt" or "kbd" or "samp": span.FontFamily = new("Cascadia Mono, Consolas"); span.FontSize = 14; break;
        }
        if ((element.GetAttribute("style") ?? "").Contains("italic", StringComparison.OrdinalIgnoreCase)) span.FontStyle = FontStyles.Italic;
        if ((element.GetAttribute("style") ?? "").Contains("bold", StringComparison.OrdinalIgnoreCase)) span.FontWeight = FontWeights.Bold;
        foreach (var child in element.ChildNodes) Inline(child, span.Inlines);
        Register(element, span);
        target.Add(span);
    }

    Span Link(string reference)
    {
        // Links inside the book navigate the preview; web links are shown, never opened.
        if (reference.Contains("://") || reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var external = new Span { ToolTip = reference }; external.SetResourceReference(TextElement.ForegroundProperty, "Palette.8DB6FF"); return external;
        }
        string target = EbookBook.Resolve(href, reference) + (reference.IndexOf('#') is >= 0 and var at ? reference[at..] : "");
        var link = new Hyperlink { ToolTip = "Go to this place in the book" };
        link.SetResourceReference(TextElement.ForegroundProperty, "Palette.8DB6FF");
        link.Click += (_, _) => navigate(target);
        return link;
    }

    static string Collapse(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length); bool space = false;
        foreach (char c in text) { if (c is ' ' or '\n' or '\r' or '\t') { if (!space) builder.Append(' '); space = true; } else { builder.Append(c); space = false; } }
        return builder.ToString();
    }

    FrameworkElement Picture(string source, bool page)
    {
        if (++images > 400 || !book.Resources.TryGetValue(EbookBook.Resolve(href, source), out var bytes) && !book.Resources.TryGetValue(source.TrimStart('#'), out bytes)) return null;
        try
        {
            // Large scans are decoded at most 1200 pixels wide; smaller images keep their own size.
            int width = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).PixelWidth;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (width > 1200) bitmap.DecodePixelWidth = 1200;
            bitmap.StreamSource = new MemoryStream(bytes); bitmap.EndInit(); bitmap.Freeze();
            // Images fit the column; small inline images (drop caps, symbols) keep their size.
            var picture = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 600, MaxHeight = page ? 760 : 640, Margin = new(0, 6, 0, 6) };
            if (!page && bitmap.PixelHeight < 64) { picture.MaxHeight = bitmap.PixelHeight; picture.Margin = default; }
            return picture;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException) { return null; }
    }
}
