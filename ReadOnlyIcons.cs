using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer;

internal static class ReadOnlyIcons
{
    internal static string AttributeReason(bool directory, FileAttributes attributes) =>
        !directory && (attributes & FileAttributes.ReadOnly) != 0 ? "File is read-only." : "";

    internal static string Reason(Entry entry)
    {
        try
        {
            string reason = AttributeReason(entry.IsDir, File.GetAttributes(entry.Path));
            if (reason.Length > 0) return reason;
            return !entry.IsDir && Archive.IsArchiveName(entry.Path) ? Archive.ChangeReason(entry.Path) is { Length: > 0 } why ? "Archive contents are read-only: " + why : "" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; } // unknown access is not a read-only claim
    }

    internal static ImageSource Gray(ImageSource image)
    {
        if (image is not BitmapSource source) return image;
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte gray = (byte)((pixels[i + 2] * 77 + pixels[i + 1] * 150 + pixels[i] * 29 + 128) >> 8);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
        }
        var result = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze(); return result;
    }
}
