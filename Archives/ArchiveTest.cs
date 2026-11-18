using System.IO;
using SharpSevenZip;
using SharpSevenZip.EventArguments;
using SharpSevenZip.Exceptions;

namespace FileExplorer;

// ARCHIVE-13 Test archive, as 7-Zip's t command: every entry decoded once (a solid block in a single pass) into
// nothing, 7-Zip checking each one against its stored CRC or MAC; what fails is reported by entry, nothing is written.
internal static partial class Archive
{
    internal sealed record TestResult(int Tested, long Bytes, IReadOnlyList<(string Entry, string Problem)> Failures);

    internal static TestResult Test(string archive, Job job)
    {
        var entries = Read(archive);
        var files = entries.Where(e => !e.IsDir).ToDictionary(e => e.Index);
        if (job != null) { job.TotalFiles = files.Count; job.TotalBytes = files.Values.Sum(e => e.Size); job.Phase = Phase.Verifying; }
        var failures = new List<(string, string)>();
        int tested = 0; long bytes = 0;
        if (TarGzip(archive))
        {
            // GZip's own CRC covers the whole stream; TAR entries carry no checksum of their content
            try
            {
                VisitTarGzip(archive, (name, dir, size, time, stream) =>
                {
                    if (dir) return;
                    Check(job);
                    if (job != null) { job.Current = name; job.CurrentSize = size; job.CurrentDone = 0; }
                    using var sink = new CheckedOutput(Stream.Null, size, job, progress: job != null);
                    stream.CopyTo(sink);
                    tested++; bytes += size; if (job != null) job.DoneFiles++;
                });
            }
            catch (InvalidDataException ex) { failures.Add(("(compressed stream)", ex.Message)); }
            return new(tested, bytes, failures);
        }
        using var reader = Open(archive);
        Exception stop = null;
        CheckedOutput current = null;
        try
        {
            reader.ExtractFiles(args =>
            {
                try
                {
                    var info = args.ArchiveFileInfo;
                    if (!files.TryGetValue(info.Index, out var entry)) return;
                    switch (args.Reason)
                    {
                        case ExtractFileCallbackReason.Start:
                            Check(job);
                            if (job != null) { job.Current = entry.Path; job.CurrentSize = entry.Size; job.CurrentDone = 0; }
                            args.ExtractToStream = current = new CheckedOutput(Stream.Null, entry.Size, job, progress: job != null);
                            break;
                        case ExtractFileCallbackReason.Done:
                            if (current != null && current.Length != entry.Size) failures.Add((entry.Path, "Decoded size differs from the size the archive records"));
                            else { tested++; bytes += entry.Size; }
                            if (job != null) job.DoneFiles++;
                            current?.Dispose(); current = null;
                            break;
                        case ExtractFileCallbackReason.Failure:
                            failures.Add((entry.Path, Describe(args.Exception, entry)));
                            if (job != null) job.DoneFiles++;
                            current?.Dispose(); current = null;
                            break;
                    }
                }
                catch (Exception ex) { stop = ex; args.CancelExtraction = true; }
            });
        }
        catch (SharpSevenZipException ex) when (stop == null)
        {
            // a broken container (headers, a missing volume) stops 7-Zip before or between entries
            if (failures.Count == 0 || tested + failures.Count < files.Count) failures.Add(("(archive)", Describe(ex, null)));
        }
        Check(job);
        if (stop is OperationCanceledException) throw stop;
        if (stop != null) throw new IOException("Testing stopped: " + stop.Message, stop);
        return new(tested, bytes, failures);
    }

    static string Describe(Exception ex, Raw entry)
    {
        string text = ex?.Message ?? "";
        if (text.Contains("password", StringComparison.OrdinalIgnoreCase) || entry?.Encrypted == true && text.Length == 0) return "Wrong password, or the data is damaged";
        if (text.Contains("CRC", StringComparison.OrdinalIgnoreCase)) return "Fails its stored CRC: the data is damaged";
        if (text.Contains("data", StringComparison.OrdinalIgnoreCase)) return entry?.Encrypted == true ? "Data error: wrong password, or the data is damaged" : "Data error: the data is damaged";
        return text.Length > 0 ? text : "Can't be decoded";
    }
}
