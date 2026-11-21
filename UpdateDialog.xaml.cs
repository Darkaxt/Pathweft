using System.IO;
using System.Net.Http;
using System.Windows;

namespace FileExplorer;

// "Update available": the new version's notes, Later or Update now. (What's new after an update is WhatsNewWindow.)
public partial class UpdateDialog : Window
{
    Updater.Release release;
    internal Updater.Release OfferedRelease=>release;
    internal string CheckState{get;private set;}
    readonly CancellationTokenSource cancel = new();
    Task<Updater.Choice> choice;
    CancellationTokenSource choosing;

    public UpdateDialog(Window owner, Updater.Release release=null)
    {
        InitializeComponent();
        Owner = owner;
        AuxiliaryWindows.Prepare(this,Root);
#if DEBUG
        HiddenTrialPresentation.Prepare(this);
#endif
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        if(release!=null)ShowRelease(release);else ShowChecking();
        Closed += (_, _) => cancel.Cancel();
    }

    void Later_Click(object s, RoutedEventArgs e) => Close();

    internal void ShowChecking()
    {
        choosing?.Cancel();choice=null;
        release=null;CheckState="checking";
        Heading.Text="Checking for updates…";Sub.Text=$"Installed version: Pathweft {Updater.Current}";
        Status.Text="";NotesCard.Visibility=UpdateButton.Visibility=Visibility.Collapsed;
        Progress.Visibility=Visibility.Visible;Progress.IsIndeterminate=true;
        LaterButton.Content="Close";LaterButton.IsEnabled=true;
        Height=Math.Min(MaxHeight,240*Math.Max(1,Settings.TextScale));MinHeight=200;
    }
    internal void ShowCheckResult(bool failed,string message)
    {
        CheckState=failed?"failed":"current";
        Heading.Text=failed?"Couldn't check for updates":$"Pathweft {Updater.Current} is up to date";
        Sub.Text=message;Status.Text="";Progress.Visibility=NotesCard.Visibility=Visibility.Collapsed;
        UpdateButton.Content=failed?"Retry":"Check again";UpdateButton.Visibility=Visibility.Visible;UpdateButton.IsEnabled=true;
        LaterButton.Content="Close";LaterButton.IsEnabled=true;
    }
    internal void ShowRelease(Updater.Release available,bool continueFull=false)
    {
        release=available;CheckState="available";
        Heading.Text=$"Pathweft {release.Version} is available";
        Notes.Text=string.IsNullOrWhiteSpace(release.Notes)?"No release notes.":release.Notes.Trim();
        NotesCard.Visibility=UpdateButton.Visibility=Visibility.Visible;Progress.Visibility=Visibility.Collapsed;Status.Text="";
        UpdateButton.Content="Update now";LaterButton.Content="Later";UpdateButton.IsEnabled=LaterButton.IsEnabled=true;
        Height=Math.Min(MaxHeight,440*Math.Max(1,Settings.TextScale));MinHeight=300;
        choosing?.Cancel();
        if(Settings.Portable!=null)Choose(new(release.Full,null));
        else if(continueFull)
        {
            // LIGHT-03: Pathweft was reopened by a light update that found changed files; the full one continues
            Choose(new(release.Full,"the light update couldn't be used on this installation"));
            Update_Click(null,null);
        }
        else choice=ChooseAsync();
    }
    void Choose(Updater.Choice chosen){choice=Task.FromResult(chosen);Sub.Text=PackageText(chosen);}
    internal Updater.Choice ChosenPackage=>choice is {IsCompletedSuccessfully:true} done?done.Result:null;
    static string PackageText(Updater.Choice chosen)=>chosen.Package.Light
        ?$"You have {Updater.Current}. The update is {MainWindow.Fmt(chosen.Package.Size)}: only the files that changed."
        :$"You have {Updater.Current}. The installer is {MainWindow.Fmt(chosen.Package.Size)}"+(chosen.Reason!=null?$" (full installer: {chosen.Reason}).":".");

