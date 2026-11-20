#if DEBUG
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using FileExplorer.Search;

namespace FileExplorer;

// CONTENT-01..06, Pathweft's side, against an in-process substitute for a protocol-2 index service on a private
// pipe: the toggle and its requests, the real worker process reading owned fixtures and pushing chunks, badges,
// content search locations, open-at-line, Settings rows, and a protocol-1 service hiding the feature.
// The installed service is never asked to change anything (no content_scope_add/remove, put or fail).
public partial class MainWindow
{
    sealed class FakeContentFile
    {
        internal long Id; internal string Path; internal ContentStamp Stamp;
        internal bool Leased; internal string State = "pending"; internal string Error; internal long Indexed;
        internal readonly List<JsonObject> Puts = new(); internal JsonObject Fail;
    }

    internal async Task VerifyContentIndex19(string root, Action<bool, string> check)
    {
        string inputs = Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        string docs = Path.Combine(root, "docs"), sub = Path.Combine(docs, "sub");
        Directory.CreateDirectory(sub);
        string notes = Path.Combine(docs, "notes.txt"), jsonl = Path.Combine(docs, "events.jsonl"), big = Path.Combine(docs, "big.log");
        string word = Path.Combine(docs, "report.docx"), image = Path.Combine(docs, "picture.png"), binary = Path.Combine(docs, "binary.txt"), refused = Path.Combine(docs, "refused.txt");
        File.WriteAllText(notes, "first line\nsecond line\nthe pathweftneedle is here\n" + string.Concat(Enumerable.Range(4, 36).Select(i => $"line {i}\n")) + "pathweftneedle again\n");
        File.WriteAllText(jsonl, string.Concat(Enumerable.Range(1, 20).Select(i => $"{{\"event\":{i},\"text\":\"entry {i} é 子\"}}\n")));
        var bigText = new StringBuilder();
        for (int i = 0; bigText.Length < 1_500_000; i++) bigText.Append($"{i:D7} log entry with ordinary words and an emoji 😀 to keep pieces honest\n");
        File.WriteAllText(big, bigText.ToString());
        File.Copy(Path.Combine(inputs, "document.docx"), word);
        File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);
        File.WriteAllBytes(binary, [0x41, 0x42, 0, 0x43, 0, 0, 0x44]);
        File.WriteAllText(refused, "refused contents\n");

