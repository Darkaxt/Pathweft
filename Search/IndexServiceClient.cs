using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace FileExplorer.Search;

// SEARCH-06: client of the Pathweft index service (docs/index-service/PROTOCOL.md).
// Each message is a 4-byte little-endian length and UTF-8 JSON. Connections are
// pooled; one is used by one request at a time. A pipe that does not connect
// within ConnectTimeout means "absent" (null result), remembered for AbsentFor;
// searches then report it and folder sizes are measured (SEARCH-09).
internal sealed class IndexServiceClient(string pipeName)
{
    internal const string Engine = "Pathweft index", NotRunning = "The index service is not running.";
    internal static IndexServiceClient Shared { get; } = new("PathweftIndex");
    internal static TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(250), ReplyTimeout = TimeSpan.FromSeconds(30), AbsentFor = TimeSpan.FromSeconds(10);
    readonly ConcurrentBag<NamedPipeClientStream> idle = new();
    long absentUntil;
    internal string PipeName => pipeName;
    // Why the last connection attempt failed; shown by Settings.
    internal string LastConnectError { get; private set; }

    internal void ForgetAbsence() => Interlocked.Exchange(ref absentUntil, 0);

    // Null when the service is absent. A service refusal throws InvalidOperationException
    // with its message, and so does a lost or unanswered connection.
    internal async Task<JsonDocument> TryRequestAsync(object request, CancellationToken token)
    {
        if (Environment.TickCount64 < Interlocked.Read(ref absentUntil)) return null;
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(request);
        for (int attempt = 0; ; attempt++)
        {
            bool pooled = idle.TryTake(out var pipe);
            if (!pooled)
            {
                pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
                try { await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, token).ConfigureAwait(false); LastConnectError = null; }
                catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    pipe.Dispose();
                    LastConnectError = ex is UnauthorizedAccessException ? "The index service does not admit this user." : ex is TimeoutException ? NotRunning : ex.Message;
                    Interlocked.Exchange(ref absentUntil, Environment.TickCount64 + (long)AbsentFor.TotalMilliseconds);
                    return null;
                }
                catch { pipe.Dispose(); throw; }
            }
            using var timeout = new CancellationTokenSource(ReplyTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            bool keep = false;
            try
            {
                var frame = new byte[4 + body.Length];
                BitConverter.TryWriteBytes(frame.AsSpan(0, 4), body.Length);
                body.CopyTo(frame, 4);
                await pipe.WriteAsync(frame, linked.Token).ConfigureAwait(false);
                await pipe.FlushAsync(linked.Token).ConfigureAwait(false);
                var length = new byte[4];
                await pipe.ReadExactlyAsync(length, linked.Token).ConfigureAwait(false);
                int size = BitConverter.ToInt32(length);
                if (size <= 0 || size > 64 << 20) throw new IOException("The index service sent an invalid reply.");
                var reply = new byte[size];
                await pipe.ReadExactlyAsync(reply, linked.Token).ConfigureAwait(false);
                var document = JsonDocument.Parse(reply);
                keep = true;
                var root = document.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    string error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : "The index service refused the request.";
                    document.Dispose();
                    throw new InvalidOperationException(error);
                }
                return document;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new InvalidOperationException($"The Pathweft index didn't answer within {ReplyTimeout.TotalSeconds:0} seconds. Search again to reconnect.");
            }
            // A pooled connection may have been closed by a service restart: one fresh retry.
            catch (Exception ex) when (pooled && attempt == 0 && ex is IOException or EndOfStreamException) { }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or JsonException)
            {
                throw new InvalidOperationException("The connection to the Pathweft index was lost. Search again to reconnect.", ex);
            }
            finally
            {
                if (keep && pipe.IsConnected) idle.Add(pipe); else pipe.Dispose();
            }
        }
    }

    internal async Task<IndexedPage> QueryAsync(string expression, uint offset, uint count, string sort, bool descending, SearchSwitches switches, bool visibleOnly, CancellationToken token)
    {
        using var reply = await TryRequestAsync(new
        {
            op = "query", query = expression, offset, count, sort, descending,
            match_path = switches?.MatchPath ?? false, match_case = switches?.MatchCase ?? false,
            whole_word = switches?.WholeWord ?? false, regex = switches?.Regex ?? false, visible_only = visibleOnly,
        }, token).ConfigureAwait(false);
        if (reply == null) return null;
        var root = reply.RootElement;
        var hits = new List<IndexedHit>();
        if (root.TryGetProperty("hits", out var array))
            foreach (var hit in array.EnumerateArray())
            {
                long size = hit.GetProperty("size").GetInt64();
                hits.Add(new(hit.GetProperty("path").GetString(), hit.GetProperty("folder").GetBoolean(), size < 0 ? -2 : size,
                    Time(hit, "modified"), Time(hit, "created"), hit.TryGetProperty("attributes", out var a) ? a.GetUInt32() : null,
                    !hit.TryGetProperty("visible", out var v) || v.GetBoolean()));
            }
        ulong total = root.TryGetProperty("total", out var t) ? t.GetUInt64() : (ulong)hits.Count;
        return new(hits, (uint)Math.Min(total, uint.MaxValue), Engine, true, Updating(root));
    }

    static DateTime Time(JsonElement hit, string name)
        => hit.TryGetProperty(name, out var value) && value.GetInt64() is > 0 and var filetime ? DateTime.FromFileTime(filetime) : default;
    static bool Updating(JsonElement root) => root.TryGetProperty("updating", out var u) && u.GetBoolean();

    // Exact indexed folders only; an absent size means unknown, never 0.
    internal async Task<Dictionary<string, long>> FolderSizesAsync(IEnumerable<string> paths, CancellationToken token)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1024))
        {
            using var reply = await TryRequestAsync(new { op = "folder_sizes", paths = batch }, token).ConfigureAwait(false);
            if (reply == null) return null;
            foreach (var item in reply.RootElement.GetProperty("sizes").EnumerateArray())
                if (item.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
                    result[Path.TrimEndingDirectorySeparator(item.GetProperty("path").GetString())] = size.GetInt64();
        }
        return result;
    }

    internal async Task<IndexServiceStatus> StatusAsync(CancellationToken token)
    {
        ForgetAbsence();
        using var reply = await TryRequestAsync(new { op = "status" }, token).ConfigureAwait(false);
        return reply == null ? null : IndexServiceStatus.Parse(reply.RootElement.GetProperty("status"));
    }

    internal Task FolderAddAsync(string path, CancellationToken token) => Command(new { op = "folder_add", path }, token);
    internal Task FolderRemoveAsync(string path, CancellationToken token) => Command(new { op = "folder_remove", path }, token);
    internal Task RescanAsync(string target, CancellationToken token) => Command(new { op = "rescan", target }, token);
    async Task Command(object request, CancellationToken token)
    {
        ForgetAbsence();
        using var reply = await TryRequestAsync(request, token).ConfigureAwait(false);
        if (reply == null) throw new InvalidOperationException(LastConnectError ?? NotRunning);
    }
}

internal sealed record IndexStoreStatus(string Name, string Kind, string State, ulong Items, ulong Folders, ulong MemoryBytes, string Error, string Owner);
internal sealed record IndexServiceStatus(string Version, ulong WorkingSet, ulong PrivateBytes, ulong UptimeSeconds, IReadOnlyList<IndexStoreStatus> Stores)
{
    internal static IndexServiceStatus Parse(JsonElement status)
    {
        static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static ulong Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out ulong n) ? n : 0;
        var stores = status.TryGetProperty("stores", out var array)
            ? array.EnumerateArray().Select(s => new IndexStoreStatus(Text(s, "name"), Text(s, "kind"), Text(s, "state"), Number(s, "items"), Number(s, "folders"), Number(s, "memory_bytes"), Text(s, "error"), Text(s, "owner"))).ToList()
            : new List<IndexStoreStatus>();
        return new(Text(status, "version"), Number(status, "working_set"), Number(status, "private_bytes"), Number(status, "uptime_s"), stores);
    }
}
