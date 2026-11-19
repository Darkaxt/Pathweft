using System.IO;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

namespace FileExplorer;

// TOOLS-03 Resize images…: the selected pictures through the Image Resizer engine, as one job on the Transfers card
// (progress, Cancel), never over an existing file. Beside the originals or in a folder, a name already taken gets
// " (2)"; Replace the originals writes each result to a side name first, sends the originals to the Recycle Bin, then
// gives the results their names. One Undo entry takes the results away and brings the originals back.
public partial class MainWindow
{
    Job lastResizeJob;
    Task resizeCompletion = Task.CompletedTask; // the last resize, its Undo entry and report included, for verification
#if DEBUG
    /// Called on the worker before each picture (its index); verification cancels from here.
    internal Action<Job, int> ResizeStartingForVerification;
#endif

    static bool ResizableImage(Entry e) => !e.IsDir && PaneView.ArchiveOf(e.Path) == null && ImageResizer.CanResize(e.Path);
    internal static bool CanResizeSelection(List<Entry> sel) => sel != null && sel.Any(ResizableImage);

    sealed record ResizedImage(string Source, string Written, string Final);

    void ResizeImages() => ResizeImages(active.Selected);

    // The selection, or from a folder's background the pictures it holds.
    void ResizeImages(List<Entry> sel)
    {
        var pane = active;
        if (pane.ArchiveRoot != null || PaneView.IsVirtual(pane.Dir)) { Status.Text = "Pictures inside an archive can't be resized. Extract them first."; return; }
        var images = sel.Where(ResizableImage).Select(e => e.Path).ToList();
        var unsupported = sel.Where(e => !ResizableImage(e)).Select(e => e.Path).ToList();
        if (images.Count == 0) { Status.Text = "None of the selected items is a picture Resize images can read (BMP, GIF, JPEG, JPEG XR, PNG, TIFF)."; return; }
        if (ImageResizeDialog.Ask(this, pane.Dir, images.Count) is not { } choice) return;
        bool replace = choice.Output == ImageResizeDialog.Output.Replace;
        if (replace && images.Any(BinIsOff))
        { Status.Text = "The Recycle Bin is turned off on this drive, so replaced originals couldn't come back. Resize beside the originals instead."; return; }
        string destination = choice.Output == ImageResizeDialog.Output.Folder ? choice.Folder : pane.Dir;
        var o = choice.Options;
        var done = new List<ResizedImage>();
        var failed = new List<(string Path, string Why)>();
        bool madeFolder = false;
        Status.Text = $"Resizing {images.Count:N0} picture(s)…";
        var job = lastResizeJob = SubmitArchiveEdit(images, destination, $"Resize {(images.Count == 1 ? Path.GetFileName(images[0]) : $"{images.Count:N0} pictures")}", job =>
        {
            using var cancel = new CancellationTokenSource();
            void Watch(object s, System.ComponentModel.PropertyChangedEventArgs e) { if (job.IsCancelled) cancel.Cancel(); }
            job.PropertyChanged += Watch;
            try
            {
                if (job.IsCancelled) cancel.Cancel();
                job.TotalFiles = images.Count;
                job.ExcludedItems = unsupported.Count;
                foreach (var image in images) try { job.TotalBytes += new FileInfo(image).Length; } catch (IOException) { }
                if (!replace && !Directory.Exists(destination)) { Directory.CreateDirectory(destination); madeFolder = true; }
                job.Phase = Phase.Copying;
                for (int i = 0; i < images.Count && !cancel.IsCancellationRequested; i++)
                {
                    string source = images[i];
                    long size = 0;
                    try { size = new FileInfo(source).Length; } catch (IOException) { }
                    job.Current = Path.GetFileName(source); job.CurrentSize = size; job.CurrentDone = 0;
#if DEBUG
                    ResizeStartingForVerification?.Invoke(job, i);
#endif
                    try
                    {
                        string name = ImageResizer.OutputFileName(source, o);
                        string written, final;
                        if (replace)
                        {
                            string dir = Path.GetDirectoryName(source), stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(name);
                            // the same name when the format stays; a picture the engine writes in another format keeps its new extension
                            final = ext.Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase) ? source : Path.Combine(dir, stem + ext);
                            written = Unique(Path.Combine(dir, $"{stem}.resizing{ext}"));
                        }
                        else final = written = Unique(Path.Combine(destination, name));
                        for (int attempt = 0; ; attempt++)
                            try { ImageResizer.ResizeAsync(source, written, o, cancel.Token).GetAwaiter().GetResult(); break; }
                            catch (IOException) when (attempt < 3 && File.Exists(written)) // taken meanwhile: the next free name
                            { written = Unique(written); if (!replace) final = written; }
                        if (replace) File.SetAttributes(written, File.GetAttributes(written) | FileAttributes.Hidden); // until it takes the original's name
                        lock (done) done.Add(new(source, written, final));
                        job.CopiedFiles++;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        // an unreadable or unsupported picture (WIC's COMException), a locked file, a full disk: this one only
                        string why = ex is System.Runtime.InteropServices.COMException ? "Windows can't decode this picture: " + ex.Message : ex.Message;
                        lock (failed) failed.Add((source, why));
                        job.Errors.Enqueue($"{Path.GetFileName(source)}: {why}");
                    }
                    job.DoneFiles++; job.DoneBytes += size; job.CurrentDone = size;
                }
                job.Arrivals = replace ? new() : done.Select(d => Path.GetFileName(d.Final)).ToList();
            }
            finally { job.PropertyChanged -= Watch; }
        });
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resizeCompletion = finished.Task;
        _ = job.Completion.ContinueWith(_ => Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var outputs = replace ? await ReplaceOriginals(done, failed) : done.Select(d => d.Final).ToList();
                if (!replace)
                {
                    var steps = done.Select(d => Made(d.Final)).ToList();
                    if (madeFolder) steps.Add(new UndoStep("rmdir", destination));
                    if (done.Count > 0) Remember($"resize of {(done.Count == 1 ? Path.GetFileName(done[0].Final) : $"{done.Count:N0} pictures")}", steps);
                }
                var rows = failed.Select(f => new ReportRow(Path.GetFileName(f.Path), "Not resized", f.Why, -1))
                    .Concat(unsupported.Select(u => new ReportRow(Path.GetFileName(u.TrimEnd('\\')), "Skipped", Directory.Exists(u) ? "A folder, not a picture." : "Not a picture Resize images can read.", -1))).ToList();
                string cancelled = job.IsCancelled ? " Cancelled; the picture being resized was not kept." : "";
                Status.Text = $"Resized {outputs.Count:N0} of {images.Count:N0} picture(s)" + (failed.Count > 0 ? $", {failed.Count:N0} failed" : "") + "." + cancelled;
                if (rows.Count > 0)
                    ToolReport.Open(this, "Resize images", $"{rows.Count:N0} item(s) were not resized", rows, $"{outputs.Count:N0} resized", nameWidth: 220);
                foreach (var p in new[] { Left, Right })
                    if (outputs.Count > 0 && p.Dir.TrimEnd('\\').Equals(Path.GetDirectoryName(outputs[0]).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    { p.SelectAfterRefresh(outputs.Where(x => Path.GetDirectoryName(x).Equals(Path.GetDirectoryName(outputs[0]), StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName), focus: p == active); p.Refresh(); }
            }
            finally { finished.TrySetResult(); }
        }), TaskScheduler.Default);
    }

    /// The originals to the Recycle Bin, then each result takes its name (or the next free one when the original
    /// stayed). Undo: the result moves aside to a free name, the original comes back from the bin, the result is recycled.
    async Task<List<string>> ReplaceOriginals(List<ResizedImage> done, List<(string Path, string Why)> failed)
    {
        if (done.Count == 0) return new();
        var originals = done.Select(d => d.Source).ToList();
        var when = DateTime.Now.AddSeconds(-2); // the $I record's clock and ours may differ a little
        var recycled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        undoWork.Add(recorded.Task);
        try
        {
            Recycle(originals, UIOption.OnlyErrorDialogs, done: () => recycled.TrySetResult());
            await recycled.Task;
            var (outputs, steps) = await Task.Run(() =>
            {
                var bin = RecycleBinWindow.Find(originals.Where(p => !Exists(p)), when);
                var list = new List<string>();
                var undoSteps = new List<UndoStep>();
                foreach (var d in done)
                {
                    try
                    {
                        string final = Unique(d.Final); // the original still there (not recycled): beside it instead
                        File.Move(d.Written, final);
                        File.SetAttributes(final, File.GetAttributes(final) & ~FileAttributes.Hidden);
                        list.Add(final);
                        bool back = bin.TryGetValue(d.Source, out var item);
                        if (back && final.Equals(d.Source, StringComparison.OrdinalIgnoreCase))
                        {
                            // in the original's place: renamed aside before the original is restored, then recycled
                            string dir = Path.GetDirectoryName(final);
                            string aside = Path.GetFileName(Unique(Path.Combine(dir, Path.GetFileNameWithoutExtension(final) + " (resized)" + Path.GetExtension(final))));
                            undoSteps.Add(new UndoStep("rename", final, aside, Ferry.Stamp(final)));
                            undoSteps.Add(new UndoStep("restore", d.Source, item));
                            undoSteps.Add(new UndoStep("recycle", Path.Combine(dir, aside)));
                        }
                        else
                        {
                            undoSteps.Add(Made(final));
                            if (back) undoSteps.Add(new UndoStep("restore", d.Source, item));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        lock (failed) failed.Add((d.Source, "The resized picture couldn't take the original's name: " + ex.Message));
                        if (File.Exists(d.Written)) { File.SetAttributes(d.Written, File.GetAttributes(d.Written) & ~FileAttributes.Hidden); undoSteps.Add(Made(d.Written)); list.Add(d.Written); }
                        if (bin.TryGetValue(d.Source, out var item)) undoSteps.Add(new UndoStep("restore", d.Source, item));
                    }
                }
                return (list, undoSteps);
            });
            if (steps.Count > 0)
                Remember($"resize of {(done.Count == 1 ? Path.GetFileName(done[0].Source) : $"{done.Count:N0} pictures")} (replaced)", steps, when);
            return outputs;
        }
        finally { undoWork.Remove(recorded.Task); recorded.TrySetResult(); }
    }

#if DEBUG
    /// TOOLS-03: the real Resize images path on fixtures under `root`, the dialog answered as a person would.
    internal async Task VerifyImageResize20(string root, Action<bool, string> check)
    {
        await ImageResizer.Checks(Path.Combine(root, "engine"), (ok, label) => check(ok, "TOOLS-03: engine: " + label));
        string dir = Path.Combine(root, "pictures 子");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "wide.png"), jpg = Path.Combine(dir, "photo.jpg"), gif = Path.Combine(dir, "anim.gif"),
               tif = Path.Combine(dir, "scan.tif"), bmp = Path.Combine(dir, "plain.bmp"), bad = Path.Combine(dir, "broken.png"), txt = Path.Combine(dir, "notes.txt");
        ResizeFixture(png, new System.Windows.Media.Imaging.PngBitmapEncoder(), 400, 300);
        bool exif = ResizeFixture(jpg, new System.Windows.Media.Imaging.JpegBitmapEncoder(), 400, 300, orientation: 6);
        ResizeFixture(gif, new System.Windows.Media.Imaging.GifBitmapEncoder(), 400, 300);
        ResizeFixture(tif, new System.Windows.Media.Imaging.TiffBitmapEncoder(), 400, 300);
        ResizeFixture(bmp, new System.Windows.Media.Imaging.BmpBitmapEncoder(), 400, 300);
        File.WriteAllBytes(bad, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5 });
        File.WriteAllText(txt, "not a picture");
        static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var hashes = new[] { png, jpg, gif, tif, bmp }.ToDictionary(p => p, Hash);
        check(exif, "TOOLS-03: fixture JPEG carries EXIF orientation 6");

        var report = new List<ToolReport>();
        ToolReport.Opened += Collect;
        void Collect(ToolReport r) => report.Add(r);
        void Choose(System.Windows.Controls.ComboBox box, object tag) => box.SelectedItem = box.Items.Cast<System.Windows.Controls.ComboBoxItem>().Single(i => Equals(i.Tag, tag));
        string savedSettings = Settings.ImageResize;
        async Task<bool> Run(string[] names, Action<ImageResizeDialog> answer)
        {
            await S1Verification.Navigate(Left, dir);
            SetActive(Left);
            Left.List.SelectedItems.Clear();
            foreach (var n in names) Left.List.SelectedItems.Add(Left.Items.Single(e => e.Name == n));
            var before = lastResizeJob; bool answered = false;
            ImageResizeDialog.VerificationAnswer = d => { answered = true; answer(d); };
            try { ResizeImages(); } finally { ImageResizeDialog.VerificationAnswer = null; }
            if (lastResizeJob == before) return false;
            await lastResizeJob.Completion; await resizeCompletion;
            return answered;
        }
        try
        {
            check(CanResizeSelection(new() { new Entry { Name = "a.png", Path = png } }) && !CanResizeSelection(new() { new Entry { Name = "notes.txt", Path = txt } }),
                "TOOLS-03: the menu is offered for advertised pictures only");

            // custom Stretch 120×80 beside the originals, metadata removed, on every advertised format, with a broken PNG and a text file
            bool ran = await Run(new[] { "wide.png", "photo.jpg", "anim.gif", "scan.tif", "plain.bmp", "broken.png", "notes.txt" }, d =>
            {
                Choose(d.PresetBox, -1); Choose(d.FitBox, ResizeFit.Stretch); Choose(d.UnitBox, ResizeUnit.Pixel);
                d.WidthBox.Text = "120"; d.HeightBox.Text = "80"; d.MetadataBox.IsChecked = true; d.OrientationBox.IsChecked = false; d.ShrinkBox.IsChecked = false; d.DateBox.IsChecked = false;
                Choose(d.DestinationBox, ImageResizeDialog.Output.Beside); d.NameBox.Text = "%1 (%2)"; d.Accept();
            });
            check(ran, "TOOLS-03: Resize images opens its dialog and submits one job");
            foreach (var (src, ext) in new[] { (png, ".png"), (jpg, ".jpg"), (gif, ".gif"), (tif, ".tif"), (bmp, ".bmp") })
            {
                string output = Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + " (Custom)" + ext);
                check(File.Exists(output) && ResizedSize(output) == (120, 80), $"TOOLS-03: {ext} output decodes at the requested 120 × 80");
            }
            check(ResizeOrientation(Path.Combine(dir, "photo (Custom).jpg")) == 6, "TOOLS-03: removing metadata keeps the JPEG's EXIF orientation");
            check(hashes.All(h => Hash(h.Key) == h.Value), "TOOLS-03: originals are unchanged beside the results");
            check(lastResizeJob.Errors.Count == 1 && report.LastOrDefault()?.VerificationRows is { } rows
                  && rows.Any(r => r.Name == "broken.png" && r.State < 0) && rows.Any(r => r.Name == "notes.txt" && r.State < 0),
                "TOOLS-03: a broken picture and a non-picture are reported per file, the rest resized");
            check(!Directory.EnumerateFiles(dir, "broken (Custom)*").Any(), "TOOLS-03: a failed picture leaves no partial output");
            foreach (var r in report) r.Close();
            report.Clear();

            // a preset with Fit into a chosen folder; a second run takes the next free name; Undo takes the results away
            string outFolder = Path.Combine(root, "resized");
            Action<ImageResizeDialog> toFolder = d =>
            {
                Choose(d.PresetBox, 0); Choose(d.FitBox, ResizeFit.Fit); d.ShrinkBox.IsChecked = false; d.MetadataBox.IsChecked = false;
                Choose(d.DestinationBox, ImageResizeDialog.Output.Folder); d.FolderBox.Text = outFolder; d.NameBox.Text = "%1 %5x%6"; d.Accept();
            };
            await Run(new[] { "wide.png" }, toFolder);
            await Run(new[] { "wide.png" }, toFolder);
            string first = Path.Combine(outFolder, "wide 640x480.png"), second = Path.Combine(outFolder, "wide 640x480 (2).png");
            check(ResizedSize(first) == (640, 480) && ResizedSize(second) == (640, 480), "TOOLS-03: Fit to the Small preset (854 × 480) in a chosen folder; a taken name gets (2)");
            check(Settings.ImageResize.Contains("out:folder") && ImageResizeDialog.Parse(Settings.ImageResize).Options.FileNameFormat == "%1 %5x%6",
                "TOOLS-03: the dialog's choices are remembered");
            Undo(); await undoCompletion;
            check(!File.Exists(second) && File.Exists(first), "TOOLS-03: Undo removes the resize's own output only");
            Undo(); await undoCompletion;
            check(!File.Exists(first) && !Directory.Exists(outFolder), "TOOLS-03: Undo removes the output and the folder it made");

            // cancellation: the second picture is cancelled before it is written, the first stays and nothing partial is left
            ResizeStartingForVerification = (job, i) => { if (i == 1) job.Cancel(); };
            try
            {
                await Run(new[] { "wide.png", "plain.bmp", "scan.tif" }, d =>
                {
                    Choose(d.PresetBox, -1); Choose(d.FitBox, ResizeFit.Fit); Choose(d.UnitBox, ResizeUnit.Percent); d.WidthBox.Text = "50"; d.ShrinkBox.IsChecked = false;
                    Choose(d.DestinationBox, ImageResizeDialog.Output.Beside); d.NameBox.Text = "%1 half"; d.Accept();
                });
            }
            finally { ResizeStartingForVerification = null; }
            var halves = Directory.EnumerateFiles(dir, "* half*").ToList();
            check(lastResizeJob.Phase == Phase.Cancelled && halves.Count == 1 && ResizedSize(halves[0]) == (200, 150)
                  && !Directory.EnumerateFiles(dir, "*.resizing*").Any(), "TOOLS-03: Cancel stops the batch and leaves no partial file");
            Undo(); await undoCompletion;
            foreach (var r in report) r.Close();
            report.Clear();

            // Replace the originals, never remembered; Undo brings the originals back exactly
            if (BinIsOff(png)) check(true, "TOOLS-03: Replace not tried: the Recycle Bin is off on the scratch drive");
            else
            {
                await Run(new[] { "wide.png", "photo.jpg" }, d =>
                {
                    check(d.Destination != ImageResizeDialog.Output.Replace, "TOOLS-03: Replace the originals is never preselected");
                    Choose(d.PresetBox, -1); Choose(d.FitBox, ResizeFit.Fit); Choose(d.UnitBox, ResizeUnit.Pixel); d.WidthBox.Text = "100"; d.HeightBox.Text = ""; d.ShrinkBox.IsChecked = false;
                    Choose(d.DestinationBox, ImageResizeDialog.Output.Replace); d.Accept();
                });
                check(ResizedSize(png) == (100, 75) && ResizedSize(jpg) == (100, 75) && !Directory.EnumerateFiles(dir, "*.resizing*").Any(),
                    "TOOLS-03: Replace puts the resized pictures under the original names");
                check(!Settings.ImageResize.Contains("replace"), "TOOLS-03: Replace the originals is not remembered");
                Undo(); await undoCompletion;
                check(File.Exists(png) && Hash(png) == hashes[png] && Hash(jpg) == hashes[jpg] && !Directory.EnumerateFiles(dir, "* (resized)*").Any(),
                    "TOOLS-03: Undo of Replace restores the exact originals and removes the results");
            }
        }
        finally
        {
            ToolReport.Opened -= Collect;
            foreach (var r in report) r.Close();
            Settings.ImageResize = savedSettings; Settings.Save();
        }
    }

    static bool ResizeFixture(string path, System.Windows.Media.Imaging.BitmapEncoder encoder, int w, int h, ushort orientation = 0)
    {
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { int i = (y * w + x) * 4; pixels[i] = (byte)x; pixels[i + 1] = (byte)y; pixels[i + 2] = (byte)(x ^ y); pixels[i + 3] = 255; }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, w * 4);
        System.Windows.Media.Imaging.BitmapMetadata metadata = null;
        if (orientation != 0)
        {
            metadata = new System.Windows.Media.Imaging.BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
            metadata.CameraManufacturer = "PathweftTest";
        }
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap, null, metadata, null));
        using (var file = File.Create(path)) encoder.Save(file);
        return orientation == 0 || ResizeOrientation(path) == orientation;
    }

    static System.Windows.Media.Imaging.BitmapDecoder ResizeDecode(string path)
    {
        using var file = File.OpenRead(path);
        return System.Windows.Media.Imaging.BitmapDecoder.Create(file, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat
            | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
    }
    static (int, int) ResizedSize(string path)
    {
        try { var f = ResizeDecode(path).Frames[0]; return (f.PixelWidth, f.PixelHeight); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException) { return (0, 0); }
    }
    static int ResizeOrientation(string path)
    {
        try { return ResizeDecode(path).Frames[0].Metadata is System.Windows.Media.Imaging.BitmapMetadata m && m.GetQuery("/app1/ifd/{ushort=274}") is ushort o ? o : 0; }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }
#endif
}
