using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

/// OPUS-07: a short name for a folder, typed in the address field as @name (or @name\below\it). A real path
/// always wins: @name is looked up only when no file or folder of that name is where the address points.
/// An alias whose folder is missing or offline says so and goes nowhere; it is never redirected. An alias
/// is a way to get somewhere: renaming or removing one never touches the folder.
internal sealed class PathAlias
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
}

internal static class Aliases
{
    internal static readonly Regex Valid = new(@"^[\p{L}\p{N}_-]{1,40}$");
    internal static readonly JsonStore<PathAlias> Store = new(() => Path.Combine(Settings.Dir, "aliases.json"),
        list => list.Where(a => Valid.IsMatch(a.Name ?? "") && !string.IsNullOrWhiteSpace(a.Target))
                    .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList());
    internal static IReadOnlyList<PathAlias> All => Store.All;
    internal static PathAlias Find(string name) => All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// "@name" or "@name\rest": the alias and the path it stands for; null when the text is not one of them.
    internal static (PathAlias Alias, string Path)? Expand(string text)
    {
        if (text is not ['@', ..]) return null;
        int cut = text.IndexOfAny(new[] { '\\', '/' });
        var name = cut < 0 ? text[1..] : text[1..cut];
        if (Find(name) is not { } alias) return null;
        var rest = cut < 0 ? "" : text[(cut + 1)..].Trim('\\', '/');
        return (alias, rest.Length == 0 ? alias.Target : Path.Combine(alias.Target, rest));
    }

    /// Null when the name can be used; else why not.
    internal static string Problem(string name, PathAlias except = null) =>
        !Valid.IsMatch(name) ? "Use letters, digits, - and _ only (no spaces), up to 40 characters"
        : Find(name) is { } other && other != except ? $"@{other.Name} already exists" : null;
}

public partial class MainWindow
{
    /// Folder menu → Add alias for this folder…
    internal void AddAlias(string folder)
    {
        string suggested = Regex.Replace(Path.GetFileName(folder.TrimEnd('\\')), @"[^\p{L}\p{N}_-]+", "-").Trim('-').ToLowerInvariant();
        string label = $"Type @name in the address to go to {folder}";
        while (true)
        {
            var name = PromptDialog.Ask(this, "Add alias", label, suggested, "Add", selectAll: true)?.Trim().TrimStart('@');
            if (name == null) return;
            if (Aliases.Problem(name) is { } problem) { label = problem + ". Try another name."; suggested = name; continue; }
            Aliases.Store.Put(new PathAlias { Name = name, Target = folder.TrimEnd('\\') is { Length: > 2 } t ? t : folder });
            Status.Text = $"@{name} goes to {folder}";
            return;
        }
    }

    internal void ManageAliases() => new AliasesWindow(this).Present();

    internal sealed class AliasesWindow : AuxDialog
    {
        readonly MainWindow main;
        readonly ListBox list = new() { Height = 280, Margin = new Thickness(0, 6, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly Button rename, retarget, remove;
        readonly TextBlock note = Note("");

        internal AliasesWindow(MainWindow main) : base(main, "Path aliases", "Close", 560)
        {
            this.main = main;
            foreach (var b in Footer.Children.OfType<Button>().Where(b => b != Accept)) b.Visibility = Visibility.Collapsed;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            System.Windows.Automation.AutomationProperties.SetName(list, "Aliases");
            Body.Children.Add(Label("Type @name in the address field to go to its folder. A real file or folder with the same name wins.", 0));
            Body.Children.Add(list);
            Body.Children.Add(note);
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(Action("Add for current folder…", () => { main.AddAlias(main.active.Dir); Fill(); }));
            actions.Children.Add(rename = Action("Rename…", Rename));
            actions.Children.Add(retarget = Action("Point to current folder", Retarget));
            actions.Children.Add(remove = Action("Remove", Remove));
            Body.Children.Add(actions);
            list.SelectionChanged += (_, _) => Describe();
            Fill();
        }

        PathAlias Selected => (list.SelectedItem as ListBoxItem)?.Tag as PathAlias;

        void Fill()
        {
            list.Items.Clear();
            foreach (var a in Aliases.All.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new DockPanel { Margin = new Thickness(2, 3, 2, 3) };
                var name = new TextBlock { Text = "@" + a.Name, Width = 140, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                DockPanel.SetDock(name, Dock.Left); row.Children.Add(name);
                row.Children.Add(new TextBlock { Text = a.Target, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = a.Target });
                list.Items.Add(new ListBoxItem { Content = row, Tag = a });
            }
            if (list.Items.Count == 0) list.Items.Add(new ListBoxItem { Content = "No aliases yet", IsEnabled = false });
            else list.SelectedIndex = 0;
            Describe();
        }

        void Describe()
        {
            var a = Selected;
            rename.IsEnabled = retarget.IsEnabled = remove.IsEnabled = a != null;
            note.Text = a == null ? "" : Directory.Exists(a.Target) ? $"@{a.Name} → {a.Target}" : $"@{a.Name} points to a folder that is missing or offline now: {a.Target}";
        }

        void Rename()
        {
            if (Selected is not { } a) return;
            var name = PromptDialog.Ask(this, "Rename alias", "New name", a.Name, "Rename", selectAll: true)?.Trim().TrimStart('@');
            if (name == null || name == a.Name) return;
            if (Aliases.Problem(name, a) is { } problem) { note.Text = problem; return; }
            Aliases.Store.Put(new PathAlias { Name = name, Target = a.Target }, a); Fill();
        }

        void Retarget()
        {
            if (Selected is not { } a) return;
            var dir = main.active.Dir;
            if (dir == PaneView.ThisPC || PaneView.IsVirtual(dir)) { note.Text = "Open a folder in the active pane first"; return; }
            Aliases.Store.Put(new PathAlias { Name = a.Name, Target = dir }, a); Fill();
        }

        void Remove()
        {
            if (Selected is not { } a) return;
            Aliases.Store.Remove(a); Fill();
        }
    }
}
