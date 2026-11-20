#if DEBUG
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using FileExplorer.Search;

namespace FileExplorer;

// SEARCH-06..08: the engine router, the pipe client and the Settings section,
// against an in-process substitute service on a private pipe; then read-only
// checks against the installed service when it is running.
public partial class MainWindow
{
    sealed class FakeIndexService : IDisposable
    {
        internal readonly string Name = "Pathweft-IndexVerify-" + Guid.NewGuid().ToString("N");
        internal readonly ConcurrentQueue<JsonObject> Requests = new();
        // Returns the reply object, or null to close the connection without answering;
        // "hold" waits until the connection is closed by the client.
        internal Func<JsonObject, JsonObject> Reply = _ => new JsonObject { ["ok"] = true };
        internal bool Hold;
        readonly CancellationTokenSource stop = new();
        internal FakeIndexService() { for (int i = 0; i < 4; i++) _ = Serve(); }
        async Task Serve()
        {
            while (!stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try
                {
                    await pipe.WaitForConnectionAsync(stop.Token);
                    while (true)
                    {
                        var length = new byte[4];
                        await pipe.ReadExactlyAsync(length, stop.Token);
                        var body = new byte[BitConverter.ToInt32(length)];
                        await pipe.ReadExactlyAsync(body, stop.Token);
                        var request = JsonNode.Parse(body).AsObject();
                        Requests.Enqueue(request);
                        if (Hold) { await Task.Delay(Timeout.Infinite, stop.Token).WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { }); break; }
                        var reply = Reply(request);
                        if (reply == null) break;
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(reply);
                        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), stop.Token);
                        await pipe.WriteAsync(bytes, stop.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or EndOfStreamException or ObjectDisposedException) { }
            }
        }
        public void Dispose() => stop.Cancel();
    }

    internal async Task VerifyIndexService54(string root, Action<bool, string> check)
    {
        var connect = IndexServiceClient.ConnectTimeout; var reply = IndexServiceClient.ReplyTimeout; var absent = IndexServiceClient.AbsentFor;
        using var fake = new FakeIndexService();
        try
        {
            static JsonObject Hit(string path, bool visible, bool folder = false) => new() { ["path"] = path, ["visible"] = visible, ["folder"] = folder, ["size"] = folder ? -1 : 2048, ["modified"] = 133700000000000000L, ["created"] = 133600000000000000L, ["attributes"] = 32 };
            var visiblePath = Path.Combine(root, "visible.txt"); var protectedPath = Path.Combine(root, "protected.txt");
            fake.Reply = request => (string)request["op"] switch
            {
                "query" when ((string)request["query"]).Contains("refuse") => new JsonObject { ["ok"] = false, ["error"] = "unsupported search function dm:" },
                "query" => new JsonObject { ["ok"] = true, ["updating"] = true, ["updating_stores"] = new JsonArray("H:\\"), ["total"] = 2, ["hits"] = new JsonArray(Hit(visiblePath, true), Hit(protectedPath, false)) },
                "folder_sizes" => new JsonObject { ["ok"] = true, ["sizes"] = new JsonArray(new JsonObject { ["path"] = root, ["size"] = 4096 }, new JsonObject { ["path"] = Path.Combine(root, "unindexed") }) },
                "status" => new JsonObject
                {
                    ["ok"] = true,
                    ["status"] = new JsonObject
                    {
                        ["version"] = "0.1.0", ["protocol"] = 1, ["working_set"] = 700_000_000L, ["private_bytes"] = 677_000_000L, ["uptime_s"] = 60,
                        ["stores"] = new JsonArray(
                            new JsonObject { ["name"] = "C:", ["kind"] = "ntfs", ["state"] = "ready", ["items"] = 3_000_000, ["folders"] = 400_000 },
                            new JsonObject { ["name"] = "H:\\", ["kind"] = "folder", ["state"] = "scanning", ["items"] = 0, ["folders"] = 0 },
                            new JsonObject { ["name"] = "\\\\server\\share", ["kind"] = "folder", ["state"] = "error", ["items"] = 10, ["error"] = "The network path was not found." }),
                    },
                },
                _ => new JsonObject { ["ok"] = true },
            };
            IndexedSearch.ServiceOverride = new IndexServiceClient(fake.Name);
            // The content coordinator checks the service's status once a minute; those requests are not the ones under test.
            JsonObject Last() { JsonObject last = null; while (fake.Requests.TryDequeue(out var r)) if ((string)r["op"] != "status") last = r; return last; }

            // Router: pane query goes to the service with visible_only and column mapping.
            var page = await IndexedSearch.QueryAsync("visible", 0, 50, "Modified", true, CancellationToken.None);
            var sent = Last();
            check(page.Engine == IndexServiceClient.Engine && page.Updating && page.Total == 2 && page.Hits.Count == 2 && page.Hits[0].Visible && !page.Hits[1].Visible && page.Hits[0].Size == 2048 && page.Hits[0].Modified == DateTime.FromFileTime(133700000000000000L),
                "SEARCH-06: a pane query is answered by the index service with engine, updating, visibility, size and time");
            check((string)sent["op"] == "query" && (bool)sent["visible_only"] && (string)sent["sort"] == "modified" && (bool)sent["descending"] && (uint)sent["count"] == 50,
                "SEARCH-06: pane queries ask for visible_only and map Modified to modified, descending");
            await IndexedSearch.QueryAsync("visible", 0, 1, "Type", false, CancellationToken.None);
            check((string)Last()["sort"] == "extension", "SEARCH-06: the Type column sorts by extension");
            // MCP search: every match with switches and no visible_only.
            page = await IndexedSearch.SearchAsync("visible", 5, 10, "path", false, new SearchSwitches(true, true, false, true), CancellationToken.None);
            sent = Last();
            check(page.Hits.Count == 2 && !(bool)sent["visible_only"] && (bool)sent["match_path"] && (bool)sent["match_case"] && !(bool)sent["whole_word"] && (bool)sent["regex"] && (uint)sent["offset"] == 5 && (string)sent["sort"] == "path",
                "SEARCH-06: the MCP search sends the switches, offset and sort and keeps every match");
            // Refusal is shown as is, never retried.
            string refusal = null;
            try { await IndexedSearch.QueryAsync("refuse", 0, 10, "Name", false, CancellationToken.None); } catch (InvalidOperationException ex) { refusal = ex.Message; }
            check(refusal == "unsupported search function dm:", "SEARCH-06: a service refusal is reported as is: " + refusal);
            // Folder sizes: unknown stays absent; the source names the engine.
            var sizes = await IndexedSearch.FolderSizesAsync([root + "\\", Path.Combine(root, "unindexed")], CancellationToken.None);
            sent = Last();
            check(sizes.Count == 1 && sizes.TryGetValue(root, out long size) && size == 4096
                && sent["paths"].AsArray().Select(p => (string)p).SequenceEqual([root, Path.Combine(root, "unindexed")]),
                "SEARCH-06: folder sizes come from the service, unknown sizes stay absent and paths are normalized");
            // A reply missing its members is an ordinary index error (callers then measure), never an unhandled exception.
            var normal = fake.Reply; fake.Reply = _ => new JsonObject { ["ok"] = true };
            string broken = null;
            try { await IndexedSearch.FolderSizesAsync([root], CancellationToken.None); } catch (InvalidOperationException ex) { broken = ex.Message; }
            fake.Reply = normal;
            check(broken == "The index service sent an invalid reply.", "SEARCH-09: a malformed folder-size reply is reported as an index error, not a crash: " + broken);

            // Lost connection: the request fails with a message, the next one reconnects.
            fake.Reply = request => null;
            string lost = null;
            try { await IndexedSearch.QueryAsync("visible", 0, 1, "Name", false, CancellationToken.None); } catch (InvalidOperationException ex) { lost = ex.Message; }
            check(lost?.Contains("connection to the Pathweft index was lost") == true, "SEARCH-06: a lost connection ends that request with a message: " + lost);
            fake.Reply = request => new JsonObject { ["ok"] = true, ["total"] = 0, ["hits"] = new JsonArray() };
            page = await IndexedSearch.QueryAsync("visible", 0, 1, "Name", false, CancellationToken.None);
            check(page.Engine == IndexServiceClient.Engine && page.Total == 0 && !page.Updating, "SEARCH-06: the next request reconnects");
            // Unanswered request: bounded by the reply timeout.
            IndexServiceClient.ReplyTimeout = TimeSpan.FromSeconds(1); fake.Hold = true;
            string unanswered = null; var watch = Stopwatch.StartNew();
            try { await IndexedSearch.QueryAsync("visible", 0, 1, "Name", false, CancellationToken.None); } catch (InvalidOperationException ex) { unanswered = ex.Message; }
            check(unanswered?.Contains("didn't answer within 1 seconds") == true && watch.Elapsed < TimeSpan.FromSeconds(5), $"SEARCH-06: an unanswered request ends after the reply timeout ({watch.ElapsedMilliseconds} ms): {unanswered}");
            fake.Hold = false; IndexServiceClient.ReplyTimeout = reply;

            // Absent service (SEARCH-09): null from the client, remembered; search says so and folder sizes are left to measurement.
            var missing = new IndexServiceClient("Pathweft-IndexVerify-absent-" + Guid.NewGuid().ToString("N"));
            watch.Restart();
            var first = await missing.QueryAsync("x", 0, 1, "name", false, null, true, CancellationToken.None);
            long firstMs = watch.ElapsedMilliseconds; watch.Restart();
            var second = await missing.QueryAsync("x", 0, 1, "name", false, null, true, CancellationToken.None);
            check(first == null && second == null && firstMs < 2000 && watch.ElapsedMilliseconds < 50 && missing.LastConnectError != null,
                $"SEARCH-06: an absent service returns no answer within the connect bound ({firstMs} ms) and is remembered ({watch.ElapsedMilliseconds} ms)");
            IndexedSearch.ServiceOverride = missing;
            string absentMessage = null;
            try { await IndexedSearch.QueryAsync("pathweft-index-verification-unlikely-name", 0, 1, "Name", false, CancellationToken.None); }
            catch (InvalidOperationException ex) { absentMessage = ex.Message; }
            check(absentMessage == IndexedSearch.Absent, "SEARCH-09: without the service a search says the index service is not running: " + absentMessage);
            string mcpAbsent = null;
            try { await IndexedSearch.SearchAsync("x", 0, 1, "name", false, null, CancellationToken.None); } catch (InvalidOperationException ex) { mcpAbsent = ex.Message; }
            var absentSizes = await IndexedSearch.FolderSizesAsync([root], CancellationToken.None);
            check(mcpAbsent == IndexedSearch.Absent && absentSizes.Count == 0, "SEARCH-09: the MCP search reports the absent service and folder sizes are left to measurement");
            IndexedSearch.ServiceOverride = new IndexServiceClient(fake.Name);

            // Pane search: hidden protected hit, engine status and updating hint.
            fake.Reply = request => (string)request["op"] == "query"
                ? new JsonObject { ["ok"] = true, ["updating"] = true, ["total"] = 2, ["hits"] = new JsonArray(Hit(visiblePath, true), Hit(protectedPath, false)) }
                : new JsonObject { ["ok"] = true, ["sizes"] = new JsonArray() };
            File.WriteAllText(visiblePath, "v"); File.WriteAllText(protectedPath, "p");
            SetSinglePane(true); SetActive(Left);
            await S1Verification.Navigate(Left, root);
            Left.VerificationSearchScope = 0;
            ((TextBox)Left.FindName("FilterBox")).Text = "visible";
            Left.Search("visible");
            await Left.SearchCompletion;
            var results = Left.IndexedResultsForVerification;
            check(results.Count == 1 && results[0].Path == visiblePath, "SEARCH-06: the pane lists only hits the user can open: " + string.Join(";", results.Select(r => r.Path)));
            check(Left.SearchCoverage.Text.StartsWith(IndexServiceClient.Engine + " · updating", StringComparison.Ordinal), "SEARCH-06: the search status names the index and says it is updating: " + Left.SearchCoverage.Text);
            check(Left.Footer.Text.Contains("2 indexed matches checked · index updating"), "SEARCH-06: the result count carries the updating hint: " + Left.Footer.Text);
            await S1Verification.Navigate(Left, root);

            // Settings section against the substitute.
            fake.Reply = request => (string)request["op"] == "status"
                ? new JsonObject
                {
                    ["ok"] = true,
                    ["status"] = new JsonObject
                    {
                        ["version"] = "0.1.0", ["private_bytes"] = 677_000_000L, ["working_set"] = 700_000_000L,
                        ["stores"] = new JsonArray(
                            new JsonObject { ["name"] = "C:", ["kind"] = "ntfs", ["state"] = "ready", ["items"] = 3_000_000 },
                            new JsonObject { ["name"] = "H:\\", ["kind"] = "folder", ["state"] = "scanning", ["items"] = 0 },
                            new JsonObject { ["name"] = "\\\\server\\share", ["kind"] = "folder", ["state"] = "error", ["items"] = 10, ["error"] = "The network path was not found." }),
                    },
                }
                : new JsonObject { ["ok"] = true };
            var settings = new SettingsWindow(this);
            try
            {
                await settings.RefreshIndex();
                var rows = settings.IndexStores.Items.Cast<SettingsWindow.IndexStoreRow>().ToList();
                check(settings.IndexStatus.Text.StartsWith("Running · version 0.1.0 · 3,000,010 entries", StringComparison.Ordinal) && settings.IndexStatus.Text.Contains("updating H:\\"),
                    "SEARCH-07: the status line shows version, entries, memory and updating stores: " + settings.IndexStatus.Text);
                check(rows.Count == 3 && rows[0].Title == "C:\\" && rows[0].State == "Ready" && rows[0].RemoveVisible != System.Windows.Visibility.Visible
                    && rows[1].State == "Scanning" && rows[1].RescanVisible != System.Windows.Visibility.Visible && rows[1].RemoveVisible == System.Windows.Visibility.Visible
                    && rows[2].State == "Error" && rows[2].Detail.Contains("network path was not found"),
                    "SEARCH-07: store rows show drives first, states, errors, Rescan only when idle and Remove for folder roots: " + string.Join(" | ", rows.Select(r => $"{r.Title} {r.State} {r.Detail}")));
                check(settings.IndexInstall.Visibility != System.Windows.Visibility.Visible && settings.IndexUninstall.Visibility != System.Windows.Visibility.Visible,
                    "SEARCH-07: install and remove are not offered outside the Program Files installation");
            }
            finally { settings.Close(); }
            fake.Requests.Clear();
            await IndexedSearch.Service.FolderAddAsync("H:\\", CancellationToken.None);
            await IndexedSearch.Service.FolderRemoveAsync("\\\\server\\share", CancellationToken.None);
            await IndexedSearch.Service.RescanAsync("C:", CancellationToken.None);
            var commands = fake.Requests.Where(r => (string)r["op"] != "status").ToArray();
            check(commands.Length == 3 && (string)commands[0]["op"] == "folder_add" && (string)commands[0]["path"] == "H:\\" && (string)commands[1]["op"] == "folder_remove" && (string)commands[2]["op"] == "rescan" && (string)commands[2]["target"] == "C:",
                "SEARCH-07: folder-root add, remove and rescan send their pipe commands");
            var install = IndexServiceSetup.Command(true); var remove = IndexServiceSetup.Command(false);
            check(install.Arguments == "install" && remove.Arguments == "uninstall" && install.FileName.EndsWith(@"\services\file_index\pathweft-indexd.exe", StringComparison.OrdinalIgnoreCase) && install.CreateNoWindow && !install.UseShellExecute,
                "SEARCH-07: the elevated commands run the shipped service binary hidden with install/uninstall");
            string refused = null;
            try { IndexServiceSetup.Run(true); } catch (InvalidOperationException ex) { refused = ex.Message; }
            check(refused?.Contains("Program Files installation") == true, "SEARCH-07: a non-Program Files copy refuses to install the service");
        }
        finally
        {
            IndexedSearch.ServiceOverride = null;
            IndexServiceClient.ConnectTimeout = connect; IndexServiceClient.ReplyTimeout = reply; IndexServiceClient.AbsentFor = absent;
        }

        // The installed service, read-only, when it is running on this PC.
        var live = new IndexServiceClient("PathweftIndex");
        IndexServiceStatus status = null;
        try { status = await live.StatusAsync(CancellationToken.None); } catch (InvalidOperationException) { }
        if (status == null) { check(true, "SEARCH-06: the installed service is not running here; live checks skipped (" + live.LastConnectError + ")"); return; }
        IndexedSearch.ServiceOverride = live;
        try
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            check(status.Stores.Any(s => s.Kind == "ntfs" && s.State == "ready" && s.Items > 0), "SEARCH-06: the installed service reports a ready NTFS store: " + string.Join(", ", status.Stores.Select(s => $"{s.Name} {s.State} {s.Items}")));
            var hit = await IndexedSearch.QueryAsync(IndexedSearch.FolderExpression([windows]), 0, 5, "Name", false, CancellationToken.None);
            check(hit.Engine == IndexServiceClient.Engine && hit.Hits.Any(h => h.IsFolder && string.Equals(h.Path, windows, StringComparison.OrdinalIgnoreCase)), "SEARCH-06: the installed service finds the Windows folder by exact path");
            var liveSizes = await IndexedSearch.FolderSizesAsync([windows], CancellationToken.None);
            check(liveSizes.TryGetValue(windows, out long windowsBytes) && windowsBytes > 1L << 30, $"SEARCH-06: the installed service sizes the Windows folder ({windowsBytes:N0} bytes)");
            var fixture = await IndexedSearch.QueryAsync("visible.txt " + "path:\"" + root + "\\\"", 0, 5, "Name", false, CancellationToken.None);
            check(fixture.Hits.Any(h => string.Equals(h.Path, Path.Combine(root, "visible.txt"), StringComparison.OrdinalIgnoreCase)) || fixture.Updating,
                "SEARCH-06: a file created by this run is found through the USN journal: " + string.Join(";", fixture.Hits.Select(h => h.Path)));
        }
        finally { IndexedSearch.ServiceOverride = null; }
    }
}
#endif
