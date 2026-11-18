using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

/// A row of a tool's result: what it is about, its value, and an optional line under it.
/// `State`: 1 good (matches), -1 bad (failed, mismatch), 0 plain.
internal sealed record ReportRow(string Name, string Value, string Detail = null, int State = 0, bool Code = false);

// The results window of the S20 tools: Checksum, Test archive and What's using this. Modeless where the user may act
// in between (close a program, then Refresh), with an optional extra action that recomputes the rows.
public partial class ToolReport : Window
{
    internal sealed record Shown(string Name, string Value, string Detail, Brush Brush, FontFamily Font, GridLength NameWidth)
    {
        public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    Func<CancellationToken, Task<(string Caption, IReadOnlyList<ReportRow> Rows, string Summary)>> extra;
    CancellationTokenSource running;
    IReadOnlyList<ReportRow> rows = Array.Empty<ReportRow>();
    readonly double nameWidth;

    ToolReport(Window owner, string title, double nameWidth)
    {
        InitializeComponent();
        AuxiliaryWindows.Prepare(this, Root);
        Owner = owner; Title = title; this.nameWidth = nameWidth;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => running?.Cancel();
    }

    /// Shows rows already computed, or (with `extra`) computes them now and again on the extra button (Refresh).
    internal static ToolReport Open(Window owner, string title, string caption, IReadOnlyList<ReportRow> rows, string summary,
        double nameWidth = 150, string extraLabel = null, Func<CancellationToken, Task<(string Caption, IReadOnlyList<ReportRow> Rows, string Summary)>> extra = null,
        bool modal = false)
    {
        var w = new ToolReport(owner, title, nameWidth) { extra = extra };
        w.Set(caption, rows, summary);
        if (extraLabel != null) { w.ExtraButton.Content = extraLabel; w.ExtraButton.Visibility = Visibility.Visible; }
        if (extra != null) w.Loaded += (_, _) => w.Run();
#if DEBUG
        HiddenTrialPresentation.Prepare(w);
        Opened?.Invoke(w);
#endif
        if (modal) w.ShowDialog(); else w.Show();
        return w;
    }
#if DEBUG
    internal static Action<ToolReport> Opened;
    internal IReadOnlyList<ReportRow> VerificationRows => rows;
    internal string VerificationCaption => Caption.Text;
    internal string VerificationSummary => Summary.Text;
    internal Task VerificationRun() => runTask ?? Task.CompletedTask;
#endif

    internal void Set(string caption, IReadOnlyList<ReportRow> list, string summary)
    {
        rows = list ?? Array.Empty<ReportRow>();
        Caption.Text = caption ?? ""; Caption.Visibility = string.IsNullOrEmpty(caption) ? Visibility.Collapsed : Visibility.Visible;
        Summary.Text = summary ?? "";
        var mono = new FontFamily("Cascadia Mono, Consolas");
        Rows.ItemsSource = rows.Select(r => new Shown(r.Name, r.Value, r.Detail,
            MainWindow.Hex(r.State > 0 ? "#64C3A3" : r.State < 0 ? "#F87171" : "#E8EEF6"), r.Code ? mono : FontFamily, new GridLength(nameWidth))).ToList();
        CopyButton.IsEnabled = rows.Count > 0;
    }

    Task runTask;
    void Run()
    {
        running?.Cancel();
        var cancel = running = new CancellationTokenSource();
        ExtraButton.IsEnabled = false;
        Summary.Text = "Working…";
        runTask = Work();
        async Task Work()
        {
            try
            {
                var (caption, list, summary) = await extra(cancel.Token);
                if (!cancel.IsCancellationRequested) Set(caption, list, summary);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!cancel.IsCancellationRequested) Summary.Text = ex.Message; }
            finally { if (IsLoaded) ExtraButton.IsEnabled = true; }
        }
    }

    void Extra_Click(object s, RoutedEventArgs e) => Run();
    void Copy_Click(object s, RoutedEventArgs e) =>
        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(r => r.Value is { Length: > 0 } v ? $"{r.Name}\t{v}" : r.Name)));
    void Close_Click(object s, RoutedEventArgs e) => Close();
}
