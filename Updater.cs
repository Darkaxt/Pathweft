using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace FileExplorer;

// Self-update, the way Shelf does it (vault: Shelf/Self-update.md):
//   • check 3 s after launch and every 60 minutes — this app stays open for days, and a release
//     published in the morning was otherwise invisible until a restart;
//   • a check that fails (offline, timeout, GitHub rate limit or 5xx, an unreadable answer) throws,
//     so it is never mistaken for "nothing newer": the manual check says it couldn't check, the
//     automatic one stays quiet and tries again next hour;
//   • "Later" silences that one version for this session only, and the manual check in Settings
//     ignores it, because asking explicitly deserves an answer;
//   • updating downloads the release's installer, checks its size and SHA-256 against the GitHub asset,
//     starts it through UAC (per machine since 1.0.48) and quits: nothing in this process can outlive
//     the installer that is about to overwrite it. The installer waits for us to go and starts the app
//     again itself (a [Run] entry for silent installs in Pathweft.iss, as the signed-in user).
public static class Updater
{
    public const string Repo = "Darkaxt/Pathweft";

    public record Release(Version Version, string Tag, string Notes, string AssetUrl, long AssetSize, string Sha256);

    public static Version Current => typeof(Updater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(0, 0, 0);
    static string dismissed; // tag the user said "Later" to; deliberately not persisted

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // the self-test points this at an unreachable host or a local server that answers 403
    internal static string Feed = $"https://api.github.com/repos/{Repo}/releases/latest";

    public const string CheckFailed = "Couldn't check for updates — no connection or GitHub is busy. Try again later.";

    /// True for the exceptions a failed check throws (as opposed to a bug).
    public static bool Failed(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException;

    /// The latest release when it is newer than what's running, else null. Throws when the check
    /// itself failed (see Failed).
    public static async Task<Release> Check(bool manual)
    {
        var release = await Latest();
        if (release == null) return null;
        if (release.Version <= Current) return null;
        if (!manual && release.Tag == dismissed) return null; // "Later" must not become a nag
        return release;
    }

    /// The latest release as published, whatever its version; null when it has no installer.
    /// Throws when GitHub couldn't be asked or its answer couldn't be read.
    public static async Task<Release> Latest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Feed);
        request.Headers.UserAgent.ParseAdd("Pathweft");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode(); // 403 (rate limit) and 5xx are failures, not "nothing new"
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) throw new JsonException($"tag {tag} is not a version");
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith("setup.exe", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object) return null; // a release without an installer: nothing to install
        // GitHub publishes each asset's SHA-256 as "sha256:<hex>"; older assets have none
        var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        return new Release(version, tag, root.GetProperty("body").GetString() ?? "",
                           asset.GetProperty("browser_download_url").GetString(), asset.GetProperty("size").GetInt64(),
                           digest?.StartsWith("sha256:") == true ? digest[7..] : null);
    }

    public static void Dismiss(Release release) => dismissed = release.Tag;

    /// Downloads the installer, reporting 0..1 progress. Returns its path.
    public static async Task<string> Download(Release release, IProgress<double> progress, CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Pathweft-{release.Version}-setup.exe");
        using (var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.AssetSize;
            using var source = await response.Content.ReadAsStreamAsync(token);
            using var file = File.Create(path);
            var buffer = new byte[128 * 1024];
            long done = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), token);
                done += n;
                progress?.Report(total > 0 ? (double)done / total : 0);
            }
        }
        // Size check, as in Shelf: a truncated download would otherwise be run as an installer.
        var length = new FileInfo(path).Length;
        if (length != release.AssetSize)
        {
            File.Delete(path);
            throw new IOException($"Download is {MainWindow.Fmt(length)}, expected {MainWindow.Fmt(release.AssetSize)}");
        }
        // The size catches a cut-off download; the checksum catches everything else. This file is about
        // to be run as an installer, so one that differs from what was published never is.
        if (release.Sha256 != null)
        {
            string actual;
            using (var file = File.OpenRead(path)) actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new IOException("The download doesn't match the checksum GitHub published for it");
            }
        }
        return path;
    }

    /// Starts the installer and returns true; the caller then quits so it can replace us. Since 1.0.48 it installs per
    /// machine, so it goes through ShellExecute and the UAC prompt (CreateProcess would fail with error 740). It waits for
    /// this window to close, closes the agent itself and starts Pathweft again ([Run] with WizardSilent, Pathweft.iss).
    /// False when the user said no to the prompt: nothing changed, the app carries on.
    public static bool InstallAndRestart(string installer)
    {
        try
        {
            // Not Verb = "runas": started elevated, Setup has no un-elevated user to go back to, and its runasoriginaluser restart
            // brought the app back as administrator (26.10.2026: Pathweft, whose pipe then refused every hand-off). Setup asks for
            // elevation itself (PrivilegesRequired=admin, asInvoker manifest) and restarts the app as the user.
            Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; } // ERROR_CANCELLED: UAC declined
    }
}
