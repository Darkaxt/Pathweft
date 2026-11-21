using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;

namespace FileExplorer;

// Self-update, the way Shelf does it (vault: Shelf/Self-update.md):
//   • check 3 s after launch and every 60 minutes — this app stays open for days, and a release
//     published in the morning was otherwise invisible until a restart;
//   • a check that fails (offline, timeout, GitHub rate limit or 5xx, an unreadable answer) throws,
//     so it is never mistaken for "nothing newer": the manual check says it couldn't check, the
//     automatic one stays quiet and tries again next hour;
//   • an automatic check that finds a release only lights the dot on the sidebar update button
//     (UPDATE-03); "Later" just closes the window and the dot stays;
//   • updating verifies the installer size and published SHA-256, then starts
//     the protected installer with this process's identity and installation path.
//     Setup waits for actual exit, stops only the owned helper, and restarts it.
public static class Updater
{
    public const string Repo = "Darkaxt/Pathweft";

    public record Release(Version Version, string Tag, string Notes, string AssetUrl, long AssetSize, string Sha256, LightAsset Light = null)
    {
        public Package Full => new(AssetUrl, AssetSize, Sha256, $"Pathweft-{Version}-setup.exe", false);
    }
    /// LIGHT-01: the release's light package and its description (base version, base file list hash).
    public record LightAsset(string InfoUrl, string Url, long Size, string Sha256);
    /// What gets downloaded and run: the full installer or the light package.
    public record Package(string Url, long Size, string Sha256, string FileName, bool Light);
    /// LIGHT-02: the package chosen before downloading, and why the full installer when it is the full one.
    public record Choice(Package Package, string Reason);
    public record DownloadProgress(string Stage,long Received,long Total);

    // the self-test points this at an owned fixture installation
    internal static string InstalledRoot = AppContext.BaseDirectory;

