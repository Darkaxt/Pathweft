using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

/// BROWSE-11: symbolic links, hard links and directory junctions, separate from .lnk shortcuts.
/// Reading a link never follows it into its target's contents; deleting one goes through the
/// normal delete, which removes the link itself.
internal static class Links
{
    internal enum Kind { Symbolic, Hard, Junction }
    internal static string Label(Kind kind) => kind switch { Kind.Symbolic => "Symbolic link", Kind.Hard => "Hard link", _ => "Junction" };

    /// "Symbolic link → target", "Junction → target (broken)", or "" for a plain item or a cloud placeholder.
    internal static string Describe(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0) return "";
        try
        {
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            if (info.LinkTarget is not { } target) return "";
            string kind = (attributes & FileAttributes.Directory) != 0 && IsJunction(path) ? "Junction" : "Symbolic link";
            string full = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", target));
            bool broken = !(File.Exists(full) || Directory.Exists(full));
            return $"{kind} → {target}{(broken ? "  (broken)" : "")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }
    /// The resolved target of a symbolic link or junction, or null.
    internal static string Target(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0) return null;
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            if (info.LinkTarget is not { } target) return null;
            return Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    const uint ReparseJunction = 0xA0000003;
    static bool IsJunction(string path)
    {
        using var handle = Open(path, write: false);
        if (handle.IsInvalid) return false;
        var buffer = new byte[16 * 1024];
        return DeviceIoControl(handle, FsctlGetReparsePoint, null, 0, buffer, buffer.Length, out _, IntPtr.Zero) && BitConverter.ToUInt32(buffer, 0) == ReparseJunction;
    }

    /// Why a kind can't be made here, or null. Symbolic links need Developer Mode or administrator rights;
    /// hard links need a file on the same volume; junctions need a local folder.
    internal static string Unavailable(Kind kind, string folder, string target)
    {
        bool targetDir = Directory.Exists(target), targetFile = File.Exists(target);
        switch (kind)
        {
            case Kind.Symbolic:
                return CanCreateSymbolicLinks() ? null : "Symbolic links need Developer Mode (Settings → System → For developers) or Pathweft running as administrator";
            case Kind.Hard:
                if (targetDir) return "A hard link can only point to a file";
                if (!targetFile) return "A hard link needs an existing file";
                if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(target)), Path.GetPathRoot(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase)) return "A hard link must be on the same drive as its file";
                return Supports(folder, 0x0) ? null : "This drive's filesystem doesn't support hard links";
            default:
                if (targetFile) return "A junction can only point to a folder";
                if (folder.StartsWith(@"\\", StringComparison.Ordinal)) return "A junction must be on a local drive";
                if (!Path.IsPathRooted(target) || target.StartsWith(@"\\", StringComparison.Ordinal)) return "A junction needs a folder on a local drive";
                return Supports(folder, 0x80) ? null : "This drive's filesystem doesn't support junctions";
        }
    }
    /// FILE_SUPPORTS_REPARSE_POINTS (0x80); 0 checks only that the volume is NTFS or ReFS for hard links.
    static bool Supports(string folder, uint flag)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(folder));
        var name = new StringBuilder(64);
        if (root == null || !GetVolumeInformation(root, null, 0, out _, out _, out uint flags, name, name.Capacity)) return false;
        return flag == 0 ? name.ToString() is "NTFS" or "ReFS" : (flags & flag) != 0;
    }
    static bool CanCreateSymbolicLinks()
    {
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
            if (key?.GetValue("AllowDevelopmentWithoutDevLicense") is int on && on != 0) return true;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// Makes the link at linkPath; target as typed (relative stays relative for symbolic links).
    internal static void Create(Kind kind, string linkPath, string target)
    {
        if (File.Exists(linkPath) || Directory.Exists(linkPath) || new FileInfo(linkPath).LinkTarget != null) throw new IOException(Path.GetFileName(linkPath) + " already exists");
        string full = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath) ?? "", target));
        switch (kind)
        {
            case Kind.Symbolic:
                if (Directory.Exists(full)) Directory.CreateSymbolicLink(linkPath, target); else File.CreateSymbolicLink(linkPath, target);
                break;
            case Kind.Hard:
                if (!CreateHardLink(linkPath, full, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                break;
            default:
                CreateJunction(linkPath, Path.GetFullPath(full));
                break;
        }
    }
    static void CreateJunction(string linkPath, string target)
    {
        Directory.CreateDirectory(linkPath);
        try
        {
            string substitute = @"\??\" + target.TrimEnd('\\') + (target.Length <= 3 ? "\\" : ""), print = target;
            byte[] sub = Encoding.Unicode.GetBytes(substitute), shown = Encoding.Unicode.GetBytes(print);
            int pathBuffer = sub.Length + 2 + shown.Length + 2;
            var data = new byte[8 + 8 + pathBuffer];
            BitConverter.GetBytes(ReparseJunction).CopyTo(data, 0);
            BitConverter.GetBytes((ushort)(8 + pathBuffer)).CopyTo(data, 4);
            BitConverter.GetBytes((ushort)0).CopyTo(data, 8);                       // substitute name offset
            BitConverter.GetBytes((ushort)sub.Length).CopyTo(data, 10);
            BitConverter.GetBytes((ushort)(sub.Length + 2)).CopyTo(data, 12);       // print name offset
            BitConverter.GetBytes((ushort)shown.Length).CopyTo(data, 14);
            sub.CopyTo(data, 16); shown.CopyTo(data, 16 + sub.Length + 2);
            using var handle = Open(linkPath, write: true);
            if (handle.IsInvalid || !DeviceIoControl(handle, FsctlSetReparsePoint, data, data.Length, null, 0, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { try { Directory.Delete(linkPath); } catch (IOException) { } throw; }
    }

    static SafeFileHandle Open(string path, bool write) =>
        CreateFile(path, write ? 0x40000000u : 0x80000000u, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero); // backup semantics, open the reparse point itself
    const uint FsctlSetReparsePoint = 0x000900A4, FsctlGetReparsePoint = 0x000900A8;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateHardLink(string link, string existing, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformation(string root, StringBuilder volumeName, int volumeNameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, int fileSystemSize);
}

/// New > Link…: name, target, kind and relative/absolute, with the target inspected as it is typed.
internal sealed class LinkDialog : AuxDialog
{
    readonly string folder;
    readonly TextBox name, target;
    readonly ComboBox kind;
    readonly CheckBox relative;
    readonly TextBlock inspection;
    LinkDialog(Window owner, string folder, string suggestedTarget) : base(owner, "New link", "Create", 520)
    {
        this.folder = folder;
        Body.Children.Add(Label("Target (a file or folder)", 0));
        target = Field(suggestedTarget ?? "", "Link target", out var targetFrame);
        var row = new DockPanel();
        var browseFile = Action("File…", () => Browse(folders: false)); var browseFolder = Action("Folder…", () => Browse(folders: true));
        browseFolder.Margin = new Thickness(6, 0, 0, 0); browseFile.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(browseFolder, Dock.Right); DockPanel.SetDock(browseFile, Dock.Right);
        row.Children.Add(browseFolder); row.Children.Add(browseFile); row.Children.Add(targetFrame);
        Body.Children.Add(row);
        inspection = Note("");
        Body.Children.Add(inspection);
        Body.Children.Add(Label("Kind"));
        kind = Choice("Link kind", Links.Label(Links.Kind.Symbolic), Links.Label(Links.Kind.Hard), Links.Label(Links.Kind.Junction));
        Body.Children.Add(kind);
        relative = Check("Store the target relative to this folder (symbolic links)", false);
        Body.Children.Add(relative);
        Body.Children.Add(Label("Link name"));
        name = Field(suggestedTarget is { Length: > 0 } ? Path.GetFileName(suggestedTarget.TrimEnd('\\')) : "", "Link name", out var nameFrame);
        Body.Children.Add(nameFrame);
        Body.Children.Add(Note("A link is not a copy: opening it opens the target. Deleting the link leaves the target alone."));
        target.TextChanged += (_, _) => { if (name.Text.Length == 0 || name.Tag as string == "auto") { name.Text = Path.GetFileName(target.Text.Trim().Trim('"').TrimEnd('\\')); name.Tag = "auto"; } Inspect(); };
        name.TextChanged += (_, _) => { if (name.IsKeyboardFocusWithin) name.Tag = null; };
        kind.SelectionChanged += (_, _) => Inspect();
        Loaded += (_, _) => { Inspect(); target.Focus(); target.SelectAll(); };
    }
    Links.Kind Selected => (Links.Kind)Math.Max(0, kind.SelectedIndex);
    string TargetPath => target.Text.Trim().Trim('"');
    string FullTarget => Path.IsPathRooted(TargetPath) ? TargetPath : Path.GetFullPath(Path.Combine(folder, TargetPath));

    void Browse(bool folders)
    {
        if (folders)
        {
            var pick = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = folder };
            if (pick.ShowDialog(this) == true) target.Text = pick.FolderName;
        }
        else
        {
            var pick = new Microsoft.Win32.OpenFileDialog { InitialDirectory = folder, DereferenceLinks = false };
            if (pick.ShowDialog(this) == true) target.Text = pick.FileName;
        }
    }
    /// Shows what the target is and enables only what can be made: a kind that can't is refused with its reason.
    void Inspect()
    {
        string why;
        if (TargetPath.Length == 0) why = "Type or choose the target.";
        else
        {
            string full;
            try { full = FullTarget; } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { inspection.Text = "The target isn't a valid path."; Accept.IsEnabled = false; return; }
            string what = Directory.Exists(full) ? "Folder" : File.Exists(full) ? "File" : null;
            string existing = Links.Describe(full, what == null ? 0 : File.GetAttributes(full));
            why = Links.Unavailable(Selected, folder, full);
            inspection.Text = (what == null ? "The target doesn't exist" + (Selected == Links.Kind.Symbolic ? "; the link will be broken until it does." : ".") : $"{what}: {full}")
                + (existing.Length > 0 ? $"\nThe target is itself a {existing}" : "")
                + (why != null ? "\n" + why : "");
            relative.IsEnabled = Selected == Links.Kind.Symbolic;
            Accept.IsEnabled = why == null && (what != null || Selected == Links.Kind.Symbolic);
            return;
        }
        inspection.Text = why; Accept.IsEnabled = false;
    }
    protected override bool Validate()
    {
        var linkName = name.Text.Trim();
        if (linkName.Length == 0 || linkName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { inspection.Text = "Choose a valid name for the link."; return false; }
        var linkPath = Path.Combine(folder, linkName);
        string stored = Selected == Links.Kind.Symbolic && relative.IsChecked == true ? Path.GetRelativePath(folder, FullTarget) : FullTarget;
        try { Links.Create(Selected, linkPath, stored); Created = linkPath; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        { inspection.Text = $"Couldn't create the {Links.Label(Selected).ToLowerInvariant()}: {ex.Message}"; return false; }
    }
    internal string Created { get; private set; }
    /// The path of the link made, or null.
    internal static string Ask(Window owner, string folder, string suggestedTarget)
    {
        var dialog = new LinkDialog(owner, folder, suggestedTarget);
        return dialog.Present() ? dialog.Created : null;
    }
}

/// BROWSE-11: Properties of a symbolic link or junction. Windows' own dialog describes the target, not the
/// link, so Pathweft shows the kind and the target first, with Windows' Properties one click away.
internal sealed class LinkPropertiesDialog : AuxDialog
{
    internal int Choice { get; private set; } = -1; // 0 open target location, 1 Windows Properties
    LinkPropertiesDialog(Window owner, string path, string description, string target) : base(owner, "Link properties", "Windows Properties…", 520)
    {
        int arrow = description.IndexOf(" \u2192 ", StringComparison.Ordinal);
        string kind = arrow > 0 ? description[..arrow] : description;
        bool broken = description.EndsWith("(broken)", StringComparison.Ordinal);
        Body.Children.Add(Label("Name", 0));
        Body.Children.Add(new TextBlock { Text = Path.GetFileName(path.TrimEnd('\\')), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) });
        Body.Children.Add(Label("Kind"));
        Body.Children.Add(new TextBlock { Text = kind, Margin = new Thickness(2, 0, 0, 0) });
        Body.Children.Add(Label("Target"));
        var targetText = new TextBox { Text = target ?? "", IsReadOnly = true, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        targetText.SetResourceReference(StyleProperty, "Bare");
        System.Windows.Automation.AutomationProperties.SetName(targetText, "Link target");
        Body.Children.Add(targetText);
        if (broken) Body.Children.Add(Note("The target is missing: the link is broken. Deleting the link never touches its target."));
        else Body.Children.Add(Note("Deleting the link removes only the link, never its target."));
        var open = Action("Open target location", () => { Choice = 0; DialogResult = true; });
        open.IsEnabled = !broken;
        DockPanel.SetDock(open, Dock.Left); Footer.Children.Insert(0, open);
    }
    protected override bool Validate() { Choice = 1; return true; }
    /// Shows the link's properties; returns 0 (open target location), 1 (Windows Properties) or -1.
    internal static int Show(Window owner, string path, string description, string target)
    {
        var dialog = new LinkPropertiesDialog(owner, path, description, target);
        return dialog.Present() ? dialog.Choice : -1;
    }
}
