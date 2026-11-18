using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Windows.Media.Ocr;
using Gdi = System.Drawing;
using WinImg = Windows.Graphics.Imaging;
using WinLanguage = Windows.Globalization.Language;
using Wpf = System.Windows.Media.Imaging;

namespace FileExplorer;

// Text Extractor engine (TOOLS-04), ported from PowerToys v0.103.3151 (2d817b0a, MIT): PowerOCR/PowerOCR.Core/**
// (Ocr/WindowsOcrRecognizer, Services/TextExtractorService, Imaging/BitmapPreprocessor+PreparedBitmap,
// Geometry/SelectionGeometry, Formatting/OcrTextFormatter+TableTextFormatter, Models/*). The interfaces are folded
// into concrete static code; the screen-capture app around it is not taken.
// Image path: WPF BitmapSource → (region crop) → PNG in a MemoryStream → System.Drawing.Bitmap for the donor's GDI+
// preprocessor → the donor's BMP stream → WinRT BitmapDecoder → SoftwareBitmap → Windows.Media.Ocr.
// Never installs language packs: a missing one is reported by Unavailable.

internal enum OcrCaptureMode { Region, SingleLine, Table, Word }

internal readonly record struct DisplayBounds(int X, int Y, int Width, int Height);
internal readonly record struct OcrPoint(double X, double Y);
internal readonly record struct PixelRect(int X, int Y, int Width, int Height);
internal readonly record struct PixelSelection(PixelRect Local, PixelRect Absolute);
internal sealed record OcrWordData(string Text, OcrRect Bounds);
internal sealed record OcrLineData(string Text, OcrRect Bounds, IReadOnlyList<OcrWordData> Words);
internal sealed record OcrDocument(IReadOnlyList<OcrLineData> Lines)
{
    internal IEnumerable<OcrWordData> Words => Lines.SelectMany(line => line.Words);
}

internal readonly record struct OcrRect(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal bool Contains(OcrPoint p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    internal bool Intersects(OcrRect o) => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;
    internal double IntersectionArea(OcrRect o) =>
        Math.Max(0, Math.Min(Right, o.Right) - Math.Max(X, o.X)) * Math.Max(0, Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y));
    internal OcrRect Union(OcrRect o)
    {
        double left = Math.Min(X, o.X), top = Math.Min(Y, o.Y);
        return new(left, top, Math.Max(Right, o.Right) - left, Math.Max(Bottom, o.Bottom) - top);
    }
}

internal static class SelectionGeometry
{
    // Two DIP corners → device pixels, clamped to the display
    internal static PixelSelection ToPixels(OcrPoint firstDip, OcrPoint secondDip, double rasterizationScale, DisplayBounds display)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rasterizationScale, 0);
        int left = (int)Math.Round(Math.Min(firstDip.X, secondDip.X) * rasterizationScale);
        int top = (int)Math.Round(Math.Min(firstDip.Y, secondDip.Y) * rasterizationScale);
        int right = (int)Math.Round(Math.Max(firstDip.X, secondDip.X) * rasterizationScale);
        int bottom = (int)Math.Round(Math.Max(firstDip.Y, secondDip.Y) * rasterizationScale);
        left = Math.Clamp(left, 0, display.Width);
        top = Math.Clamp(top, 0, display.Height);
        right = Math.Clamp(right, left, display.Width);
        bottom = Math.Clamp(bottom, top, display.Height);
        var local = new PixelRect(left, top, right - left, bottom - top);
        return new(local, new PixelRect(display.X + left, display.Y + top, local.Width, local.Height));
    }
}

internal sealed class PreparedBitmap(Gdi.Bitmap bitmap, double scaleX, double scaleY, int offsetX, int offsetY) : IDisposable
{
    internal Gdi.Bitmap Bitmap { get; } = bitmap;
    internal double ScaleX { get; } = scaleX;
    internal double ScaleY { get; } = scaleY;
    internal int OffsetX { get; } = offsetX;
    internal int OffsetY { get; } = offsetY;
    public void Dispose() => Bitmap.Dispose();
}

internal static class BitmapPreprocessor
{
    // Windows OCR misses text in tiny images; pad small ones onto the background colour
    const int MinimumDimension = 64, Padding = 8;

