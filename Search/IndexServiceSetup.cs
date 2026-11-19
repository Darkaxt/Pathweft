using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace FileExplorer.Search;

// SEARCH-07/08: the service binaries ship in <app>\services\file_index, and `install` registers the LocalSystem
// service where they are (S19; an earlier registration in %ProgramFiles%\PathweftIndex is moved here and that copy
// removed). Installing, updating and removing run only inside the approved administrator session and only from the
// Program Files installation.
internal static class IndexServiceSetup
{
    internal static string ShippedExecutable => Path.Combine(AppContext.BaseDirectory, "services", "file_index", "pathweft-indexd.exe");
    // The binary the registered service runs, from its service entry; null when it is not installed.
    internal static string InstalledExecutable
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PathweftIndex");
                if (key?.GetValue("ImagePath") is not string image) return null;
                image = Environment.ExpandEnvironmentVariables(image.Trim());
                return image.StartsWith('"') ? image[1..Math.Max(1, image.IndexOf('"', 1))] : image.Split(' ')[0];
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
        }
    }
    internal static bool Shipped => File.Exists(ShippedExecutable);
    internal static bool Installed => InstalledExecutable is { } path && File.Exists(path);

    // True when the installed service binary differs from the one this Pathweft ships.
    internal static bool UpdateAvailable
    {
        get
        {
            if (!Shipped || InstalledExecutable is not { } installed || !File.Exists(installed)) return false;
            if (!Path.GetFullPath(installed).Equals(Path.GetFullPath(ShippedExecutable), StringComparison.OrdinalIgnoreCase)) return true; // the earlier separate copy
            try { return !Hash(ShippedExecutable).AsSpan().SequenceEqual(Hash(installed)); }
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
