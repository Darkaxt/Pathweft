// MOBI/AZW3 reading adapted from calibre (src/calibre/ebooks/mobi, compression/palmdoc.c,
// commit 8f8119ef2d4fb902540022b7a8b0bcb7080c85a7). Copyright (C) Kovid Goyal. GPL-3.0-or-later.
using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FileExplorer.Preview.Ebook;

// EBOOK-01: read-only MOBI6 / KF8 (AZW3) / PRC reader. All KF8 byte work is done on Latin-1 strings
// (one char per byte, so calibre's byte offsets stay valid) and decoded by the book codepage at the end.
internal static class MobiReader
{
    const uint Null = 0xFFFFFFFF;
    const int MaxText = 64 << 20;
    const string DrmNotice = "This book is DRM-protected; only its details and cover can be shown.";
    static readonly Encoding Cp1252;

    const RegexOptions IC = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex FileposLink = new(@"<[^<>]+filepos=['""]{0,1}(\d+)[^<>]*>", IC);
    static readonly Regex EntityAnchor = new(@"&([^;]*?)(<a id=""filepos\d+""></a>)([^;]*);");
    static readonly Regex PageBreak = new(@"<\s*/{0,1}\s*mbp:pagebreak((?:\s+[^/>]*){0,1})/{0,1}\s*>\s*(?:<\s*/{0,1}\s*mbp:pagebreak\s*/{0,1}\s*>)*", IC);
    static readonly Regex AnchorId = new(@"\s(?:filepos-)?id=""filepos(\d+)""");
    static readonly Regex FileposAttr = new(@"\sfilepos\s*=\s*['""]?(\d+)['""]?", IC);
    static readonly Regex ImgTag = new(@"<img\b[^>]*>", IC);
    static readonly Regex RecIndex = new(@"\s(lowrecindex|recindex|hirecindex)\s*=\s*['""]?(\d+)['""]?", IC);
    static readonly Regex Reference = new(@"<reference\b[^>]*>", IC);
    static readonly Regex TypeToc = new(@"\stype\s*=\s*['""]?toc['""\s/>]", IC);
    static readonly Regex TocLink = new(@"<a\b([^>]*)>(.*?)</a\s*>", IC | RegexOptions.Singleline);
    static readonly Regex AnyTag = new(@"<[^>]*>");
    static readonly Regex Spaces = new(@"\s+");
    static readonly Regex PosFid = new(@"(['""])kindle:pos:fid:([0-9A-V]+):off:([0-9A-V]+)[^'""]*['""]");
    static readonly Regex Embed = new(@"kindle:embed:([0-9A-V]+)(?:\?[^'""\)\s>]*)?");
    static readonly Regex FlowLink = new(@"<link\b[^>]*kindle:flow[^>]*>", IC);
    static readonly Regex FlowRef = new(@"kindle:flow:[0-9A-V]+(?:\?[^'""\)\s>]*)?", IC);
    static readonly Regex IdRe = new(@"^<[^>]+\s(?:id|ID)\s*=\s*['""]([^'""]+)['""]");
    static readonly Regex NameRe = new(@"^<\s*a\s*\s(?:name|NAME)\s*=\s*['""]([^'""]+)['""]");
    static readonly Regex AidRe = new(@"^<[^>]+\s(?:aid|AID)\s*=\s*['""]([^'""]+)['""]");
    static readonly Regex AidTag = new(@"<([A-Za-z][^\s>/]*)([^>]*?\s(?:aid|AID)\s*=\s*['""]([^'""]+)['""][^>]*)>");
    static readonly Regex HasId = new(@"\sid\s*=", IC);

