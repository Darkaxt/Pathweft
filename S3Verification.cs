#if DEBUG
using FileExplorer.Preview;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyS3(string root)
    {
        var observations = new List<object>();
        var started = System.Diagnostics.Stopwatch.StartNew();
        string evidence = Path.Combine(root, "evidence", Environment.ProcessId.ToString());
        Directory.CreateDirectory(evidence);
        int result = 1;
        bool pdfDiagnostic = Environment.GetEnvironmentVariable("PATHWEFT_S3_PDF_ONLY") == "1";
        bool f3dDiagnostic = Environment.GetEnvironmentVariable("PATHWEFT_S3_F3D_ONLY") == "1";
        bool workflowDiagnostic = Environment.GetEnvironmentVariable("PATHWEFT_S3_DIAGNOSTIC") == "1";
        void Check(bool passed, string criterion)
        {
            HiddenTrialPresentation.Phase = criterion;
            observations.Add(new { criterion, passed, elapsedMs = started.Elapsed.TotalMilliseconds });
            File.WriteAllText(Path.Combine(evidence, "phase.json"), JsonSerializer.Serialize(new { criterion, passed }));
            if (!passed) throw new InvalidOperationException(criterion);
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Key(Window window, System.Windows.Input.Key key, object source = null)
        {
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = source ?? window };
            window.RaiseEvent(input);
        }
        async Task Select(string name)
        {
            SetActive(Left);
            Left.ListForVerification.SelectedItem = Left.Items.Single(e => e.Name == name);
            // Folder change notifications can replace a request while it is loading.
            // Observe the complete owned generation chain, not an obsolete Task snapshot.
            await PreviewSession.DrainAsync();
        }
        IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T value) yield return value;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
        }
        void Ready(PreviewSession session, string module) => Check(session.State == "ready" && session.ModuleId == module,
            $"PREVIEW-01/02 actual {module} content ready through the selected internal module");
        async Task<SearchWidgetWindow> WidgetResult(string name)
        {
            Settings.TaskbarSearch = true; taskbarSearch.ApplySettings(); await taskbarSearch.LayoutCompletion;
            taskbarSearch.ShowSearch();
            var widget = taskbarSearch.Widget;
            await S1Verification.Navigate(widget.Pane, Path.Combine(root, "files"));
            widget.Pane.VerificationSearchScope=0;
            ((TextBox)widget.Pane.FindName("FilterBox")).Text = name;
            widget.Pane.Search(name); await widget.Pane.SearchCompletion;
            widget.Pane.ListForVerification.SelectedItem = widget.Pane.Items.Single(e => e.Name == name);
            return widget;
        }
        try
        {
            root = Path.GetFullPath(root);
            if (!root.StartsWith(@"D:\Temp\pathweft-s3-", StringComparison.OrdinalIgnoreCase) || Settings.Profile == null)
                throw new ArgumentException("S3 requires registered fixtures and an isolated profile.");
            if (!pdfDiagnostic && !f3dDiagnostic && !workflowDiagnostic)
                Check(System.Diagnostics.Process.GetProcessesByName("QuickLook").Length == 0, "PREVIEW-01 standalone QuickLook is absent from the actual runtime trial");
            File.WriteAllText(Path.Combine(root, "active-trial.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, evidence, restart = Environment.GetEnvironmentVariable("PATHWEFT_S3_RESTART") == "1" }));
            await S1Verification.Navigate(Left, Path.Combine(root, "files"));
            await S1Verification.Navigate(Right, root);
            SetActive(Left);
            if (!Settings.SinglePane) SetSinglePane(true);
            Width = 1280; Height = 820;
            // The existing preview toolbar uses the same click handler; find its routed button.
            if (!Settings.Preview)
            {
                var toggle = Descendants<Button>(this).First(b => b.ToolTip?.ToString() == "Show the selected file beside the panes");
                Click(toggle);
            }
            Check(Settings.Preview && PreviewPane.IsVisible, "PREVIEW-04 real preview toolbar enables the side pane");
            if (pdfDiagnostic)
            {
                await Select("pwfts3_sample.pdf"); Ready(sidePreview, "pdf");
                Click(Descendants<Button>(PreviewBody).Single(b => b.Content?.ToString() == "Next page"));
                await PreviewSession.DrainAsync();
                observations.Add(new { beforeMeasuredLayout = Descendants<Image>(PreviewBody).Select(i => (i.Source as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth).ToArray() });
                await S1Verification.Snapshot(this, evidence, "pdf-page2-diagnostic");
                observations.Add(new { labels = Descendants<TextBlock>(PreviewBody).Select(t => t.Text).ToArray(), widths = Descendants<Image>(PreviewBody).Select(i => (i.Source as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth).ToArray() });
                Check(Descendants<TextBlock>(PreviewBody).Any(b => b.Text == "Page 2 of 2") && Descendants<Image>(PreviewBody).Any(i => i.Source is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0 }), "PDF diagnostic: actual second page is present in the measured view");
                result = 0; return;
            }
            if (f3dDiagnostic)
            {
                await Select("pwfts3_f3d.glb");
                Check(sidePreview.ModuleId != "f3d" && sidePreview.Initializations.GetValueOrDefault("f3d") == 0, "PREVIEW-05 default-disabled 3D module does not initialize its native renderer");
                var moduleSettings = new SettingsWindow(this); moduleSettings.Show();
                var toggle = moduleSettings.PreviewModulesPanel.Children.OfType<CheckBox>().Single(c => (string)c.Tag == "f3d");
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await sidePreview.Completion; Ready(sidePreview, "f3d");
                var renderer = (F3dViewer)sidePreview.ViewerForVerification;
                var first = renderer.LastFrame;
                Check(first is { Offscreen: true, Bitmap.PixelWidth: 960, Bitmap.PixelHeight: 720 }, "PREVIEW-05 actual native F3D engine renders an offscreen GLB frame");
                var pixels = new System.Windows.Media.Imaging.FormatConvertedBitmap(first.Bitmap, PixelFormats.Bgra32, null, 0);
                var pixelBytes = new byte[pixels.PixelWidth * pixels.PixelHeight * 4]; pixels.CopyPixels(pixelBytes, pixels.PixelWidth * 4, 0);
                var colors = new HashSet<int>(); for (int i = 0; i < pixelBytes.Length; i += 64) colors.Add(BitConverter.ToInt32(pixelBytes, i) & 0xFFFFFF);
                Check(colors.Count > 32, "PREVIEW-05 rendered frame contains model detail rather than an empty background");
                File.WriteAllBytes(Path.Combine(evidence, "f3d-user-model.png"), first.Png);
                moduleSettings.Close();
                await S1Verification.Snapshot(this, evidence, "side-f3d-prototype");
                Click(Descendants<Button>(PreviewBody).Single(b => b.Content?.ToString() == "Orbit left")); await renderer.RenderCompletion;
                Check(!first.Camera.SequenceEqual(renderer.LastFrame.Camera) && !first.Png.SequenceEqual(renderer.LastFrame.Png), "PREVIEW-05 actual orbit control changes both camera state and rendered model image");
                Click(Descendants<Button>(PreviewBody).Single(b => b.Content?.ToString() == "Zoom in")); await renderer.RenderCompletion;
                Click(Descendants<Button>(PreviewBody).Single(b => b.Content?.ToString() == "Reset view")); await renderer.RenderCompletion;
                Check(first.Camera.Zip(renderer.LastFrame.Camera).All(pair => Math.Abs(pair.First - pair.Second) < 0.000001), "PREVIEW-05 reset restores the fitted camera after orbit/zoom");
                Key(this, System.Windows.Input.Key.Space, Left.ListForVerification); await quickPreview.Completion; Ready(quickPreview, "f3d");
                await PreviewSession.DrainAsync();
                Check(!renderer.WorkerAlive, "PREVIEW-05 switching surfaces joins the previous native render thread after engine destruction");
                await S1Verification.Snapshot(quick, evidence, "space-f3d-prototype");
                Key(quick, System.Windows.Input.Key.Escape); await sidePreview.Completion;
                var widget3d = await WidgetResult("pwfts3_f3d.glb");
                Click(widget3d.PreviewButton); await quickPreview.Completion; Ready(quickPreview, "f3d");
                var widgetRenderer = (F3dViewer)quickPreview.ViewerForVerification;
                await S1Verification.Snapshot(quick, evidence, "widget-f3d-prototype");
                moduleSettings = new SettingsWindow(this); moduleSettings.Show();
                toggle = moduleSettings.PreviewModulesPanel.Children.OfType<CheckBox>().Single(c => (string)c.Tag == "f3d");
                toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await quickPreview.Completion; await PreviewSession.DrainAsync();
                Check(!widgetRenderer.WorkerAlive && quickPreview.ModuleId != "f3d", "PREVIEW-05 disabling an active 3D preview releases its native context and worker");
                moduleSettings.Close(); widget3d.Dismiss();
                PreviewSession.ClearAll(); await PreviewSession.DrainAsync();
                string fixture = Path.Combine(root, "files", "pwfts3_f3d.glb"), renamed = fixture + ".released";
                File.Move(fixture, renamed); File.Delete(renamed);
                Check(!File.Exists(fixture) && !File.Exists(renamed), "PREVIEW-05 actual model source is available for rename/delete after native cleanup");
                using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "user-glb-readback.json")));
                string original = input.RootElement.GetProperty("originalPath").GetString();
                using (var originalStream = File.OpenRead(original))
                    Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalStream)) == input.RootElement.GetProperty("sha256").GetString(), "PREVIEW-05 original user asset remains byte-identical");
                File.Copy(original, fixture);
                Check(HiddenTrialPresentation.ForegroundViolations == 0, "PREVIEW-05 offscreen native rendering does not activate a test window");
                result = 0; return;
            }
            await Select("pwfts3_sample.md");
            if (Environment.GetEnvironmentVariable("PATHWEFT_S3_RESTART") == "1")
            {
                Check(Settings.DisabledPreviewModules.Contains("markdown") && sidePreview.ModuleId == "text" && sidePreview.Initializations.GetValueOrDefault("markdown") == 0,
                    "PREVIEW-03 a new process reads the persisted module toggle without constructing or initializing the disabled viewer");
                Check(PreviewBody.Content is TextBox, "PREVIEW-03 restart uses the eligible read-only text alternative");
                result = 0; return;
            }
            Ready(sidePreview, "markdown");
            var markdown = (FlowDocumentScrollViewer)PreviewBody.Content;
            string markdownText = new TextRange(markdown.Document.ContentStart, markdown.Document.ContentEnd).Text;
            Check(markdownText.Contains("Pathweft preview") && markdown.Document.Blocks.FirstBlock is Paragraph { FontWeight: var weight } && weight == FontWeights.SemiBold,
                "PREVIEW-02 real Markdown heading and content are rendered as native document elements");
            Check(!Descendants<System.Windows.Interop.HwndHost>(markdown).Any() && !markdown.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Hyperlink>().Any(),
                "PREVIEW-04 Markdown uses no browser host or active hyperlink");
            await S1Verification.Snapshot(this, evidence, "side-markdown-1280x820");

            var settings = new SettingsWindow(this); settings.Show();
            var toggles = settings.PreviewModulesPanel.Children.OfType<CheckBox>().ToDictionary(c => (string)c.Tag);
            Check(toggles.Count == PreviewModules.All.Length, "CORE-03 one Settings interface exposes every compiled-in module");
            var samples = new Dictionary<string, string>
            {
                ["text"] = "pwfts3_sample.cs", ["markdown"] = "pwfts3_sample.md", ["image"] = "pwfts3_sample.png",
                ["pdf"] = "pwfts3_sample.pdf", ["archive"] = "pwfts3_sample.zip", ["media"] = "pwfts3_sample.wav", ["font"] = "pwfts3_sample.ttf", ["f3d"] = "pwfts3_f3d.glb"
            };
            foreach (var module in PreviewModules.All)
            {
                if (toggles[module.Id].IsChecked != true) { toggles[module.Id].IsChecked = true; toggles[module.Id].RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); }
                await Select(samples[module.Id]); Ready(sidePreview, module.Id);
                int initializations = sidePreview.Initializations[module.Id];
                var toggle = toggles[module.Id]; toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await sidePreview.Completion;
                Check(sidePreview.ModuleId != module.Id && sidePreview.Initializations.GetValueOrDefault(module.Id) == initializations,
                    "PREVIEW-03 disabling active " + module.Id + " releases its view and never initializes that disabled module");
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                await sidePreview.Completion; Ready(sidePreview, module.Id);
            }
            var modulesPanel = settings.PreviewModulesPanel;
            modulesPanel.BringIntoView(new Rect(0, 0, modulesPanel.ActualWidth, Math.Min(260, modulesPanel.ActualHeight)));
            await S1Verification.Snapshot(settings, evidence, "settings-modules-top");
            toggles["text"].BringIntoView();
            await S1Verification.Snapshot(settings, evidence, "settings-modules-bottom");
            settings.Close();

            await Select("pwfts3_sample.pdf"); Ready(sidePreview, "pdf");
            Click(Descendants<Button>(PreviewBody).Single(b => b.Content?.ToString() == "Next page"));
            await PreviewSession.DrainAsync();
            await S1Verification.Snapshot(this, evidence, "side-pdf-page2");
            Check(Descendants<TextBlock>(PreviewBody).Any(b => b.Text == "Page 2 of 2") && Descendants<Image>(PreviewBody).Any(i => i.Source is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0 }),
                "PREVIEW-02 actual PDF page button renders the second document page");
            await Select("pwfts3_utf16.txt"); Ready(sidePreview, "text");
            Check(((TextBox)PreviewBody.Content).Text.Contains("子"), "PREVIEW-02 UTF-16 BOM text decodes correctly");
            await Select("pwfts3_large.txt");
            Check(((TextBox)PreviewBody.Content).Text.Contains("Preview truncated at 256 KiB"), "PREVIEW-04 large text has bounded reads and an honest visible limit");
            foreach (string bad in new[] { "pwfts3_bad.pdf", "pwfts3_bad.png", "pwfts3_huge.png" })
            {
                await Select(bad);
                Check(sidePreview.State == "error", "PREVIEW-04 malformed/oversized " + bad + " reports a preview error");
            }
            foreach (string unsupported in new[] { "pwfts3_unsupported.bin", "pwfts3_sample.docm" })
            { await Select(unsupported); Check(sidePreview.State == "unsupported", "PREVIEW-04 unsupported input does not invoke an executable or document macro handler"); }
            await Select("pwfts3_sample.html");
            Check(PreviewBody.Content is TextBox { IsReadOnly: true } text && text.Text.Contains("<script>"), "PREVIEW-04 HTML is inert source text, not a document/browser script");
            await Select("pwfts3_sample.zip");
            Check(((TextBox)PreviewBody.Content).Text.Contains("../escaped.exe") && !File.Exists(Path.Combine(root, "escaped.exe")), "PREVIEW-04 archive traversal entry is listed without extraction");

            var loads = new List<Task>();
            foreach (string name in new[] { "pwfts3_sample.png", "pwfts3_sample.pdf", "pwfts3_sample.md", "pwfts3_sample.txt" })
            {
                Left.ListForVerification.SelectedItem = Left.Items.Single(e => e.Name == name);
                loads.Add(sidePreview.Completion);
            }
            await Task.WhenAll(loads); await PreviewSession.DrainAsync();
            Check(sidePreview.Entry.Name == "pwfts3_sample.txt" && sidePreview.ModuleId == "text" && PreviewBody.Content is TextBox latest && latest.Text.Contains("Unicode"),
                "PREVIEW-04 rapid real selection changes cannot publish obsolete image/PDF/Markdown content");
            Key(this, System.Windows.Input.Key.Space, Left.ListForVerification);
            await quickPreview.Completion; Ready(quickPreview, "text");
            Check(sidePreview.Entry == null, "PREVIEW-04 opening Space releases the side preview session");
            await S1Verification.Snapshot(quick, evidence, "space-text-1000x720");
            Key(quick, System.Windows.Input.Key.Escape);
            Check(quick == null, "PREVIEW-04 Escape closes the actual Space preview window");

            // Actual Windows media decoding/playback, with silent fixtures and hidden-trial volume zero.
            foreach (string file in new[] { "pwfts3_sample.wav", "pwfts3_sample.mp4" })
            {
                await Select(file); Ready(sidePreview, "media");
                var element = Descendants<MediaElement>(PreviewBody).Single();
                Check(element.NaturalDuration.HasTimeSpan && (file.EndsWith(".wav") || element.NaturalVideoWidth == 160), "PREVIEW-02 actual Windows media decoder opened the audio/video fixture");
                var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                RoutedEventHandler Ended = (_, _) => ended.TrySetResult(); element.MediaEnded += Ended;
                try { Click(Descendants<Button>(PreviewBody).Single(b => b.Content?.ToString() == "Play / pause")); await ended.Task; }
                finally { element.MediaEnded -= Ended; }
                Check(element.Position == TimeSpan.Zero, "PREVIEW-04 actual media-end event resets playback without a completion timer");
            }

            await Select("pwfts3_sample.md");
            var mainSelection = Left.Selected.Single().Path;
            var widget = await WidgetResult("pwfts3_sample.md");
            Click(widget.PreviewButton); await quickPreview.Completion; Ready(quickPreview, "markdown");
            Check(Left.Selected.Single().Path == mainSelection && quickSource == widget.Pane, "PREVIEW-04 widget search-result preview keeps main and widget selection independent");
            await S1Verification.Snapshot(quick, evidence, "widget-markdown-preview");
            Settings.TextScale = 1.5; ApplyAppearance();
            quick.Width = 700; quick.Height = 500;
            await S1Verification.Snapshot(quick, evidence, "preview-700x500-text150");
            Key(quick, System.Windows.Input.Key.Escape);
            Key(widget, System.Windows.Input.Key.Space, widget.Pane.ListForVerification); await quickPreview.Completion;
            Check(quickSource == widget.Pane && quickPreview.ModuleId == "markdown", "PREVIEW-04 widget Space uses the same governed preview path as its Preview button");
            widget.Dismiss(); Check(quick == null, "PREVIEW-04 dismissing the widget releases its owned preview");
            Settings.TaskbarSearch = false; taskbarSearch.ApplySettings();
            Settings.TextScale = 1; ApplyAppearance();

            // Each file is actually previewed, then renamed/deleted after closing.
            string releaseFolder = Directory.CreateDirectory(Path.Combine(root, "release")).FullName;
            foreach (var sample in samples.Values.Append("pwfts3_sample.mp4")) File.Copy(Path.Combine(root, "files", sample), Path.Combine(releaseFolder, sample), true);
            await S1Verification.Navigate(Left, releaseFolder);
            foreach (var sample in samples.Values.Append("pwfts3_sample.mp4"))
            {
                await Select(sample);
                observations.Add(new { releaseSample = sample, state = sidePreview.State, module = sidePreview.ModuleId,
                    selected = Left.Selected.Select(e => e.Name).ToArray(), entry = sidePreview.Entry?.Name,
                    messages = Descendants<TextBlock>(PreviewBody).Select(t => t.Text).ToArray() });
                Check(sidePreview.State == "ready", "PREVIEW-04 release fixture was actually opened: " + sample);
                sidePreview.Clear(); await PreviewSession.DrainAsync();
                string path = Path.Combine(releaseFolder, sample), moved = path + ".released";
                File.Move(path, moved); File.Delete(moved);
                Check(!File.Exists(path) && !File.Exists(moved), "PREVIEW-04 close releases actual source for rename/delete: " + sample);
            }
            await S1Verification.Navigate(Left, Path.Combine(root, "files")); await Select("pwfts3_sample.md");
            settings = new SettingsWindow(this); settings.Show();
            var markdownToggle = settings.PreviewModulesPanel.Children.OfType<CheckBox>().Single(c => (string)c.Tag == "markdown");
            markdownToggle.IsChecked = false; markdownToggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            await sidePreview.Completion;
            Check(sidePreview.ModuleId == "text" && File.ReadAllText(Settings.FilePath).Contains("preview.modules.disabled=markdown"), "PREVIEW-03 actual toggle writes the state used by the next process");
            settings.Close();
            Check(HiddenTrialPresentation.ForegroundObserverActive && HiddenTrialPresentation.ForegroundViolations == 0, "Hidden preview verification generated no foreground event for its process");
            result = 0;
        }
        catch (Exception ex) { observations.Add(new { failure = ex.ToString() }); }
        finally
        {
            closingPreviews = true;
            taskbarSearch?.Dispose();
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) if (window != this) window.Close();
            PreviewSession.ClearAll(); await PreviewSession.DrainAsync();
            File.WriteAllText(Path.Combine(evidence, "s3-results.json"), JsonSerializer.Serialize(new { result, pid = Environment.ProcessId,
                scenario = f3dDiagnostic ? "native-f3d-prototype" : pdfDiagnostic ? "pdf-page-diagnostic" : Environment.GetEnvironmentVariable("PATHWEFT_S3_RESTART") == "1" ? "new-process-persisted-preview-modules" : workflowDiagnostic ? "internal-preview-workflow-diagnostic" : "complete-internal-preview-workflow",
                hidden = HiddenTrialPresentation.Enabled, foregroundViolations = HiddenTrialPresentation.ForegroundViolations, observations }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit += (_, exit) => exit.ApplicationExitCode = result;
            Quit();
        }
    }
}
#endif
