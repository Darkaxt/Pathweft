#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Preview;
using FileExplorer.Preview.Ebook;

namespace FileExplorer;

// EBOOK-01 verification (specialist scenario "ebook"): owner-shaped EPUB, FB2 and MOBI fixtures created here, plus a
// read-only pass over the owner's library when PATHWEFT_EBOOK_LIBRARY names it (books are read, never copied or changed).
public partial class MainWindow
{
    internal async Task VerifyEbooks(string root, Action<bool, string> check)
    {
        string epub = Path.Combine(root, "voyage.epub"), fb2 = Path.Combine(root, "tales.fb2"), mobi = Path.Combine(root, "letters.mobi"), drm = Path.Combine(root, "locked.epub");
        File.WriteAllBytes(epub, FixtureEpub(false)); File.WriteAllBytes(drm, FixtureEpub(true));
        File.WriteAllBytes(fb2, FixtureFb2()); File.WriteAllBytes(mobi, FixtureMobi());
        var hashes = new[] { epub, fb2, mobi, drm }.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        var body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var window = new Window { Content = body, Width = 520, Height = 760, ShowInTaskbar = false, Background = new SolidColorBrush(Appearance.Background), Foreground = (Brush)FindResource("Palette.E8EEF6") };
        HiddenTrialPresentation.Prepare(window); window.Show();
        using var session = new PreviewSession(this, body, _ => { });
        static async Task<bool> Until(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++) await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ContinueWith(_ => Task.Delay(25)).Unwrap();
            return condition();
        }
        string Label() => PreviewChildren<TextBlock>(body).FirstOrDefault(t => t.Text.StartsWith("Page ") || t.Text.StartsWith("Laying"))?.Text ?? "";
        try
        {
            // EPUB 3: metadata, cover, nav contents, paged chapters, chapter crossing, contents and in-book links.
            session.Show(new Entry { Path = epub, Name = "voyage.epub" }); await session.Completion;
            check(session.State == "ready" && session.ModuleId == "ebook", "EBOOK-01 an EPUB opens in the eBook preview module");
            var viewer = (EbookViewer)session.ViewerForVerification;
            var texts = PreviewChildren<TextBlock>(body).Select(t => t.Text).ToArray();
            check(texts.Contains("Fixture Voyage") && texts.Contains("Ada Writer") && texts.Any(t => t.StartsWith("EPUB 3 · 2 parts · Harbor Press · 2021")) && PreviewChildren<Image>(body).Any(i => AutomationProperties.GetName(i) == "Cover"),
                "EPUB title, author, format, publisher, year and cover are shown above the pages");
            check(viewer.ContentsForVerification.Items.Count == 3 && ((ComboBoxItem)viewer.ContentsForVerification.Items[2]).Content.ToString().Trim() == "Night", "The EPUB 3 navigation document fills the contents picker with its nested entries");
            check(await Until(() => Label().StartsWith("Page 1 of ") && Label().EndsWith(" · Part 1 of 2") && viewer.PagesForVerification.PageCount > 1), "The first chapter is laid out as several pages: " + Label());
            var document = (FlowDocument)viewer.PagesForVerification.Document;
            check(LogicalTreeHelper.GetChildren(document).OfType<object>().Any() && FlowDescendants(document).OfType<InlineUIContainer>().Any(c => c.Child is Image { Source: not null }) && FlowDescendants(document).OfType<Bold>().Any() && FlowDescendants(document).OfType<Table>().Any() && FlowDescendants(document).OfType<List>().Any(),
                "Chapter markup becomes native flow content: images from the book, emphasis, lists and tables");
            int firstPages = viewer.PagesForVerification.PageCount;
            for (int i = 0; i < firstPages; i++) { viewer.NextForVerification(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            check(await Until(() => viewer.ChapterForVerification == 1 && Label().StartsWith("Page 1 of ") && Label().EndsWith("Part 2 of 2")), "Next page past the last page continues with the next chapter: " + Label());
            viewer.PreviousForVerification();
            check(await Until(() => viewer.ChapterForVerification == 0 && Label() == $"Page {firstPages} of {firstPages} · Part 1 of 2"), "Previous page from a chapter's first page opens the previous chapter's last page: " + Label());
            viewer.ContentsForVerification.SelectedIndex = 2;
            check(await Until(() => viewer.ChapterForVerification == 1 && viewer.PagesForVerification.MasterPageNumber > 1), "Choosing a contents entry opens its chapter at the entry's place (page " + viewer.PagesForVerification.MasterPageNumber + ")");
            viewer.ContentsForVerification.SelectedIndex = 0;
            await Until(() => viewer.ChapterForVerification == 0);
            var link = FlowDescendants((FlowDocument)viewer.PagesForVerification.Document).OfType<Hyperlink>().First();
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            check(await Until(() => viewer.ChapterForVerification == 1 && viewer.PagesForVerification.MasterPageNumber > 1 && ((ComboBoxItem)viewer.ContentsForVerification.SelectedItem).Content.ToString().Trim() == "Night"),
                "A link inside the book opens its target chapter and place, and the contents picker follows");
            var next = PreviewChildren<Button>(body).Single(b => AutomationProperties.GetName(b) == "Next page");
            for (int i = 0; i < 100 && next.IsEnabled; i++) { next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            check(!next.IsEnabled && Label().EndsWith("Part 2 of 2"), "Next page is disabled on the book's last page");
            await S1Verification.Snapshot(window, root, "ebook-epub");

            session.Show(new Entry { Path = drm, Name = "locked.epub" }); await session.Completion;
            check(session.State == "ready" && PreviewChildren<TextBlock>(body).Any(t => t.Text.Contains("DRM-protected")) && PreviewChildren<TextBlock>(body).Any(t => t.Text == "Fixture Voyage"),
                "A DRM-protected EPUB shows its details and says the text cannot be shown");

            // FB2: description, binary cover, sections as chapters and contents, note links into the notes body.
            session.Show(new Entry { Path = fb2, Name = "tales.fb2" }); await session.Completion;
            var tales = (EbookViewer)session.ViewerForVerification;
            check(session.ModuleId == "ebook" && tales.BookForVerification.Format == "FB2" && tales.BookForVerification.Author == "Ivan Petrov" && tales.BookForVerification.Cover != null
                && tales.BookForVerification.Toc.Select(t => t.Title).SequenceEqual(new[] { "The River", "The Mill", "Notes" }), "FB2 author, cover and section contents are read: " + string.Join(", ", tales.BookForVerification.Toc.Select(t => t.Title)));
            await Until(() => Label().StartsWith("Page 1"));
            var note = FlowDescendants((FlowDocument)tales.PagesForVerification.Document).OfType<Hyperlink>().First();
            note.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            check(await Until(() => tales.ChapterForVerification == tales.BookForVerification.Chapters.Count - 1), "An FB2 note link opens the note in the notes body");

            // MOBI 6: PalmDB records, EXTH author and cover, page breaks as parts, recindex images.
            session.Show(new Entry { Path = mobi, Name = "letters.mobi" }); await session.Completion;
            var letters = session.ViewerForVerification as EbookViewer;
            check(session.ModuleId == "ebook" && letters?.BookForVerification is { Format: "MOBI", Title: "Letters Home", Author: "Mary Shore", Cover: not null } mobiBook && mobiBook.Chapters.Count == 2,
                "MOBI title, author, cover and page-broken parts are read: " + (letters?.BookForVerification is { } b ? $"{b.Format} '{b.Title}' '{b.Author}' {b.Chapters.Count} parts" : session.State));
            if (letters != null)
            {
                await Until(() => Label().StartsWith("Page 1"));
                letters.NextForVerification(); await Until(() => letters.ChapterForVerification == 1);
                check(FlowDescendants((FlowDocument)letters.PagesForVerification.Document).OfType<InlineUIContainer>().Any(c => c.Child is Image { Source: not null }), "The MOBI image record referenced by recindex is shown in its part");
            }
            session.Clear(); await PreviewSession.DrainAsync();
            check(hashes.All(h => h.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(h.Key)))), "Previewing books leaves their bytes unchanged");
            check(HiddenTrialPresentation.ForegroundViolations == 0, "eBook previews perform no foreground takeover");
        }
        finally { session.Clear(); await PreviewSession.DrainAsync(); window.Close(); }
        VerifyEbookLibrary(check);
    }

