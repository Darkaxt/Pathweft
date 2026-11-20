#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using FileExplorer.Search;

namespace FileExplorer;

// CONTENT-01..06 against a real protocol-2 index service: a private console instance on its own pipe
// (PATHWEFT_CONTENT_PIPE), never the installed PathweftIndex. Toggle -> worker -> store -> content: query ->
// locations -> open at line, a change re-read, badges, Settings, stop and purge, with measured resources.
public partial class MainWindow
{
    // A one-page PDF with real text (Helvetica), built here so the fixture is owned.
    static void WritePdf(string path, string text)
    {
        string content = $"BT /F1 18 Tf 72 700 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(pdf.ToString()));
    }

    internal async Task VerifyContentLive19(string root, Action<bool, string> check)
    {
        string pipe = Environment.GetEnvironmentVariable("PATHWEFT_CONTENT_PIPE");
        if (string.IsNullOrEmpty(pipe) || pipe.Equals("PathweftIndex", StringComparison.OrdinalIgnoreCase))
        { check(false, "CONTENT-06: the live scenario needs a private console instance's pipe, never the installed service"); return; }
        string inputs = Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        string live = Path.Combine(root, "live");
        Directory.CreateDirectory(live);
        string notes = Path.Combine(live, "notes.txt"), events = Path.Combine(live, "events.jsonl"), word = Path.Combine(live, "report.docx"), pdf = Path.Combine(live, "invoice.pdf"), image = Path.Combine(live, "picture.png");
        File.WriteAllText(notes, "alpha\nbeta\ngamma\ndelta\nthe pathweftlive needle\n" + string.Concat(Enumerable.Range(6, 20).Select(i => $"line {i}\n")));
        File.WriteAllText(events, string.Concat(Enumerable.Range(1, 50).Select(i => $"{{\"event\":{i},\"kind\":\"pathweftjsonl\"}}\n")));
        File.Copy(Path.Combine(inputs, "document.docx"), word);
        WritePdf(pdf, "pathweftpdf invoice total");
        File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        var client = new IndexServiceClient(pipe);
        static async Task<bool> Until(Func<Task<bool>> condition, TimeSpan limit, TimeSpan every)
        {
            var end = DateTime.UtcNow + limit;
            while (true) { if (await condition()) return true; if (DateTime.UtcNow > end) return false; await Task.Delay(every); }
        }
        ContentIndexing.ResetForVerification();
        IndexedSearch.ServiceOverride = client;
        bool rootAdded = false;
        try
        {
            var status = await client.StatusAsync(CancellationToken.None);
            check(status?.HasContentIndex == true, $"CONTENT-06: the private instance on {pipe} answers with protocol {status?.Protocol}");
            if (status?.HasContentIndex != true) return;

            // The private instance indexes only this run's folder (content scopes need an indexed folder).
            await client.FolderAddAsync(root, CancellationToken.None); rootAdded = true;
            bool ready = await Until(async () => (await client.StatusAsync(CancellationToken.None)).Stores.Any(s => string.Equals(Path.TrimEndingDirectorySeparator(s.Name ?? ""), root, StringComparison.OrdinalIgnoreCase) && s.State == "ready"), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(500));
            check(ready, "CONTENT-06: the run folder is an indexed folder in the private instance");

            SetSinglePane(true); SetActive(Left);
            await S1Verification.Navigate(Left, live);
            await ContentIndexing.Check();
            UpdateCommandStates();
            check(ContentIndexing.Available && IndexContentsCommand.Visibility == System.Windows.Visibility.Visible && IndexContentsCommand.IsEnabled, "CONTENT-01: the live service offers Index contents for the folder shown");
            var started = Stopwatch.StartNew();
            IndexContentsCommand.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            bool scoped = await Until(async () => (await client.ContentScopesAsync(CancellationToken.None)).Any(s => s.Path.Equals(live, StringComparison.OrdinalIgnoreCase)), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            check(scoped, "CONTENT-01: the toggle adds the folder as a content scope in the service");

            // Indexing: Pathweft's periodic check starts the worker while files wait; measure the worker.
            long peakWorker = 0; TimeSpan workerCpu = TimeSpan.Zero; var workers = new HashSet<int>();
            ContentStatus content = null;
            bool done = await Until(async () =>
            {
                await ContentIndexing.Check();
                var worker = ContentIndexing.WorkerForVerification;
                if (worker != null)
                    try
                    {
                        workers.Add(worker.Id);
                        if (!worker.HasExited) { worker.Refresh(); peakWorker = Math.Max(peakWorker, worker.PeakWorkingSet64); }
                        workerCpu = worker.TotalProcessorTime;
                    }
                    catch (InvalidOperationException) { }
                content = await client.ContentStatusAsync(CancellationToken.None);
                var scope = content?.Scopes.FirstOrDefault(s => s.Path.Equals(live, StringComparison.OrdinalIgnoreCase));
                return scope is { Files: >= 5, Pending: 0, Stale: 0 } && !ContentIndexing.Running;
            }, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(1));
            var scopeStatus = content?.Scopes.FirstOrDefault(s => s.Path.Equals(live, StringComparison.OrdinalIgnoreCase));
            check(done && scopeStatus.Current == 4 && scopeStatus.Unsupported == 1 && scopeStatus.Failed == 0,
                $"CONTENT-04: the worker indexes the folder in {started.Elapsed.TotalSeconds:0.0} s: {scopeStatus?.Current} current, {scopeStatus?.Unsupported} unsupported, {scopeStatus?.Failed} failed of {scopeStatus?.Files}");
            check(true, $"CONTENT-06 measured: worker peak working set {peakWorker / 1048576.0:0} MB, CPU {workerCpu.TotalSeconds:0.0} s, {workers.Count} worker run(s); content store {content?.StoreBytes / 1024.0:0} KB");

            var states = await client.ContentStateAsync(live, CancellationToken.None);
            ContentState? State(string path) => states.TryGetValue(path, out var file) ? file.State : null;
            check(State(notes) == ContentState.Current && State(events) == ContentState.Current && State(word) == ContentState.Current && State(pdf) == ContentState.Current && State(image) == ContentState.Unsupported,
                "CONTENT-01/02: text, JSONL, Word and PDF are current; the image is unsupported: " + string.Join(", ", states.Values.Select(f => Path.GetFileName(f.Path) + " " + f.State)));

            // Queries with locations, straight to the service.
            async Task<IndexedPage> Query(string expression)
            {
                var watch = Stopwatch.StartNew();
                var page = await client.QueryAsync(expression, 0, 20, "name", false, null, true, CancellationToken.None, PaneView.MatchLocations);
                lastQueryMs = watch.ElapsedMilliseconds;
                return page;
            }
            var hit = (await Query("content:pathweftlive")).Hits.SingleOrDefault();
            check(hit?.Path == notes && hit.Locations?.Select(ContentExtractors.Line).Contains(1) == true,
                $"CONTENT-05: content:pathweftlive finds notes.txt with its chunk location ({lastQueryMs} ms): " + string.Join(" | ", hit?.Locations ?? []));
            var pdfHit = (await Query("content:pathweftpd*")).Hits.SingleOrDefault();
            check(pdfHit?.Path == pdf && pdfHit.Locations?.Any(l => ContentExtractors.Describe(l) == "page 1") == true,
                $"CONTENT-05: a prefix finds the PDF on page 1 ({lastQueryMs} ms): " + string.Join(" | ", pdfHit?.Locations ?? []));
            var phrase = (await Query("content:\"invoice total\"")).Hits;
            check(phrase.Count == 1 && phrase[0].Path == pdf, $"CONTENT-05: a phrase finds the PDF ({lastQueryMs} ms)");
            var jsonlHits = (await Query("content:pathweftjsonl ext:jsonl")).Hits;
            check(jsonlHits.Count == 1 && jsonlHits[0].Path == events, $"CONTENT-05: content: combines with ext: in one query ({lastQueryMs} ms)");

            // Through the pane: results show locations; Open at line moves the editor's cursor there.
            await S1Verification.Navigate(Left, root);
            Left.VerificationSearchScope = 0;
            ((TextBox)Left.FindName("FilterBox")).Text = "content:pathweftlive";
            Left.Search("content:pathweftlive");
            await Left.SearchCompletion;
            var results = Left.IndexedResultsForVerification;
            check(results.Count == 1 && results[0].Path == notes && results[0].Where.Contains("near line 1"), "CONTENT-05: the pane search lists the match with its location: " + (results.Count > 0 ? results[0].Where : "none"));
            if (results.Count == 1)
            {
                var headers = new List<string>();
                void Collect(ItemsControl items) { foreach (var item in items.Items.OfType<MenuItem>()) { headers.Add(item.Header as string); Collect(item); } }
                Collect(ItemMenu(Left, new() { results[0] }));
                check(headers.Contains("Open at line 5"), "CONTENT-05: the context menu offers the exact line of the match (5, in the chunk from line 1): " + string.Join(", ", headers.Where(h => h?.StartsWith("Open at") == true)));
                EditorWindow.OpenAt(notes, 5);
                string position = null;
                bool moved = await Until(async () => (position = await EditorWindow.PositionForVerification()) == "Ln 5, Col 1", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250));
                EditorWindow.OpenAt(notes, 12);
                bool again = await Until(async () => (position = await EditorWindow.PositionForVerification()) == "Ln 12, Col 1", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250));
                check(moved && again, "CONTENT-05: Open at line opens the file at the line, and moves an open tab's cursor: " + position);
                await EditorWindow.CloseForVerification();
            }

            // Badges in the pane.
            await S1Verification.Navigate(Left, live);
            bool badged = await Until(() => Task.FromResult(Left.List.Items.Cast<Entry>().Any(e => e.Path == notes && e.Content?.State == ContentState.Current)), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            var picture = Left.List.Items.Cast<Entry>().FirstOrDefault(e => e.Path == image);
            check(badged && picture?.Content?.State == ContentState.Unsupported && picture.Tip.Contains("Contents not indexed"), "CONTENT-01: the pane shows the live states as badges and tooltips");

            // A change is noticed by the service and re-read by the worker; the new words answer.
            File.AppendAllText(notes, "pathweftchanged at the end\n");
            var changed = Stopwatch.StartNew();
            bool reread = await Until(async () => { await ContentIndexing.Check(); return (await Query("content:pathweftchanged")).Hits.Any(h => h.Path == notes); }, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(2));
            check(reread, $"CONTENT-04: an edited file is re-read and its new words are found after {changed.Elapsed.TotalSeconds:0} s");

            // Settings shows the scope with its counts.
            var settings = new SettingsWindow(this);
            try
            {
                await settings.RefreshIndex();
                var rows = settings.ContentScopes.Items.Cast<SettingsWindow.ContentScopeRow>().ToList();
                check(settings.ContentIndexPanel.Visibility == System.Windows.Visibility.Visible && rows.Any(r => r.Path == live && r.Detail.StartsWith("4 of 5 files indexed", StringComparison.Ordinal)),
                    "CONTENT-03: Settings lists the live scope: " + settings.ContentIndexStatus.Text + " | " + string.Join(" | ", rows.Select(r => r.Path + ": " + r.Detail)));
            }
            finally { settings.Close(); }

            // Stop: the scope and its terms go; the files stay.
            await S1Verification.Navigate(Left, live);
            UpdateCommandStates();
            IndexContentsCommand.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            bool removed = await Until(async () => (await client.ContentScopesAsync(CancellationToken.None)).Count == 0, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            var after = await Query("content:pathweftlive");
            check(removed && after.Hits.Count == 0 && File.Exists(notes) && File.Exists(pdf), "CONTENT-06: Stop indexing removes the scope and its terms; the files remain");
        }
        finally
        {
            ContentIndexing.ResetForVerification();
            if (rootAdded) try { await client.FolderRemoveAsync(root, CancellationToken.None); } catch (InvalidOperationException) { }
            IndexedSearch.ServiceOverride = null;
        }
    }
    long lastQueryMs;
}
#endif
