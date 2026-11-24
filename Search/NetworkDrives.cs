using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FileExplorer.Search;

// IDX-21 (owner, 2026-11-24: "Why is browsing M: so slow and lagging? Isn't everything indexed?"): the index service
// runs as SYSTEM and knows a network share added in Settings by its UNC path (\\server\share), while a mapped drive
// letter exists only in the user's session. A path on a mapped drive is sent as the share's UNC path when the service
// indexes that share and has no store for the letter itself (T:, a Tresorit drive, is indexed as T:), and paths the
// service returns are translated back, so M:\ is answered from the index under the name the user browses.
internal static class NetworkDrives
{
    static Dictionary<char, string> mapped = new();
    static long resolvedAt = long.MinValue;
    static readonly object gate = new();
    // Drive mappings change rarely; WNetGetConnection is a local call, re-read at most this often.
    static readonly TimeSpan Refresh = TimeSpan.FromSeconds(10);
#if DEBUG
    // Verification profiles substitute mappings (no real share is mapped on the test desktop).
    internal static Dictionary<char, string> MappedOverride;
#endif

    static Dictionary<char, string> Mapped()
    {
#if DEBUG
        if (MappedOverride != null) return MappedOverride;
#endif
        lock (gate)
        {
            if (resolvedAt != long.MinValue && Environment.TickCount64 - resolvedAt < (long)Refresh.TotalMilliseconds) return mapped;
            var current = new Dictionary<char, string>();
            uint bits = GetLogicalDrives();
            for (int i = 0; i < 26; i++)
            {
                if ((bits & (1u << i)) == 0) continue;
                char letter = (char)('A' + i);
                var buffer = new StringBuilder(1024); int length = buffer.Capacity;
                if (WNetGetConnectionW(letter + ":", buffer, ref length) == 0 && buffer.ToString().TrimEnd('\\') is { Length: > 2 } unc && unc.StartsWith(@"\\"))
                    current[letter] = unc;
            }
            mapped = current; resolvedAt = Environment.TickCount64;
            return mapped;
        }
    }

    static bool Covers(string store, string path)
        => path.StartsWith(store, StringComparison.OrdinalIgnoreCase) && (path.Length == store.Length || path[store.Length] == '\\');
    static string Root(string store) => store.TrimEnd('\\');

    // The share behind a drive letter, when the service indexes (part of) that share and not the letter.
    static string Share(char letter, IReadOnlyCollection<string> stores)
    {
        if (!Mapped().TryGetValue(char.ToUpperInvariant(letter), out var unc)) return null;
        string drive = char.ToUpperInvariant(letter) + ":";
        if (stores.Any(s => Covers(drive, Root(s)))) return null;
        return stores.Any(s => Covers(Root(s), unc) || Covers(unc, Root(s))) ? unc : null;
    }

    // M:\Books -> \\server\share\Books; any other path unchanged.
    internal static string ToService(string path, IReadOnlyCollection<string> stores)
    {
        if (path is not { Length: >= 2 } || path[1] != ':' || (path.Length > 2 && path[2] != '\\')) return path;
        return Share(path[0], stores) is { } unc ? unc + path[2..] : path;
    }

    // \\server\share\Books -> M:\Books (the longest mapping wins); any other path unchanged.
    internal static string FromService(string path, IReadOnlyCollection<string> stores)
    {
        if (path is not { Length: > 2 } || !path.StartsWith(@"\\")) return path;
        string best = null; char letter = '\0';
        foreach (var (drive, unc) in Mapped())
            if (Covers(unc, path) && unc.Length > (best?.Length ?? 0) && Share(drive, stores) != null) { best = unc; letter = drive; }
        if (best == null) return path;
        return path.Length == best.Length ? letter + @":\" : letter + ":" + path[best.Length..];
    }

    // Drive paths written in a search expression (path:"M:\Books\", parent:M:\x, a typed M:\...).
    static readonly Regex DrivePath = new(@"(?<=^|[\s""<|:!])([A-Za-z]):\\", RegexOptions.CultureInvariant);
    internal static string Expression(string expression, IReadOnlyCollection<string> stores)
        => expression == null || !expression.Contains(":\\") ? expression
            : DrivePath.Replace(expression, m => Share(m.Groups[1].Value[0], stores) is { } unc ? unc + "\\" : m.Value);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)] static extern int WNetGetConnectionW(string local, StringBuilder remote, ref int length);
    [DllImport("kernel32.dll")] static extern uint GetLogicalDrives();
}