    internal static Gdi.Size GetOutputSize(Gdi.Bitmap source, double scale) => Calculate(source, scale).Output;

    internal static PreparedBitmap Prepare(Gdi.Bitmap source, double scale)
    {
        var d = Calculate(source, scale);
        var output = new Gdi.Bitmap(d.Output.Width, d.Output.Height, Gdi.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Gdi.Graphics.FromImage(output))
        {
            g.Clear(source.GetPixel(0, 0));
            g.InterpolationMode = Gdi.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = Gdi.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Gdi.Rectangle(d.Offset, d.Scaled), new Gdi.Rectangle(Gdi.Point.Empty, source.Size), Gdi.GraphicsUnit.Pixel);
        }
        return new PreparedBitmap(output, d.Scaled.Width / (double)source.Width, d.Scaled.Height / (double)source.Height, d.Offset.X, d.Offset.Y);
    }

    static (Gdi.Size Scaled, Gdi.Size Output, Gdi.Point Offset) Calculate(Gdi.Bitmap source, double scale)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);
        int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1, (int)Math.Round(source.Height * scale));
        bool padX = w < MinimumDimension, padY = h < MinimumDimension;
        int ow = padX ? Math.Max(w + Padding * 2, MinimumDimension + Padding * 2) : w;
        int oh = padY ? Math.Max(h + Padding * 2, MinimumDimension + Padding * 2) : h;
        return (new Gdi.Size(w, h), new Gdi.Size(ow, oh), new Gdi.Point(padX ? Padding : 0, padY ? Padding : 0));
    }
}

internal static class WindowsOcrRecognizer
{
    internal static async Task<OcrDocument> RecognizeAsync(Gdi.Bitmap bitmap, WinLanguage language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(language);
        var engine = OcrEngine.TryCreateFromLanguage(language)
            ?? throw new InvalidOperationException($"OCR engine could not be created for language '{language.LanguageTag}'.");
        using var stream = new MemoryStream();
        bitmap.Save(stream, Gdi.Imaging.ImageFormat.Bmp);
        stream.Position = 0;
        using var ras = stream.AsRandomAccessStream();
        var decoder = await WinImg.BitmapDecoder.CreateAsync(ras).AsTask(ct);
        using var soft = await decoder.GetSoftwareBitmapAsync().AsTask(ct);
        var result = await engine.RecognizeAsync(soft).AsTask(ct);
        return new OcrDocument(result.Lines.Select(MapLine).ToList());
    }

    static OcrLineData MapLine(OcrLine line)
    {
        var words = line.Words.Select(w => new OcrWordData(w.Text, new OcrRect(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))).ToList();
        var bounds = words.Count == 0 ? new OcrRect(0, 0, 0, 0) : words.Select(w => w.Bounds).Aggregate((a, b) => a.Union(b));
        return new OcrLineData(line.Text, bounds, words);
    }
}

internal static partial class OcrTextFormatter
{
    [GeneratedRegex(@"[ ]{2,}")]
    private static partial Regex RepeatedSpaces();