        var gate = new object();
        var scopes = new List<string>();
        var files = new List<FakeContentFile>();
        int protocol = 1;
        long nextId = 1;
        JsonObject lastQuery = null;
        JsonObject Ok() => new() { ["ok"] = true };
        ContentStamp Read(JsonNode n) => new((long)n["size"], (long)n["modified"]);
        JsonObject Reply(JsonObject request)
        {
            lock (gate)
            {
                switch ((string)request["op"])
                {
                    case "status":
                        return new JsonObject { ["ok"] = true, ["status"] = new JsonObject { ["version"] = "0.2.0", ["protocol"] = protocol, ["stores"] = new JsonArray(new JsonObject { ["name"] = "D:", ["kind"] = "ntfs", ["state"] = "ready", ["items"] = 10 }) } };
                    case "content_scopes" when protocol >= 2:
                        return new JsonObject { ["ok"] = true, ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)new JsonObject { ["path"] = s, ["added"] = 1L, ["reconciled"] = 133700000000000000L }).ToArray()) };
                    case "content_scope_add" when protocol >= 2:
                        {
                            string path = (string)request["path"];
                            if (!scopes.Contains(path, StringComparer.OrdinalIgnoreCase))
                            {
                                scopes.Add(path);
                                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                                {
                                    var info = new FileInfo(file);
                                    files.Add(new FakeContentFile { Id = nextId++, Path = file, Stamp = new ContentStamp(info.Length, info.LastWriteTimeUtc.ToFileTimeUtc()) });
                                }
                            }
                            return Ok();
                        }
                    case "content_scope_remove" when protocol >= 2:
                        scopes.RemoveAll(s => s.Equals((string)request["path"], StringComparison.OrdinalIgnoreCase));
                        files.Clear();
                        return Ok();
                    case "content_next" when protocol >= 2:
                        {
                            var batch = files.Where(f => !f.Leased && f.State == "pending").Take((int)request["count"]).ToList();
                            foreach (var f in batch) f.Leased = true;
                            return new JsonObject { ["ok"] = true, ["files"] = new JsonArray(batch.Select(f => (JsonNode)new JsonObject { ["id"] = f.Id, ["path"] = f.Path, ["size"] = f.Stamp.Size, ["modified"] = f.Stamp.Modified }).ToArray()) };
                        }
                    case "content_put" when protocol >= 2:
                        {
                            var file = files.FirstOrDefault(f => f.Id == (long)request["id"]);
                            if (file == null) return new JsonObject { ["ok"] = false, ["error"] = "unknown content file" };
                            if (file.Path == refused) return new JsonObject { ["ok"] = false, ["error"] = "the lease ended" };
                            file.Puts.Add((JsonObject)request.DeepClone());
                            if ((bool)request["final"])
                            {
                                bool current = Read(request["read"]) == Read(request["after"]);
                                file.State = current ? "current" : "stale"; file.Indexed = 133800000000000000L;
                            }
                            return Ok();
                        }
                    case "content_fail" when protocol >= 2:
                        {
                            var file = files.FirstOrDefault(f => f.Id == (long)request["id"]);
                            if (file == null) return new JsonObject { ["ok"] = false, ["error"] = "unknown content file" };
                            file.Fail = (JsonObject)request.DeepClone();
                            file.State = (bool)request["unsupported"] ? "unsupported" : "failed"; file.Error = (string)request["error"];
                            return Ok();
                        }
                    case "content_state" when protocol >= 2:
                        {
                            string folder = (string)request["folder"];
                            return new JsonObject { ["ok"] = true, ["files"] = new JsonArray(files.Where(f => string.Equals(Path.GetDirectoryName(f.Path), folder, StringComparison.OrdinalIgnoreCase))
                                .Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["state"] = f.State, ["indexed"] = f.Indexed, ["error"] = f.Error }).ToArray()) };
                        }
                    case "content_status" when protocol >= 2:
                        {
                            long Count(string state) => files.Count(f => f.State == state);
                            return new JsonObject
                            {
                                ["ok"] = true,
                                ["content"] = new JsonObject
                                {
                                    ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)new JsonObject
                                    {
                                        ["path"] = s, ["files"] = files.Count, ["pending"] = Count("pending"), ["current"] = Count("current"), ["stale"] = Count("stale"),
                                        ["failed"] = Count("failed"), ["unsupported"] = Count("unsupported"), ["unavailable"] = 0, ["reconciled"] = 133700000000000000L,
                                    }).ToArray()),
                                    ["store_bytes"] = 3L << 20, ["waiting"] = files.Count(f => f.State == "pending" && !f.Leased),
                                },
                            };
                        }
                    case "query":
                        {
                            lastQuery = (JsonObject)request.DeepClone();
                            var hit = new JsonObject { ["path"] = notes, ["visible"] = true, ["folder"] = false, ["size"] = new FileInfo(notes).Length, ["modified"] = 133700000000000000L, ["attributes"] = 32 };
                            if (request["locations"] != null)
                                hit["locations"] = new JsonArray(new JsonObject { ["line"] = 3, ["column"] = 1, ["offset"] = 23 }, new JsonObject { ["line"] = 40, ["column"] = 1, ["offset"] = 330 });
                            return new JsonObject { ["ok"] = true, ["total"] = 1, ["hits"] = new JsonArray(hit) };
                        }
                    default:
                        return new JsonObject { ["ok"] = false, ["error"] = "bad request: unknown variant `" + (string)request["op"] + "`" };
                }
            }
        }

        using var fake = new FakeIndexService { Reply = Reply };
        ContentIndexing.ResetForVerification();
        IndexedSearch.ServiceOverride = new IndexServiceClient(fake.Name);
        static async Task<bool> Until(Func<bool> condition, int seconds)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition()) { if (DateTime.UtcNow > end) return false; await Task.Delay(100); }
            return true;
        }
        List<Entry> Entries() => Left.List.Items.Cast<Entry>().ToList();
        try
        {
            // Splitting stays within the service's chunk limit and loses nothing.
            string splitSource = string.Concat(Enumerable.Repeat("ab😀 ", 40_000));
            var pieces = ContentIndexer.Split(splitSource).ToList();
            check(pieces.Count > 1 && pieces.All(p => Encoding.UTF8.GetByteCount(p) <= ContentIndexer.ChunkBytes) && string.Concat(pieces) == splitSource && pieces.All(p => !char.IsHighSurrogate(p[^1])),
                $"CONTENT-02: text splits into {pieces.Count} pieces of at most 64 KiB, never inside a character");
            check(PaneView.ContentQuery("report content:needle") && PaneView.ContentQuery("content:\"a phrase\"") && !PaneView.ContentQuery("contents.txt") && !PaneView.ContentQuery("mycontent:x"),
                "CONTENT-05: only a content: term asks the service for match locations");

            SetSinglePane(true); SetActive(Left);
            await S1Verification.Navigate(Left, docs);

            // A protocol-1 service (the installed 0.1.0) has no content index: no button, no Settings section.
            await ContentIndexing.Check();
            UpdateCommandStates();
            check(!ContentIndexing.Available && IndexContentsCommand.Visibility == System.Windows.Visibility.Collapsed, "CONTENT-01: with a protocol-1 index service the Index contents button is hidden");
            var settings = new SettingsWindow(this);
            try { await settings.RefreshIndex(); check(settings.ContentIndexPanel.Visibility == System.Windows.Visibility.Collapsed, "CONTENT-03: with a protocol-1 service Settings shows no content section"); }
            finally { settings.Close(); }

            // Protocol 2: the button appears for a listed folder and indexes it.
            lock (gate) protocol = 2;
            await ContentIndexing.Check();
            UpdateCommandStates();
            check(ContentIndexing.Available && IndexContentsCommand.Visibility == System.Windows.Visibility.Visible && IndexContentsCommand.IsEnabled
                && (string)IndexContentsCommand.ToolTip == "Index contents of this folder and its subfolders" && IndexContentsCommand.Tag == null,
                "CONTENT-01: with a protocol-2 service the button is offered for the folder shown: " + IndexContentsCommand.ToolTip);
            while (fake.Requests.TryDequeue(out _)) { }
            IndexContentsCommand.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            bool added = await Until(() => { lock (gate) return scopes.Count == 1; }, 10);
            var addRequest = fake.Requests.FirstOrDefault(r => (string)r["op"] == "content_scope_add");
            check(added && (string)addRequest?["path"] == docs, "CONTENT-01: Index contents sends content_scope_add for the folder shown: " + addRequest?.ToJsonString());
            await Until(() => ContentIndexing.Covering(docs) != null && IndexContentsCommand.IsEnabled, 10);
            check(Status.Text.StartsWith("Indexing contents of " + docs, StringComparison.Ordinal) && (string)IndexContentsCommand.Tag == "on" && (string)IndexContentsCommand.ToolTip == "Stop indexing contents of this folder",
                "CONTENT-01: the button shows the folder as indexed and the status says what happens: " + Status.Text);

            // The worker process: started by Pathweft, below normal priority, reads every file and exits when nothing is left.
            var process = await Until(() => ContentIndexing.WorkerForVerification != null, 10) ? ContentIndexing.WorkerForVerification : null;
            string priority = "unknown";
            try { if (process is { HasExited: false }) priority = process.PriorityClass.ToString(); } catch (InvalidOperationException) { }
            check(process != null && priority is "BelowNormal" or "Idle" or "unknown", "CONTENT-04: Pathweft starts the content worker below normal priority: " + priority);
            bool exited = process != null && await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(4)).ContinueWith(t => t.IsCompletedSuccessfully);
            check(exited && process.ExitCode == 0, $"CONTENT-04: the worker drains the queue and exits by itself (exit {(exited ? process.ExitCode : -1)})");

            FakeContentFile File_(string path) { lock (gate) return files.Single(f => f.Path == path); }
            IEnumerable<JsonObject> Chunks(FakeContentFile f) => f.Puts.SelectMany(p => p["chunks"].AsArray().Select(c => c.AsObject()));
            string Text(FakeContentFile f) => string.Concat(Chunks(f).Select(c => (string)c["text"]));
            bool Stamped(FakeContentFile f) => f.Puts.All(p => Read(p["read"]) == f.Stamp) && f.Puts.Count > 0 && (bool)f.Puts[^1]["final"] && f.Puts.Take(f.Puts.Count - 1).All(p => !(bool)p["final"]) && Read(f.Puts[^1]["after"]) == f.Stamp;

            var noteFile = File_(notes);
            var needle = Chunks(noteFile).FirstOrDefault(c => ((string)c["text"]).Contains("pathweftneedle is here"));
            check(noteFile.State == "current" && Stamped(noteFile) && needle != null && (int)needle["location"]["line"] == 1 && Text(noteFile).Contains("pathweftneedle again")
                && Chunks(noteFile).All(c => c["location"]["line"] != null && c["location"]["offset"] != null) && Chunks(noteFile).Any(c => (int)c["location"]["line"] > 1),
                "CONTENT-02: a text file is put in located chunks (line, column, offset) with its read stamp, final last: " + string.Join(" | ", Chunks(noteFile).Select(c => c["location"].ToJsonString())));
            var eventsFile = File_(jsonl);
            check(eventsFile.State == "current" && Stamped(eventsFile) && Text(eventsFile).Contains("entry 20 é 子"), "CONTENT-02: JSONL lines are indexed as text, Unicode intact");
            var bigFile = File_(big);
            check(bigFile.State == "current" && Stamped(bigFile) && bigFile.Puts.Count >= 3 && Chunks(bigFile).All(c => Encoding.UTF8.GetByteCount((string)c["text"]) <= ContentIndexer.ChunkBytes)
                && Text(bigFile).Replace("\n", "") == bigText.ToString().Replace("\n", ""),
                $"CONTENT-02: a 1.5 MB log goes in {bigFile.Puts.Count} puts of whole chunks within the 64 KiB limit, nothing lost");
            var wordFile = File_(word);
            check(wordFile.State == "current" && Stamped(wordFile) && Chunks(wordFile).Any() && Chunks(wordFile).All(c => (int?)c["location"]["engine"] == 1)
                && ContentExtractors.Describe(Chunks(wordFile).First()["location"].ToJsonString()).StartsWith("paragraph ", StringComparison.Ordinal),
                "CONTENT-02: a Word document is indexed through its Craft core with engine-marked locations: " + string.Join(" | ", Chunks(wordFile).Take(3).Select(c => c["location"].ToJsonString())));
            var imageFile = File_(image); var binaryFile = File_(binary); var refusedFile = File_(refused);
            check(imageFile.State == "unsupported" && (bool)imageFile.Fail["unsupported"] && imageFile.Puts.Count == 0 && Read(imageFile.Fail["read"]) == imageFile.Stamp,
                "CONTENT-02: an unsupported format is reported with content_fail unsupported and its stamp: " + imageFile.Error);
            check(binaryFile.State == "unsupported" && binaryFile.Error?.Contains("Binary") == true, "CONTENT-02: binary data in a .txt is reported as not text: " + binaryFile.Error);
            check(refusedFile.State == "pending" && refusedFile.Fail == null, "CONTENT-04: a refused put skips that file without failing it; the rest are indexed");

            // Badges in the pane, from content_state.
            await S1Verification.Navigate(Left, docs);
            bool badged = await Until(() => Entries().Any(e => e.Path == notes && e.HasContentBadge), 10);
            var rows = Entries();
            var noteEntry = rows.FirstOrDefault(e => e.Path == notes); var imageEntry = rows.FirstOrDefault(e => e.Path == image); var subEntry = rows.FirstOrDefault(e => e.Path == sub);
            check(badged && noteEntry.Content.State == ContentState.Current && noteEntry.Tip.Contains("Contents indexed ") && imageEntry?.HasContentBadge == true && imageEntry.Tip.Contains("Contents not indexed: ")
                && subEntry?.HasContentBadge == false,
                "CONTENT-01: files show their content state as a badge and in the tooltip; folders show none");

            // A subfolder is covered by the scope: the button is shown on but disabled.
            await S1Verification.Navigate(Left, sub);
            UpdateCommandStates();
            check(!IndexContentsCommand.IsEnabled && (string)IndexContentsCommand.ToolTip == "Contents are indexed with " + docs && (string)IndexContentsCommand.Tag == "on",
                "CONTENT-01: inside an indexed folder the button says which folder covers it: " + IndexContentsCommand.ToolTip);

            // Content search: locations requested and shown; a text hit opens at its lines.
            await S1Verification.Navigate(Left, root);
            Left.VerificationSearchScope = 0;
            ((TextBox)Left.FindName("FilterBox")).Text = "content:pathweftneedle";
            Left.Search("content:pathweftneedle");
            await Left.SearchCompletion;
            var results = Left.IndexedResultsForVerification;
            check((int?)lastQuery?["locations"] == PaneView.MatchLocations && results.Count == 1 && results[0].Matches?.Count == 2 && results[0].Where.Contains("line 3, line 40") && results[0].Tip.Contains("Content matches: line 3, line 40"),
                "CONTENT-05: a content search asks for match locations and each result shows them: " + (results.Count > 0 ? results[0].Where : "no result"));
            var menu = ItemMenu(Left, new() { results[0] });
            var headers = new List<string>();
            void Collect(ItemsControl items) { foreach (var item in items.Items.OfType<MenuItem>()) { headers.Add(item.Header as string); Collect(item); } }
            Collect(menu);
            check(headers.Contains("Open at line 3") && headers.Contains("Open at line 40") && headers.IndexOf("Open at line 3") > headers.IndexOf("Open file location"),
                "CONTENT-05: a text match offers Open at line for each matching line, after Open file location");
            ((TextBox)Left.FindName("FilterBox")).Text = "plainname";
            Left.Search("plainname");
            await Left.SearchCompletion;
            check(lastQuery?["locations"] == null, "CONTENT-05: a name-only search does not ask for locations");
            await S1Verification.Navigate(Left, docs);

            // Settings: the scope, its counts and the store size.
            settings = new SettingsWindow(this);
            try
            {
                await settings.RefreshIndex();
                var scopeRows = settings.ContentScopes.Items.Cast<SettingsWindow.ContentScopeRow>().ToList();
                check(settings.ContentIndexPanel.Visibility == System.Windows.Visibility.Visible && settings.ContentIndexStatus.Text.StartsWith("4 files indexed · 3", StringComparison.Ordinal)
                    && scopeRows.Count == 1 && scopeRows[0].Path == docs && scopeRows[0].Detail.StartsWith("4 of 7 files indexed · 1 waiting", StringComparison.Ordinal) && scopeRows[0].Detail.Contains("2 not indexable"),
                    "CONTENT-03: Settings lists the indexed folder with its counts and the index size: " + settings.ContentIndexStatus.Text + " | " + string.Join(" | ", scopeRows.Select(r => r.Path + ": " + r.Detail)));
            }
            finally { settings.Close(); }

            // Stop: removes the scope; the files stay.
            UpdateCommandStates();
            while (fake.Requests.TryDequeue(out _)) { }
            IndexContentsCommand.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            bool removed = await Until(() => { lock (gate) return scopes.Count == 0; }, 10);
            var removeRequest = fake.Requests.FirstOrDefault(r => (string)r["op"] == "content_scope_remove");
            await Until(() => ContentIndexing.Covering(docs) == null && IndexContentsCommand.IsEnabled, 10);
            check(removed && (string)removeRequest?["path"] == docs && File.Exists(notes) && File.Exists(word) && IndexContentsCommand.Tag == null && Status.Text.StartsWith("Stopped indexing contents of " + docs, StringComparison.Ordinal),
                "CONTENT-06: Stop indexing removes the scope and leaves the files: " + Status.Text);
            bool cleared = await Until(() => Entries().All(e => !e.HasContentBadge), 10);
            check(cleared, "CONTENT-06: badges go away when the folder is no longer indexed");
        }
        finally
        {
            ContentIndexing.ResetForVerification();
            IndexedSearch.ServiceOverride = null;
        }
    }
}
#endif
