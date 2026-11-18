using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace FileExplorer;

// Image Resizer engine (TOOLS-03), ported from PowerToys v0.103.3151 (2d817b0a, MIT; code forked from
// Brice Lambson's ImageResizer): imageresizer/ui/Models/{ResizeOperation,ResizeBatch,ResizeSize,CustomSize,
// ResizeFit,ResizeUnit,ImagingEnums,ResizeError}.cs, ui/Utilities/CodecHelper.cs, ui/Properties/Settings.cs defaults.
// Same WinRT Windows.Graphics.Imaging pipeline. Dropped: AI super-resolution, PowerToys settings/JSON, ResourceLoader,
// telemetry, the parallel batch, and the donor's own destination naming/uniquifier, Replace and recycle-bin backup:
// the engine writes only to the path the caller gives, the caller owns conflicts, replacement and Undo.

internal enum ResizeFit { Fill, Fit, Stretch }
internal enum ResizeUnit { Centimeter, Inch, Percent, Pixel }
internal enum PngInterlaceOption { Default = 0, On = 1, Off = 2 }
internal enum TiffCompressOption { Default = 0, None = 1, Ccitt3 = 2, Ccitt4 = 3, Lzw = 4, Rle = 5, Zip = 6 }

internal sealed record ResizeSize(string Name, ResizeFit Fit, double Width, double Height, ResizeUnit Unit)
{
    // 0 or NaN means "auto" for that side
    internal bool HasAuto => Width == 0 || Height == 0 || double.IsNaN(Width) || double.IsNaN(Height);

    internal double PixelWidth(int original, double dpi) => ToPixels(Width, original, dpi);

    // A percent Fit/Fill size uses Width for both sides (the donor hides the height box then)
    internal double PixelHeight(int original, double dpi) =>
        ToPixels(Fit != ResizeFit.Stretch && Unit == ResizeUnit.Percent ? Width : Height, original, dpi);

    double ToPixels(double value, int original, double dpi)
    {
        if (value == 0 || double.IsNaN(value)) return Fit == ResizeFit.Fit ? double.PositiveInfinity : original;
        return Unit switch
        {
            ResizeUnit.Inch => value * dpi,
            ResizeUnit.Centimeter => value * dpi / 2.54,
            ResizeUnit.Percent => value / 100 * original,
            _ => value,
        };
    }
}

// FallbackEncoder is the donor's legacy container-format GUID (as in its settings) or a WinRT encoder id.
internal sealed record ResizeOptions(ResizeSize Size, bool ShrinkOnly, bool IgnoreOrientation, bool RemoveMetadata, int JpegQuality,
    PngInterlaceOption PngInterlace, TiffCompressOption TiffCompress, Guid FallbackEncoder, string FileNameFormat, bool KeepDateModified);

internal static class ImageResizer
{
    // Settings.cs defaults; "$small$" etc. resolve to these English names in the donor's resources
    internal static IReadOnlyList<ResizeSize> Presets { get; } =
    [
        new("Small", ResizeFit.Fit, 854, 480, ResizeUnit.Pixel),
        new("Medium", ResizeFit.Fit, 1366, 768, ResizeUnit.Pixel),
        new("Large", ResizeFit.Fit, 1920, 1080, ResizeUnit.Pixel),
        new("Phone", ResizeFit.Fit, 320, 568, ResizeUnit.Pixel),
    ];

