using System.Globalization;
using System.IO;

namespace FileExplorer;

/// ACCESS-10 (owner 2026-11-27: "the previewer is not properly wired to use the admin toggle"): with administrator
/// browsing on, a file the signed-in user may not read is previewed, and opened in the editor, from a copy the approved
/// session makes. The copy keeps the file's name (so its type and viewer still apply), lives in its own folder under
/// the cache, and goes when the preview or editor is done with it. Reading never prompts and never changes the file.
internal static class AdministratorCopies
{
    internal static string Root => Path.Combine(DataFolder.Cache, "AdministratorCopies");

    /// Whether this local file needs the approved session to be read: administrator browsing is on for it and the
    /// signed-in user is refused.
#if DEBUG
    internal static Func<string, bool> NeededForVerification;
    internal static Func<string, long, CancellationToken, Task<string>> CopyForVerification;
#endif
    internal static bool Needed(string path)
    {
#if DEBUG
        if (NeededForVerification != null) return NeededForVerification(path);
#endif
        if (!AdministratorSession.BrowsingFor(path) || !AdministratorSession.Available || !Path.IsPathFullyQualified(path)) return false;
        try { using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return false; }
    }

    /// A user-readable copy of `path` made by the approved session, at most `limit` bytes.
    internal static async Task<string> Copy(string path, long limit, CancellationToken cancel)
    {
#if DEBUG
        if (CopyForVerification != null) return await CopyForVerification(path, limit, cancel).ConfigureAwait(false);
#endif
        string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, Path.GetFileName(path));
        try
        {
            await using var client = new MaintenanceClient();
            var result = await client.Start(new("copy-file", Path.GetFullPath(path), limit.ToString(CultureInfo.InvariantCulture), copy), true, cancel).ConfigureAwait(false);
            if (!result.Ok) throw new IOException(result.Message);
            return copy;
        }
        catch { Release(copy); throw; }
    }

    /// The copy and its folder go.
    internal static void Release(string copy)
    {
        try { string folder = Path.GetDirectoryName(copy); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// At exit: copies still left (a viewer held one open) go.
    internal static void Cleanup()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// The worker's side: copies the source into a fresh copy folder under Root, nowhere else, and never more than
    /// the limit. The source is only read.
    internal static string Apply(MaintenanceRequest request, CancellationToken cancel)
    {
        if (!Path.IsPathFullyQualified(request.Id) || !Path.IsPathFullyQualified(request.Cache)) throw new ArgumentException("An absolute file and copy location are required");
        if (FileExplorer.Search.NetworkDrives.IsNetwork(request.Id)) throw new ArgumentException("Network files are read with standard access");
        if (!long.TryParse(request.Journal, NumberStyles.None, CultureInfo.InvariantCulture, out long limit)) throw new ArgumentException("A size limit is required");
        string source = Path.GetFullPath(request.Id), copy = Path.GetFullPath(request.Cache), folder = Path.GetDirectoryName(copy);
        if (!string.Equals(Path.GetDirectoryName(folder), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(folder), "N", out _)
            || !string.Equals(Path.GetFileName(copy), Path.GetFileName(source), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The copy is outside its folder");
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("The copy folder is not a fresh folder");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > limit) throw new IOException($"{Path.GetFileName(source)} is too large to preview ({MainWindow.Fmt(input.Length)}).");
        using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920]; long total = 0; int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                if ((total += read) > limit) throw new IOException($"{Path.GetFileName(source)} grew past the preview limit.");
                output.Write(buffer, 0, read);
            }
        }
        return "File copied";
    }
}
