using System.Globalization;
using System.IO;

namespace FileExplorer;

/// ARCHIVE-12: the settings chosen in Compress… for a new archive. Archive.Create without them writes the
/// one-click defaults (Normal level, the format's own method, no password, one file).
internal sealed record ArchiveOptions
{
    public string Format { get; init; } = "7z";   // 7z, zip, rar, tar
    public int Level { get; init; } = 3;          // 0 Store, 1 Fastest, 2 Fast, 3 Normal, 4 Maximum, 5 Ultra
    public string Method { get; init; } = "";     // "" = the format's default
    public long Dictionary { get; init; }         // bytes (PPMd: model memory); 0 = by level
    public int Word { get; init; }                // LZMA/Deflate fast bytes, PPMd order; 0 = by level
    public string Solid { get; init; } = "";      // 7z: "" by level, "off", "on" or a block size ("64m"); RAR: "on" or ""
    public int Threads { get; init; } = 2;        // 0 = every logical processor
    public long VolumeSize { get; init; }         // bytes; 0 = one file
    public string Password { get; init; }         // kept only for the job, never saved
    public bool ZipCrypto { get; init; }          // ZIP: the legacy cipher instead of AES-256
    public bool EncryptNames { get; init; }       // 7z and RAR: the file list is encrypted too
    public int Recovery { get; init; }            // RAR recovery record, percent
    public bool DeleteAfter { get; init; }        // the sources go to the Recycle Bin after a verified creation; never saved

    public string Extension => "." + Format;
    public bool Encrypted => !string.IsNullOrEmpty(Password);
    public bool Rar => Format == "rar";

    public static readonly string[] Formats = { "7z", "zip", "rar", "tar" };
    public static readonly string[] LevelNames = { "Store", "Fastest", "Fast", "Normal", "Maximum", "Ultra" };
    public static string[] Methods(string format) => format switch
    {
        "7z" => new[] { "LZMA2", "LZMA", "PPMd", "BZip2" },
        "zip" => new[] { "Deflate", "Deflate64", "BZip2", "LZMA", "PPMd" },
        _ => Array.Empty<string>()
    };
    public static string DefaultMethod(string format) => Methods(format) is { Length: > 0 } methods ? methods[0] : "";
    const long K = 1024, M = K * K;
    /// Dictionary sizes the method takes (PPMd: its model memory); none for Deflate's fixed 32/64 KB window.
    public static long[] Dictionaries(string format, string method) => method switch
    {
        "LZMA2" or "LZMA" => new[] { 64 * K, 1 * M, 2 * M, 4 * M, 8 * M, 16 * M, 32 * M, 64 * M, 128 * M, 256 * M, 512 * M },
        "PPMd" when format == "zip" => new[] { 1 * M, 2 * M, 4 * M, 8 * M, 16 * M, 32 * M, 64 * M, 128 * M, 256 * M },
        "PPMd" => new[] { 1 * M, 2 * M, 4 * M, 8 * M, 16 * M, 32 * M, 64 * M, 128 * M, 256 * M, 512 * M },
        "BZip2" => Enumerable.Range(1, 9).Select(n => n * 100 * K).ToArray(),
        _ => Array.Empty<long>()
    };
    /// Word sizes: LZMA and Deflate fast bytes, PPMd model order.
    public static int[] Words(string format, string method) => method switch
    {
        "LZMA2" or "LZMA" => new[] { 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 273 },
        "PPMd" when format == "zip" => new[] { 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16 },
        "PPMd" => new[] { 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32 },
        "Deflate" or "Deflate64" => new[] { 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 257 },
        _ => Array.Empty<int>()
    };
    public static readonly string[] SolidBlocks = { "off", "16m", "64m", "256m", "1g", "4g", "on" };
    public static readonly long[] VolumePresets = { 10 * M, 100 * M, 650 * M, 700 * M, 4092 * M, 4480 * M, 8128 * M };
    public static readonly int[] RecoveryPercents = { 1, 3, 5, 10 };

