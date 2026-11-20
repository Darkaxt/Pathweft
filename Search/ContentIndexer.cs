using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FileExplorer.Search;

// CONTENT-02/04: the extraction worker, `Pathweft.exe --content-index-worker <parent pid>`. Pathweft starts it
// in a job (memory cap, ended with Pathweft) when the index service has files waiting. It runs as the user in
// background mode (low CPU, I/O and memory priority), so reading a cloud file fetches it through its provider
// (the service never reads contents, IDX-11). It asks the service for work, pushes each file's text chunks with
// their locations, and exits once the service has nothing left for it.
internal static class ContentIndexer
{
    internal const string MutexName = @"Local\Pathweft.ContentIndexer";
    static readonly TimeSpan Settle = TimeSpan.FromSeconds(2), FileTimeout = TimeSpan.FromMinutes(2), Idle = TimeSpan.FromSeconds(5);
    const int Batch = 16, IdleChecks = 3;
    // Service limits (IDX-17): 64 KiB per chunk, 64 MiB of text per file. Puts are sent about every 512 KiB.
    internal const int ChunkBytes = 64 * 1024, PutBytes = 512 * 1024;
    internal const long FileTextBytes = 64L << 20;
#if DEBUG
    internal static Action<string> Observed;
#endif

    internal static async Task<int> Run(int parent, string pipe = null)
    {
        // One worker per sign-in session: whoever creates the named object runs. A semaphore, not a mutex, because
        // the work continues on pool threads; the object goes away with the last handle, also after a crash.
        using var single = new Semaphore(0, 1, MutexName, out bool first);
        if (!first) return 0;
        try
        {
            SetPriorityClass(new IntPtr(-1), 0x00100000); // PROCESS_MODE_BACKGROUND_BEGIN
            using var stop = new CancellationTokenSource();
            _ = WatchParent(parent, stop);
            var service = pipe == null ? IndexServiceClient.Shared : new IndexServiceClient(pipe);
            await Drain(service, stop.Token);
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (InvalidOperationException error) { Trace.WriteLine("Content indexer stopped: " + error.Message); return 1; }
    }

    static async Task WatchParent(int parent, CancellationTokenSource stop)
    {
        try { using var process = Process.GetProcessById(parent); await process.WaitForExitAsync(); }
        catch (ArgumentException) { }
        stop.Cancel();
    }

    // Until the service has had nothing to hand out for a few checks (a new scope's files appear once it has
    // listed them). Files the service hands out are leased for 10 minutes, so a file skipped here returns later.
    internal static async Task Drain(IndexServiceClient service, CancellationToken token)
    {
        int idle = 0;
        while (idle < IdleChecks)
        {
            token.ThrowIfCancellationRequested();
            var batch = await service.ContentNextAsync(Batch, ContentExtractors.Version, token);
            if (batch.Count == 0) { idle++; await Task.Delay(Idle, token); continue; }
            idle = 0;
            foreach (var work in batch) await Extract(service, work, token);
        }
    }

    internal static ContentStamp? Stat(string path)
    {
        var info = new FileInfo(path);
        try { return info.Exists ? new ContentStamp(info.Length, info.LastWriteTimeUtc.ToFileTimeUtc()) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    // Reads one file and publishes it. A file deleted since the service listed it is left to the service's next
    // reconcile; a refusal for one file (its lease ended, its scope was removed) skips that file.
    internal static async Task Extract(IndexServiceClient service, ContentWork work, CancellationToken token)
    {
        if (Stat(work.Path) is not { } read) return;
        var age = DateTime.UtcNow - DateTime.FromFileTimeUtc(read.Modified);
        if (age < Settle)
        {
            await Task.Delay(Settle - age, token); // still being written: let it settle once
            if (Stat(work.Path) is not { } settled) return;
            read = settled;
        }
        int version = ContentExtractors.Version;
        string kind = ContentExtractors.Kind(work.Path);
        try
        {
            if (kind == null) { await Call(() => service.ContentFailAsync(work.Id, version, read, "Not a supported format.", true, token)); return; }
            long limit = kind == "text" ? ContentExtractors.TextLimit : ContentExtractors.DocumentLimit;
            if (read.Size > limit) { await Call(() => service.ContentFailAsync(work.Id, version, read, $"Larger than the {limit >> 20} MB limit for {kind} content.", true, token)); return; }
        }
        catch (IndexServiceRefusedException error) { Note($"refused {work.Path}: {error.Message}"); return; }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(FileTimeout);
        var pending = new List<(string Location, string Text)>();
        long pendingBytes = 0, total = 0; int chunks = 0;
        async Task Send(bool final)
        {
            var after = final ? Stat(work.Path) ?? read : read;
            await Call(() => service.ContentPutAsync(work.Id, version, read, after, pending, final, token));
            chunks += pending.Count; pending.Clear(); pendingBytes = 0;
            if (final) Note($"indexed {work.Path} chunks={chunks} bytes={total} {(after == read ? "current" : "stale")}");
        }
        async Task Add(string location, string text)
        {
            foreach (var piece in Split(text))
            {
                int bytes = Encoding.UTF8.GetByteCount(piece);
                total += bytes;
                if (total > FileTextBytes) throw new ContentUnsupportedException($"More than {FileTextBytes >> 20} MB of text.");
                pending.Add((location, piece)); pendingBytes += bytes;
                if (pendingBytes >= PutBytes) await Send(false);
            }
        }
        try
        {
            if (kind == "text")
            {
                // Read on a pool thread, sent in order: the reader yields chunks, the puts await the pipe.
                using var stream = new FileStream(work.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                using var chunksRead = ContentExtractors.ReadText(stream, read.Size, timeout.Token).GetEnumerator();
                while (await Task.Run(chunksRead.MoveNext, timeout.Token)) await Add(chunksRead.Current.Location, chunksRead.Current.Text);
            }
            else
            {
                await using var document = new Preview.OfficeDocument();
                if (kind == "PDF") await document.OpenCore(work.Path, "pdfcraft", timeout.Token); else await document.Open(work.Path, timeout.Token);
                Preview.OfficeWorker.TextCursor cursor = null;
                do
                {
                    var page = await document.ExtractText(cursor, timeout.Token, 48 * 1024);
                    if (page.Status is "unsupported" or "image-only" && chunks == 0 && pending.Count == 0 && page.Records.Length == 0)
                        throw new ContentUnsupportedException(page.Status == "image-only" ? "Only images, no text (no OCR)." : page.Coverage);
                    foreach (var record in page.Records) await Add(ContentExtractors.Compact(record.Location), record.Text);
                    cursor = page.Next;
                }
                while (cursor != null);
            }
            await Send(true);
        }
        catch (IndexServiceRefusedException error) { Note($"refused {work.Path}: {error.Message}"); } // this file's put
        catch (ServiceLostException) { throw; }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            bool unsupported = error is ContentUnsupportedException or Preview.OfficePolicyException;
            string reason = error is OperationCanceledException ? $"Extraction took longer than {FileTimeout.TotalMinutes:0} minutes." : error.Message;
            try { await Call(() => service.ContentFailAsync(work.Id, version, read, reason, unsupported, token)); }
            catch (IndexServiceRefusedException refused) { Note($"refused {work.Path}: {refused.Message}"); return; }
            Note($"failed {work.Path}: {reason}");
        }
    }

    // A service call: a refusal stays IndexServiceRefusedException (that file is skipped); a lost or unanswered
    // connection becomes ServiceLostException, which stops the worker. Pathweft starts it again when the service answers.
    static async Task Call(Func<Task> call)
    {
        try { await call(); }
        catch (InvalidOperationException error) when (error is not IndexServiceRefusedException and not ObjectDisposedException) { throw new ServiceLostException(error); }
    }
    sealed class ServiceLostException(Exception inner) : InvalidOperationException(inner.Message, inner);

    // Pieces of at most ChunkBytes UTF-8 bytes, never cutting a surrogate pair.
    internal static IEnumerable<string> Split(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= ChunkBytes) { yield return text; yield break; }
        int start = 0;
        while (start < text.Length)
        {
            int length = Math.Min(text.Length - start, ChunkBytes / 3);
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1])) length--;
            yield return text.Substring(start, length);
            start += length;
        }
    }

    static void Note(string line)
    {
        Trace.WriteLine("Content indexer: " + line);
#if DEBUG
        Observed?.Invoke(line);
#endif
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetPriorityClass(IntPtr process, uint priority);
}