    internal static string FormatDocument(OcrDocument document, string languageTag)
    {
        bool useLineText = UsesSpaces(languageTag);
        bool rtl = CultureInfo.GetCultureInfo(languageTag).TextInfo.IsRightToLeft;
        var lines = new List<string>(document.Lines.Count);
        foreach (var line in document.Lines)
        {
            string text = useLineText ? line.Text : JoinCjkAwareWords(line.Words);
            if (rtl) text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse());
            lines.Add(text);
        }
        return string.Join(Environment.NewLine, lines).Trim();
    }

    internal static string FormatSingleLine(OcrDocument document, string languageTag) =>
        UsesSpaces(languageTag) ? CollapseToSingleLine(FormatDocument(document, languageTag)) : JoinCjkAwareWords(document.Words.ToList()).Trim();

    internal static string CollapseToSingleLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string collapsed = text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
        return RepeatedSpaces().Replace(collapsed, " ").Trim();
    }

    internal static bool UsesSpaces(string languageTag) =>
        !languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && !languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

    internal static string JoinCjkAwareWords(IReadOnlyList<OcrWordData> words)
    {
        var builder = new StringBuilder();
        WordBoundary? previous = null;
        foreach (var word in words)
        {
            if (!TryGetWordBoundary(word.Text, out var current)) continue;
            if (previous is WordBoundary p && ShouldInsertSpace(p, current)) builder.Append(' ');
            builder.Append(word.Text);
            previous = current;
        }
        return builder.ToString();
    }

    static bool ShouldInsertSpace(WordBoundary left, WordBoundary right)
    {
        var lc = Rune.GetUnicodeCategory(left.LastRune);
        var rc = Rune.GetUnicodeCategory(right.FirstRune);
        // A leading '.' or '%' can belong to a word (.gitignore, %PATH%); only punctuation-only tokens attach as suffixes
        if (lc is UnicodeCategory.OpenPunctuation or UnicodeCategory.InitialQuotePunctuation
            || rc is UnicodeCategory.ClosePunctuation or UnicodeCategory.FinalQuotePunctuation
            || (right.IsPunctuationOnly && IsSuffixPunctuation(right.FirstRune))
            || IsCjkTightPunctuation(left.LastRune) || IsCjkTightPunctuation(right.FirstRune)
            || rc is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return false;
        return left.UsesSpaces || right.UsesSpaces;
    }

    static bool IsSuffixPunctuation(Rune r) => r.Value is ',' or '.' or ':' or ';' or '!' or '?' or '%' or 0x2025 or 0x2026;

    // Ideographic/fullwidth/halfwidth comma, full stop, middle dot, ! % , . : ; ?
    static bool IsCjkTightPunctuation(Rune r) => r.Value is 0x3001 or 0x3002 or 0x30FB or 0xFF01 or 0xFF05 or 0xFF0C or 0xFF0E
        or 0xFF1A or 0xFF1B or 0xFF1F or 0xFF61 or 0xFF64 or 0xFF65;

    static bool TryGetWordBoundary(string text, out WordBoundary boundary)
    {
        Rune first = default, last = default;
        bool found = false, usesSpaces = false, punctuationOnly = true;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!found) { first = rune; found = true; }
            last = rune;
            usesSpaces |= Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.DecimalDigitNumber;
            punctuationOnly &= Rune.IsPunctuation(rune);
        }
        boundary = new WordBoundary(first, last, usesSpaces, punctuationOnly);
        return found;
    }

    readonly record struct WordBoundary(Rune FirstRune, Rune LastRune, bool UsesSpaces, bool IsPunctuationOnly);
}

internal static class TableTextFormatter
{
    // The legacy extractor removed six pixels from OCR bounds before projecting them onto rows and columns;
    // split across both edges so small OCR overlaps do not collapse adjacent cells
    const double Tolerance = 6, MinimumSizeForTolerance = 10;

    internal static string Format(IReadOnlyList<OcrLineData> cells, string languageTag)
    {
        if (cells.Count == 0) return string.Empty;
        var bounds = cells.Select(c => c.Bounds).Aggregate((a, b) => a.Union(b));
        var rows = BuildSegments(cells, bounds, horizontal: true);
        var columns = BuildSegments(cells, bounds, horizontal: false);
        var values = new string[rows.Count, columns.Count];
        bool usesSpaces = OcrTextFormatter.UsesSpaces(languageTag);
        string separator = usesSpaces ? " " : string.Empty;
        foreach (var cell in cells)
        {
            int row = BestIntersection(rows, cell.Bounds), column = BestIntersection(columns, cell.Bounds);
            string text = usesSpaces ? cell.Text : OcrTextFormatter.JoinCjkAwareWords(cell.Words);
            values[row, column] = string.IsNullOrEmpty(values[row, column]) ? text : string.Concat(values[row, column], separator, text);
        }
        var output = new List<string>(rows.Count);
        for (int row = 0; row < rows.Count; row++)
        {
            var inRow = new List<string>(columns.Count);
            for (int column = 0; column < columns.Count; column++) inRow.Add(values[row, column] ?? string.Empty);
            while (inRow.Count > 0 && string.IsNullOrEmpty(inRow[^1])) inRow.RemoveAt(inRow.Count - 1);
            output.Add(string.Join('\t', inRow));
        }
        return string.Join(Environment.NewLine, output);
    }

