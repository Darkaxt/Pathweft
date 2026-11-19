#if DEBUG
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FileExplorer;

// S20 qualification ("tools" scenario): every TOOLS and ARCHIVE-13/14 check, plus the new commands' places and icons in
// the real grouped menus. Everything stays under `root`.
public partial class MainWindow
{
    internal async Task VerifyTools20(string root, Action<bool, string> strict)
    {
        // a census: every check is recorded, a failure doesn't stop the others; the run fails at the end with all of them
        var failures = new List<string>();
        void check(bool ok, string label) { if (ok) strict(true, label); else failures.Add(label); }
        await Step("archive", () => VerifyArchiveTools20(Path.Combine(root, "archive"), check));
        await Step("rename-locksmith", () => VerifyRenameLocksmith20(Path.Combine(root, "rename-locksmith"), check));
        await Step("resize", () => VerifyImageResize20(Path.Combine(root, "resize"), check));
        await Step("paste-new", () => VerifyPasteNew20(Path.Combine(root, "paste-new"), check));
        await Step("preview", () => VerifyPreviewTools20(Path.Combine(root, "preview"), check));
        await Step("menus", () => VerifyToolMenus20(Path.Combine(root, "menus"), check));
        strict(failures.Count == 0, "S20: every tools check passed" + string.Concat(failures.Select(f => " | failed: " + f)));

        async Task Step(string name, Func<Task> run)
        {
            try { await run(); }
            catch (Exception e) { check(false, $"S20 {name} checks ran to the end: {e.GetType().Name}: {e.Message}"); }
        }
    }

    async Task VerifyToolMenus20(string root, Action<bool, string> check)
    {
        var dir = Directory.CreateDirectory(root).FullName;
        var png = Path.Combine(dir, "picture.png");
        var bitmap = BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16);
        using (var s = File.Create(png)) { var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(bitmap)); e.Save(s); }
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "notes");

        static IEnumerable<MenuItem> All(ItemsControl menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) { yield return item; foreach (var child in All(item)) yield return child; }
        }
        static bool HasIcon(MenuItem item) => item.Icon is TextBlock { Text.Length: > 0 } or Image;
        static MenuItem Group(ContextMenu menu, string name) => menu.Items.OfType<MenuItem>().SingleOrDefault(i => i.Header as string == name);
        static MenuItem In(MenuItem group, string name) => group?.Items.OfType<MenuItem>().SingleOrDefault(i => i.Header as string == name);
        ContextMenu MenuOn(PaneView pane)
        {
            SetActive(pane); archiveMenuForVerification = null; captureS4Menu = true;
            try { pane.S4ContextAction(); } finally { captureS4Menu = false; }
            return archiveMenuForVerification ?? throw new IOException("Context menu was not constructed");
        }
        void Closed(ContextMenu menu) => menu.RaiseEvent(new System.Windows.RoutedEventArgs(ContextMenu.ClosedEvent));

        await S1Verification.Navigate(Left, dir);
        SetActive(Left);
        Left.SetSelection(e => e.Name == "picture.png");
        var menu = MenuOn(Left);
        try
        {
            var tools = Group(menu, "Tools");
            var resize = In(tools, "Resize images…");
            var using_ = In(tools, "What's using this?");
            check(resize != null && HasIcon(resize) && using_ != null && HasIcon(using_),
                "TOOLS-02/03: Resize images… and What's using this? are in the Tools group with icons");
            check(!All(menu).Any(i => i.Header as string == "Checksum (SHA-256)") && All(menu).Any(i => i.Header as string == "Checksum" && HasIcon(i)),
                "ARCHIVE-13: the Checksum submenu with an icon replaces Checksum (SHA-256)");
            var compress = All(menu).SingleOrDefault(i => i.Header as string == "Compress…");
            check(compress != null && HasIcon(compress), "ARCHIVE-13: Compress… has an icon");
        }
        finally { Closed(menu); }

        Left.SetSelection(e => e.Name == "notes.txt");
        menu = MenuOn(Left);
        try { check(In(Group(menu, "Tools"), "Resize images…") == null, "TOOLS-03: Resize images… isn't offered for a text file"); }
        finally { Closed(menu); }

        Left.SetSelection(_ => false);
        menu = MenuOn(Left);
        try
        {
            var pasteAs = menu.Items.OfType<MenuItem>().SingleOrDefault(i => i.Header as string == "Paste as file");
            var fromTemplate = In(Group(menu, "New"), "New from template");
            check(pasteAs != null && HasIcon(pasteAs), "TOOLS-06: Paste as file sits beside Paste in the folder menu, with an icon");
            check(fromTemplate != null && HasIcon(fromTemplate) && fromTemplate.Items.Count > 0,
                "TOOLS-07: New from template is in the New group, with an icon and its template entries");
            var tools = Group(menu, "Tools");
            var renameAll = In(tools, "Rename all…");
            var resizeHere = In(tools, "Resize images…");
            var usingHere = In(tools, "What's using this?");
            check(renameAll is { IsEnabled: true } && HasIcon(renameAll) && resizeHere != null && HasIcon(resizeHere) && usingHere != null && HasIcon(usingHere),
                "TOOLS-01..03: a folder's background menu has Rename all…, Resize images… (it holds a picture) and What's using this? in Tools, with icons");
            check(!All(menu).Any(i => i.Header is string h && MainWindow.PathweftHasPowerToy(ShellMenu.Comparable(h))),
                "TOOLS-01..03: the background menu has no PowerToys PowerRename, File Locksmith or Image Resizer entry");
        }
        finally { Closed(menu); }
    }
}
#endif