    // The owner's library (read-only): every book of a supported format must open with text, or say why it cannot.
    static void VerifyEbookLibrary(Action<bool, string> check)
    {
        string library = Environment.GetEnvironmentVariable("PATHWEFT_EBOOK_LIBRARY");
        if (string.IsNullOrEmpty(library) || !Directory.Exists(library)) { check(true, "Owner library pass not requested (PATHWEFT_EBOOK_LIBRARY unset)"); return; }
        var counts = new Dictionary<string, (int Opened, int Protected, int Failed)>();
        var failures = new List<string>();
        // A file the share refuses to open (another program rewriting it, permissions) is not a reader failure; it is listed.
        var unreadable = new List<string>();
        foreach (var path in Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories).Where(p => EbookViewer.Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).Take(1000))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            var (opened, locked, failed) = counts.GetValueOrDefault(extension);
            try
            {
                var book = extension == ".epub" ? EpubReader.Read(path, CancellationToken.None) : extension == ".fb2" ? Fb2Reader.Read(File.ReadAllBytes(path), CancellationToken.None) : MobiReader.Read(File.ReadAllBytes(path), CancellationToken.None);
                if (book.Notice != null) locked++;
                else
                {
                    // The text must survive layout: some chapter yields flow content with words in it.
                    bool text = book.Chapters.Take(6).Any(c => EbookFlow.Build(book, c, _ => { }).Document is var flow && new TextRange(flow.ContentStart, flow.ContentEnd).Text.Count(char.IsLetter) > 20);
                    if (text) opened++; else { failed++; failures.Add(Path.GetFileName(path) + ": no text"); }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { unreadable.Add(Path.GetFileName(path) + ": " + ex.GetType().Name); }
            catch (Exception ex) { failed++; failures.Add(Path.GetFileName(path) + ": " + ex.GetType().Name + " " + ex.Message); }
            counts[extension] = (opened, locked, failed);
        }
        string summary = string.Join("; ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value.Opened} opened, {c.Value.Protected} DRM, {c.Value.Failed} failed"));
        check(counts.Count > 0 && counts.Values.All(c => c.Failed == 0), "Owner library: every book opens with text or is reported DRM-protected: " + summary + (unreadable.Count > 0 ? $"; {unreadable.Count} not readable on the share" : "") + (failures.Count > 0 ? " | " + string.Join(" | ", failures.Take(20)) : ""));
    }

    static IEnumerable<DependencyObject> FlowDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var next in FlowDescendants(child)) yield return next; }
    }

    static byte[] FixturePng(Color color, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    static string Paragraphs(string topic, int count) => string.Concat(Enumerable.Range(1, count).Select(i =>
        $"<p>{topic} {i}: the ship kept to the coast while the crew counted the lights of the villages, and nobody spoke of the storm that had passed in the night before the harbor came in sight.</p>"));

    static byte[] FixtureEpub(bool encrypted)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string name, string text, CompressionLevel level = CompressionLevel.Optimal) { using var writer = new StreamWriter(zip.CreateEntry(name, level).Open(), new UTF8Encoding(false)); writer.Write(text); }
            void AddBytes(string name, byte[] data) { using var entry = zip.CreateEntry(name).Open(); entry.Write(data); }
            Add("mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            Add("META-INF/container.xml", "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
            if (encrypted) Add("META-INF/encryption.xml", "<encryption xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" xmlns:enc=\"http://www.w3.org/2001/04/xmlenc#\"><enc:EncryptedData><enc:EncryptionMethod Algorithm=\"http://www.w3.org/2001/04/xmlenc#aes128-cbc\"/><enc:CipherData><enc:CipherReference URI=\"OEBPS/text/ch1.xhtml\"/></enc:CipherData></enc:EncryptedData></encryption>");
            // The package declares XML 1.1, as some Tor books do; .NET reads only 1.0, so the reader treats it as 1.0.
            Add("OEBPS/content.opf", "<?xml version=\"1.1\" encoding=\"utf-8\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
                "<dc:identifier id=\"id\">urn:uuid:fixture</dc:identifier><dc:title>Fixture Voyage</dc:title><dc:creator>Ada Writer</dc:creator><dc:publisher>Harbor Press</dc:publisher><dc:date>2021-05-01</dc:date><dc:language>en</dc:language>" +
                "<dc:description>&lt;p&gt;A short voyage along the coast.&lt;/p&gt;</dc:description><meta name=\"cover\" content=\"cover-img\"/></metadata><manifest>" +
                "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/><item id=\"ch1\" href=\"text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"ch2\" href=\"text/ch2.xhtml\" media-type=\"application/xhtml+xml\"/>" +
                "<item id=\"cover-img\" href=\"images/cover.png\" media-type=\"image/png\"/><item id=\"pic\" href=\"images/pic.png\" media-type=\"image/png\"/></manifest><spine><itemref idref=\"ch1\"/><itemref idref=\"ch2\"/></spine></package>");
            Add("OEBPS/nav.xhtml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><body><nav epub:type=\"toc\"><ol><li><a href=\"text/ch1.xhtml\">Departure</a></li><li><a href=\"text/ch2.xhtml#harbor\">Harbor</a><ol><li><a href=\"text/ch2.xhtml#night\">Night</a></li></ol></li></ol></nav></body></html>");
            Add("OEBPS/text/ch1.xhtml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>1</title><style>p{color:red}</style></head><body><h1>Departure</h1><p>We left at <strong>dawn</strong> and <em>quietly</em>; see <a href=\"ch2.xhtml#night\">the night</a>.</p>" +
                "<p><img src=\"../images/pic.png\" alt=\"map\"/></p><ul><li>rope</li><li>lamp</li></ul><table><tr><th>Day</th><th>Miles</th></tr><tr><td>1</td><td>40</td></tr></table>" + Paragraphs("Departure", 40) + "</body></html>");
            Add("OEBPS/text/ch2.xhtml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><body><h2 id=\"harbor\">Harbor</h2>" + Paragraphs("Harbor", 40) + "<h2 id=\"night\">Night</h2>" + Paragraphs("Night", 12) + "</body></html>");
            AddBytes("OEBPS/images/cover.png", FixturePng(Colors.SteelBlue, 60, 90));
            AddBytes("OEBPS/images/pic.png", FixturePng(Colors.DarkOrange, 200, 120));
        }
        return stream.ToArray();
    }

    static byte[] FixtureFb2()
    {
        string cover = Convert.ToBase64String(FixturePng(Colors.SeaGreen, 60, 90));
        string text = "<?xml version=\"1.0\" encoding=\"utf-8\"?><FictionBook xmlns=\"http://www.gribuser.ru/xml/fictionbook/2.0\" xmlns:l=\"http://www.w3.org/1999/xlink\"><description><title-info><genre>prose</genre>" +
            "<author><first-name>Ivan</first-name><last-name>Petrov</last-name></author><book-title>Tales</book-title><annotation><p>Two short tales.</p></annotation><date>1999</date><coverpage><image l:href=\"#cover.png\"/></coverpage><lang>en</lang></title-info></description>" +
            "<body><title><p>Tales</p></title><section><title><p>The River</p></title><p>The river ran<a l:href=\"#n1\" type=\"note\">[1]</a> past the mill.</p>" + Paragraphs("River", 20).Replace("<p>", "<p>").Replace("</p>", "</p>") + "</section>" +
            "<section><title><p>The Mill</p></title><p>The <emphasis>mill</emphasis> wheel turned.</p><empty-line/><poem><stanza><v>Turn, wheel,</v><v>turn.</v></stanza></poem></section></body>" +
            "<body name=\"notes\"><title><p>Notes</p></title><section id=\"n1\"><title><p>1</p></title><p>A small river in the north.</p></section></body>" +
            $"<binary id=\"cover.png\" content-type=\"image/png\">{cover}</binary></FictionBook>";
        return Encoding.UTF8.GetBytes(text);
    }

    // A minimal uncompressed MOBI 6 (PalmDB "BOOKMOBI"): record 0 header with EXTH, one text record, two image records.
    static byte[] FixtureMobi()
    {
        byte[] html = Encoding.UTF8.GetBytes("<html><head><guide></guide></head><body><h1>First letter</h1><p>Dear <b>home</b>, the weather is fine.</p>" + Paragraphs("Letter", 3) +
            "<mbp:pagebreak/><h1>Second letter</h1><p>Here is a picture: <img recindex=\"00002\"/></p></body></html>");
        if (html.Length > 4096) throw new InvalidOperationException("fixture text exceeds one record");
        byte[] cover = FixturePng(Colors.Firebrick, 60, 90), picture = FixturePng(Colors.Goldenrod, 120, 80);
        static void U32(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
        static void U16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }
        byte[] title = Encoding.UTF8.GetBytes("Letters Home");
        var exth = new MemoryStream();
        void Item(uint type, byte[] value) { var head = new byte[8]; U32(head, 0, type); U32(head, 4, (uint)(value.Length + 8)); exth.Write(head); exth.Write(value); }
        Item(100, Encoding.UTF8.GetBytes("Mary Shore")); Item(503, title); var zero = new byte[4]; Item(201, zero);
        int exthLength = (int)exth.Length + 12, exthPadded = (exthLength + 3) / 4 * 4;
        var record0 = new byte[16 + 0xE8 + exthPadded + title.Length + 4];
        U16(record0, 0, 1); U32(record0, 4, (uint)html.Length); U16(record0, 8, 1); U16(record0, 10, 4096); U16(record0, 12, 0);
        Encoding.ASCII.GetBytes("MOBI").CopyTo(record0, 16); U32(record0, 0x14, 0xE8); U32(record0, 0x18, 2); U32(record0, 0x1C, 65001); U32(record0, 0x24, 6);
        for (int at = 0x28; at < 16 + 0xE8; at += 4) if (at is not (0x54 or 0x58 or 0x5C or 0x68 or 0x6C or 0x70 or 0x74 or 0x80 or 0xF0)) U32(record0, at, 0xFFFFFFFF);
        U32(record0, 0x54, (uint)(16 + 0xE8 + exthPadded)); U32(record0, 0x58, (uint)title.Length); U32(record0, 0x5C, 9);
        U32(record0, 0x68, 6); U32(record0, 0x6C, 2); U32(record0, 0x70, 0); U32(record0, 0x74, 0); U32(record0, 0x80, 0x40); U32(record0, 0xF0, 0);
        int e = 16 + 0xE8; Encoding.ASCII.GetBytes("EXTH").CopyTo(record0, e); U32(record0, e + 4, (uint)exthLength); U32(record0, e + 8, 3); exth.ToArray().CopyTo(record0, e + 12);
        title.CopyTo(record0, 16 + 0xE8 + exthPadded);
        var records = new[] { record0, html, cover, picture };
        int headerLength = 78 + records.Length * 8 + 2;
        var output = new MemoryStream(); var header = new byte[headerLength];
        Encoding.ASCII.GetBytes("Letters_Home").CopyTo(header, 0); Encoding.ASCII.GetBytes("BOOKMOBI").CopyTo(header, 60); U16(header, 76, records.Length);
        int offset = headerLength;
        for (int i = 0; i < records.Length; i++) { U32(header, 78 + i * 8, (uint)offset); U32(header, 78 + i * 8 + 4, (uint)(i * 2)); offset += records[i].Length; }
        output.Write(header); foreach (var record in records) output.Write(record);
        return output.ToArray();
    }
}
#endif
