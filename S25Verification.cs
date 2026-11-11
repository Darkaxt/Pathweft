#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileExplorer;

// S25 UI review: renders every main surface per theme/layout for visual review, and times the ordinary
// browsing paths (UIQA-02) in-process, so the same harness gives before/after numbers on this machine.
public partial class MainWindow
{
    internal async Task VerifyS25(string root, Action<bool, string> check)
    {
        string shots = Directory.CreateDirectory(Path.Combine(root, "shots")).FullName;
        string big = Environment.GetEnvironmentVariable("PATHWEFT_S25_BIG") ?? @"C:\Windows\System32";
        string many = Environment.GetEnvironmentVariable("PATHWEFT_S25_MANY") ?? @"C:\Windows\WinSxS";
        string home = Environment.GetEnvironmentVariable("PATHWEFT_S25_HOME") ?? @"D:\Projects\Pathweft";
        var timings = new Dictionary<string, object>();
        async Task Idle() => await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle);

        void Shot(Window w, string label)
        {
            w.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(w);
            var root0 = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(root0.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root0.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // the window's own background: transparent (acrylic) windows otherwise render on black
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Appearance.Background), null, new Rect(0, 0, root0.ActualWidth, root0.ActualHeight));
            bmp.Render(dv); bmp.Render(root0);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(Path.Combine(shots, label + ".png")); png.Save(f);
        }

        // Longest UI-thread stall while `work` runs: an Input-priority heartbeat records the gaps between its turns.
        async Task<(double total, double maxStall)> Measure(Func<Task> work)
        {
            var sw = Stopwatch.StartNew(); double last = 0, max = 0; bool done = false;
            async Task Beat()
            {
                while (!done)
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
                    double now = sw.Elapsed.TotalMilliseconds; max = Math.Max(max, now - last); last = now;
                    await Task.Delay(1);
                }
            }
            var beat = Beat();
            await work(); await Idle();
            double total = sw.Elapsed.TotalMilliseconds; done = true; await beat;
            return (Math.Round(total, 1), Math.Round(max, 1));
        }
        async Task Time(string name, Func<Task> work, int runs = 3)
        {
            var r = new List<(double total, double maxStall)>();
            for (int i = 0; i < runs; i++) r.Add(await Measure(work));
            timings[name] = new { totalMs = r.Select(x => x.total).ToArray(), maxStallMs = r.Select(x => x.maxStall).ToArray() };
            File.WriteAllText(Path.Combine(root, "timings.json"), JsonSerializer.Serialize(timings, new JsonSerializerOptions { WriteIndented = true }));
        }

        Width = 1400; Height = 900;
        SetSinglePane(false);
        await S1Verification.Navigate(Left, home); await S1Verification.Navigate(Right, big); SetActive(Left); await Idle();

        // ---- timings (dark native theme, two panes, as configured by the launcher) ----
        await Time("navigate-big-first", async () => await S1Verification.Navigate(Left, big), runs: 1);
        await Time("navigate-big-revisit", async () => { await S1Verification.Navigate(Left, home); await S1Verification.Navigate(Left, big); });
        await Time("navigate-many-subfolders", async () => { await S1Verification.Navigate(Left, many); await Task.Delay(1500); }, runs: 1);
        await S1Verification.Navigate(Left, big); await Idle();
        await Time("select-200-rows", async () =>
        {
            for (int i = 0; i < 200 && i < Left.List.Items.Count; i++) { Left.List.SelectedIndex = i; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); }
        });
        await Time("view-modes", async () =>
        {
            foreach (var m in new[] { PaneView.ViewMode.IconsL, PaneView.ViewMode.List, PaneView.ViewMode.Details }) { Left.SetView(m); await Idle(); }
        });
        await Time("performance-toggle", async () => { await TogglePerformance(); await Idle(); await TogglePerformance(); });
        await Time("maintenance-toggle", async () => { await ToggleMaintenance(); await Idle(); await ToggleMaintenance(); });
        await Time("resize-20", async () => { for (int i = 0; i < 20; i++) { Width = 1400 - i * 20; await Idle(); } Width = 1400; });
        check(true, "Timings recorded");

        // ---- captures ----
        var themes = new (string name, bool native, string mode)[] { ("dark", true, "dark"), ("light", true, "light"), ("original", false, "dark") };
        foreach (var (name, native, mode) in themes)
        {
            Settings.NativeTheme = native; Settings.NativeMode = mode; ApplyAppearance(); await Idle();
            Width = 1400; Height = 900; SetSinglePane(false);
            await S1Verification.Navigate(Left, home); await S1Verification.Navigate(Right, big); SetActive(Left); await Idle();
            Shot(this, $"{name}-two-pane");
            SetSinglePane(true); await Idle(); Shot(this, $"{name}-single");
            Width = 900; Height = 700; await Idle(); Shot(this, $"{name}-narrow"); Width = 1400; Height = 900; await Idle();
            await TogglePerformance(); await Idle(); await Task.Delay(1200); await Idle(); Shot(this, $"{name}-performance"); await TogglePerformance();
            await ToggleMaintenance(); await Idle(); await Task.Delay(1500); await Idle(); Shot(this, $"{name}-maintenance"); await ToggleMaintenance();
            Settings.Preview = true; ApplyPreview();
            Left.List.SelectedItem = Left.Items.FirstOrDefault(e => e.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase)); await Task.Delay(800); await Idle();
            Shot(this, $"{name}-preview"); Settings.Preview = false; ApplyPreview();
            Left.SetView(PaneView.ViewMode.IconsL); await Idle(); Shot(this, $"{name}-icons"); Left.SetView(PaneView.ViewMode.Details);
            var settings = new SettingsWindow(this); settings.Show(); await Idle();
            for (int i = 0; i < settings.SettingsCategories.Items.Count; i++)
            {
                settings.SettingsCategories.SelectedIndex = i; await Idle();
                Shot(settings, $"{name}-settings-{i}");
            }
            settings.Close(); await Idle();
        }
        check(Directory.GetFiles(shots, "*.png").Length > 20, "Captures written");
        check(HiddenTrialPresentation.ForegroundViolations == 0, "The S25 review never activates a desktop window");
    }
}
#endif
