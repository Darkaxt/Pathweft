using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

/// OPUS-01: one pane's tabs in a workspace, as the session keeps them (Settings.Tabs/ActiveTab/Pins).
internal sealed class WorkspacePane
{
    public List<string> Tabs { get; set; } = new();
    public int Active { get; set; }
    public List<int> Pinned { get; set; } = new();
}

/// A named set of left/right tabs and the active pane. Opening it replaces the unpinned tabs or adds to
/// them; removing it forgets the record only, never a folder or a file.
internal sealed class Workspace
{
    public string Name { get; set; } = "";
    public WorkspacePane Left { get; set; } = new();
    public WorkspacePane Right { get; set; } = new();
    public string ActivePane { get; set; } = "Left";
    /// OPUS-02: the linked roots when the workspace was saved with the panes linked (null: not linked).
    public string LinkLeft { get; set; }
    public string LinkRight { get; set; }
    internal int TabCount => Left.Tabs.Count + Right.Tabs.Count;
}

internal static class Workspaces
{
    internal static readonly JsonStore<Workspace> Store = new(() => Path.Combine(Settings.Dir, "workspaces.json"),
        list => list.Where(w => !string.IsNullOrWhiteSpace(w.Name) && w.Left != null && w.Right != null).ToList());
    internal static IReadOnlyList<Workspace> All => Store.All;
    internal static Workspace Find(string name) => All.FirstOrDefault(w => w.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
}

public partial class MainWindow
{
    Workspace CaptureWorkspace(string name)
    {
        WorkspacePane Pane(PaneView p) => new() { Tabs = p.OpenTabs.ToList(), Active = p.ActiveTab, Pinned = p.PinnedTabs };
        return new Workspace
        {
            Name = name, Left = Pane(Left), Right = Pane(Right), ActivePane = active == Right ? "Right" : "Left",
            LinkLeft = paneLink?.Left, LinkRight = paneLink?.Right,
        };
    }

    /// Save workspace…: the open tabs of both panes under a name. A name already used asks before replacing.
    internal void SaveWorkspace()
    {
        var name = PromptDialog.Ask(this, "Save workspace", "Name for the open tabs of both panes", "", "Save", selectAll: true);
        if (name == null) return;
        var existing = Workspaces.Find(name);
        if (existing != null && PromptDialog.Choose(this, $"Replace the workspace “{existing.Name}”?", "It will hold the tabs open now.", "Replace", "Cancel") != 0) return;
        var error = Workspaces.Store.Write(Workspaces.All.Where(w => w != existing).Append(CaptureWorkspace(name)).ToList());
        Status.Text = error == null ? $"Saved workspace “{name}”" : "Workspace not saved: " + error;
    }

    /// Replace: the unpinned tabs close and the workspace's open (pinned tabs stay). Add: its tabs join the open ones.
    internal void OpenWorkspace(Workspace workspace, bool replace)
    {
        int missing = 0;
        foreach (var (pane, saved) in new[] { (Left, workspace.Left), (Right, workspace.Right) })
        {
            if (saved.Tabs.Count == 0 || pane == Right && Settings.SinglePane && saved.Tabs.All(t => Right.FindTab(t) >= 0)) continue;
            int gone = saved.Tabs.Count(t => t != PaneView.ThisPC && !PaneView.IsVirtual(t) && !RemoteAddress.IsRemote(t) && !Directory.Exists(t));
            pane.OpenTabSet(saved.Tabs, saved.Active, saved.Pinned, replace);
            // in the pane, which keeps it while it shows this folder (the status bar is replaced by the folder's path)
            if (gone > 0) pane.ShowNotice($"Workspace “{workspace.Name}”: {gone:N0} folder{(gone == 1 ? " is" : "s are")} missing and opened at the nearest folder that exists");
            missing += gone;
        }
        var target = workspace.ActivePane == "Right" && !Settings.SinglePane ? Right : Left;
        SetActive(target); target.FocusList();
        if (workspace.LinkLeft != null && workspace.LinkRight != null) LinkPanes(workspace.LinkLeft, workspace.LinkRight, quiet: true);
        Status.Text = $"{(replace ? "Opened" : "Added")} workspace “{workspace.Name}”"
            + (missing > 0 ? $" · {missing:N0} folder{(missing == 1 ? " is" : "s are")} missing and opened at the nearest folder that exists" : "");
    }

    void RenameWorkspace(Workspace workspace)
    {
        var name = PromptDialog.Ask(this, "Rename workspace", "New name", workspace.Name, "Rename", selectAll: true);
        if (name == null || name == workspace.Name) return;
        if (Workspaces.Find(name) is { } other && other != workspace) { Status.Text = $"A workspace named “{other.Name}” already exists"; return; }
        Workspaces.Store.Put(new Workspace { Name = name, Left = workspace.Left, Right = workspace.Right, ActivePane = workspace.ActivePane, LinkLeft = workspace.LinkLeft, LinkRight = workspace.LinkRight }, workspace);
    }

    void UpdateWorkspace(Workspace workspace)
    {
        var updated = CaptureWorkspace(workspace.Name);
        Workspaces.Store.Put(updated, workspace);
        Status.Text = $"Workspace “{workspace.Name}” now holds the open tabs";
    }

    /// The sidebar's Workspaces section: click opens (replacing), the menu has the rest.
    void BuildWorkspaces()
    {
        WorkspaceList.Children.Clear();
        foreach (var saved in Workspaces.All)
        {
            var w = saved;
            var button = SideButton("", w.Name, "", click: () => OpenWorkspace(w, replace: true));
            button.ToolTip = $"{w.Name}\n{w.Left.Tabs.Count:N0} left · {w.Right.Tabs.Count:N0} right tab(s)\nClick replaces the unpinned tabs; middle click adds";
            button.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { OpenWorkspace(w, replace: false); e.Handled = true; } };
            var menu = new ContextMenu();
            menu.Items.Add(Item("Open (replace tabs)", "", "", () => OpenWorkspace(w, replace: true)));
            menu.Items.Add(Item("Add to open tabs", "", "Middle click", () => OpenWorkspace(w, replace: false)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Update with open tabs", "", "", () => UpdateWorkspace(w)));
            menu.Items.Add(Item("Rename…", "", "", () => RenameWorkspace(w)));
            menu.Items.Add(Item("Remove", "", "", () => { Workspaces.Store.Remove(w); Status.Text = $"Removed workspace “{w.Name}” (no folders or files were touched)"; }));
            button.ContextMenu = menu;
            WorkspaceList.Children.Add(button);
        }
        var save = SideButton("", "Save open tabs as workspace…", "", click: SaveWorkspace);
        save.Opacity = 0.75;
        WorkspaceList.Children.Add(save);
    }
}
