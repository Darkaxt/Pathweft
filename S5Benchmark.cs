#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileExplorer;

// A windowless real Ferry caller for comparable process-level measurements.
// It never loads/saves normal settings, registers shell handlers or uses clipboard.
internal static class S5Benchmark
{
    internal sealed record Request(string Source, string Destination, string Receipt, bool Verify, bool Parallel);
    internal static bool Owned(string path) => Path.GetFullPath(path).StartsWith(@"D:\Temp\pathweft-s5-native\", StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).StartsWith(@"E:\Temp\pathweft-s5-review\", StringComparison.OrdinalIgnoreCase);

    internal static int Run(string requestPath)
    {
        try
        {
            if (!Owned(requestPath) || Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s5-native\", StringComparison.OrdinalIgnoreCase) != true)
                throw new IOException("S5 benchmark requires its exact isolated fixture/profile.");
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath));
            if (request == null || !Owned(request.Source) || !Owned(request.Destination) || !Owned(request.Receipt)
                || !Directory.Exists(request.Source) || !Directory.Exists(request.Destination)
                || Directory.EnumerateFileSystemEntries(request.Destination).Any() || File.Exists(request.Receipt))
                throw new IOException("S5 benchmark requires an existing source, empty owned destination and new receipt.");
            Settings.Verify = request.Verify; Settings.ParallelCopy = request.Parallel;
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            var job = Ferry.Submit(new[] { request.Source }, request.Destination, move: false);
            job.Completion.GetAwaiter().GetResult();
            clock.Stop(); process.Refresh();
            var result = new
            {
                engineWallMilliseconds = clock.Elapsed.TotalMilliseconds,
                engineCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                processPeakWorkingSetBytes = process.PeakWorkingSet64,
                processPrivateBytesAtCompletion = process.PrivateMemorySize64,
                phase = job.Phase.ToString(), job.TotalFiles, job.DoneFiles, job.TotalBytes, job.DoneBytes, job.VerificationBytes,
                errors = job.Errors.ToArray(), notes = job.Notes.ToArray(), job.SkippedFiles,
                request.Verify, request.Parallel,
                copyPolicy = "Single-stream data including compressed/sparse: shared buffered 1 MiB copy with source hash captured during writes; named streams/encryption: native CopyFileEx, unbuffered from 64 MiB; compression/sparse destination policy reported",
                assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Ferry).Assembly.Location))),
                operatingSystem = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(),
                source = request.Source, destination = request.Destination,
                timingBoundary = "Submit through actual Ferry completion; independent content validation belongs to supervising caller"
            };
            File.WriteAllText(request.Receipt, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return job.Phase == Phase.Done && job.Errors.IsEmpty && job.DoneFiles == job.TotalFiles ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (Owned(requestPath)) File.WriteAllText(requestPath + ".error.txt", ex.ToString());
            return 1;
        }
    }
}
#endif
