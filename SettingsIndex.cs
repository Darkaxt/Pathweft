using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using FileExplorer.Search;

namespace FileExplorer;

// SEARCH-07: Settings > Windows integration > Pathweft index.
public partial class SettingsWindow
{
    internal sealed record IndexStoreRow(string Name, string Title, string Detail, string State, Brush StateBrush, bool Folder, bool CanRescan)
    {
        public Visibility RescanVisible => CanRescan ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RemoveVisible => Folder ? Visibility.Visible : Visibility.Collapsed;
    }
    internal IndexServiceStatus IndexServiceState { get; private set; }
    int indexRefresh;

    internal async Task RefreshIndex(string action = null)
    {
        int generation = ++indexRefresh;
        IndexStatus.Text = "Reading the index status…";
        IndexServiceStatus status = null; string failure = null;
        var service = IndexedSearch.Service;
        try { status = await service.StatusAsync(CancellationToken.None); }
        catch (InvalidOperationException ex) { failure = ex.Message; }
        if (generation != indexRefresh) return;
        IndexServiceState = status;
        bool canInstall = Settings.Profile == null && StartupTasks.ProtectedInstall && IndexServiceSetup.Shipped;
        if (status != null)
        {
            var updating = status.Stores.Where(s => s.State is not ("ready" or "offline" or "error")).ToList();
            IndexStatus.Text = $"Running · version {status.Version} · {status.Stores.Sum(s => (double)s.Items):N0} entries · {MainWindow.Fmt((long)status.PrivateBytes)} memory"
                + (updating.Count > 0 ? $" · updating {string.Join(", ", updating.Select(s => s.Name))}" : "");
            IndexStores.ItemsSource = status.Stores.OrderBy(s => s.Kind == "folder").ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(Row).ToList();
        }
        else
        {
            IndexStatus.Text = failure ?? (IndexServiceSetup.Installed ? service.LastConnectError ?? "Installed, but not answering." : "Not installed. Searches are unavailable and folder sizes are measured on disk.");
            IndexStores.ItemsSource = null;
        }
        IndexAddFolder.IsEnabled = status != null;
        IndexInstall.Visibility = canInstall && (!IndexServiceSetup.Installed || IndexServiceSetup.UpdateAvailable) ? Visibility.Visible : Visibility.Collapsed;
        IndexInstall.Content = IndexServiceSetup.Installed ? "Update service" : "Install service";
        IndexUninstall.Visibility = canInstall && IndexServiceSetup.Installed ? Visibility.Visible : Visibility.Collapsed;
        IndexActionStatus.Text = action ?? (Settings.Profile == null && !StartupTasks.ProtectedInstall
            ? "Installing the service needs the Program Files installation of Pathweft." : "");
    }


    IndexStoreRow Row(IndexStoreStatus store)
    {
        bool folder = store.Kind == "folder";
        string state = store.State switch
        {
            "ready" => "Ready", "loading" => "Loading", "reloading" => "Re-reading", "scanning" => "Scanning",
            "waiting-for-user" => "Waiting for sign-in", "offline" => "Offline", "error" => "Error", var other => other,
        };
        string resource = store.State switch { "ready" => null, "offline" or "error" => "RiskHigh", _ => "RiskCaution" };
        var brush = resource != null && TryFindResource(resource) is Brush risk ? risk : TryFindResource("Palette.E8EEF6") as Brush ?? Brushes.White;
        string detail = $"{(folder ? "Folder root" : "NTFS drive")} · {store.Items:N0} entries";
        if (store.Error is { Length: > 0 } error) detail += " · " + error;
        return new(store.Name, folder ? store.Name : store.Name + "\\", detail, state, brush, folder, store.State is "ready" or "error" or "offline");
    }

    async void IndexRefresh_Click(object sender, RoutedEventArgs e) => await RefreshIndex();

    async void IndexAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var pick = new Microsoft.Win32.OpenFolderDialog { Title = "Add a folder to the Pathweft index", Multiselect = false };
        if (pick.ShowDialog(this) != true) return;
        string path = Unc(Path.GetFullPath(pick.FolderName));
        await IndexCommand(service => service.FolderAddAsync(path, CancellationToken.None), $"Added {path}. It is searchable once its first scan finishes.");
    }

    async void IndexRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IndexStoreRow row) return;
        await IndexCommand(service => service.FolderRemoveAsync(row.Name, CancellationToken.None), $"Removed {row.Name} from the index.");
    }

    async void IndexRescan_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IndexStoreRow row) return;
        await IndexCommand(service => service.RescanAsync(row.Name, CancellationToken.None), $"Rescanning {row.Title}. The current entries keep answering until it finishes.");
    }

    async Task IndexCommand(Func<IndexServiceClient, Task> command, string done)
    {
        if (IndexedSearch.Service is not { } service) return;
        IndexActionStatus.Text = "Working…";
        try { await command(service); await RefreshIndex(done); }
        catch (InvalidOperationException ex) { IndexActionStatus.Text = ex.Message; }
    }

    async void IndexInstall_Click(object sender, RoutedEventArgs e) => await IndexSetup(true);
    async void IndexUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Remove the Pathweft index service? Searches are unavailable and folder sizes are measured on disk until it is installed again. Its folder roots are kept for a later install.", "Pathweft index", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await IndexSetup(false);
    }

    async Task IndexSetup(bool install)
    {
        IndexInstall.IsEnabled = IndexUninstall.IsEnabled = false;
        IndexActionStatus.Text = install ? "Installing the index service… The NTFS drives load in about a minute; folder roots take longer." : "Removing the index service…";
        string result;
        try { await AdministratorSession.SetIndexService(install); result = install ? "The index service is running." : "The index service is removed."; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { result = ex.Message; }
        finally { IndexInstall.IsEnabled = IndexUninstall.IsEnabled = true; }
        await RefreshIndex(result);
    }

    // Mapped network drives are per-user; the service needs the UNC path.
    static string Unc(string path)
    {
        string root = Path.GetPathRoot(path);
        if (root is not { Length: 3 } || root[1] != ':') return path;
        var buffer = new StringBuilder(1024); int length = buffer.Capacity;
        if (WNetGetConnectionW(root[..2], buffer, ref length) != 0) return path;
        return Path.Join(buffer.ToString(), path[3..]).TrimEnd('\\') is { Length: > 0 } unc ? unc : path;
    }
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)] static extern int WNetGetConnectionW(string local, StringBuilder remote, ref int length);
}