    // LIGHT-02: the light package only when every installed file is the base release's; closing the window cancels the check
    async Task<Updater.Choice> ChooseAsync()
    {
        var mine=choosing=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        var offered=release;
        Sub.Text=$"You have {Updater.Current}. Checking the installed files to choose the smaller update…";
        Progress.Visibility=Visibility.Visible;Progress.IsIndeterminate=true;
        Updater.Choice result;
        try{result=await Updater.Choose(offered,new Progress<Updater.DownloadProgress>(ReportCheck),mine.Token);}
        catch(OperationCanceledException){throw;}
        catch(Exception error){System.Diagnostics.Trace.TraceWarning("Light update check: "+error.Message);result=new(offered.Full,"the installed files couldn't be checked");}
        if(ReferenceEquals(choosing,mine)&&ReferenceEquals(release,offered))
        {
            Sub.Text=PackageText(result);
            if(CheckState is "available" or "choosing"){Progress.Visibility=Visibility.Collapsed;Status.Text="";}
        }
        return result;
    }
    void ReportCheck(Updater.DownloadProgress progress)
    {
        if(CheckState is not ("available" or "choosing"))return;
        Progress.Visibility=Visibility.Visible;Progress.IsIndeterminate=progress.Total<=0;
        if(progress.Total>0)Progress.Value=Math.Clamp((double)progress.Received/progress.Total,0,1);
        Status.Text=progress.Total>0?$"Checking installed files · {Progress.Value*100:0}%":"Checking installed files…";
    }

    internal void BeginDownload()
    {
        CheckState="downloading";
        UpdateButton.Visibility=NotesCard.Visibility=Visibility.Collapsed;
        LaterButton.Content="Cancel";
        LaterButton.IsEnabled=true;
        Progress.Visibility=Visibility.Visible;Height=Math.Min(MaxHeight,260*Math.Max(1,Settings.TextScale));MinHeight=220;
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
        if(release==null){if(Owner is MainWindow main)await main.CheckForUpdate(manual:true);return;}
        // a portable copy isn't installed: its update is the new zip, unpacked over this folder (Data stays as it is)
        if (Settings.Portable != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://github.com/{Updater.Repo}/releases/tag/{release.Tag}") { UseShellExecute = true });
            Status.Text = "This copy is portable: download the portable zip from the page that just opened and unpack it over this folder. Your settings in Data stay.";
            return;
        }
        if(Owner is MainWindow current&&current.UpdateInstallationBlocker is { } blocker){Status.Text=blocker;return;}
        UpdateButton.IsEnabled=false;
        string installer=null;bool handedOff=false;
        try
        {
            // LIGHT-02: wait for a check still running; the package is decided before anything is downloaded
            Updater.Choice chosen;
            if(choice is {IsCompleted:false})
            {
                CheckState="choosing";UpdateButton.Visibility=NotesCard.Visibility=Visibility.Collapsed;LaterButton.Content="Cancel";
                Status.Text="Checking installed files…";
            }
            chosen=choice==null?new(release.Full,null):await choice;
            BeginDownload();
            var progress = new Progress<Updater.DownloadProgress>(ReportDownload);
            installer = await Updater.Download(chosen.Package, progress, cancel.Token);
            if(Owner is MainWindow owner&&owner.UpdateInstallationBlocker is { } pending)
            {
                Status.Text=pending;Progress.Visibility=Visibility.Collapsed;
                RestoreButtons();return;
            }
            Status.Text = "Installing — Pathweft will restart.";
            LaterButton.IsEnabled=false;
            if (!Updater.InstallAndRestart(installer,chosen.Package.Sha256))
            {
                Progress.Visibility = Visibility.Collapsed;
                Status.Text = "Update not installed: Windows asked for permission and it wasn't given. Try again whenever you like.";
                RestoreButtons();
                return;
            }
            handedOff=true;
            Application.Current.Shutdown(); // the installer replaces our files; nothing of ours may stay
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Progress.Visibility = Visibility.Collapsed;
            Status.Text = ex.Message;
            RestoreButtons();
        }
        finally{if(installer!=null&&!handedOff)UpdateDownloads.Discard(installer);}
    }
    void RestoreButtons()
    {
        UpdateButton.Visibility=Visibility.Visible;
        UpdateButton.IsEnabled=LaterButton.IsEnabled=true;
        LaterButton.Content="Later";
    }
}