    static MobiReader()
    {
        Encoding e;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); e = Encoding.GetEncoding(1252); }
        catch (Exception) { e = Encoding.Latin1; }
        Cp1252 = e;
    }

    internal static bool IsMobi(ReadOnlySpan<byte> head) =>
        head.Length >= 68 && (head.Slice(60, 8).SequenceEqual("BOOKMOBI"u8) || head.Slice(60, 8).SequenceEqual("TEXtREAd"u8));

    static InvalidDataException Bad(string message) => new(message);

    // ---- PalmDB container (headers.py / mobi6 MobiReader.__init__) ----
    sealed class Pdb
    {
        internal byte[] Data; internal int[] Off; internal int Count;
        internal ReadOnlySpan<byte> Rec(int i) =>
            (uint)i < (uint)Count ? Data.AsSpan(Off[i], Off[i + 1] - Off[i]) : throw Bad("The book refers to a record that does not exist.");
    }

    static uint U32(ReadOnlySpan<byte> s, long o) =>
        o >= 0 && o + 4 <= s.Length ? BinaryPrimitives.ReadUInt32BigEndian(s.Slice((int)o)) : throw Bad("The book's header data is truncated.");
    static int U16(ReadOnlySpan<byte> s, long o) =>
        o >= 0 && o + 2 <= s.Length ? BinaryPrimitives.ReadUInt16BigEndian(s.Slice((int)o)) : throw Bad("The book's header data is truncated.");

    // ---- MOBI / EXTH header (headers.py BookHeader, EXTHHeader) ----
    sealed class Hdr
    {
        internal int Compression, TextRecords, Encryption, Codepage = 1252, Version = 1;
        internal uint FirstImage = Null, HuffOff = Null, HuffCount, ExtraFlags, Ncx = Null, Fdst = Null, Skel = Null, Div = Null;
        internal string FullName;
        internal readonly Dictionary<uint, List<byte[]>> Exth = new();
        internal Encoding Enc => Codepage == 65001 ? Encoding.UTF8 : Cp1252;
    }

    static Hdr ParseHeader(ReadOnlySpan<byte> r, bool textRead)
    {
        if (r.Length < 16) throw Bad("The book's first record is too short.");
        var h = new Hdr { Compression = U16(r, 0), TextRecords = U16(r, 8), Encryption = U16(r, 12) };
        if (textRead || r.Length < 0x84 || !r.Slice(16, 4).SequenceEqual("MOBI"u8)) return h;
        uint len = U32(r, 20);
        h.Codepage = (int)U32(r, 28);
        if (len >= 0xE4 && r.Length >= 0xF4) h.ExtraFlags = (uint)U16(r, 0xF2);
        h.HuffOff = U32(r, 0x70); h.HuffCount = U32(r, 0x74);
        uint toff = U32(r, 0x54), tlen = U32(r, 0x58);
        if (toff < r.Length && tlen <= r.Length - toff) h.FullName = h.Enc.GetString(r.Slice((int)toff, (int)tlen)).Trim('\0', ' ');
        h.Version = (int)U32(r, 0x68);
        h.FirstImage = U32(r, 0x6C);
        uint exthFlag = U32(r, 0x80);
        if ((exthFlag & 0x40) != 0 && len < r.Length - 16) ParseExth(r.Slice(16 + (int)len), h);
        if (r.Length >= 0xF8) h.Ncx = U32(r, 0xF4);
        if (h.Version == 8 && r.Length >= 0xF8 + 16) { h.Div = U32(r, 0xF8); h.Skel = U32(r, 0xFC); h.Fdst = U32(r, 0xC0); }
        return h;
    }

    static void ParseExth(ReadOnlySpan<byte> e, Hdr h)
    {
        if (e.Length < 12 || !e.Slice(0, 4).SequenceEqual("EXTH"u8)) return;
        uint count = U32(e, 8);
        int pos = 12;
        for (uint i = 0; i < count && pos + 8 <= e.Length; i++)
        {
            uint type = U32(e, pos), size = U32(e, pos + 4);
            if (size < 8 || size > e.Length - pos) break;
            if (!h.Exth.TryGetValue(type, out var list)) h.Exth[type] = list = new();
            list.Add(e.Slice(pos + 8, (int)size - 8).ToArray());
            pos += (int)size;
        }
    }

    static List<string> Strings(Hdr h, Hdr h0, uint type)
    {
        foreach (var x in new[] { h, h0 })
            if (x.Exth.TryGetValue(type, out var l))
                return l.Select(b => x.Enc.GetString(b).Trim('\0', ' ', '\r', '\n', '\t')).Where(s => s.Length > 0).ToList();
        return new();
    }

    static uint? Num(Hdr h, Hdr h0, uint type)
    {
        foreach (var x in new[] { h, h0 })
            if (x.Exth.TryGetValue(type, out var l) && l[0].Length >= 4) return BinaryPrimitives.ReadUInt32BigEndian(l[0]);
        return null;
    }

    static string NonEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static string PlainText(string html) =>
        NonEmpty(Spaces.Replace(WebUtility.HtmlDecode(AnyTag.Replace(html ?? "", " ")), " "));

    internal static EbookBook Read(byte[] data, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 86 || !IsMobi(data)) throw Bad("This is not a MOBI or PalmDOC book.");
        bool textRead = data.AsSpan(60, 8).SequenceEqual("TEXtREAd"u8);
        int n = U16(data, 76);
        if (n == 0 || 78 + 8L * n > data.Length) throw Bad("The book's record table is corrupt.");
        var off = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            uint o = U32(data, 78 + 8 * i);
            if (o > data.Length || o < (i == 0 ? 78 + 8L * n : off[i - 1])) throw Bad("The book's record table is corrupt.");
            off[i] = (int)o;
        }
        off[n] = data.Length;
        var pdb = new Pdb { Data = data, Off = off, Count = n };
        int z = Array.IndexOf<byte>(data, 0, 0, 32);
        string palmName = Encoding.Latin1.GetString(data, 0, z < 0 ? 32 : z);

        var h0 = ParseHeader(pdb.Rec(0), textRead);
        Hdr h = h0; int bas = 0; bool kf8 = false;
        if (h0.Version == 8 && h0.Skel != Null) kf8 = true;
        else if (Num(h0, h0, 121) is uint k8 && k8 != Null && k8 >= 1 && k8 < n && pdb.Rec((int)k8 - 1).SequenceEqual("BOUNDARY"u8))
        {
            try
            {
                var hk = ParseHeader(pdb.Rec((int)k8), false);
                if (hk.Version == 8 && hk.Skel != Null) { h = hk; bas = (int)k8; kf8 = true; }
            }
            catch (InvalidDataException) { }
        }

        var book = new EbookBook { Format = kf8 ? "AZW3 (KF8)" : "MOBI" };
        book.Title = Strings(h, h0, 503).FirstOrDefault() ?? NonEmpty(h.FullName) ?? NonEmpty(h0.FullName) ?? NonEmpty(palmName);
        var authors = Strings(h, h0, 100);
        if (authors.Count > 0) book.Author = string.Join(", ", authors);
        book.Publisher = Strings(h, h0, 101).FirstOrDefault();
        book.Description = PlainText(Strings(h, h0, 103).FirstOrDefault());
        book.Published = Strings(h, h0, 106).FirstOrDefault();
        book.Language = Strings(h, h0, 524).FirstOrDefault();

        // Cover: EXTH 201 (cover offset) else 202 (thumbnail), relative to the MOBI6 first image index.
        foreach (uint t in new uint[] { 201, 202 })
        {
            if (Num(h, h0, t) is not uint c || c == Null || h0.FirstImage == Null) continue;
            long idx = (long)h0.FirstImage + c;
            if (idx >= n) continue;
            var rec = pdb.Rec((int)idx);
            if (ImageExt(rec) == null) continue;
            book.Cover = rec.ToArray();
            break;
        }

        if (h.Encryption != 0 || h0.Encryption != 0) { book.Notice = DrmNotice; return book; }

        cancel.ThrowIfCancellationRequested();
        byte[] text = ExtractText(pdb, h, bas, cancel);

        // Resource ranges (mobi8.Mobi8Reader.__call__ resource_offsets); kindle:embed / recindex numbers are 1-based into this list.
        var res = new List<int>();
        if (kf8 && bas > 0)
        {
            AddRange(res, FirstResource(h0.FirstImage, h0.TextRecords, 1), bas - 1, n);
            AddRange(res, h.FirstImage == Null ? n : (int)Math.Min((long)h.FirstImage + bas, n), n, n);
        }
        else AddRange(res, FirstResource(h.FirstImage, h.TextRecords, 1), n, n);

        if (kf8) ReadKf8(book, pdb, h, bas, text, res, cancel);
        else ReadMobi6(book, pdb, h, text, res, cancel);
        return book;
    }

    static int FirstResource(uint fii, int textRecords, int firstText) =>
        fii == Null ? textRecords + firstText : (int)Math.Min(fii, int.MaxValue);

    static void AddRange(List<int> res, int start, int end, int count)
    {
        end = Math.Min(end, count);
        for (int i = Math.Max(start, 0); i < end; i++) res.Add(i);
    }

    static string ImageExt(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return null;
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.StartsWith("GIF8"u8)) return "gif";
        if (b[0] == (byte)'B' && b[1] == (byte)'M') return "bmp";
        return null;
    }

    static string Image(EbookBook book, Pdb pdb, List<int> res, long num)
    {
        if (num < 1 || num > res.Count) return null;
        var rec = pdb.Rec(res[(int)num - 1]);
        string ext = ImageExt(rec);
        if (ext == null) return null;
        string key = $"images/{num:D5}.{ext}";
        if (!book.Resources.ContainsKey(key)) book.Resources[key] = rec.ToArray();
        return key;
    }

    // ---- Text extraction (mobi6.MobiReader.extract_text, sizeof_trailing_entries) ----
    static byte[] ExtractText(Pdb pdb, Hdr h, int bas, CancellationToken cancel)
    {
        Huff huff = null;
        if (h.Compression == 17480)
        {
            if (h.HuffOff == Null || h.HuffCount == 0 || h.HuffCount > 4096) throw Bad("The book's HUFF/CDIC tables are missing.");
            huff = new Huff(pdb, bas + (long)h.HuffOff, (int)h.HuffCount);
        }
        else if (h.Compression != 1 && h.Compression != 2) throw Bad("The book uses an unknown compression method.");

        var ms = new MemoryStream();
        var buf = Array.Empty<byte>();
        int last = Math.Min(bas + h.TextRecords, pdb.Count - 1);
        for (int i = bas + 1; i <= last; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var rec = pdb.Rec(i);
            rec = rec.Slice(0, rec.Length - Trailing(rec, h.ExtraFlags));
            if (h.Compression == 1) ms.Write(rec);
            else if (h.Compression == 2) { int len = PalmDoc(rec, ref buf); ms.Write(buf, 0, len); }
            else huff.Unpack(rec, ms, 0);
            if (ms.Length > MaxText) throw Bad("The book's text is larger than 64 MiB.");
        }

        var raw = ms.GetBuffer();
        int end = (int)ms.Length;
        if (end > 0 && raw[end - 1] == (byte)'#') end--;
        bool cp1252 = h.Codepage != 65001;
        var outp = new byte[end];
        int o = 0;
        for (int i = 0; i < end; i++)
        {
            byte b = raw[i];
            if (b == 0 || (cp1252 && (b == 0x1E || b == 0x02))) continue;
            outp[o++] = b;
        }
        Array.Resize(ref outp, o);
        return outp;
    }

    static int Trailing(ReadOnlySpan<byte> d, uint extra)
    {
        int num = 0, size = d.Length;
        for (uint f = extra >> 1; f != 0; f >>= 1)
        {
            if ((f & 1) == 0) continue;
            int psize = size - num;
            if (psize <= 0) return 0;
            int bitpos = 0, result = 0;
            while (true)
            {
                int v = d[psize - 1];
                result |= (v & 0x7F) << bitpos;
                bitpos += 7; psize--;
                if ((v & 0x80) != 0 || bitpos >= 28 || psize == 0) break;
            }
            num += result;
            if (num > size || num < 0) return 0;   // corrupt trailing entries: calibre keeps the whole record
        }
        if ((extra & 1) != 0)
        {
            int o = size - num - 1;
            num += o >= 0 ? (d[o] & 0x3) + 1 : 1;
        }
        return Math.Min(num, size);
    }

    // compression/palmdoc.c cpalmdoc_decompress
    static int PalmDoc(ReadOnlySpan<byte> inp, ref byte[] buf)
    {
        int need = inp.Length * 8 + 16;
        if (buf.Length < need) buf = new byte[need];
        var b = buf;
        int i = 0, o = 0;
        while (i < inp.Length)
        {
            int c = inp[i++];
            if (c >= 1 && c <= 8) { while (c-- > 0 && i < inp.Length) b[o++] = inp[i++]; }
            else if (c <= 0x7F) b[o++] = (byte)c;
            else if (c >= 0xC0) { b[o++] = (byte)' '; b[o++] = (byte)(c ^ 0x80); }
            else if (i < inp.Length)
            {
                c = (c << 8) | inp[i++];
                int di = (c & 0x3FFF) >> 3;
                if (di > 0 && di <= o)
                    for (int k = (c & 7) + 3; k-- > 0; o++) b[o] = b[o - di];
            }
        }
        return o;
    }

    // huffcdic.py Reader / HuffReader
    sealed class Huff
    {
        readonly (int len, bool term, long max)[] d1 = new (int, bool, long)[256];
        readonly long[] minc = new long[33], maxc = new long[33];
        readonly List<byte[]> dict = new();
        readonly List<bool> flag = new();
        bool[] busy;

        internal Huff(Pdb pdb, long first, int count)
        {
            if (first + count > pdb.Count) throw Bad("The book's HUFF/CDIC tables are missing.");
            var huff = pdb.Rec((int)first);
            if (huff.Length < 16 || !huff.Slice(0, 8).SequenceEqual("HUFF\0\0\0\x18"u8)) throw Bad("The book's HUFF table is invalid.");
            uint off1 = U32(huff, 8), off2 = U32(huff, 12);
            for (int i = 0; i < 256; i++)
            {
                uint v = U32(huff, off1 + 4L * i);
                int codelen = (int)(v & 0x1F); bool term = (v & 0x80) != 0;
                if (codelen == 0 || (codelen <= 8 && !term)) throw Bad("The book's HUFF table is invalid.");
                d1[i] = (codelen, term, (((long)(v >> 8) + 1) << (32 - codelen)) - 1);
            }
            for (int codelen = 1; codelen <= 32; codelen++)
            {
                minc[codelen] = (long)U32(huff, off2 + 8L * (codelen - 1)) << (32 - codelen);
                maxc[codelen] = (((long)U32(huff, off2 + 8L * (codelen - 1) + 4) + 1) << (32 - codelen)) - 1;
            }
            for (int k = 1; k < count; k++)
            {
                var cdic = pdb.Rec((int)first + k);
                if (cdic.Length < 16 || !cdic.Slice(0, 8).SequenceEqual("CDIC\0\0\0\x10"u8)) throw Bad("The book's CDIC table is invalid.");
                uint phrases = U32(cdic, 8), bits = U32(cdic, 12);
                if (bits > 31) throw Bad("The book's CDIC table is invalid.");
                long nn = Math.Min(1L << (int)bits, (long)phrases - dict.Count);
                for (long j = 0; j < nn; j++)
                {
                    int o = U16(cdic, 16 + 2 * j);
                    int blen = U16(cdic, 16 + o);
                    int len = blen & 0x7FFF;
                    if (18 + o + len > cdic.Length) throw Bad("The book's CDIC table is invalid.");
                    dict.Add(cdic.Slice(18 + o, len).ToArray());
                    flag.Add((blen & 0x8000) != 0);
                }
            }
            busy = new bool[dict.Count];
        }

        internal void Unpack(ReadOnlySpan<byte> data, MemoryStream outp, int depth)
        {
            long bitsleft = data.Length * 8L;
            var buf = new byte[data.Length + 8];
            data.CopyTo(buf);
            int pos = 0, nbits = 32;
            ulong x = BinaryPrimitives.ReadUInt64BigEndian(buf);
            while (true)
            {
                if (nbits <= 0)
                {
                    pos += 4;
                    if (pos + 8 > buf.Length) break;
                    x = BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(pos));
                    nbits += 32;
                }
                long code = (long)((x >> nbits) & 0xFFFFFFFF);
                var (cl, term, mx) = d1[code >> 24];
                if (!term)
                {
                    while (cl < 32 && code < minc[cl]) cl++;
                    mx = maxc[cl];
                }
                nbits -= cl; bitsleft -= cl;
                if (bitsleft < 0) break;
                long r = (mx - code) >> (32 - cl);
                if (r < 0 || r >= dict.Count) throw Bad("The book's compressed text is corrupt.");
                int ri = (int)r;
                if (!flag[ri])
                {
                    if (busy[ri] || depth > 32) throw Bad("The book's compressed text is corrupt.");
                    busy[ri] = true;
                    var sub = new MemoryStream();
                    Unpack(dict[ri], sub, depth + 1);
                    dict[ri] = sub.ToArray(); flag[ri] = true; busy[ri] = false;
                }
                outp.Write(dict[ri]);
                if (outp.Length > MaxText) throw Bad("The book's text is larger than 64 MiB.");
            }
        }
    }

    // ---- MOBI6 (mobi6.MobiReader.add_anchors, replace_page_breaks, image and filepos handling) ----
    static void ReadMobi6(EbookBook book, Pdb pdb, Hdr h, byte[] text, List<int> res, CancellationToken cancel)
    {
        string L = Encoding.Latin1.GetString(text);
        var positions = new SortedSet<int>();
        foreach (Match m in FileposLink.Matches(L))
            if (int.TryParse(m.Groups[1].ValueSpan, out int p)) positions.Add(p);
        cancel.ThrowIfCancellationRequested();

        var sb = new StringBuilder(L.Length + positions.Count * 32);
        int pos = 0;
        foreach (int target in positions)
        {
            if (target == 0) continue;
            int end = Math.Min(target, L.Length);
            string anchor = $"<a id=\"filepos{target}\"></a>";
            int l = L.IndexOf('<', end), r = L.IndexOf('>', end);
            if (r > -1 && (r < l || l == end || l == -1))
            {
                int p = L.Length == 0 ? -1 : L.LastIndexOf('<', Math.Min(end, L.Length - 1));
                if (pos < end && p > -1 && !IsEndTag(L, p, r) && !(r > 0 && L[r - 1] == '/'))
                { anchor = $" filepos-id=\"filepos{target}\""; end = r; }
                else end = r + 1;
            }
            if (end < pos) end = pos;
            sb.Append(L, pos, end - pos).Append(anchor);
            pos = end;
        }
        sb.Append(L, pos, L.Length - pos);
        string anchored = EntityAnchor.Replace(sb.ToString(), "&$1$3;$2");
        string html = h.Enc.GetString(Encoding.Latin1.GetBytes(anchored));
        anchored = null; L = null;
        cancel.ThrowIfCancellationRequested();

        // Chapter segments between page breaks.
        var segs = new List<(int start, int end)>();
        int segStart = 0;
        foreach (Match m in PageBreak.Matches(html))
        {
            if (HasText(html, segStart, m.Index)) segs.Add((segStart, m.Index));
            segStart = m.Index + m.Length;
        }
        if (HasText(html, segStart, html.Length)) segs.Add((segStart, html.Length));
        bool single = segs.Count <= 1;
        if (single) segs = new() { (0, html.Length) };

        var hrefs = new List<string>();
        for (int i = 0; i < segs.Count; i++) hrefs.Add($"part{i + 1:D4}.html");
        var anchorChapter = new Dictionary<int, int>();
        int si = 0;
        foreach (Match m in AnchorId.Matches(html))
        {
            while (si < segs.Count - 1 && m.Index >= segs[si].end) si++;
            if (int.TryParse(m.Groups[1].ValueSpan, out int fp)) anchorChapter.TryAdd(fp, si);
        }

        // NCX-less TOC: the guide reference type="toc" points at an HTML table of contents in the text.
        foreach (Match rm in Reference.Matches(html))
        {
            if (!TypeToc.IsMatch(rm.Value)) continue;
            var fm = FileposAttr.Match(rm.Value);
            if (!fm.Success || !int.TryParse(fm.Groups[1].ValueSpan, out int tocPos)) break;
            int at = html.IndexOf($"\"filepos{tocPos}\"", StringComparison.Ordinal);
            if (at < 0) break;
            var next = PageBreak.Match(html, at);
            int stop = Math.Min(next.Success ? next.Index : html.Length, at + (1 << 20));
            foreach (Match am in TocLink.Matches(html.Substring(at, stop - at)))
            {
                var lm = FileposAttr.Match(am.Groups[1].Value);
                if (!lm.Success || !int.TryParse(lm.Groups[1].ValueSpan, out int fp)) continue;
                string title = PlainText(am.Groups[2].Value);
                if (title == null) continue;
                book.Toc.Add(new EbookTocEntry(title, FileposHref(anchorChapter, hrefs, fp, -1), 0));
                if (book.Toc.Count >= 10000) break;
            }
            break;
        }

        for (int i = 0; i < segs.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            string c = html.Substring(segs[i].start, segs[i].end - segs[i].start);
            if (single) c = PageBreak.Replace(c, "");
            c = c.Replace(" filepos-id=\"filepos", " id=\"filepos", StringComparison.Ordinal);
            int cur = i;
            c = FileposAttr.Replace(c, m =>
                int.TryParse(m.Groups[1].ValueSpan, out int fp) ? $" href=\"{FileposHref(anchorChapter, hrefs, fp, cur)}\"" : m.Value);
            c = ImgTag.Replace(c, m =>
            {
                long recindex = -1;
                int rank = -1;
                foreach (Match a in RecIndex.Matches(m.Value))
                {
                    string name = a.Groups[1].Value.ToLowerInvariant();
                    int k = name == "hirecindex" ? 2 : name == "recindex" ? 1 : 0;
                    if (k >= rank && long.TryParse(a.Groups[2].ValueSpan, out long v)) { rank = k; recindex = v; }
                }
                if (rank < 0) return m.Value;
                string tag = RecIndex.Replace(m.Value, "");
                string src = Image(book, pdb, res, recindex);
                return src == null ? tag : tag.Insert(4, $" src=\"{src}\"");
            });
            book.Chapters.Add(new EbookChapter(hrefs[i], c));
        }
    }

    static bool IsEndTag(string s, int p, int r)
    {
        if (p >= s.Length || s[p] != '<') return false;
        int i = p + 1;
        while (i < r && char.IsWhiteSpace(s[i])) i++;
        return i < r && s[i] == '/';
    }

    static bool HasText(string s, int start, int end)
    {
        for (int i = start; i < end; i++) if (!char.IsWhiteSpace(s[i])) return true;
        return false;
    }

    static string FileposHref(Dictionary<int, int> map, List<string> hrefs, int fp, int current)
    {
        if (!map.TryGetValue(fp, out int ch)) return fp == 0 ? hrefs[0] : $"#filepos{fp}";
        return ch == current ? $"#filepos{fp}" : $"{hrefs[ch]}#filepos{fp}";
    }

    // ---- INDX / TAGX / CNCX (index.py read_index, parse_index_record, get_tag_map, CNCX; utils.decint) ----
    sealed class IndexEntry { internal string Ident; internal Dictionary<int, List<long>> Tags; }

    static long Decint(ReadOnlySpan<byte> s, ref int pos)
    {
        long v = 0;
        for (int k = 0; pos < s.Length && k < 9; k++)
        {
            byte b = s[pos++];
            v = (v << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) != 0) break;
        }
        return v;
    }

    static (List<IndexEntry> entries, Dictionary<long, string> cncx) ReadIndex(Pdb pdb, int idx, Encoding enc, CancellationToken cancel)
    {
        var d = pdb.Rec(idx);
        if (d.Length < 184 || !d.Slice(0, 4).SequenceEqual("INDX"u8)) throw Bad("The book's index is corrupt.");
        uint count = U32(d, 24), code = U32(d, 28), ncncx = U32(d, 52), oentries = U32(d, 168), ordt2 = U32(d, 176), tagx = U32(d, 180);
        if (count > pdb.Count || ncncx > pdb.Count) throw Bad("The book's index is corrupt.");

        char[] ordt = null;
        if (code == 65002 && ordt2 > 0 && ordt2 + 4L + 2L * oentries <= d.Length && d.Slice((int)ordt2, 4).SequenceEqual("ORDT"u8))
        {
            ordt = new char[oentries];
            for (int i = 0; i < oentries; i++)
            {
                byte b = d[(int)ordt2 + 4 + 2 * i + 1];
                ordt[i] = b > 0x20 && b < 0x7F ? (char)b : '?';
            }
        }

        var cncx = new Dictionary<long, string>();
        for (long k = 0; k < ncncx; k++)
        {
            long ri = idx + count + 1 + k;
            if (ri >= pdb.Count) break;
            var raw = pdb.Rec((int)ri);
            int p = 0;
            while (p < raw.Length)
            {
                int start = p;
                long length = Decint(raw, ref p);
                if (length > 0)
                {
                    if (length > raw.Length - p) break;
                    cncx[start + k * 0x10000] = enc.GetString(raw.Slice(p, (int)length));
                    p += (int)length;
                }
            }
        }

        int ts = (int)Math.Min(tagx, int.MaxValue);
        if (ts + 12L > d.Length || !d.Slice(ts, 4).SequenceEqual("TAGX"u8))
        {
            int f = d.Slice(184).IndexOf("TAGX"u8);
            if (f < 0) throw Bad("The book's index has no TAGX section.");
            ts = 184 + f;
        }
        var t = d.Slice(ts);
        uint firstEntry = U32(t, 4), cbc = U32(t, 8);
        if (cbc > 16) throw Bad("The book's index is corrupt.");
        var tags = new List<(int tag, int nv, int mask, int eof)>();
        for (long i = 12; i < firstEntry && i + 4 <= t.Length; i += 4)
            tags.Add((t[(int)i], t[(int)i + 1], t[(int)i + 2], t[(int)i + 3]));

        var entries = new List<IndexEntry>();
        for (long i = idx + 1; i <= idx + count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            if (i >= pdb.Count) break;
            var r = pdb.Rec((int)i);
            if (r.Length < 28) continue;
            uint start = U32(r, 20), ecount = U32(r, 24);
            if (start + 4L + 2L * ecount > r.Length) throw Bad("The book's index is corrupt.");
            var ps = new int[ecount + 1];
            for (int j = 0; j < ecount; j++) ps[j] = U16(r, start + 4 + 2 * j);
            ps[ecount] = (int)start;
            for (int j = 0; j < ecount; j++)
            {
                int a = ps[j], b = ps[j + 1];
                if (a >= b || b > r.Length) continue;
                var rec = r.Slice(a, b - a);
                int ilen = rec[0];
                if (1 + ilen > rec.Length) continue;
                var id = rec.Slice(1, ilen);
                string ident;
                if (ordt != null) { var cs = new char[ilen]; for (int q = 0; q < ilen; q++) cs[q] = id[q] < ordt.Length ? ordt[id[q]] : '?'; ident = new string(cs); }
                else ident = enc.GetString(id);
                entries.Add(new IndexEntry { Ident = ident, Tags = TagMap((int)cbc, tags, rec.Slice(1 + ilen)) });
            }
        }
        return (entries, cncx);
    }

    static Dictionary<int, List<long>> TagMap(int cbc, List<(int tag, int nv, int mask, int eof)> tagx, ReadOnlySpan<byte> rec)
    {
        var ans = new Dictionary<int, List<long>>();
        if (rec.Length < cbc) return ans;
        int cb = 0, pos = cbc;
        var ptags = new List<(int tag, long count, long bytes, int nv)>();
        foreach (var x in tagx)
        {
            if (x.eof == 1) { cb++; continue; }
            if (cb >= cbc) break;
            int value = rec[cb] & x.mask;
            if (value == 0) continue;
            long cnt = -1, byts = -1;
            if (value == x.mask)
            {
                if (System.Numerics.BitOperations.PopCount((uint)x.mask) > 1) byts = Decint(rec, ref pos);
                else cnt = 1;
            }
            else
            {
                int m = x.mask;
                while ((m & 1) == 0) { m >>= 1; value >>= 1; }
                cnt = value;
            }
            ptags.Add((x.tag, cnt, byts, x.nv));
        }
        foreach (var x in ptags)
        {
            var values = new List<long>();
            if (x.count >= 0)
            {
                for (long k = 0; k < x.count * x.nv && pos < rec.Length; k++) values.Add(Decint(rec, ref pos));
            }
            else
            {
                long consumed = 0;
                while (consumed < x.bytes && pos < rec.Length)
                {
                    int before = pos;
                    values.Add(Decint(rec, ref pos));
                    consumed += pos - before;
                }
            }
            ans[x.tag] = values;
        }
        return ans;
    }

    static long Tag(Dictionary<int, List<long>> m, int tag, int i) =>
        m.TryGetValue(tag, out var l) && i < l.Count ? l[i] : throw Bad("The book's index is corrupt.");

    // ---- KF8 (mobi8.Mobi8Reader read_indices, build_parts, get_file_info, get_id_tag, create_ncx; markup.py link rewriting) ----
    sealed record Elem(long InsertPos, string TocText, long FileNum, long StartPos, long Length);
    sealed record PartInfo(string Name, long Start, long End);

    sealed class CharBuf
    {
        internal char[] A; internal int N;
        internal CharBuf(string s) { A = new char[Math.Max(16, s.Length * 2)]; s.CopyTo(0, A, 0, s.Length); N = s.Length; }
        internal ReadOnlySpan<char> Span => A.AsSpan(0, N);
        internal void Insert(int at, string s)
        {
            if (N + s.Length > A.Length) Array.Resize(ref A, Math.Max(A.Length * 2, N + s.Length));
            Array.Copy(A, at, A, at + s.Length, N - at);
            s.CopyTo(0, A, at, s.Length);
            N += s.Length;
        }
    }

    static string Sub(string s, long start, long len)
    {
        long a = Math.Clamp(start, 0, s.Length), b = Math.Clamp(start + Math.Max(len, 0), 0, s.Length);
        return s.Substring((int)a, (int)(b - a));
    }

    static long B32(string s)
    {
        long v = 0;
        foreach (char ch in s.ToUpperInvariant())
        {
            int d = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'A' && ch <= 'V' ? ch - 'A' + 10 : -1;
            if (d < 0) return -1;
            v = v * 32 + d;
            if (v > int.MaxValue) return -1;
        }
        return v;
    }

    sealed class Kf8
    {
        internal List<Elem> Elems = new();
        internal List<string> Parts = new();
        internal List<PartInfo> Info = new();
        internal List<HashSet<string>> LinkedAids = new();

        internal int FileAt(long pos)
        {
            for (int i = 0; i < Info.Count; i++) if (pos >= Info[i].Start && pos < Info[i].End) return i;
            return -1;
        }

        // Nearest id/name (or aid) before pos in the containing part; null when no part contains pos.
        internal string IdTag(long pos, out int file)
        {
            file = FileAt(pos);
            if (file < 0) return null;
            string tb = Parts[file];
            int npos = (int)Math.Clamp(pos - Info[file].Start, 0, tb.Length);
            int pgt = tb.IndexOf('>', npos), plt = tb.IndexOf('<', npos);
            if (plt == npos || pgt < plt) npos = pgt + 1;
            int end = npos;
            for (int guard = 0; end > 0 && guard < 4000; guard++)
            {
                int gt = tb.LastIndexOf('>', end - 1);
                if (gt < 1) break;
                int lt = tb.LastIndexOf('<', gt - 1);
                if (lt < 0) break;
                string tag = tb.Substring(lt, gt - lt + 1);
                var m = IdRe.Match(tag);
                if (!m.Success) m = NameRe.Match(tag);
                if (m.Success) return m.Groups[1].Value;
                m = AidRe.Match(tag);
                if (m.Success) { LinkedAids[file].Add(m.Groups[1].Value); return "aid-" + m.Groups[1].Value; }
                end = lt;
            }
            return "";
        }

        internal string Href(long pos, Encoding enc)
        {
            string id = IdTag(pos, out int file);
            if (id == null) return null;
            return id.Length == 0 ? Info[file].Name : Info[file].Name + "#" + enc.GetString(Encoding.Latin1.GetBytes(id));
        }

        internal string HrefByPosFid(long fid, long offset, Encoding enc) =>
            fid >= 0 && fid < Elems.Count ? Href(Elems[(int)fid].InsertPos + offset, enc) : null;
    }

    static int AbsIndex(Pdb pdb, int bas, uint v) => v == Null || bas + (long)v >= pdb.Count ? -1 : bas + (int)v;

    static void ReadKf8(EbookBook book, Pdb pdb, Hdr h, int bas, byte[] text, List<int> res, CancellationToken cancel)
    {
        var enc = h.Enc;
        string L = Encoding.Latin1.GetString(text);

        // FDST: flow 0 is the XHTML text; the other flows (CSS, SVG) are dropped.
        string flow0 = L;
        int fdst = AbsIndex(pdb, bas, h.Fdst);
        if (fdst >= 0)
        {
            var f = pdb.Rec(fdst);
            if (f.Length < 12 || !f.Slice(0, 4).SequenceEqual("FDST"u8)) throw Bad("The book's FDST record is invalid.");
            uint secStart = U32(f, 4), num = U32(f, 8);
            if (num > 0) flow0 = Sub(L, U32(f, secStart), (long)U32(f, secStart + 4) - U32(f, secStart));
        }
        L = null;

        var k = new Kf8();
        var files = new List<(string name, long divcnt, long skelpos, long skellen)>();
        int skel = AbsIndex(pdb, bas, h.Skel);
        if (skel >= 0)
            foreach (var e in ReadIndex(pdb, skel, enc, cancel).entries)
                files.Add((e.Ident, Tag(e.Tags, 1, 0), Tag(e.Tags, 6, 0), Tag(e.Tags, 6, 1)));
        int div = AbsIndex(pdb, bas, h.Div);
        if (div >= 0)
        {
            var (entries, cncx) = ReadIndex(pdb, div, enc, cancel);
            foreach (var e in entries)
            {
                if (!long.TryParse(e.Ident, out long insert)) throw Bad("The book's fragment index is corrupt.");
                cncx.TryGetValue(Tag(e.Tags, 2, 0), out string toc);
                k.Elems.Add(new Elem(insert, toc ?? "", Tag(e.Tags, 3, 0), Tag(e.Tags, 6, 0), Tag(e.Tags, 6, 1)));
            }
        }

        // build_parts: insert each fragment into its skeleton.
        int divptr = 0;
        for (int fi = 0; fi < files.Count; fi++)
        {
            cancel.ThrowIfCancellationRequested();
            var (_, divcnt, skelpos, skellen) = files[fi];
            long baseptr = skelpos + skellen;
            var sk = new CharBuf(Sub(flow0, skelpos, skellen));
            string name = null, aid = "";
            for (long i = 0; i < divcnt; i++)
            {
                if (divptr >= k.Elems.Count) throw Bad("The book's fragment index is corrupt.");
                var e = k.Elems[divptr];
                if (i == 0)
                {
                    aid = e.TocText.Length > 14 ? e.TocText[12..^2] : "";
                    name = $"part{e.FileNum:D4}.html";
                }
                string part = Sub(flow0, baseptr, e.Length);
                long ins = Math.Clamp(e.InsertPos - skelpos, 0, sk.N);
                var head = sk.Span[..(int)ins];
                var tail = sk.Span[(int)ins..];
                if ((tail.IndexOf('>') < tail.IndexOf('<') || head.LastIndexOf('>') < head.LastIndexOf('<')) && aid.Length > 0)
                {
                    int bp = 0, ep = 0;
                    foreach (var m in Regex.EnumerateMatches(sk.Span, @"<[^>]*\said\s*=\s*['""]" + Regex.Escape(aid) + @"['""][^>]*>", IC))
                    {
                        bp = m.Index;
                        int g = sk.Span[(bp + 1)..].IndexOf('>');
                        ep = g < 0 ? -1 : bp + 1 + g;
                        break;
                    }
                    if (bp != ep) ins = Math.Clamp(ep + 1 + e.StartPos, 0, sk.N);
                }
                if (sk.N + (long)part.Length > MaxText) throw Bad("The book's text is larger than 64 MiB.");
                sk.Insert((int)ins, part);
                baseptr += e.Length;
                divptr++;
            }
            k.Parts.Add(new string(sk.Span));
            k.Info.Add(new PartInfo(divcnt < 1 || name == null ? $"part-empty{fi:D4}.html" : name, skelpos, baseptr));
            k.LinkedAids.Add(new HashSet<string>(StringComparer.Ordinal));
        }
        if (k.Parts.Count == 0)
        {
            k.Parts.Add(flow0);
            k.Info.Add(new PartInfo("part0001.html", 0, flow0.Length));
            k.LinkedAids.Add(new HashSet<string>(StringComparer.Ordinal));
        }
        flow0 = null;

        // kindle:pos:fid links -> part file + nearest id.
        for (int i = 0; i < k.Parts.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            k.Parts[i] = PosFid.Replace(k.Parts[i], m =>
            {
                long fid = B32(m.Groups[2].Value), offs = B32(m.Groups[3].Value);
                string href = fid < 0 || offs < 0 ? null : k.HrefByPosFid(fid, offs, enc);
                string q = m.Groups[1].Value;
                return q + (href ?? "#") + q;
            });
        }

        // NCX (ncx.read_ncx + mobi8.create_ncx), with the heading level as depth.
        int ncx = AbsIndex(pdb, bas, h.Ncx);
        if (ncx >= 0)
        {
            try
            {
                var (entries, cncx) = ReadIndex(pdb, ncx, enc, cancel);
                foreach (var e in entries)
                {
                    string href;
                    if (e.Tags.TryGetValue(6, out var pf) && pf.Count >= 2) href = k.HrefByPosFid(pf[0], pf[1], enc);
                    else if (e.Tags.TryGetValue(1, out var p) && p.Count > 0) href = k.Href(p[0], enc);
                    else continue;
                    if (href == null) continue;
                    string title = e.Tags.TryGetValue(3, out var t) && t.Count > 0 && cncx.TryGetValue(t[0], out var s) ? s : e.Ident;
                    int depth = e.Tags.TryGetValue(4, out var hl) && hl.Count > 0 ? (int)Math.Clamp(hl[0], 0, 20) : 0;
                    book.Toc.Add(new EbookTocEntry(NonEmpty(title) ?? "Untitled", href, depth));
                }
            }
            catch (InvalidDataException) { book.Toc.Clear(); }
        }

        // Give linked aid elements an id, rewrite images, drop flow references, decode.
        for (int i = 0; i < k.Parts.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            string p = k.Parts[i];
            var aids = k.LinkedAids[i];
            if (aids.Count > 0)
                p = AidTag.Replace(p, m => aids.Contains(m.Groups[3].Value) && !HasId.IsMatch(m.Groups[2].Value)
                    ? $"<{m.Groups[1].Value} id=\"aid-{m.Groups[3].Value}\"{m.Groups[2].Value}>" : m.Value);
            p = Embed.Replace(p, m => Image(book, pdb, res, B32(m.Groups[1].Value)) ?? "missing-image");
            p = FlowLink.Replace(p, "");
            p = FlowRef.Replace(p, "#");
            book.Chapters.Add(new EbookChapter(k.Info[i].Name, enc.GetString(Encoding.Latin1.GetBytes(p))));
            k.Parts[i] = null;
        }
    }
}
