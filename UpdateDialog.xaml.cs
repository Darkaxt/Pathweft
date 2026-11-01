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

    internal void BeginDownload()
    {
        UpdateButton.Visibility=NotesCard.Visibility=Visibility.Collapsed;
        LaterButton.Content="Cancel";
        LaterButton.IsEnabled=true;
        Progress.Visibility=Visibility.Visible;Height=240;MinHeight=220;
        Heading.Text=$"Updating Pathweft to {release.Version}";
        Sub.Text="Pathweft will reopen when the update is installed.";
        Status.Text="Downloading…";
    }
    internal void ReportDownload(Updater.DownloadProgress progress)
    {
        Progress.IsIndeterminate=progress.Total<=0;
        if(progress.Total>0)Progress.Value=Math.Clamp((double)progress.Received/progress.Total,0,1);
        Status.Text=progress.Total>0?$"Downloading · {MainWindow.Fmt(progress.Received)} of {MainWindow.Fmt(progress.Total)} · {Progress.Value*100:0}%":progress.Stage+"…";
    }

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
        UpdateButton.IsEnabled=false;
        BeginDownload();
        try
        {
            var progress = new Progress<Updater.DownloadProgress>(ReportDownload);
            var installer = await Updater.Download(release, progress, cancel.Token);
            if(Owner is MainWindow owner&&owner.UpdateInstallationBlocker is { } pending)
            {
                Status.Text=pending;Progress.Visibility=Visibility.Collapsed;
                RestoreButtons();return;
            }
            Status.Text = "Installing — Pathweft will restart.";
            LaterButton.IsEnabled=false;
            if (!Updater.InstallAndRestart(installer))
            {
                Progress.Visibility = Visibility.Collapsed;
                Status.Text = "Update not installed: Windows asked for permission and it wasn't given. Try again whenever you like.";
                RestoreButtons();
                return;
            }
            Application.Current.Shutdown(); // the installer replaces our files; nothing of ours may stay
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            Progress.Visibility = Visibility.Collapsed;
            Status.Text = ex.Message;
            RestoreButtons();
        }
    }
    void RestoreButtons()
    {
        UpdateButton.Visibility=Visibility.Visible;
        UpdateButton.IsEnabled=LaterButton.IsEnabled=true;
        LaterButton.Content="Later";
    }
}
