using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace FileExplorer.Search;

// SEARCH-07/08: the service binaries ship in <app>\index; `install` copies them to
// %ProgramFiles%\PathweftIndex and registers the LocalSystem service. Installing,
// updating and removing run only inside the approved administrator session and
// only from the Program Files installation.
internal static class IndexServiceSetup
{
    internal static string ShippedExecutable => Path.Combine(AppContext.BaseDirectory, "index", "pathweft-indexd.exe");
    internal static string InstalledExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PathweftIndex", "pathweft-indexd.exe");
    internal static bool Shipped => File.Exists(ShippedExecutable);
    internal static bool Installed => File.Exists(InstalledExecutable);

    // True when the installed service binary differs from the one this Pathweft ships.
    internal static bool UpdateAvailable
    {
        get
        {
            if (!Shipped || !Installed) return false;
            try { return !Hash(ShippedExecutable).AsSpan().SequenceEqual(Hash(InstalledExecutable)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }
    static byte[] Hash(string path) { using var stream = File.OpenRead(path); return SHA256.HashData(stream); }

    internal static ProcessStartInfo Command(bool install) => new(ShippedExecutable, install ? "install" : "uninstall")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(ShippedExecutable) };

    // Runs inside the elevated administrator session.
    internal static string Run(bool install)
    {
        if (!StartupTasks.ProtectedInstall) throw new InvalidOperationException("The index service can be installed only from the Program Files installation of Pathweft.");
        if (!Shipped) throw new FileNotFoundException("This Pathweft does not include the index service.", ShippedExecutable);
        using var process = Process.Start(Command(install)) ?? throw new InvalidOperationException("The index service installer did not start.");
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(180_000)) { process.Kill(); throw new TimeoutException("The index service installer did not finish within 3 minutes."); }
        string message = (error.Result.Trim() is { Length: > 0 } e ? e : output.Trim()).Split('\n').LastOrDefault()?.Trim() ?? "";
        if (process.ExitCode != 0) throw new InvalidOperationException(message.Length > 0 ? message : $"The index service installer failed (exit {process.ExitCode}).");
        return install ? "The Pathweft index service is installed and running" : "The Pathweft index service is removed";
    }
}