    static List<OcrRect> BuildSegments(IReadOnlyList<OcrLineData> cells, OcrRect bounds, bool horizontal)
    {
        var occupied = cells
            .Select(c => horizontal ? (Start: c.Bounds.Y, End: c.Bounds.Bottom) : (Start: c.Bounds.X, End: c.Bounds.Right))
            .Where(s => s.End > s.Start)
            .Select(s =>
            {
                double inset = s.End - s.Start > MinimumSizeForTolerance ? Tolerance / 2 : 0;
                return (Start: s.Start + inset, End: s.End - inset);
            })
            .OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        if (occupied.Count == 0) return [bounds];
        var segments = new List<OcrRect>();
        double start = occupied[0].Start, end = occupied[0].End;
        foreach (var current in occupied.Skip(1))
        {
            if (current.Start >= end) { segments.Add(Segment(bounds, horizontal, start, end)); start = current.Start; end = current.End; }
            else end = Math.Max(end, current.End);
        }
        segments.Add(Segment(bounds, horizontal, start, end));
        return segments;
    }

    static OcrRect Segment(OcrRect b, bool horizontal, double start, double end) =>
        horizontal ? new(b.X, start, b.Width, end - start) : new(start, b.Y, end - start, b.Height);

    static int BestIntersection(IReadOnlyList<OcrRect> segments, OcrRect cell)
    {
        int best = 0; double bestArea = double.MinValue;
        for (int i = 0; i < segments.Count; i++)
        {
            double area = segments[i].IntersectionArea(cell);
            if (area > bestArea) { bestArea = area; best = i; }
        }
        return best;
    }
}

internal static class TextExtractor
{
    const double DefaultScale = 1.0, EnhancedScale = 1.5;

