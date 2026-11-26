#if DEBUG
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using M = FileExplorer.EmbeddedMetadata;

namespace FileExplorer;

public partial class MainWindow
{
    /// OPUS-09/10 (S58): Edit metadata… on copies of real files of every qualified format, in scratch only.
    internal async Task VerifyMetadata58(string root, Action<bool, string> check)
    {
        string dir = Directory.CreateDirectory(Path.Combine(root, "metadata é 子")).FullName;
        string inputs = Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        string ours = Path.Combine(inputs, "../../2026-11-26-metadata/inputs");
        string jpg = Path.Combine(dir, "photo.jpg"), tif = Path.Combine(dir, "scan.tif");
        MakeImage(jpg, new JpegBitmapEncoder { QualityLevel = 90 }, "jpg"); MakeImage(tif, new TiffBitmapEncoder(), "tiff");
        var files = new List<string> { jpg, tif };
        foreach (var (from, name) in new[] { (ours, "sample.mp3"), (ours, "sample.flac"), (ours, "sample.m4a"), (ours, "sample.wma"), (ours, "sample.wmv"), (inputs, "sdr.mp4"), (inputs, "document.docx"), (inputs, "spreadsheet.xlsx"), (inputs, "presentation.pptx") })
        { string to = Path.Combine(dir, name); File.Copy(Path.Combine(from, name), to); files.Add(to); }
        string readOnly = Path.Combine(dir, "read only.jpg"); MakeImage(readOnly, new JpegBitmapEncoder(), "jpg"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        string text = Path.Combine(dir, "notes.txt"); File.WriteAllText(text, "S58");
        string folder = Directory.CreateDirectory(Path.Combine(dir, "folder")).FullName;
        string readOnlyHash = Hash(readOnly);
        try
        {
            // the menu offers it in Tools for a selection holding a qualified file
            await S1Verification.Navigate(Left, dir); SetActive(Left); Left.SetSelection(e => !e.IsUp);
            var tools = MenuCommands(ItemMenu(Left, Left.Selected)).FirstOrDefault(i => i.Header as string == "Tools");
            check(tools != null && MenuCommands(tools).Any(i => i.Header as string == "Edit metadata…" && i.IsEnabled), "OPUS-09: Tools → Edit metadata… is offered for the selection");
            Left.SetSelection(e => e.Path == text);
            check(!MenuCommands(ItemMenu(Left, Left.Selected)).Any(i => i.Header as string == "Edit metadata…"), "OPUS-09: not offered when no selected file has a supported format");

            // what each format's handler can write, and why the others are left alone
            var all = await M.InspectAsync(files.Concat(new[] { readOnly, text, folder }).ToList());
            var by = all.ToDictionary(i => i.Path);
            M.Item Of(string path) => by[path];
            string writable = string.Join(" | ", files.Select(f => Path.GetExtension(f) + ": " + string.Join(",", Of(f).Writable.Select(n => n.Split('.').Last())) + (Of(f).TakenWritable ? ",DateTaken" : "") + (Of(f).Problem is { } p ? " PROBLEM " + p : "")));
            var core = new Dictionary<M.Family, string[]>
            {
                [M.Family.Photo] = new[] { M.Title.Name, M.Tags.Name, M.Rating.Name, M.Comment.Name, M.Authors.Name },
                [M.Family.Music] = new[] { M.Title.Name, M.Artist.Name, M.Album.Name, M.Year.Name, M.Track.Name, M.Genre.Name },
                [M.Family.Video] = new[] { M.Title.Name },
                [M.Family.Document] = new[] { M.Title.Name, M.Authors.Name },
            };
            check(!Of(files.Single(f => f.EndsWith(".flac"))).Writable.Contains(M.Rating.Name), "OPUS-09a: FLAC is not offered a rating (its handler accepts one but doesn't keep it)");
            check(files.All(f => Of(f).Problem == null && core[Of(f).Family.Value].All(Of(f).Writable.Contains)) && Of(jpg).TakenWritable, "OPUS-09a: every qualified format opens for writing with its core details: " + writable);
            check(Of(readOnly).Problem == "read-only" && Of(text).Problem?.Contains(".txt") == true && Of(folder).Problem?.Contains("folder") == true, "OPUS-09: read-only, unsupported and folder items are listed as left alone: " + Of(readOnly).Problem + " / " + Of(text).Problem);

            // set a known date taken first (Set mode), then mixed values across two photos
            var first = await M.ApplyAsync(new[] { Of(jpg), Of(tif) }.Where(i => i.TakenWritable).ToArray(), new M.Change(new Dictionary<string, string[]>(), M.DateMode.Set, new DateTime(2026, 1, 2, 3, 4, 5), default, false, false, true, true), null, CancellationToken.None);
            await M.ApplyAsync(new[] { (await M.InspectAsync(new[] { jpg }))[0] }, Fields(M.Title.Name, "Alpha"), null, CancellationToken.None);
            await M.ApplyAsync(new[] { (await M.InspectAsync(new[] { tif }))[0] }, Fields(M.Title.Name, "Beta"), null, CancellationToken.None);
            var photos = await M.InspectAsync(new[] { jpg, tif });
            check(first.All(o => o.Changed) && first.Count > 0 && photos.Where(p => p.TakenWritable).All(p => p.Taken?.ToLocalTime() == new DateTime(2026, 1, 2, 3, 4, 5)), "OPUS-09: Set date taken is written and reads back: " + string.Join(", ", photos.Select(p => p.Taken?.ToLocalTime().ToString("s") ?? "none")));
            var mixed = new MetadataDialog(this, photos, null);
            check(mixed.Boxes.TryGetValue(M.Title, out var mixedTitle) && mixedTitle.Text == MetadataDialog.Mixed, "OPUS-09: differing titles show as (multiple values)");
            mixed.Close();

            // the dialog: edit, review, write, results
            var before = (await M.InspectAsync(files)).ToDictionary(i => i.Path);
            var camera = new[] { ShellProperties.Find("System.Photo.CameraManufacturer"), ShellProperties.Find("System.Photo.CameraModel") }.Where(d => d != null).ToArray();
            var duration = new[] { ShellProperties.Find("System.Media.Duration") }.Where(d => d != null).ToArray();
            string Camera(string f) => string.Join("/", ShellProperties.Read(f, camera, false).Select(kv => kv.Key + "=" + kv.Value.Text).OrderBy(x => x));
            string Duration(string f) => ShellProperties.Read(f, duration, false).TryGetValue("System.Media.Duration", out var d) ? d.Text : "";
            var cameraBefore = new[] { jpg, tif }.ToDictionary(f => f, f => Camera(f));
            var durationBefore = files.Where(f => Of(f).Family is M.Family.Music or M.Family.Video).ToDictionary(f => f, f => Duration(f));
            var pixelsBefore = new[] { jpg, tif }.ToDictionary(f => f, f => Pixels(f));
            var partsBefore = files.Where(f => Of(f).Family == M.Family.Document).ToDictionary(f => f, f => Parts(f));
            var modifiedBefore = files.ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));
            List<M.Outcome> recorded = null; UndoEntry entry = null;
            var dialog = new MetadataDialog(this, await M.InspectAsync(files.Concat(new[] { readOnly, text, folder }).ToList()), (o, _) => { recorded = o; entry = RecordMetadata(o); });
            const string title = "S58 title é 子";
            dialog.Boxes[M.Title].Text = title; dialog.Boxes[M.Tags].Text = "one; two"; dialog.Boxes[M.Artist].Text = "Artist A"; dialog.Boxes[M.Comment].Text = "Comment line";
            dialog.PressStar(4);
            dialog.Dates.SelectedIndex = (int)M.DateMode.Shift; dialog.When.Text = "+1:00"; dialog.Modified.IsChecked = true; dialog.Taken.IsChecked = true;
            dialog.Press();
            string summary = dialog.Planned == null ? "" : dialog.Describe(dialog.Planned);
            check(dialog.Phase == 1 && summary.Contains("Title → " + title) && summary.Contains("Tags → one; two") && summary.Contains("Rating → ★★★★") && summary.Contains("shifted by +0.01:00") && summary.Contains("Left as they are (3)"),
                  "OPUS-09: Review lists each change, the date shift and what is left alone before anything is written: " + summary.Replace(Environment.NewLine, " / "));
            check(files.All(f => File.GetLastWriteTimeUtc(f) == modifiedBefore[f]), "OPUS-09: nothing is written before Apply");
            var clock = Stopwatch.StartNew(); long peakBefore = Process.GetCurrentProcess().PeakWorkingSet64;
            dialog.Press(); await dialog.Writing;
            long ms = clock.ElapsedMilliseconds;
            var outcomes = dialog.Outcomes;
            check(outcomes.All(o => o.Note?.Contains("not kept") != true), "OPUS-09: every written detail reads back: " + string.Join(", ", outcomes.Where(o => o.Note?.Contains("not kept") == true).Select(o => Path.GetFileName(o.Path) + "=" + o.Note)));
            check(dialog.Phase == 3 && outcomes.Count == files.Count + 3 && files.All(f => outcomes.Single(o => o.Path == f).Changed) && new[] { readOnly, text, folder }.All(f => !outcomes.Single(o => o.Path == f).Changed),
                  "OPUS-09: Apply changes every qualified file and none of the others: " + string.Join(", ", outcomes.Where(o => !o.Changed || o.Error != null).Select(o => Path.GetFileName(o.Path) + "=" + (o.Error ?? o.Note))));
            check(recorded == outcomes && entry != null && entry.Steps.Count == files.Count && entry.Steps.All(s => s.Kind == "meta"), "OPUS-09: one Undo entry holds every changed file");
            var history = OperationHistory.Store.All.FirstOrDefault();
            check(history?.What == $"Metadata edit of {outcomes.Count:N0} files" && history.Result == "done" && history.UndoWhat == entry?.What, "OPUS-05/09: Operation history records the edit with its outcome and Undo");

            // reopened: the new values, the shifted dates, and everything else as it was
            var after = (await M.InspectAsync(files)).ToDictionary(i => i.Path);
            string V(string f, M.Field field) => after[f].Values.TryGetValue(field.Name, out var v) ? string.Join("; ", v) : "";
            var wrong = files.SelectMany(f => new[]
            {
                after[f].Writable.Contains(M.Title.Name) && V(f, M.Title) != title ? "title" : null,
                after[f].Writable.Contains(M.Tags.Name) && V(f, M.Tags) != "one; two" ? "tags=" + V(f, M.Tags) : null,
                after[f].Writable.Contains(M.Artist.Name) && V(f, M.Artist) != "Artist A" ? "artist=" + V(f, M.Artist) : null,
                after[f].Writable.Contains(M.Comment.Name) && V(f, M.Comment) != "Comment line" ? "comment=" + V(f, M.Comment) : null,
                after[f].Writable.Contains(M.Rating.Name) && M.Stars(uint.TryParse(V(f, M.Rating), out uint r) ? r : 0) != 4 ? "rating=" + V(f, M.Rating) : null,
                File.GetLastWriteTimeUtc(f) != modifiedBefore[f] + TimeSpan.FromHours(1) ? "modified" : null,
            }.Where(w => w != null).Select(w => Path.GetFileName(f) + ":" + w)).ToList();
            check(wrong.Count == 0, "OPUS-09: reopened, each file holds the new details and its modified date moved one hour: " + string.Join(", ", wrong));
            check(new[] { jpg, tif }.All(f => after[f].Taken == before[f].Taken + TimeSpan.FromHours(1)), "OPUS-09: date taken shifted one hour");
            check(new[] { jpg, tif }.All(f => Camera(f) == cameraBefore[f] && cameraBefore[f].Contains("Pathweft")) && new[] { jpg, tif }.All(f => Pixels(f).SequenceEqual(pixelsBefore[f])),
                  "OPUS-09: unrelated photo details (camera) and the decoded pixels are unchanged: " + Camera(jpg));
            check(durationBefore.All(kv => Duration(kv.Key) == kv.Value && kv.Value.Length > 0), "OPUS-09: media durations are unchanged: " + string.Join(", ", durationBefore.Select(kv => Path.GetExtension(kv.Key) + "=" + Duration(kv.Key))));
            check(partsBefore.All(kv => Parts(kv.Key) == kv.Value), "OPUS-09: Office documents' content parts are byte-identical");
            check(Hash(readOnly) == readOnlyHash && File.GetAttributes(readOnly).HasFlag(FileAttributes.ReadOnly), "OPUS-09: a read-only file is not touched");
            check(Directory.GetFiles(dir).Length == files.Count + 2 && !File.Exists(jpg + ":Zone.Identifier"), "OPUS-09: nothing is written beside the files");
            check(true, $"OPUS-10: {files.Count} files of {files.Select(f => Path.GetExtension(f)).Distinct().Count()} formats written in {ms:N0} ms; peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576:N0} MB (was {peakBefore / 1048576:N0} MB); no background work before or after");

            // Undo, from the history saved to disk as after a restart
            await undoSave;
            var saved = LoadUndo().LastOrDefault(e => e.What == entry?.What);
            check(saved != null && saved.Steps.Count == entry.Steps.Count && saved.Steps.All(s => s.To?.Contains("@modified") == true), "OPUS-09: the Undo entry with each file's earlier values is in undo.json (survives a restart)");
            Undo(entry); await undoCompletion;
            var undone = (await M.InspectAsync(files)).ToDictionary(i => i.Path);
            var notBack = files.Where(f => File.GetLastWriteTimeUtc(f) != modifiedBefore[f]
                || M.Family.Photo == Of(f).Family && undone[f].Taken != before[f].Taken
                || Of(f).Fields.Any(field => string.Join("; ", undone[f].Values.GetValueOrDefault(field.Name) ?? Array.Empty<string>()) != string.Join("; ", before[f].Values.GetValueOrDefault(field.Name) ?? Array.Empty<string>())))
                .Select(f => Path.GetFileName(f) + "(" + string.Join(",", Of(f).Fields.Where(field => string.Join("; ", undone[f].Values.GetValueOrDefault(field.Name) ?? Array.Empty<string>()) != string.Join("; ", before[f].Values.GetValueOrDefault(field.Name) ?? Array.Empty<string>())).Select(field => field.Label)) + (File.GetLastWriteTimeUtc(f) != modifiedBefore[f] ? ",modified" : "") + ")").ToList();
            check(notBack.Count == 0 && !undo.Any(u => u.When == entry.When && u.What == entry.What), "OPUS-09: Undo puts every file's earlier details and dates back: " + string.Join(", ", notBack) + " / " + Status.Text);

            // a file in use fails alone; the others still change, and what wasn't edited stays
            var three = await M.InspectAsync(new[] { jpg, tif, files[2] });
            List<M.Outcome> partial;
            using (new FileStream(tif, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                partial = await M.ApplyAsync(three, Fields(M.Comment.Name, "Second"), null, CancellationToken.None);
            var reread = (await M.InspectAsync(new[] { jpg, tif, files[2] })).ToDictionary(i => i.Path);
            check(partial[0].Changed && partial[2].Changed && !partial[1].Changed && partial[1].Error == "in use by another program"
                  && reread[jpg].Values.GetValueOrDefault(M.Comment.Name)?.SingleOrDefault() == "Second" && reread[tif].Values.GetValueOrDefault(M.Comment.Name)?.SingleOrDefault() != "Second",
                  "OPUS-09: a file another program holds fails alone with the reason; the rest change: " + partial[1].Error);
            check(reread[jpg].Values.GetValueOrDefault(M.Title.Name)?.SingleOrDefault() == before[jpg].Values.GetValueOrDefault(M.Title.Name)?.SingleOrDefault(), "OPUS-09: details not edited keep their values");

            // stop: the file being written finishes, the rest aren't started
            var stop = new CancellationTokenSource();
            var batch = await M.InspectAsync(new[] { files[2], files[3], files[4] });
            var stopped = await M.ApplyAsync(batch, Fields(M.Title.Name, "Stopped"), (i, _) => { if (i == 0) stop.Cancel(); }, stop.Token);
            var left = (await M.InspectAsync(new[] { files[3], files[4] }));
            check(stopped[0].Changed && stopped.Skip(1).All(o => !o.Changed && o.Note == "not started: stopped") && left.All(i => i.Values.GetValueOrDefault(M.Title.Name)?.SingleOrDefault() != "Stopped"),
                  "OPUS-09: Stop finishes the current file and starts no other");
            check(MetadataDialog.TryShift("-1.02:30", out var back) && back == -new TimeSpan(1, 2, 30, 0) && !MetadataDialog.TryShift("soon", out _), "OPUS-09: a shift reads days.hours:minutes with a minus for earlier");
        }
        finally { try { File.SetAttributes(readOnly, FileAttributes.Normal); } catch (IOException) { } }

        static M.Change Fields(string name, string value) => new(new Dictionary<string, string[]> { [name] = new[] { value } }, M.DateMode.Keep, default, default, false, false, false, false);
        static string Hash(string f) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        static string Parts(string f)
        {
            using var zip = ZipFile.OpenRead(f);
            return string.Join("|", zip.Entries.Where(e => !e.FullName.StartsWith("docProps/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName)
                .Select(e => { using var s = e.Open(); return e.FullName + "=" + Convert.ToHexString(SHA256.HashData(s)); }));
        }
        static byte[] Pixels(string f)
        {
            using var stream = File.OpenRead(f);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var pixels = new byte[frame.PixelHeight * frame.PixelWidth * 4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, frame.PixelWidth * 4, 0);
            return pixels;
        }
        static void MakeImage(string path, BitmapEncoder encoder, string format)
        {
            var pixels = new byte[48 * 32 * 3];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 % 251);
            var metadata = new BitmapMetadata(format) { CameraManufacturer = "Pathweft", CameraModel = "S58" };
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(48, 32, 96, 96, PixelFormats.Bgr24, null, pixels, 48 * 3), null, metadata, null));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
#endif
