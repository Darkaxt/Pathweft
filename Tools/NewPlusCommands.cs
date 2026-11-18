using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;

// New from template (TOOLS-07): New+ templates in New file (Ctrl+N), New folder and the folder background menu.
// Templates are only copied (NewPlus.Create), never opened; the copy is selected and renamed in place, as Explorer does
// with a new item. Undo sends it to the Recycle Bin while it still is what was created.
public partial class MainWindow
{
    /// The template folder; the default one is made (empty) the first time it is used.
    internal static string TemplateFolder()
    {
        if (Settings.NewPlusFolder is { Length: > 0 } chosen) return chosen;
        try { Directory.CreateDirectory(NewPlus.DefaultFolder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return NewPlus.DefaultFolder;
    }

    /// Each template with the title the settings ask for.
    internal static IReadOnlyList<(NewTemplate Template, string Title)> Templates()
    {
        var now = DateTime.Now;
        return NewPlus.Discover(TemplateFolder())
            .Select(t => (t, NewPlus.MenuTitle(t, !Settings.NewPlusHideExtension, !Settings.NewPlusHideDigits, Settings.NewPlusVariables, now))).ToList();
    }

    internal void OpenTemplatesFolder() => active.Navigate(TemplateFolder());

    /// The folder background menu's "New from template" submenu.
    internal IEnumerable<MenuItem> TemplateMenuItems()
    {
        bool writable = active.ArchiveRoot == null && !PaneView.IsVirtual(active.Dir);
        var items = Templates().Select(t => Item(t.Title, t.Template.IsFolder ? "" : "", "", () => _ = CreateFromTemplate(t.Template), writable)).ToList();
        if (items.Count == 0) items.Add(Item("No templates", "", "", () => { }, false));
        items.Add(Item("Open templates folder", "", "", OpenTemplatesFolder));
        return items;
    }

    Task templateCreation = Task.CompletedTask; // the last creation, for verification to wait on

    /// Creates `t` in the active folder (a folder template off the UI thread), then selects it for renaming.
    internal Task CreateFromTemplate(NewTemplate t) => templateCreation = CreateFromTemplate(t, active);

    async Task CreateFromTemplate(NewTemplate t, PaneView pane)
    {
        if (WritableFolder(pane, "New from template") is not { } dir) return;
        bool resolve = Settings.NewPlusVariables, hideDigits = Settings.NewPlusHideDigits;
        string made;
        try
        {
            if (t.IsFolder)
            {
                Status.Text = $"Creating {Path.GetFileName(t.Path)}…";
                made = await Task.Run(() => NewPlus.Create(t, dir, DateTime.Now, resolve, CancellationToken.None, hideDigits));
            }
            else made = NewPlus.Create(t, dir, DateTime.Now, resolve, CancellationToken.None, hideDigits);
        }
        catch (OperationCanceledException) { Status.Text = "New from template cancelled"; return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // NewPlus has already removed whatever it had made of the new item
            Status.Text = $"Couldn't create from {Path.GetFileName(t.Path)}: {ex.Message}";
            Left.Refresh(); Right.Refresh();
            return;
        }
        Remember($"new from template {Path.GetFileName(made)}", Made(made));
        Status.Text = $"Created {Path.GetFileName(made)}";
        ShowAndRename(pane, made);
    }

    // Selected, then the name box, as Explorer does after New. The listing may arrive later than the call, so the rename
    // starts when the pane shows the new item selected; if it never does (the user went elsewhere), nothing is renamed.
    void ShowAndRename(PaneView pane, string path)
    {
        string name = Path.GetFileName(path);
        bool Ready() => pane.Dir.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase)
                        && pane.List.SelectedItem is Entry e && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        void Shown()
        {
            if (!Ready()) return;
            pane.StatsChanged -= Shown; timeout.Stop();
            Dispatcher.BeginInvoke(() => { if (Ready()) pane.BeginRename(); }, DispatcherPriority.Loaded); // its row exists by then
        }
        timeout.Tick += (_, _) => { timeout.Stop(); pane.StatsChanged -= Shown; };
        pane.StatsChanged += Shown;
        timeout.Start();
        ShowCreated(pane, path);
        Shown();
    }
}