    internal static IReadOnlyList<(string Tag, string Name)> Languages() =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => (l.LanguageTag, l.DisplayName)).ToList();

    // null when usable; an empty tag means the user-profile languages
    internal static string Unavailable(string languageTag)
    {
        if (OcrEngine.AvailableRecognizerLanguages.Count == 0)
            return "Windows OCR has no recognizer languages. Add a language with Optical character recognition in Settings > Time & language > Language & region.";
        if (string.IsNullOrWhiteSpace(languageTag))
            return OcrEngine.TryCreateFromUserProfileLanguages() == null ? "None of the Windows display languages has an OCR language pack installed." : null;
        if (!WinLanguage.IsWellFormed(languageTag)) return $"'{languageTag}' is not a valid language tag.";
        if (!OcrEngine.IsLanguageSupported(new WinLanguage(languageTag)))
            return $"The OCR language pack for '{languageTag}' is not installed. Add it with Optical character recognition in Settings > Time & language > Language & region.";
        return null;
    }

    // Maps a selection made in DIPs on a view rendered at the given scale to image pixels, clamped to the image
    internal static Int32Rect RegionToPixels(Rect dipRegion, double scale, int pixelWidth, int pixelHeight)
    {
        var s = SelectionGeometry.ToPixels(new OcrPoint(dipRegion.Left, dipRegion.Top), new OcrPoint(dipRegion.Right, dipRegion.Bottom),
            scale, new DisplayBounds(0, 0, pixelWidth, pixelHeight));
        return new Int32Rect(s.Local.X, s.Local.Y, s.Local.Width, s.Local.Height);
    }

    internal static Task<string> RecognizeAsync(string imagePath, Int32Rect? region, string languageTag, CancellationToken ct,
        OcrCaptureMode mode = OcrCaptureMode.Region, OcrPoint? clickPoint = null)
    {
        Wpf.BitmapSource image;
        using (var file = File.OpenRead(imagePath))
        {
            // OnLoad decodes now so the file is released and the frozen frame can cross threads
            var frame = Wpf.BitmapFrame.Create(file, Wpf.BitmapCreateOptions.PreservePixelFormat, Wpf.BitmapCacheOption.OnLoad);
            frame.Freeze();
            image = frame;
        }
        return RecognizeAsync(image, region, languageTag, ct, mode, clickPoint);
    }

    // region and clickPoint are in the image's own pixels (clickPoint relative to the region); WPF work is done before the
    // first await, so an unfrozen source is only touched on the caller's thread
    internal static Task<string> RecognizeAsync(Wpf.BitmapSource image, Int32Rect? region, string languageTag, CancellationToken ct,
        OcrCaptureMode mode = OcrCaptureMode.Region, OcrPoint? clickPoint = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ct.ThrowIfCancellationRequested();
        var language = ResolveLanguage(languageTag);
        Wpf.BitmapSource source = image;
        if (region.HasValue)
        {
            var r = region.Value;
            var s = SelectionGeometry.ToPixels(new OcrPoint(r.X, r.Y), new OcrPoint(r.X + r.Width, r.Y + r.Height), 1,
                new DisplayBounds(0, 0, image.PixelWidth, image.PixelHeight));
            if (s.Local.Width == 0 || s.Local.Height == 0) return Task.FromResult(string.Empty);
            source = new Wpf.CroppedBitmap(image, new Int32Rect(s.Local.X, s.Local.Y, s.Local.Width, s.Local.Height));
        }
        var bitmap = ToGdi(source);
        return Extract(bitmap, language, mode, clickPoint, ct);
    }

    static WinLanguage ResolveLanguage(string tag)
    {
        string reason = Unavailable(tag);
        if (reason != null) throw new InvalidOperationException(reason);
        return string.IsNullOrWhiteSpace(tag) ? OcrEngine.TryCreateFromUserProfileLanguages().RecognizerLanguage : new WinLanguage(tag);
    }

    static Gdi.Bitmap ToGdi(Wpf.BitmapSource source)
    {
        var encoder = new Wpf.PngBitmapEncoder();
        encoder.Frames.Add(Wpf.BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        // GDI+ keeps reading the stream it was loaded from; copy so the stream can go
        using var loaded = new Gdi.Bitmap(stream);
        return new Gdi.Bitmap(loaded);
    }

    // ---- TextExtractorService.ExtractAsync ----
    static async Task<string> Extract(Gdi.Bitmap bitmap, WinLanguage language, OcrCaptureMode mode, OcrPoint? click, CancellationToken ct)
    {
        using (bitmap)
        {
            double scale = SelectScale(bitmap, mode);
            using var prepared = BitmapPreprocessor.Prepare(bitmap, scale);
            if (!FitsOcrLimit(prepared.Bitmap.Size)) throw new InvalidOperationException("The prepared bitmap exceeds the OCR engine's maximum dimensions.");
            var document = await WindowsOcrRecognizer.RecognizeAsync(prepared.Bitmap, language, ct);
            string tag = language.LanguageTag;
            return mode switch
            {
                OcrCaptureMode.Region => OcrTextFormatter.FormatDocument(document, tag),
                OcrCaptureMode.SingleLine => OcrTextFormatter.FormatSingleLine(document, tag),
                OcrCaptureMode.Table => TableTextFormatter.Format(document.Lines, tag),
                OcrCaptureMode.Word => ClickedWord(document, TransformPoint(click, prepared)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), "Unknown OcrCaptureMode value."),
            };
        }
    }

    static OcrPoint TransformPoint(OcrPoint? click, PreparedBitmap prepared)
    {
        if (click is not OcrPoint p) throw new InvalidOperationException("A click point is required for Word extraction mode.");
        return new OcrPoint(p.X * prepared.ScaleX + prepared.OffsetX, p.Y * prepared.ScaleY + prepared.OffsetY);
    }

    // Upscale 1.5x when it fits (better recognition), except Word mode; otherwise 1x, otherwise shrink to the limit
    static double SelectScale(Gdi.Bitmap bitmap, OcrCaptureMode mode)
    {
        if (mode != OcrCaptureMode.Word && bitmap.Width * EnhancedScale <= OcrEngine.MaxImageDimension && bitmap.Height * EnhancedScale <= OcrEngine.MaxImageDimension
            && FitsOcrLimit(BitmapPreprocessor.GetOutputSize(bitmap, EnhancedScale)))
            return EnhancedScale;
        if (FitsOcrLimit(BitmapPreprocessor.GetOutputSize(bitmap, DefaultScale))) return DefaultScale;
        double max = OcrEngine.MaxImageDimension;
        double scale = Math.Min(DefaultScale, Math.Min(max / bitmap.Width, max / bitmap.Height));
        if (!FitsOcrLimit(BitmapPreprocessor.GetOutputSize(bitmap, scale)))
            throw new InvalidOperationException("The bitmap could not be scaled within the OCR engine's maximum dimensions.");
        return scale;
    }

    static bool FitsOcrLimit(Gdi.Size size) => size.Width <= OcrEngine.MaxImageDimension && size.Height <= OcrEngine.MaxImageDimension;

    static string ClickedWord(OcrDocument document, OcrPoint point) =>
        document.Words.FirstOrDefault(w => w.Bounds.Contains(point))?.Text ?? string.Empty;

