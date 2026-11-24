// EBOOK-01 (owner, 2026-11-24: "Can we consider adding eBook previewers like the PDF ones? (M:\Books\Ebooks)",
// "Like before, we can borrow the code from https://github.com/kovidgoyal/calibre"): EPUB, FB2 and MOBI/AZW3/PRC
// books shown as native paged WPF flow documents, with the PDF viewer's page controls plus a contents picker.
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileExplorer.Preview.Ebook;

internal sealed class EbookViewer(MainWindow owner, Entry entry) : IViewer
{
    internal static readonly string[] Extensions = { ".epub", ".fb2", ".mobi", ".azw3", ".azw", ".prc" };
    ContextObject context;
    EbookBook book;
    FlowDocumentPageViewer pages;
    TextBlock label;
    TextBox number;
    Button previous, next;
    ComboBox contents;
    Dictionary<string, TextElement> anchors = new();
    int chapter = -1;
    bool cleaned, choosing;
    readonly List<(DependencyPropertyDescriptor Property, EventHandler Handler)> watchers = new();
#if DEBUG
    internal EbookBook BookForVerification => book;
    internal int ChapterForVerification => chapter;
    internal FlowDocumentPageViewer PagesForVerification => pages;
    internal ComboBox ContentsForVerification => contents;
    internal TextBlock LabelForVerification => label;
    internal void NextForVerification() => Next();
    internal void PreviousForVerification() => Previous();
    internal void NavigateForVerification(string href) => Navigate(href);
#endif

    public bool CanHandle(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[78]; int count = stream.Read(head, 0, head.Length);
        return extension switch
        {
            ".epub" => count >= 4 && head[0] == 'P' && head[1] == 'K',
            ".fb2" => true,
            _ => count >= 68 && MobiReader.IsMobi(head.AsSpan(0, count))
        };
    }
    public void Init() { }
    public void Prepare(string path, ContextObject current) { context = current; current.Title = entry.Name + " · eBook"; }