    internal static ResizeOptions Defaults { get; } = new(Presets[0], ShrinkOnly: false, IgnoreOrientation: true, RemoveMetadata: false,
        JpegQuality: 90, PngInterlaceOption.Default, TiffCompressOption.Default, new Guid("19e4a5aa-5662-4fc5-a0c0-1758028e1057"), "%1 (%2)", KeepDateModified: false);

    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".bmp", ".dib", ".gif", ".jfif", ".jpe", ".jpeg", ".jpg", ".jxr", ".png", ".rle", ".tif", ".tiff", ".wdp" };

    internal static bool CanResize(string path) => !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    // ---- CodecHelper ----
    static readonly Dictionary<Guid, Guid> LegacyToEncoder = new()
    {
        [new Guid("19e4a5aa-5662-4fc5-a0c0-1758028e1057")] = BitmapEncoder.JpegEncoderId,
        [new Guid("1b7cfaf4-713f-473c-bbcd-6137425faeaf")] = BitmapEncoder.PngEncoderId,
        [new Guid("0af1d87e-fcfe-4188-bdeb-a7906471cbe3")] = BitmapEncoder.BmpEncoderId,
        [new Guid("163bcc30-e2e9-4f0b-961d-a3e9fdb788a3")] = BitmapEncoder.TiffEncoderId,
        [new Guid("57a37caa-367a-4540-916b-f183c5093a4b")] = BitmapEncoder.JpegXREncoderId,
        [new Guid("1f8a5601-7d4d-4cbd-9c82-1bc8d4eeb9a5")] = BitmapEncoder.GifEncoderId,
    };
    static readonly Dictionary<Guid, Guid> DecoderToEncoder = new()
    {
        [BitmapDecoder.JpegDecoderId] = BitmapEncoder.JpegEncoderId,
        [BitmapDecoder.PngDecoderId] = BitmapEncoder.PngEncoderId,
        [BitmapDecoder.BmpDecoderId] = BitmapEncoder.BmpEncoderId,
        [BitmapDecoder.TiffDecoderId] = BitmapEncoder.TiffEncoderId,
        [BitmapDecoder.GifDecoderId] = BitmapEncoder.GifEncoderId,
        [BitmapDecoder.JpegXRDecoderId] = BitmapEncoder.JpegXREncoderId,
    };
    static readonly Dictionary<Guid, string[]> EncoderExtensions = new()
    {
        [BitmapEncoder.JpegEncoderId] = [".jpg", ".jpeg", ".jpe", ".jfif"],
        [BitmapEncoder.PngEncoderId] = [".png"],
        [BitmapEncoder.BmpEncoderId] = [".bmp", ".dib", ".rle"],
        [BitmapEncoder.TiffEncoderId] = [".tiff", ".tif"],
        [BitmapEncoder.GifEncoderId] = [".gif"],
        [BitmapEncoder.JpegXREncoderId] = [".jxr", ".wdp"],
    };

    // A WinRT encoder id passes through; an unknown GUID falls back to PNG as in the donor
    static Guid FallbackEncoderId(Guid g) =>
        EncoderExtensions.ContainsKey(g) ? g : LegacyToEncoder.TryGetValue(g, out var id) ? id : BitmapEncoder.PngEncoderId;

    static Guid? EncoderFor(BitmapDecoder decoder)
    {
        Guid codec = decoder.DecoderInformation?.CodecId ?? Guid.Empty;
        return DecoderToEncoder.TryGetValue(codec, out var id) ? id : null;
    }

    static Guid ChooseEncoder(BitmapDecoder decoder, ResizeOptions o)
    {
        var id = EncoderFor(decoder);
        return id.HasValue && EncoderExtensions.ContainsKey(id.Value) ? id.Value : FallbackEncoderId(o.FallbackEncoder);
    }

    // ---- naming (ResizeOperation.GetDestinationPath without directory and uniquifier) ----
    static readonly string[] AvoidNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    // Settings.FileNameFormat: %1 name, %2 size name, %3/%4 selected width/height, %5/%6 output pixel width/height
    static string ToCompositeFormat(string format) => (format ?? "%1 (%2)")
        .Replace("{", "{{", StringComparison.OrdinalIgnoreCase).Replace("}", "}}", StringComparison.OrdinalIgnoreCase)
        .Replace("%1", "{0}", StringComparison.OrdinalIgnoreCase).Replace("%2", "{1}", StringComparison.OrdinalIgnoreCase)
        .Replace("%3", "{2}", StringComparison.OrdinalIgnoreCase).Replace("%4", "{3}", StringComparison.OrdinalIgnoreCase)
        .Replace("%5", "{4}", StringComparison.OrdinalIgnoreCase).Replace("%6", "{5}", StringComparison.OrdinalIgnoreCase);

    internal static string FileName(string source, ResizeOptions o, Guid encoderId, int outWidth, int outHeight)
    {
        string ext = Path.GetExtension(source);
        var supported = EncoderExtensions.TryGetValue(encoderId, out var list) ? list : Array.Empty<string>();
        if (!supported.Contains(ext, StringComparer.OrdinalIgnoreCase)) ext = supported.FirstOrDefault() ?? ".png";
        string size = (o.Size.Name ?? "").Replace('\\', '_').Replace('/', '_');
        string name = string.Format(CultureInfo.CurrentCulture, ToCompositeFormat(o.FileNameFormat),
            Path.GetFileNameWithoutExtension(source), size, o.Size.Width, o.Size.Height, outWidth, outHeight);
        name = name.Replace(':', '_').Replace('*', '_').Replace('?', '_').Replace('"', '_').Replace('<', '_').Replace('>', '_').Replace('|', '_');
        if (AvoidNames.Contains(name.ToUpperInvariant())) name += "_";
        return name + ext;
    }

    // Needs the output size for %5/%6 and the encoder for the extension, so it reads the image header.
    // Runs on the thread pool so a UI-thread caller does not block on its own context.
    internal static string OutputFileName(string source, ResizeOptions o) => Task.Run(async () =>
    {
        using var file = File.OpenRead(source);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
        var (w, h, _, _) = OutputSize(decoder, o);
        return FileName(source, o, ChooseEncoder(decoder, o), w, h);
    }).GetAwaiter().GetResult();

    static (int W, int H, Plan P, Guid E) OutputSize(BitmapDecoder decoder, ResizeOptions o)
    {
        int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
        var p = Calculate(o, w, h, decoder.DpiX, decoder.DpiY);
        var (ow, oh) = p.NoTransform ? (w, h) : p.Crop.HasValue ? ((int)p.Crop.Value.Width, (int)p.Crop.Value.Height) : ((int)p.Width, (int)p.Height);
        return (ow, oh, p, Guid.Empty);
    }

    // ---- ResizeOperation.ExecuteAsync ----
    internal static async Task<(int Width, int Height)> ResizeAsync(string source, string destinationPath, ResizeOptions o, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(o);
        ct.ThrowIfCancellationRequested();
        DateTime? lastWrite = o.KeepDateModified ? File.GetLastWriteTimeUtc(source) : null;
        int outW, outH;
        bool created = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var winIn = input.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(winIn).AsTask(ct);
                Guid enc = ChooseEncoder(decoder, o);
                int origW = (int)decoder.PixelWidth, origH = (int)decoder.PixelHeight;
                var (w, h, plan, _) = OutputSize(decoder, o);
                (outW, outH) = (w, h);
                ct.ThrowIfCancellationRequested();

                string dir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // CreateNew: never overwrite, the caller resolves conflicts before calling
                using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                created = true;
                var winOut = output.AsRandomAccessStream();

                // Transcoding cannot accept codec options, and a transform on a transcode encoder only affects its first frame.
                bool forceFresh = (enc == BitmapEncoder.JpegEncoderId && !plan.NoTransform)
                    || (enc == BitmapEncoder.PngEncoderId && o.PngInterlace != PngInterlaceOption.Default)
                    || (enc == BitmapEncoder.TiffEncoderId && (o.TiffCompress != TiffCompressOption.Default || decoder.FrameCount > 1));
                var decoderEnc = EncoderFor(decoder);
                bool transcode = !forceFresh && !o.RemoveMetadata && decoderEnc.HasValue && decoderEnc.Value == enc;

                if (transcode)
                {
                    // Re-encodes pixels via BitmapTransform while preserving all metadata
                    winIn.Seek(0);
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(winOut, decoder).AsTask(ct);
                    if (!plan.NoTransform)
                    {
                        encoder.BitmapTransform.ScaledWidth = plan.Width;
                        encoder.BitmapTransform.ScaledHeight = plan.Height;
                        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                        if (plan.Crop.HasValue) encoder.BitmapTransform.Bounds = plan.Crop.Value;
                    }
                    // Some JPEGs with large/unusual metadata blocks lose EXIF properties during transcode; re-set the known ones
                    var src = GetMetadata(() => decoder.BitmapProperties);
                    if (src != null) await WriteMetadataAsync(encoder, await ReadMetadataAsync(src, KnownMetadata));
                    await encoder.FlushAsync().AsTask(ct);
                }
                else
                {
                    var options = EncoderOptions(enc, o);
                    var encoder = options != null
                        ? await BitmapEncoder.CreateAsync(enc, winOut, options).AsTask(ct)
                        : await BitmapEncoder.CreateAsync(enc, winOut).AsTask(ct);
                    await EncodeFramesAsync(encoder, decoder, enc, o, plan, origW, origH, ct);
                    await encoder.FlushAsync().AsTask(ct);
                }
            }
        }
        catch
        {
            // Difference from the donor: it leaves a partial output behind; we created this file, so remove it
            if (created) try { File.Delete(destinationPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        if (lastWrite.HasValue) File.SetLastWriteTimeUtc(destinationPath, lastWrite.Value);
        return (outW, outH);
    }

    // Decodes each frame, applies the transform and writes pixel data (SetSoftwareBitmap can fail for some decoder outputs)
    static async Task EncodeFramesAsync(BitmapEncoder encoder, BitmapDecoder decoder, Guid enc, ResizeOptions o, Plan plan, int origW, int origH, CancellationToken ct)
    {
        for (uint i = 0; i < decoder.FrameCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                var options = EncoderOptions(enc, o);
                if (options != null) await encoder.GoToNextFrameAsync(options).AsTask(ct);
                else await encoder.GoToNextFrameAsync().AsTask(ct);
            }
            var frame = await decoder.GetFrameAsync(i).AsTask(ct);
            if (enc == BitmapEncoder.TiffEncoderId)
            {
                origW = (int)frame.PixelWidth; origH = (int)frame.PixelHeight;
                plan = Calculate(o, origW, origH, frame.DpiX, frame.DpiY);
            }
            var transform = new BitmapTransform
            {
                ScaledWidth = plan.NoTransform ? (uint)origW : plan.Width,
                ScaledHeight = plan.NoTransform ? (uint)origH : plan.Height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            if (!plan.NoTransform && plan.Crop.HasValue) transform.Bounds = plan.Crop.Value;
            uint outW = plan.Crop?.Width ?? (plan.NoTransform ? (uint)origW : plan.Width);
            uint outH = plan.Crop?.Height ?? (plan.NoTransform ? (uint)origH : plan.Height);
            var format = (enc == BitmapEncoder.PngEncoderId || enc == BitmapEncoder.TiffEncoderId)
                && frame.BitmapPixelFormat is BitmapPixelFormat.Rgba16 or BitmapPixelFormat.Gray16 ? BitmapPixelFormat.Rgba16 : BitmapPixelFormat.Bgra8;
            var alpha = format == BitmapPixelFormat.Bgra8 ? BitmapAlphaMode.Premultiplied : frame.BitmapAlphaMode;
            // TIFF's WinRT encoder cannot retain an embedded ICC profile; encode sRGB samples so colours do not shift
            var color = enc == BitmapEncoder.TiffEncoderId ? ColorManagementMode.ColorManageToSRgb : ColorManagementMode.DoNotColorManage;
            var pixels = await frame.GetPixelDataAsync(format, alpha, transform, ExifOrientationMode.IgnoreExifOrientation, color).AsTask(ct);
            encoder.SetPixelData(format, alpha, outW, outH, frame.DpiX, frame.DpiY, pixels.DetachPixelData());
            // GoToNextFrameAsync commits the current frame, so copy its metadata before advancing
            await CopyFrameMetadataAsync(() => frame.BitmapProperties, encoder, enc, o.RemoveMetadata, copyNative: EncoderFor(decoder) == enc);
        }
    }

    // ---- metadata ----
    // Kept even when stripping: without them the output would render differently
    static readonly string[] RenderingMetadata = ["System.Photo.Orientation", "System.Image.ColorSpace"];
    static readonly string[] KnownMetadata =
        ["System.Author", "System.Title", "System.Photo.DateTaken", "System.Photo.CameraModel", "System.Photo.CameraManufacturer",
         "System.Photo.Orientation", "System.Image.ColorSpace", "System.Comment"];

    static BitmapPropertiesView GetMetadata(Func<BitmapPropertiesView> get)
    {
        try { return get(); }
        catch { return null; } // some codecs (ICO) reject even the accessor
    }

    static async Task<BitmapPropertySet> ReadMetadataAsync(BitmapPropertiesView source, string[] names)
    {
        try
        {
            var props = await source.GetPropertiesAsync(names);
            if (props.Count > 0)
            {
                var result = new BitmapPropertySet();
                foreach (var p in props) result[p.Key] = p.Value;
                return result;
            }
        }
        catch
        {
            // A codec may reject one property in a batch while supporting the rest
            var result = new BitmapPropertySet();
            foreach (var name in names)
            {
                try { var one = await source.GetPropertiesAsync(new[] { name }); if (one.TryGetValue(name, out var v)) result[name] = v; }
                catch { } // e.g. BMP supports no property queries
            }
            return result;
        }
        return null;
    }

    static async Task WriteMetadataAsync(BitmapEncoder encoder, BitmapPropertySet metadata)
    {
        if (metadata == null || metadata.Count == 0) return;
        try { await encoder.BitmapProperties.SetPropertiesAsync(metadata); }
        catch
        {
            // Don't let one unsupported property prevent the others from being saved
            foreach (var p in metadata)
                try { await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet { [p.Key] = p.Value }); } catch { }
        }
    }

    static async Task CopyFrameMetadataAsync(Func<BitmapPropertiesView> get, BitmapEncoder encoder, Guid enc, bool remove, bool copyNative)
    {
        var source = GetMetadata(get);
        if (source == null) return;
        if (!remove && copyNative) await CopyNativeMetadataAsync(source, encoder, enc);
        var metadata = await ReadMetadataAsync(source, remove ? RenderingMetadata : KnownMetadata);
        if (enc == BitmapEncoder.TiffEncoderId)
        {
            metadata ??= new BitmapPropertySet();
            metadata["System.Image.ColorSpace"] = new BitmapTypedValue((ushort)1, PropertyType.UInt16);
        }
        await WriteMetadataAsync(encoder, metadata);
    }

    static async Task CopyNativeMetadataAsync(BitmapPropertiesView source, BitmapEncoder encoder, Guid enc)
    {
        BitmapPropertySet blocks;
        // An empty query enumerates the native blocks; their BitmapTypedValues let WIC copy nested readers
        try { blocks = await source.GetPropertiesAsync(Array.Empty<string>()); }
        catch { return; }
        // Repeated blocks (PNG text chunks) must be appended in index order
        foreach (var block in blocks.OrderBy(b => BlockIndex(b.Key)))
        {
            string name = block.Key.TrimStart('/');
            if (name.StartsWith('[')) name = name[(name.IndexOf(']') + 1)..];
            name = name.ToLowerInvariant();
            // JPEG exposes quantization tables as metadata too; copying them would override the requested quality
            bool keep = enc == BitmapEncoder.JpegEncoderId ? name is "app1" or "app13" or "xmp" or "com" or "iptc" or "irb"
                : enc == BitmapEncoder.TiffEncoderId ? name == "ifd"
                : enc == BitmapEncoder.PngEncoderId && name is "text" or "ztxt" or "itxt" or "xmp" or "exif" or "iccp" or "gama" or "chrm" or "srgb" or "time" or "phys";
            if (!keep) continue;
            try { await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet { [block.Key] = block.Value }); }
            catch { } // keep the Windows-property fallback for unusual sources
        }
    }

    static int BlockIndex(string query)
    {
        string name = query.TrimStart('/');
        int end = name.IndexOf(']');
        return name.StartsWith('[') && end > 1 && int.TryParse(name.AsSpan(1, end - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int i) ? i : 0;
    }

    // ---- encoder options ----
    static BitmapPropertySet EncoderOptions(Guid enc, ResizeOptions o)
    {
        if (enc == BitmapEncoder.JpegEncoderId)
            return new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue(Math.Clamp(o.JpegQuality, 1, 100) / 100f, PropertyType.Single) } };
        // Default lets the WIC encoder decide
        if (enc == BitmapEncoder.PngEncoderId && o.PngInterlace != PngInterlaceOption.Default)
            return new BitmapPropertySet { { "InterlaceOption", new BitmapTypedValue(o.PngInterlace == PngInterlaceOption.On, PropertyType.Boolean) } };
        if (enc == BitmapEncoder.TiffEncoderId && o.TiffCompress != TiffCompressOption.Default)
            return new BitmapPropertySet { { "TiffCompressionMethod", new BitmapTypedValue((byte)o.TiffCompress, PropertyType.UInt8) } }; // enum values match WIC 1..6
        return null;
    }

    // ---- ResizeOperation.CalculateDimensions ----
    readonly record struct Plan(uint Width, uint Height, BitmapBounds? Crop, bool NoTransform);

    static Plan Calculate(ResizeOptions o, int origW, int origH, double dpiX, double dpiY)
    {
        var size = o.Size;
        double width = size.PixelWidth(origW, dpiX), height = size.PixelHeight(origH, dpiY);
        // "Ignore orientation": swap the target box when its orientation disagrees with the image's
        if (o.IgnoreOrientation && !size.HasAuto && size.Unit != ResizeUnit.Percent)
        {
            bool inLand = origW > origH, inPort = origH > origW, tLand = width > height, tPort = height > width;
            if ((inLand && tPort) || (inPort && tLand)) (width, height) = (height, width);
        }
        double sx = width / origW, sy = height / origH;
        if (size.Fit == ResizeFit.Fit) sx = sy = Math.Min(sx, sy);
        else if (size.Fit == ResizeFit.Fill) sx = sy = Math.Max(sx, sy);

        if (o.ShrinkOnly && size.Unit != ResizeUnit.Percent)
        {
            if (sx > 1 || sy > 1) return new((uint)origW, (uint)origH, null, true);
            bool cropNeeded = size.Fit == ResizeFit.Fill && (origW > width || origH > height);
            if (sx == 1 && sy == 1 && !cropNeeded) return new((uint)origW, (uint)origH, null, true);
        }
        uint w = Dimension(origW * sx), h = Dimension(origH * sy);
        if (size.Fit == ResizeFit.Fill)
        {
            uint tw = Dimension(width), th = Dimension(height);
            if (w > tw || h > th) return new(w, h, new BitmapBounds { X = (w - tw) / 2, Y = (h - th) / 2, Width = tw, Height = th }, false);
        }
        return new(w, h, null, false);
    }

    static uint Dimension(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "The calculated image dimension is out of range.");
        return (uint)Math.Max(1, (int)Math.Round(value));
    }