#if DEBUG
    internal static async Task Checks(string scratch, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        check(Unavailable("xx-ZZ") != null, "Text Extractor reports an unknown OCR language as unavailable");
        check(Unavailable("not a tag!") != null, "Text Extractor reports a malformed language tag as unavailable");
        bool threw = false;
        try { await RecognizeAsync(Render(), null, "xx-ZZ", CancellationToken.None); } catch (InvalidOperationException) { threw = true; }
        check(threw, "Text Extractor refuses to recognise with an unavailable language instead of installing it");

        check(RegionToPixels(new Rect(10, 10, 50, 20), 2, 1000, 1000) == new Int32Rect(20, 20, 100, 40), "Text Extractor scales a DIP region to pixels at 2x");
        check(RegionToPixels(new Rect(-5, 90, 50, 50), 2, 100, 200) == new Int32Rect(0, 180, 90, 20), "Text Extractor clamps a scaled region to the image");

        var words = new List<OcrWordData> { new("你", default), new("好", default), new("。", default), new("PC", default) };
        check(OcrTextFormatter.JoinCjkAwareWords(words) == "你好。PC", "Text Extractor joins CJK words without spaces as the donor");
        var cells = new List<OcrLineData>
        {
            new("a", new OcrRect(0, 0, 40, 20), []), new("b", new OcrRect(100, 0, 40, 20), []),
            new("c", new OcrRect(0, 50, 40, 20), []), new("d", new OcrRect(100, 50, 40, 20), []),
        };
        check(TableTextFormatter.Format(cells, "en-US") == $"a\tb{Environment.NewLine}c\td", "Text Extractor table mode lays cells out in tab-separated rows");

        string tag = Languages().Select(l => l.Tag).FirstOrDefault(t => t.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (tag == null) { check(true, "Text Extractor recognition checks skipped: no English OCR language on this machine"); return; }

        var image = Render();
        string all = await RecognizeAsync(image, null, tag, CancellationToken.None);
        check(all.Contains("ALPHA", StringComparison.OrdinalIgnoreCase) && all.Contains("OMEGA", StringComparison.OrdinalIgnoreCase),
            "Text Extractor recognises WPF-rendered text");
        string right = await RecognizeAsync(image, new Int32Rect(400, 0, 400, 160), tag, CancellationToken.None);
        check(right.Contains("OMEGA", StringComparison.OrdinalIgnoreCase) && !right.Contains("ALPHA", StringComparison.OrdinalIgnoreCase),
            "Text Extractor region crop recognises only the selected part");

        string png = Path.Combine(scratch, "ocr.png");
        using (var file = File.Create(png)) { var e = new Wpf.PngBitmapEncoder(); e.Frames.Add(Wpf.BitmapFrame.Create(image)); e.Save(file); }
        check((await RecognizeAsync(png, null, tag, CancellationToken.None)).Contains("ALPHA", StringComparison.OrdinalIgnoreCase), "Text Extractor recognises an image file");

        bool cancelled = false;
        try { await RecognizeAsync(image, null, tag, new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Text Extractor honours cancellation");
    }

    // RenderTargetBitmap needs an STA thread; the frozen result is usable anywhere
    static Wpf.BitmapSource Render()
    {
        Wpf.BitmapSource result = null; Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 800, 160));
                    var face = new Typeface("Arial");
                    dc.DrawText(new FormattedText("ALPHA", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 48, Brushes.Black, 1.0), new Point(30, 50));
                    dc.DrawText(new FormattedText("OMEGA", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 48, Brushes.Black, 1.0), new Point(480, 50));
                }
                var bitmap = new Wpf.RenderTargetBitmap(800, 160, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                result = bitmap;
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result;
    }
#endif
}
