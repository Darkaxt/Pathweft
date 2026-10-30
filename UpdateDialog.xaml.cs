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

        Heading.Text = $"Pathweft {release.Version} is available";
        Sub.Text = $"You have {Updater.Current}. The installer is {MainWindow.Fmt(release.AssetSize)}.";
        Closed += (_, _) => cancel.Cancel();
    }

    void Later_Click(object s, RoutedEventArgs e) => Close();

    async void Update_Click(object s, RoutedEventArgs e)
    {
        // a portable copy isn't installed: its update is the new zip, unpacked over this folder (Data stays as it is)
        if (Settings.Portable != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://github.com/{Updater.Repo}/releases/tag/{release.Tag}") { UseShellExecute = true });
            Status.Text = "This copy is portable: download the portable zip from the page that just opened and unpack it over this folder. Your settings in Data stay.";
            return;
        }
        if(Owner is MainWindow current&&current.UpdateInstallationBlocker is { } blocker){Status.Text=blocker;return;}
        UpdateButton.IsEnabled = LaterButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Status.Text = "Downloading…";
        try
        {
            var progress = new Progress<double>(p => { Progress.Value = p; Status.Text = $"Downloading… {p * 100:0}%"; });
            var installer = await Updater.Download(release, progress, cancel.Token);
            if(Owner is MainWindow owner&&owner.UpdateInstallationBlocker is { } pending)
            {
                Status.Text=pending;Progress.Visibility=Visibility.Collapsed;
                UpdateButton.IsEnabled=LaterButton.IsEnabled=true;return;
            }
            Status.Text = "Installing — Pathweft will restart.";
            if (!Updater.InstallAndRestart(installer))
            {
                Progress.Visibility = Visibility.Collapsed;
                Status.Text = "Update not installed: Windows asked for permission and it wasn't given. Try again whenever you like.";
                UpdateButton.IsEnabled = LaterButton.IsEnabled = true;
                return;
            }
            Application.Current.Shutdown(); // the installer replaces our files; nothing of ours may stay
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            Progress.Visibility = Visibility.Collapsed;
            Status.Text = ex.Message;
            UpdateButton.IsEnabled = LaterButton.IsEnabled = true;
        }
    }
}