#if DEBUG
    internal static async Task Checks(string scratch, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        string png = Path.Combine(scratch, "resize-src.png"), jpg = Path.Combine(scratch, "resize-src.jpg");
        await WriteTestImage(png, BitmapEncoder.PngEncoderId, 400, 300, null);
        await WriteTestImage(jpg, BitmapEncoder.JpegEncoderId, 400, 300, new BitmapPropertySet
        {
            ["System.Photo.Orientation"] = new BitmapTypedValue((ushort)6, PropertyType.UInt16),
            ["System.Photo.CameraManufacturer"] = new BitmapTypedValue("PathweftTest", PropertyType.String),
        });
        string hashPng = Hash(png), hashJpg = Hash(jpg);
        int n = 0;
        async Task<(int, int)> Run(string src, ResizeOptions o) => await ResizeAsync(src, Path.Combine(scratch, $"out{n++}{Path.GetExtension(src)}"), o, CancellationToken.None);
        ResizeOptions With(ResizeFit fit, double w, double h, ResizeUnit unit = ResizeUnit.Pixel) => Defaults with { Size = new("Test", fit, w, h, unit) };

        check(await Run(png, With(ResizeFit.Fit, 200, 200)) == (200, 150), "Image Resizer Fit keeps the aspect ratio inside the box");
        check(await Run(png, With(ResizeFit.Fill, 200, 200)) == (200, 200), "Image Resizer Fill scales to cover and centre-crops to the box");
        check(await Run(png, With(ResizeFit.Stretch, 100, 100)) == (100, 100), "Image Resizer Stretch ignores the aspect ratio");
        check(await Run(png, With(ResizeFit.Fit, 50, 0, ResizeUnit.Percent)) == (200, 150), "Image Resizer percent Fit uses the width percentage for both sides");
        check(await Run(png, With(ResizeFit.Fit, 2, 2, ResizeUnit.Inch)) == (192, 144), "Image Resizer inch units convert with the image DPI (96)");
        check(await Run(png, With(ResizeFit.Fit, 2.54 * 2, 2.54 * 2, ResizeUnit.Centimeter)) == (192, 144), "Image Resizer centimetre units convert with the image DPI");
        check(await Run(png, With(ResizeFit.Fit, 150, 300)) == (200, 150), "Image Resizer ignore-orientation swaps a portrait box for a landscape image");
        check(await Run(png, With(ResizeFit.Fit, 150, 300) with { IgnoreOrientation = false }) == (150, 112), "Image Resizer without ignore-orientation fits the box as given (112.5 rounds to even)");
        check(await Run(png, With(ResizeFit.Fit, 800, 600) with { ShrinkOnly = true }) == (400, 300), "Image Resizer shrink-only leaves a smaller image at its size");
        check(await Run(png, With(ResizeFit.Fit, 800, 0)) == (800, 600), "Image Resizer an auto (0) side follows the other side");

        // EXIF orientation 6: pixels stay unrotated (IgnoreExifOrientation) and the tag is kept even when stripping metadata
        string kept = Path.Combine(scratch, "orient-keep.jpg"), stripped = Path.Combine(scratch, "orient-strip.jpg");
        var size = await ResizeAsync(jpg, kept, With(ResizeFit.Fit, 200, 200), CancellationToken.None);
        await ResizeAsync(jpg, stripped, With(ResizeFit.Fit, 200, 200) with { RemoveMetadata = true }, CancellationToken.None);
        check(size == (200, 150) && await ReadProperty(kept, "System.Photo.Orientation") is ushort o1 && o1 == 6,
            "Image Resizer keeps EXIF orientation 6 and resizes the stored (unrotated) pixels");
        check(await ReadProperty(kept, "System.Photo.CameraManufacturer") as string == "PathweftTest", "Image Resizer keeps metadata by default");
        check(await ReadProperty(stripped, "System.Photo.CameraManufacturer") == null && await ReadProperty(stripped, "System.Photo.Orientation") is ushort o2 && o2 == 6,
            "Image Resizer remove-metadata strips EXIF but keeps the rendering orientation");

        check(FileName(@"C:\a\photo.jpg", Defaults, BitmapEncoder.JpegEncoderId, 854, 480) == "photo (Small).jpg", "Image Resizer default naming is \"%1 (%2)\"");
        check(FileName(@"C:\a\photo.ico", Defaults with { FileNameFormat = "%1_%3x%4_%5x%6" }, BitmapEncoder.PngEncoderId, 10, 7) == "photo_854x480_10x7.png",
            "Image Resizer naming tokens %3..%6 and the encoder's default extension");
        check(FileName(@"C:\a\x.png", Defaults with { FileNameFormat = "CON", Size = Presets[0] with { Name = "a/b" } }, BitmapEncoder.PngEncoderId, 1, 1) == "CON_.png",
            "Image Resizer naming avoids reserved device names");
        check(OutputFileName(png, Defaults with { FileNameFormat = "%1-%5x%6" }) == "resize-src-640x480.png", "Image Resizer OutputFileName reads the output size");

        string cancelled = Path.Combine(scratch, "cancelled.png");
        bool threw = false;
        try { await ResizeAsync(png, cancelled, Defaults, new CancellationToken(true)); } catch (OperationCanceledException) { threw = true; }
        check(threw && !File.Exists(cancelled), "Image Resizer cancellation throws and leaves no output");

        bool refused = false;
        try { await ResizeAsync(png, kept, Defaults, CancellationToken.None); } catch (IOException) { refused = true; }
        check(refused, "Image Resizer never overwrites an existing destination");

        var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(png, old);
        hashPng = Hash(png);
        string dated = Path.Combine(scratch, "dated.png");
        await ResizeAsync(png, dated, Defaults with { KeepDateModified = true }, CancellationToken.None);
        check(File.GetLastWriteTimeUtc(dated) == old, "Image Resizer keep-date-modified copies the source time");
        check(Hash(png) == hashPng && Hash(jpg) == hashJpg, "Image Resizer leaves the source files unchanged");
        check(CanResize("a.JPG") && CanResize("b.tiff") && !CanResize("c.webp"), "Image Resizer advertises the donor's extensions");
    }

    static async Task WriteTestImage(string path, Guid encoderId, int w, int h, BitmapPropertySet props)
    {
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                pixels[i] = (byte)x; pixels[i + 1] = (byte)y; pixels[i + 2] = (byte)(x ^ y); pixels[i + 3] = 255;
            }
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(encoderId, file.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)w, (uint)h, 96, 96, pixels);
        if (props != null) await encoder.BitmapProperties.SetPropertiesAsync(props);
        await encoder.FlushAsync();
    }

    static async Task<object> ReadProperty(string path, string name)
    {
        using var file = File.OpenRead(path);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
        try { var p = await decoder.BitmapProperties.GetPropertiesAsync(new[] { name }); return p.TryGetValue(name, out var v) ? v.Value : null; }
        catch { return null; }
    }

    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
#endif
}
