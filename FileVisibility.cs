using System.IO;
using System.IO.Enumeration;

namespace FileExplorer;

internal static class FileVisibility
{
    internal static bool Visible(ReadOnlySpan<char> name, FileAttributes attributes, bool showHidden) =>
        showHidden || (attributes & FileAttributes.Hidden) == 0 &&
        (name.IsEmpty || name[0] != '.' || name.SequenceEqual(".") || name.SequenceEqual(".."));

    // Prune hidden directories before descending, rather than walking their
    // contents and merely filtering the returned filenames.
    internal static IEnumerable<FileSystemInfo> Subtree(string root, bool showHidden)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var entries = new FileSystemEnumerable<FileSystemInfo>(root, (ref FileSystemEntry entry) => entry.ToFileSystemInfo(), options);
        entries.ShouldIncludePredicate = (ref FileSystemEntry entry) => Visible(entry.FileName, entry.Attributes, showHidden);
        entries.ShouldRecursePredicate = (ref FileSystemEntry entry) => Visible(entry.FileName, entry.Attributes, showHidden);
        return entries;
    }

    internal static bool PathVisible(string path, string root, IDictionary<string, bool> parents, CancellationToken token = default)
    {
        bool Check(string item)
        {
            token.ThrowIfCancellationRequested();
            try { return Visible(Path.GetFileName(item), File.GetAttributes(item), false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return Visible(Path.GetFileName(item), 0, false); } // preserve indexed offline/unknown results
        }
        if (!Check(path)) return false;
        string parent = Path.GetDirectoryName(path);
        while (parent != null && !parent.Equals(root?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) && Path.GetPathRoot(parent) != parent)
        {
            token.ThrowIfCancellationRequested();
            if (!parents.TryGetValue(parent, out bool visible)) parents[parent] = visible = Check(parent);
            if (!visible) return false;
            parent = Path.GetDirectoryName(parent);
        }
        return true;
    }
}