    public static Version Current => typeof(Updater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(0, 0, 0);

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // the self-test points this at an unreachable host or a local server that answers 403
    internal static string Feed = $"https://api.github.com/repos/{Repo}/releases/latest";

    public const string CheckFailed = "Couldn't check for updates — no connection or GitHub is busy. Try again later.";

    /// True for the exceptions a failed check throws (as opposed to a bug).
    public static bool Failed(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException;

    /// The latest release when it is newer than what's running, else null. Throws when the check
    /// itself failed (see Failed). LIGHT-03: reinstall also returns the running version, for the full
    /// installer after a light update whose written files didn't match.
    public static async Task<Release> Check(bool manual,CancellationToken token=default,bool reinstall=false)
    {
        var release = await Latest(token);
        if (release == null) return null;
        if (release.Version < Current || (release.Version == Current && !reinstall)) return null;
        return release;
    }

    /// The latest release as published, whatever its version; null when it has no installer.
    /// Throws when GitHub couldn't be asked or its answer couldn't be read.
    public static async Task<Release> Latest(CancellationToken token=default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Feed);
        request.Headers.UserAgent.ParseAdd("Pathweft");
        using var response = await http.SendAsync(request,token);
        response.EnsureSuccessStatusCode(); // 403 (rate limit) and 5xx are failures, not "nothing new"
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) throw new JsonException($"tag {tag} is not a version");
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith("setup.exe", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object) return null; // a release without an installer: nothing to install
        // GitHub publishes each asset's SHA-256 as "sha256:<hex>"; older assets have none
        static string Digest(JsonElement a) => a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString() is { } value && value.StartsWith("sha256:") ? value[7..] : null;
        JsonElement Named(string name) => root.GetProperty("assets").EnumerateArray().FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase));
        LightAsset light = null;
        var info = Named($"Pathweft-{version}-update.json");
        var package = Named($"Pathweft-{version}-update.exe");
        if (info.ValueKind == JsonValueKind.Object && package.ValueKind == JsonValueKind.Object)
            light = new(info.GetProperty("browser_download_url").GetString(), package.GetProperty("browser_download_url").GetString(), package.GetProperty("size").GetInt64(), Digest(package));
        return new Release(version, tag, root.GetProperty("body").GetString() ?? "",
                           asset.GetProperty("browser_download_url").GetString(), asset.GetProperty("size").GetInt64(),
                           Digest(asset), light);
    }

    /// LIGHT-02: decide before downloading. The light package only when this installation is exactly the release
    /// it was built from: the same version, the same release-manifest.json and every file it lists unchanged.
    /// Anything else means the full installer, with the reason. Only cancellation throws.
    public static async Task<Choice> Choose(Release release, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        if (release.Light == null) return new(release.Full, "no light update was published for this release");
        if (release.Light.Sha256 == null) return new(release.Full, "the light update has no published checksum");
        JsonElement description;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Light.InfoUrl);
            request.Headers.UserAgent.ParseAdd("Pathweft");
            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            description = json.RootElement.Clone();
        }
        catch (Exception error) when (error is HttpRequestException or JsonException || (error is TaskCanceledException && !token.IsCancellationRequested))
        { return new(release.Full, "the light update's description couldn't be read"); }
        string baseText, manifestSha, installer, sha; long bytes;
        try
        {
            if (description.GetProperty("version").GetString() != release.Version.ToString()) return new(release.Full, "the light update describes another release");
            baseText = description.GetProperty("base").GetString(); manifestSha = description.GetProperty("baseManifestSha256").GetString();
            installer = description.GetProperty("installer").GetString(); sha = description.GetProperty("sha256").GetString(); bytes = description.GetProperty("bytes").GetInt64();
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException)
        { return new(release.Full, "the light update's description is incomplete"); }
        if (!Version.TryParse(baseText, out var from) || installer != $"Pathweft-{release.Version}-update.exe" || bytes != release.Light.Size || !string.Equals(sha, release.Light.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(release.Full, "the light update's description doesn't match its download");
        if (from != Current) return new(release.Full, $"the light update only updates {from}");
        string listed = Path.Combine(InstalledRoot, "release-manifest.json");
        if (!File.Exists(listed)) return new(release.Full, "this installation has no file list");
        byte[] manifest = await File.ReadAllBytesAsync(listed, token);
        if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifest)).Equals(manifestSha, StringComparison.OrdinalIgnoreCase))
            return new(release.Full, $"the installed file list isn't the one of Pathweft {from}");
        var files = new List<(string Path, long Bytes, string Sha256)>();
        try
        {
            using var parsed = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(manifest).TrimStart((char)0xFEFF));
            foreach (var file in parsed.RootElement.GetProperty("files").EnumerateArray())
                files.Add((file.GetProperty("path").GetString(), file.GetProperty("bytes").GetInt64(), file.GetProperty("sha256").GetString()));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { return new(release.Full, "the installed file list can't be read"); }
        string root = Path.GetFullPath(InstalledRoot);
        long total = files.Sum(file => file.Bytes), done = 0, reported = -1;
        string differs = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        progress?.Report(new("Checking installed files", 0, total));
        try
        {
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stop.Token }, async (file, cancel) =>
            {
                string path = Path.GetFullPath(Path.Combine(root, file.Path));
                bool same = false;
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    same = stream.Length == file.Bytes && Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancel)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
                }
                if (!same) { Interlocked.CompareExchange(ref differs, file.Path, null); stop.Cancel(); return; }
                long now = Interlocked.Add(ref done, file.Bytes), percent = total > 0 ? now * 100 / total : 100;
                if (Interlocked.Exchange(ref reported, percent) != percent) progress?.Report(new("Checking installed files", now, total));
            });
        }
        catch (OperationCanceledException) when (differs != null && !token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(release.Full, "an installed file couldn't be read"); }
        if (differs != null) return new(release.Full, $"an installed file differs from Pathweft {from} ({differs})");
        return new(new(release.Light.Url, release.Light.Size, release.Light.Sha256, installer, true), null);
    }


    /// Downloads the full installer, reporting progress. Returns its path.
    public static Task<string> Download(Release release, IProgress<DownloadProgress> progress, CancellationToken token) => Download(release.Full, progress, token);

    /// Downloads the chosen package (full installer or light update), reporting progress. Returns its path.
    public static async Task<string> Download(Package release, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        var path = UpdateDownloads.Create(release.FileName);
        bool complete=false;
        try
        {
        using (var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;
            using var source = await response.Content.ReadAsStreamAsync(token);
            using var file = File.Create(path);
            var buffer = new byte[128 * 1024];
            long done = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), token);
                done += n;
                progress?.Report(new("Downloading",done,total));
            }
        }
        // Size check, as in Shelf: a truncated download would otherwise be run as an installer.
        progress?.Report(new("Checking update",0,0));
        var length = new FileInfo(path).Length;
        if (length != release.Size)
        {
            File.Delete(path);
            throw new IOException($"Download is {MainWindow.Fmt(length)}, expected {MainWindow.Fmt(release.Size)}");
        }
        // The size catches a cut-off download; the checksum catches everything else. This file is about
        // to be run as an installer, so one that differs from what was published never is.
        if (release.Sha256 != null)
        {
            string actual;
            using (var file = File.OpenRead(path)) actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file,token));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new IOException("The download doesn't match the checksum GitHub published for it");
            }
        }
        token.ThrowIfCancellationRequested();
        complete=true;return path;
        }
        finally{if(!complete)UpdateDownloads.Discard(path);}
    }

    /// Start the protected update. Setup observes this exact process's exit;
    /// manual installs do not force-close other windows or active transfers.
    public static bool InstallAndRestart(string installer,string expectedSha256)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(expectedSha256))throw new IOException("Protected updates require a published SHA-256 checksum.");
            // Keep the checked file non-writable from hashing through elevated
            // process creation, when Windows maps the installer image.
            using var held=new FileStream(installer,FileMode.Open,FileAccess.Read,FileShare.Read);
            string actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(held));
            if(!actual.Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The installer changed after download verification.");
            var launch=new ProcessStartInfo(installer){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            using var caller=Process.GetCurrentProcess();
            foreach(var argument in InstallationArguments(caller.Id,caller.StartTime.ToUniversalTime().ToFileTimeUtc(),AppContext.BaseDirectory))launch.ArgumentList.Add(argument);
            using var process=Process.Start(launch)??throw new IOException("The installer did not start.");
            try{UpdateDownloads.Handoff(installer,process);}
            catch
            {
                // Setup is waiting for this exact caller to exit. If its durable
                // handoff cannot be saved, retire only that uncommitted launch.
                if(!process.HasExited)process.Kill(entireProcessTree:true);
                process.WaitForExit();throw;
            }
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; } // ERROR_CANCELLED: UAC declined
    }
    internal static string[] InstallationArguments(int pid,long created,string directory)
    {
        static string ColorArgument(string name,Color value)=>"/UPDATE"+name+"="+(value.R|value.G<<8|value.B<<16);
        var background=Appearance.Background;
        var track=Appearance.Resolve("#1AFFFFFF");
        byte Blend(byte foreground,byte behind)=>(byte)((foreground*track.A+behind*(255-track.A)+127)/255);
        var opaqueTrack=Color.FromRgb(Blend(track.R,background.R),Blend(track.G,background.G),Blend(track.B,background.B));
        return new[]{"/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/RESTARTAPP=1","/WAITPID="+pid,
            "/WAITCREATED="+created.ToString("X16"),"/DIR="+directory.TrimEnd('\\'),
            ColorArgument("BACK",Appearance.Background),ColorArgument("INK",Appearance.Resolve("#E8EEF6")),
            ColorArgument("MUTED",Appearance.Resolve("#8A97AA")),ColorArgument("ACCENT",Appearance.Accent),ColorArgument("TRACK",opaqueTrack)};
    }
}
