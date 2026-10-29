using System.IO;

namespace FileExplorer;

internal static partial class Archive
{
    sealed record Member(string Source, string Name, bool Directory, long Size, DateTime Modified, string Hash, string Container = null, Raw Entry = null);

    // Executed by Ferry's existing destination lane, never by a second archive queue.
    internal static void Transfer(Job job)
    {
        Check(job);
        var destination = PaneView.ArchiveOf(job.Destination);
        if (destination == null && !Directory.Exists(job.Destination)) throw new IOException("The destination folder is unavailable.");
        if (destination is { } writable && ChangeReason(writable.Archive) is { Length: > 0 } destinationReason) throw new IOException(destinationReason);
        string destinationHash = destination is { } d ? Fingerprint(d.Archive) : null;
        var containers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var members = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        void Add(Member member)
        {
            Check(job); _ = Clean(member.Name);
            if (!members.TryAdd(member.Name, member)) throw new IOException("Selected inputs have colliding names: " + member.Name);
        }
        void Disk(string path, string name)
        {
            Check(job); var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive transfers do not follow links: " + path);
            bool dir = (attributes & FileAttributes.Directory) != 0;
            Add(new(path, name, dir, dir ? 0 : new FileInfo(path).Length, File.GetLastWriteTime(path), dir ? "directory" : Fingerprint(path)));
            if (dir)
            {
                string[] children = Directory.GetFileSystemEntries(path); directories.Add(path, children);
                foreach (string child in children) Disk(child, name + "/" + Path.GetFileName(child));
            }
        }
        foreach (string source in job.Sources.Where(s => !job.Sources.Any(parent => parent != s && s.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))))
        {
            var location = PaneView.ArchiveOf(source);
            if (location is not { Inner.Length: > 0 } at) { Disk(source, Path.GetFileName(source.TrimEnd('\\'))); continue; }
            if (job.IsMove && !CanChange(at.Archive)) throw new IOException("A cut cannot remove entries from this read-only container; use Copy.");
            containers.TryAdd(at.Archive, Fingerprint(at.Archive));
            string selected = Clean(at.Inner), prefix = selected.Contains('/') ? selected[..(selected.LastIndexOf('/') + 1)] : "";
            var entries = Read(at.Archive).Where(e => Under(e.Path, selected)).ToArray();
            if (entries.Length == 0) throw new IOException("A selected archive entry no longer exists.");
            if (!entries.Any(e => e.Path.Equals(selected, StringComparison.OrdinalIgnoreCase)))
                Add(new(source, selected[prefix.Length..], true, 0, entries[0].Modified, "directory", at.Archive));
            foreach (var entry in entries)
                Add(new(at.Archive + "\\" + entry.Path.Replace('/', '\\'), entry.Path[prefix.Length..], entry.IsDir, entry.Size, entry.Modified, EntryHash(at.Archive, entry, job), at.Archive, entry));
        }
        void ValidateSources()
        {
            Check(job);
            foreach (var container in containers) if (Fingerprint(container.Key) != container.Value) throw new IOException("A source archive changed during the transfer; source removal is refused.");
            foreach (var directory in directories)
                if ((File.GetAttributes(directory.Key) & FileAttributes.ReparsePoint) != 0 || !Directory.GetFileSystemEntries(directory.Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(directory.Value))
                    throw new IOException("A source folder changed during the transfer; source removal is refused.");
            foreach (var member in members.Values.Where(m => m.Container == null && !m.Directory))
                if ((File.GetAttributes(member.Source) & FileAttributes.ReparsePoint) != 0 || Fingerprint(member.Source) != member.Hash) throw new IOException("A source file changed during the transfer; source removal is refused.");
        }
        var existing = destination is { } a ? Read(a.Archive).ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, Raw>(StringComparer.OrdinalIgnoreCase);
        string destinationPrefix = destination is { } b ? Prefix(b.Inner) : "";
        var conflicts = new List<Conflict>(); var diskTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var member in members.Values)
        {
            Check(job);
            if (destination != null)
            {
                if (existing.TryGetValue(destinationPrefix + member.Name, out var hit))
                {
                    if (hit.IsDir != member.Directory) throw new IOException("File/folder type conflict: " + member.Name);
                    if (!member.Directory) conflicts.Add(new(index, member.Name, member.Size, member.Modified, hit.Size, hit.Modified));
                }
            }
            else
            {
                string target = Target(job.Destination, member.Name);
                if (File.Exists(target))
                {
                    if (member.Directory) throw new IOException("A file occupies the destination folder: " + member.Name);
                    var info = new FileInfo(target); diskTargets[target] = Fingerprint(target);
                    conflicts.Add(new(index, member.Name, member.Size, member.Modified, info.Length, info.LastWriteTime));
                }
                if (!member.Directory && Directory.Exists(target)) throw new IOException("A folder occupies the destination file: " + member.Name);
            }
            index++;
        }
        job.TotalFiles = members.Values.Count(m => !m.Directory); job.TotalBytes = members.Values.Sum(m => m.Size);
        Choice choice = Choice.Overwrite;
        if (conflicts.Count > 0)
        {
            job.Conflicts = conflicts; job.Phase = Phase.Conflicts; job.Refresh();
            choice = job.WaitForResolution() ?? throw new OperationCanceledException();
        }
        Check(job); ValidateSources();
        var planned = new List<(Member Member, string Name)>();
        var used = destination != null ? existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members.Values)
        {
            string name = member.Name;
            var conflict = conflicts.FirstOrDefault(c => c.Name == member.Name);
            if (conflict != null && (choice == Choice.Skip || choice == Choice.Newer && conflict.TargetModified >= member.Modified))
            { job.SkippedFiles++; job.Notes.Enqueue("Skipped existing destination; source retained: " + member.Name); continue; }
            if (conflict != null && choice == Choice.Rename)
            {
                string directory = name.Contains('/') ? name[..(name.LastIndexOf('/') + 1)] : "", file = name[directory.Length..];
                string stem = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file); int n = 2;
                do { name = directory + stem + " (" + n++ + ")" + ext; }
                while (destination != null ? used.Contains(destinationPrefix + name) : File.Exists(Target(job.Destination, name)) || Directory.Exists(Target(job.Destination, name)));
            }
            if (!member.Directory && !used.Add(destinationPrefix + name) && conflict == null) throw new IOException("Two selected files map to the same destination.");
            planned.Add((member, name));
        }
        if (planned.Count == 0) return;
        job.Phase = Phase.Copying;
        var completed = new HashSet<Member>();
        bool moveWithin = job.IsMove && destination is { } same && members.Values.All(m => m.Container != null && m.Container.Equals(same.Archive, StringComparison.OrdinalIgnoreCase));
        if (destination is { } into)
        {
            if (job.IsMove && !moveWithin && members.Values.Any(m => m.Container?.Equals(into.Archive, StringComparison.OrdinalIgnoreCase) == true))
                throw new IOException("A move cannot mix entries from its destination archive with other sources.");
            var changes = new Dictionary<int, string>(); var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string staging = Path.Combine(Path.GetDirectoryName(into.Archive), ".pathweft-input-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var (member, name) in planned)
                {
                    string target = destinationPrefix + name;
                    if (member.Container?.Equals(into.Archive, StringComparison.OrdinalIgnoreCase) == true && Under(target, member.Entry?.Path ?? Clean(PaneView.ArchiveOf(member.Source).Value.Inner)))
                        throw new IOException("An entry cannot be copied or moved into itself.");
                    if (existing.TryGetValue(target, out var hit) && !member.Directory) changes[hit.Index] = null;
                    if (moveWithin)
                    {
                        if (member.Entry != null) changes[member.Entry.Index] = member.Directory && existing.ContainsKey(target) ? null : target;
                    }
                    else if (!member.Directory || !existing.ContainsKey(target))
                    {
                        if (member.Directory) additions[target] = null;
                        else if (member.Container == null) additions[target] = member.Source;
                        else
                        {
                            string extracted = Target(staging, member.Name); Directory.CreateDirectory(Path.GetDirectoryName(extracted));
                            using (var file = new FileStream(extracted, FileMode.CreateNew)) CopyEntry(member.Container, member.Entry, file, job);
                            if (Fingerprint(extracted) != member.Hash) throw new IOException("Selected source-entry verification failed.");
                            additions[target] = extracted;
                        }
                    }
                    completed.Add(member);
                }
                ValidateSources();
                Edit(into.Archive, changes, additions, job, destinationHash, ValidateSources);
                foreach (var member in completed.Where(m => !m.Directory)) { job.DoneFiles++; job.AddBytes(member.Size); }
            }
            finally { Directory.Delete(staging, recursive: true); }
        }
        else
        {
            foreach (var (member, name) in planned)
            {
                Check(job); string target = Target(job.Destination, name);
                if (member.Directory) { Directory.CreateDirectory(target); completed.Add(member); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temporary = target + ".pathweft-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew))
                    {
                        if (member.Container != null) CopyEntry(member.Container, member.Entry, output, job);
                        else { using var input = File.OpenRead(member.Source); using var checkedOutput = new CheckedOutput(output, member.Size, job); input.CopyTo(checkedOutput); }
                        output.Flush(true);
                    }
                    job.Phase = Phase.Verifying;
                    if (Fingerprint(temporary) != member.Hash) throw new IOException("Transferred content verification failed.");
                    ValidateSources(); _ = Target(job.Destination, name);
                    if (diskTargets.TryGetValue(target, out var prior) && Fingerprint(target) != prior) throw new IOException("A destination changed after conflict planning.");
                    File.Move(temporary, target, diskTargets.ContainsKey(target)); File.SetLastWriteTime(target, member.Modified);
                    completed.Add(member); job.DoneFiles++; job.AddBytes(member.Size); job.Phase = Phase.Copying;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        job.Arrivals = planned.Where(p => completed.Contains(p.Member)).Select(p => p.Name.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!job.IsMove || moveWithin) return;
        Check(job); ValidateSources(); Boundary("before-source-removal"); Check(job); ValidateSources();
        foreach (var group in completed.Where(m => m.Container != null).GroupBy(m => m.Container, StringComparer.OrdinalIgnoreCase))
        {
            var removed = group.Where(m => !m.Directory && m.Entry != null).Select(m => m.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var all = Read(group.Key);
            foreach (var directory in group.Where(m => m.Directory && m.Entry != null))
                if (all.Where(e => !e.IsDir && Under(e.Path, directory.Entry.Path)).All(e => removed.Contains(e.Path))) removed.Add(directory.Entry.Path);
            Edit(group.Key, all.Where(e => removed.Contains(e.Path)).ToDictionary(e => e.Index, _ => (string)null), new(), job, containers[group.Key]);
        }
        foreach (var member in completed.Where(m => m.Container == null && !m.Directory))
        {
            Check(job);
            using var source = new FileStream(member.Source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)) != member.Hash) throw new IOException("A moved source changed; it was retained.");
            File.Delete(member.Source);
        }
        foreach (var directory in completed.Where(m => m.Container == null && m.Directory).OrderByDescending(m => m.Source.Length))
        {
            string[] remaining = Directory.GetFileSystemEntries(directory.Source);
            if (remaining.Any(path => !members.Values.Any(m => m.Source.Equals(path, StringComparison.OrdinalIgnoreCase))))
                throw new IOException("New source-folder members remain; the folder was retained.");
            if (remaining.Length == 0) Directory.Delete(directory.Source);
            else job.Notes.Enqueue("Skipped source-folder members remain: " + directory.Source);
        }
    }
}
