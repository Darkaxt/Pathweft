using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace FileExplorer;

// ARCHIVE-13: the hashes NanaZip's CRC SHA menu offers that people actually compare against published values, written
// as 7-Zip writes them: digests of 8 bytes or less (CRC-32, CRC-64, XXH64, XXH3-64) as the number in upper-case hex,
// longer ones byte by byte in lower case. CRC-64 is 7-Zip's (CRC-64/XZ), not System.IO.Hashing's ECMA-182 form.
internal static class Checksums
{
    internal static readonly string[] Names = { "CRC-32", "CRC-64", "MD5", "SHA-1", "SHA-256", "SHA-384", "SHA-512", "SHA3-256", "XXH64", "XXH3-64" };

    /// Why an algorithm can't run here, or null. SHA3 needs Windows' CNG support (Windows 11 24H2 and later).
    internal static string Unavailable(string name) => name == "SHA3-256" && !SHA3_256.IsSupported ? "Windows here has no SHA3-256" : null;

    interface IHasher { void Append(ReadOnlySpan<byte> data); string Value(); }
    sealed class Incremental(HashAlgorithmName algorithm) : IHasher
    {
        readonly IncrementalHash hash = IncrementalHash.CreateHash(algorithm);
        public void Append(ReadOnlySpan<byte> data) => hash.AppendData(data);
        public string Value() { var bytes = hash.GetHashAndReset(); hash.Dispose(); return Convert.ToHexString(bytes).ToLowerInvariant(); }
    }
    sealed class Number(NonCryptographicHashAlgorithm hash, Func<NonCryptographicHashAlgorithm, ulong> value, int digits) : IHasher
    {
        public void Append(ReadOnlySpan<byte> data) => hash.Append(data);
        public string Value() => value(hash).ToString("X" + digits);
    }
    sealed class Crc64Xz : IHasher
    {
        static readonly ulong[] table = Table();
        ulong crc = ulong.MaxValue;
        static ulong[] Table()
        {
            var t = new ulong[256];
            for (uint i = 0; i < 256; i++)
            {
                ulong r = i;
                for (int bit = 0; bit < 8; bit++) r = (r & 1) != 0 ? (r >> 1) ^ 0xC96C5795D7870F42UL : r >> 1;
                t[i] = r;
            }
            return t;
        }
        public void Append(ReadOnlySpan<byte> data) { foreach (byte b in data) crc = table[(byte)(crc ^ b)] ^ (crc >> 8); }
        public string Value() => (crc ^ ulong.MaxValue).ToString("X16");
    }

    static IHasher Create(string name) => name switch
    {
        "CRC-32" => new Number(new Crc32(), h => ((Crc32)h).GetCurrentHashAsUInt32(), 8),
        "CRC-64" => new Crc64Xz(),
        "MD5" => new Incremental(HashAlgorithmName.MD5),
        "SHA-1" => new Incremental(HashAlgorithmName.SHA1),
        "SHA-256" => new Incremental(HashAlgorithmName.SHA256),
        "SHA-384" => new Incremental(HashAlgorithmName.SHA384),
        "SHA-512" => new Incremental(HashAlgorithmName.SHA512),
        "SHA3-256" => new Incremental(HashAlgorithmName.SHA3_256),
        "XXH64" => new Number(new XxHash64(), h => ((XxHash64)h).GetCurrentHashAsUInt64(), 16),
        "XXH3-64" => new Number(new XxHash3(), h => ((XxHash3)h).GetCurrentHashAsUInt64(), 16),
        _ => throw new ArgumentException("Unknown checksum " + name),
    };

    /// Every algorithm asked for at once, fed by what is written to it (an archive entry extracted into it, say).
    internal sealed class Sink : Stream
    {
        readonly IReadOnlyCollection<string> names;
        readonly List<string> usable;
        readonly IHasher[] hashers;
        readonly CancellationToken ct;
        readonly Action<long> progress;
        long done;
        internal Sink(IReadOnlyCollection<string> names, CancellationToken ct, Action<long> progress = null)
        {
            this.names = names; this.ct = ct; this.progress = progress;
            usable = names.Where(n => Unavailable(n) == null).ToList();
            hashers = usable.Select(Create).ToArray();
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> data)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var h in hashers) h.Append(data);
            done += data.Length; progress?.Invoke(done);
        }
        /// The values, null for an algorithm this Windows lacks (Unavailable says why).
        internal Dictionary<string, string> Values()
        {
            var result = new Dictionary<string, string>();
            for (int i = 0; i < usable.Count; i++) result[usable[i]] = hashers[i].Value();
            foreach (var n in names) result.TryAdd(n, null);
            return result;
        }
        public override void Flush() { }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => done; public override long Position { get => done; set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }

    /// Every algorithm asked for in one read of the stream. `progress`: bytes read so far.
    internal static Dictionary<string, string> Compute(Stream input, IReadOnlyCollection<string> names, CancellationToken ct, Action<long> progress = null)
    {
        var sink = new Sink(names, ct, progress);
        input.CopyTo(sink, 1 << 20);
        return sink.Values();
    }

    internal static Dictionary<string, string> OfFile(string path, IReadOnlyCollection<string> names, CancellationToken ct, Action<long> progress = null)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        return Compute(s, names, ct, progress);
    }

    /// A hash copied from a download page: the algorithm whose value it is, or null. Only hex of a digest's length counts.
    internal static string Clipboard(string clip) =>
        clip is { Length: 8 or 16 or 32 or 40 or 64 or 96 or 128 } && clip.All(Uri.IsHexDigit) ? clip : null;

#if DEBUG
    internal static void Checks(Action<bool, string> check)
    {
        // the standard check input "123456789": values NanaZipC 7.0.1882 prints for it (h -scrc<name>)
        var all = Compute(new MemoryStream("123456789"u8.ToArray()), Names, CancellationToken.None);
        check(all["CRC-32"] == "CBF43926", "CRC-32 of the check string is 7-Zip's CBF43926");
        check(all["CRC-64"] == "995DC9BBDF1939FA", "CRC-64 is 7-Zip's CRC-64/XZ (995DC9BBDF1939FA), not ECMA-182");
        check(all["XXH64"] == "8CB841DB40E6AE83", "XXH64 is written as the 64-bit number, as 7-Zip does");
        check(all["XXH3-64"] == "72DCB18B67A17DFF", "XXH3-64 matches NanaZip's XXH3_64bits");
        check(all["MD5"] == "25f9e794323b453885f5181f1b624d0b", "MD5 is written byte by byte in lower case");
        check(all["SHA-256"] == "15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225", "SHA-256 matches");
        check(Unavailable("SHA3-256") != null || all["SHA3-256"] == "87cd084d190e436f147322b90e7384f6a8e0676c99d21ef519ea718e51d45f9c", "SHA3-256 matches NanaZip where Windows has it");
        check(Clipboard("CBF43926") != null && Clipboard("not a hash") == null && Clipboard(new string('a', 63)) == null, "Only a digest-length hex value on the clipboard is compared");
    }
#endif
}