    public async Task View(string path, ContextObject current)
    {
        context = current;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        book = await Task.Run(() =>
        {
            if (extension == ".epub") return EpubReader.Read(path, current.Token);
            var info = new FileInfo(path);
            if (info.Length > 256L << 20) throw new InvalidDataException("The book is larger than the preview reads (256 MiB).");
            var data = File.ReadAllBytes(path);
            return extension == ".fb2" ? Fb2Reader.Read(data, current.Token) : MobiReader.Read(data, current.Token);
        }, current.Token);
        current.Token.ThrowIfCancellationRequested();
        current.SetRenderer("eBook (" + book.Format + ")", "Read without scripts, web content or downloads.");

        var root = new DockPanel();
        var header = Header(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        if (book.Notice != null || book.Chapters.Count == 0)
        {
            root.Children.Add(BuiltinViewer.Message(book.Notice ?? "This book has no readable text."));
            current.ViewerContent = root; return;
        }
        // Contents: the book's table of contents, or its chapters in reading order when it has none.
        var items = book.Toc.Count > 0 ? book.Toc : book.Chapters.Select((c, i) => new EbookTocEntry($"Part {i + 1}", c.Href, 0)).ToList();
        contents = new ComboBox { Style = (Style)owner.FindResource("PreviewClipCombo"), Margin = new(12, 0, 12, 6), MaxDropDownHeight = 420,
            ItemsSource = items.Select(t => new ComboBoxItem { Content = new string(' ', Math.Min(t.Depth, 6) * 3) + t.Title, Tag = t.Href, ToolTip = t.Title }).ToList() };
        System.Windows.Automation.AutomationProperties.SetName(contents, "Contents");
        contents.SelectionChanged += (_, _) => { if (!choosing && contents.SelectedItem is ComboBoxItem { Tag: string href }) Navigate(href); };
        DockPanel.SetDock(contents, Dock.Top); root.Children.Add(contents);

        var controls = new WrapPanel { Margin = new(8) };
        label = BuiltinViewer.Message("Loading…"); label.Margin = new(10, 5, 10, 5);
        previous = PreviewControls.Icon(owner, "Previous page", "\uE76B");
        next = PreviewControls.Icon(owner, "Next page", "\uE76C");
        number = new TextBox { Width = 48, MinHeight = 30, Margin = new(4), VerticalContentAlignment = VerticalAlignment.Center };
        number.SetResourceReference(Control.BackgroundProperty, "Palette.08FFFFFF"); number.SetResourceReference(Control.ForegroundProperty, "Palette.E8EEF6");
        System.Windows.Automation.AutomationProperties.SetName(number, "Page number"); number.ToolTip = "Enter a page number in this chapter and press Enter";
        controls.Children.Add(previous); controls.Children.Add(label); controls.Children.Add(next); controls.Children.Add(number);
        DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);

        pages = new FlowDocumentPageViewer { Template = PageTemplate(), Focusable = true, Background = Brushes.Transparent };
        foreach (var property in new[] { DocumentViewerBase.MasterPageNumberProperty, DocumentViewerBase.PageCountProperty })
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(FlowDocumentPageViewer)); EventHandler handler = (_, _) => Update();
            descriptor.AddValueChanged(pages, handler); watchers.Add((descriptor, handler));
        }
        root.Children.Add(pages);
        previous.Click += (_, _) => Previous();
        next.Click += (_, _) => Next();
        number.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return; args.Handled = true;
            if (int.TryParse(number.Text, out int page) && page > 0 && page <= pages.PageCount) pages.GoToPage(page);
            else label.Text = $"Enter a page from 1 to {pages.PageCount}";
        };
        root.PreviewKeyDown += (_, args) =>
        {
            if (args.OriginalSource is TextBox or ComboBox or ComboBoxItem) return;
            if (args.Key is Key.Right or Key.PageDown or Key.Space) { Next(); args.Handled = true; }
            else if (args.Key is Key.Left or Key.PageUp) { Previous(); args.Handled = true; }
        };
        current.ViewerContent = root;
        Show(0, null, false);
    }

    FrameworkElement Header()
    {
        var header = new DockPanel { Margin = new(12, 10, 12, 8), LastChildFill = true };
        if (book.Cover is { } cover)
            try
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelHeight = 240;
                bitmap.StreamSource = new MemoryStream(cover); bitmap.EndInit(); bitmap.Freeze();
                var image = new Image { Source = bitmap, Height = 110, MaxWidth = 90, Stretch = Stretch.Uniform, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                System.Windows.Automation.AutomationProperties.SetName(image, "Cover"); DockPanel.SetDock(image, Dock.Left); header.Children.Add(image);
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException) { }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var title = new TextBlock { Text = book.Title ?? Path.GetFileNameWithoutExtension(entry.Name), FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Palette.E8EEF6"); text.Children.Add(title);
        if (book.Author != null) { var author = new TextBlock { Text = book.Author, FontSize = 14, Margin = new(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }; author.SetResourceReference(TextBlock.ForegroundProperty, "Palette.C9D3E0"); text.Children.Add(author); }
        string year = book.Published is { Length: >= 4 } date && int.TryParse(date[..4], out _) ? date[..4] : book.Published;
        var details = new[] { book.Format, book.Chapters.Count > 0 ? $"{book.Chapters.Count} {(book.Chapters.Count == 1 ? "part" : "parts")}" : null, book.Publisher, year, book.Language }.Where(s => !string.IsNullOrWhiteSpace(s));
        var line = new TextBlock { Text = string.Join(" · ", details), FontSize = 12, Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA"); text.Children.Add(line);
        if (book.Description is { Length: > 0 } description)
        {
            var about = new TextBlock { Text = description, FontSize = 12, Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, MaxHeight = 50, ToolTip = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 } };
            about.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA"); text.Children.Add(about);
        }
        header.Children.Add(text);
        return header;
    }

    // Only the page itself: the viewer's own toolbar is replaced by the Pathweft controls below it.
    static ControlTemplate PageTemplate()
    {
        var template = new ControlTemplate(typeof(FlowDocumentPageViewer));
        var border = new FrameworkElementFactory(typeof(AdornerDecorator));
        var page = new FrameworkElementFactory(typeof(DocumentPageView));
        page.SetValue(DocumentViewerBase.IsMasterPageProperty, true);
        page.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0));
        border.AppendChild(page); template.VisualTree = border;
        return template;
    }

    void Show(int index, string anchor, bool lastPage)
    {
        if (cleaned || index < 0 || index >= book.Chapters.Count) return;
        chapter = index;
        var (document, found) = EbookFlow.Build(book, book.Chapters[index], Navigate);
        anchors = found;
        pages.Document = document;
        var paginator = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
        if (lastPage || anchor != null)
        {
            void Completed(object sender, EventArgs args)
            {
                paginator.PaginationCompleted -= Completed;
                if (cleaned || pages.Document != document) return;
                if (lastPage) pages.GoToPage(pages.PageCount);
                else if (anchors.TryGetValue(anchor, out var target)) target.BringIntoView();
            }
            paginator.PaginationCompleted += Completed;
        }
        else pages.GoToPage(1);
        Select(book.Chapters[index].Href, anchor);
        Update();
    }

    // Keeps the contents picker on the entry that covers the current place.
    void Select(string href, string anchor)
    {
        if (contents?.ItemsSource is not List<ComboBoxItem> items) return;
        var match = items.FirstOrDefault(i => (string)i.Tag == href + "#" + anchor) ?? items.FirstOrDefault(i => ((string)i.Tag).Split('#')[0].Equals(href, StringComparison.OrdinalIgnoreCase));
        if (match == null || contents.SelectedItem == match) return;
        choosing = true; contents.SelectedItem = match; choosing = false;
    }

    void Navigate(string target)
    {
        string path = target.Split('#')[0], fragment = target.Contains('#') ? target[(target.IndexOf('#') + 1)..] : null;
        int index = book.Chapters.FindIndex(c => c.Href.Equals(path, StringComparison.OrdinalIgnoreCase));
        // A bare #id (FB2 notes, MOBI filepos) may live in another part: find the part that defines it.
        if (fragment != null && (index < 0 || index == chapter && !anchors.ContainsKey(fragment)))
        {
            int holder = book.Chapters.FindIndex(c => c.Html.Contains("id=\"" + fragment + "\"", StringComparison.Ordinal) || c.Html.Contains("id='" + fragment + "'", StringComparison.Ordinal));
            if (holder >= 0) index = holder;
        }
        if (index < 0) return;
        if (index == chapter && fragment != null && anchors.TryGetValue(fragment, out var element)) { element.BringIntoView(); return; }
        if (index == chapter && fragment == null) { pages.GoToPage(1); return; }
        Show(index, fragment, false);
        pages.Focus();
    }

    void Next()
    {
        if (pages == null) return;
        if (pages.CanGoToNextPage) pages.NextPage(); else if (chapter + 1 < book.Chapters.Count) Show(chapter + 1, null, false);
    }
    void Previous()
    {
        if (pages == null) return;
        if (pages.CanGoToPreviousPage) pages.PreviousPage(); else if (chapter > 0) Show(chapter - 1, null, true);
    }

    void Update()
    {
        if (cleaned || pages == null) return;
        int page = pages.MasterPageNumber, count = pages.PageCount;
        label.Text = (count > 0 ? $"Page {page} of {count}" : "Laying out…") + (book.Chapters.Count > 1 ? $" · Part {chapter + 1} of {book.Chapters.Count}" : "");
        if (!number.IsKeyboardFocusWithin) number.Text = page > 0 ? page.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        previous.IsEnabled = pages.CanGoToPreviousPage || chapter > 0;
        next.IsEnabled = pages.CanGoToNextPage || chapter + 1 < book.Chapters.Count;
    }

    public void Cleanup()
    {
        if (cleaned) return; cleaned = true;
        foreach (var (property, handler) in watchers) property.RemoveValueChanged(pages, handler);
        watchers.Clear();
        if (pages != null) pages.Document = null;
        book = null; anchors = new();
    }
}
