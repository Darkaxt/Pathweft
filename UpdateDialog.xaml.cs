using System.IO;
using System.Net.Http;
using System.Windows;

namespace FileExplorer;

// "Update available": the new version's notes, Later or Update now. (What's new after an update is WhatsNewWindow.)
public partial class UpdateDialog : Window
{
    readonly Updater.Release release;
    readonly CancellationTokenSource cancel = new();

    public UpdateDialog(Window owner, Updater.Release release)
    {
        InitializeComponent();
        Owner = owner;
        this.release = release;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        Notes.Text = release.Notes.Trim() == "" ? "No release notes." : release.Notes.Trim();

        Heading.Text = $"File Labs {release.Version} is available";
        Sub.Text = $"You have {Updater.Current}. The installer is {MainWindow.Fmt(release.AssetSize)}.";
        Closed += (_, _) => cancel.Cancel();
    }

    void Later_Click(object s, RoutedEventArgs e) => Close();

    async void Update_Click(object s, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = LaterButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Status.Text = "Downloading…";
        try
        {
            var progress = new Progress<double>(p => { Progress.Value = p; Status.Text = $"Downloading… {p * 100:0}%"; });
            var installer = await Updater.Download(release, progress, cancel.Token);
            Status.Text = "Installing — File Labs will restart.";
            Updater.InstallAndRestart(installer);
            Application.Current.Shutdown(); // the installer replaces our files; nothing of ours may stay
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Progress.Visibility = Visibility.Collapsed;
            Status.Text = ex.Message;
            UpdateButton.IsEnabled = LaterButton.IsEnabled = true;
        }
    }
}