    public static string Size(long bytes) => bytes % M == 0 ? bytes / M + " MB" : bytes % K == 0 ? bytes / K + " KB" : bytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes";
    public static string SolidName(string solid) => solid switch
    {
        "off" => "Non-solid", "on" => "Solid",
        _ => (solid.EndsWith('g') ? solid[..^1] + " GB" : solid[..^1] + " MB") + " blocks"
    };

    /// A typed volume size: bytes, or a number with K, M or G (binary, as 7-Zip reads them). Null when unreadable.
    public static long? ParseSize(string text)
    {
        text = text.Trim().Replace(" ", "").ToUpperInvariant();
        if (text.EndsWith('B') && text.Length > 1 && !char.IsDigit(text[^2])) text = text[..^1];
        long unit = text.EndsWith('K') ? K : text.EndsWith('M') ? M : text.EndsWith('G') ? K * M : 1;
        if (unit > 1) text = text[..^1];
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            && !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value)) return null;
        var bytes = value * unit;
        return bytes is >= 64 * K and <= long.MaxValue / 2 ? (long)bytes : null;
    }

    /// Saved in settings as key:value pairs; the password and Delete after are never written.
    public string Serialize() => string.Join(";", new[]
    {
        "format:" + Format, "level:" + Level, "method:" + Method, "dict:" + Dictionary, "word:" + Word, "solid:" + Solid,
        "mt:" + Threads, "volume:" + VolumeSize, "zipcrypto:" + (ZipCrypto ? 1 : 0), "names:" + (EncryptNames ? 1 : 0), "recovery:" + Recovery
    });
    public static ArchiveOptions Parse(string text)
    {
        var options = new ArchiveOptions();
        foreach (var pair in (text ?? "").Split(';').Select(p => p.Split(':', 2)).Where(p => p.Length == 2))
        {
            string v = pair[1];
            long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out long n);
            options = pair[0] switch
            {
                "format" when Formats.Contains(v) => options with { Format = v },
                "level" when n is >= 0 and <= 5 => options with { Level = (int)n },
                "method" => options with { Method = v },
                "dict" => options with { Dictionary = n },
                "word" => options with { Word = (int)Math.Min(n, 273) },
                "solid" when v == "" || SolidBlocks.Contains(v) => options with { Solid = v },
                "mt" when n <= Environment.ProcessorCount => options with { Threads = (int)n },
                "volume" => options with { VolumeSize = n >= 64 * K ? n : 0 },
                "zipcrypto" => options with { ZipCrypto = v == "1" },
                "names" => options with { EncryptNames = v == "1" },
                "recovery" when RecoveryPercents.Contains((int)n) || n == 0 => options with { Recovery = (int)n },
                _ => options
            };
        }
        return options.Normalized();
    }
    /// What the format can take: a method, dictionary, word size or block size it doesn't have falls back to its default.
    public ArchiveOptions Normalized()
    {
        var o = this;
        if (!Methods(o.Format).Contains(o.Method)) o = o with { Method = DefaultMethod(o.Format) };
        if (!Dictionaries(o.Format, o.Method).Contains(o.Dictionary)) o = o with { Dictionary = 0 };
        if (!Words(o.Format, o.Method).Contains(o.Word)) o = o with { Word = 0 };
        if (o.Format == "tar") o = o with { Password = null };
        if (o.Format != "7z" && !(o.Rar && o.Solid is "on" or "")) o = o with { Solid = "" };
        if (o.Format != "zip") o = o with { ZipCrypto = false };
        if (o.Format is not ("7z" or "rar")) o = o with { EncryptNames = false };
        if (!o.Rar) o = o with { Recovery = 0 };
        return o;
    }
    /// The archive's name for a requested path: the format's extension added when it's missing.
    public string PathFor(string path) => Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
}
