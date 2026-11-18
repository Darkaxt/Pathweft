using System.ComponentModel;
using System.IO;
using System.Windows;

namespace FileExplorer;

// TOOLS-02: What's using this? File Locksmith's discovery for the selection and for failures caused by a file in use.
// Read-only: it names the processes and the paths they hold; ending them or closing their handles is not offered.
public partial class MainWindow
{
    internal ToolReport WhatsUsing(IReadOnlyList<string> paths)
    {
        paths = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string caption = paths.Count == 1 ? paths[0] : $"{paths.Count:N0} items, for example {paths.FirstOrDefault()}";
        return ToolReport.Open(this, "What's using this?", caption, Array.Empty<ReportRow>(), "", nameWidth: 220, extraLabel: "Refresh",
            extra: ct => Task.Run(() => LockRows(paths, caption, ct), ct), modal: false);
    }

    static (string Caption, IReadOnlyList<ReportRow> Rows, string Summary) LockRows(IReadOnlyList<string> paths, string caption, CancellationToken ct)
    {
        var rows = new List<ReportRow>();
        LockScan scan;
        try { scan = FileLocksmith.Find(paths, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rows.Add(new ReportRow("Couldn't look", ex.Message, State: -1));
            return (caption, rows, "The open handles could not be read.");
        }
        foreach (var p in scan.Processes.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid))
        {
            string user = string.IsNullOrEmpty(p.User) ? "" : $" · {p.User}";
            rows.Add(new ReportRow($"{p.Name} ({p.Pid})", string.Join("\n", p.Files),
                (string.IsNullOrEmpty(p.ExecutablePath) ? "Executable path not available" : p.ExecutablePath) + user));
        }
        var summary = scan.Processes.Count == 0 ? "Nothing that can be seen is using it." : $"{scan.Processes.Count:N0} process(es) using it.";
        if (!scan.Elevated)
            summary += " Processes of other users and elevated ones may not be visible: run Pathweft as administrator to see them.";
        else if (scan.ProcessesNotOpened > 0)
            summary += $" {scan.ProcessesNotOpened:N0} protected process(es) couldn't be inspected.";
        return (caption, rows, summary);
    }

    // A failure caused by another program holding the file: the Win32 sharing/lock violation texts, .NET's own wording,
    // or the shell's error code.
    internal static bool InUse(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains(new Win32Exception(32).Message, StringComparison.OrdinalIgnoreCase)
            || message.Contains(new Win32Exception(33).Message, StringComparison.OrdinalIgnoreCase)
            || message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            || message.Contains("(error 32)", StringComparison.Ordinal) || message.Contains("(error 33)", StringComparison.Ordinal);
    }

    /// The failure message; when it is a file in use, it asks whether to see what's using `paths` (those still there).
    internal void ShowFailure(string message, IEnumerable<string> paths, string title)
    {
        var there = paths.Where(Exists).ToList();
        if (!InUse(message) || there.Count == 0) { MessageBox.Show(this, message, title); return; }
        if (MessageBox.Show(this, message + "\n\nSee what's using it?", title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) WhatsUsing(there);
    }

    // Transfer card Details: shown for a failed file in use; looks at the sources still there and the destination.
    void JobWhatsUsing_Click(object s, RoutedEventArgs e)
    {
        var job = JobOf(s);
        WhatsUsing(job.Sources.Append(job.Destination).Where(Exists).ToList());
    }

    void JobDetails_Expanded(object s, RoutedEventArgs e)
    {
        if (s is System.Windows.Controls.Expander { DataContext: Job job, Content: System.Windows.Controls.Panel panel })
            foreach (var b in panel.Children.OfType<System.Windows.Controls.Button>())
                b.Visibility = job.Errors.Any(InUse) ? Visibility.Visible : Visibility.Collapsed;
    }
}
