using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

/// BROWSE-04: one action's fields, as documented in docs/CUSTOM-ACTIONS.md.
internal sealed class ActionDialog : AuxDialog
{
    readonly TextBox name, glyph, submenu, executable, arguments, working, patterns;
    readonly ComboBox applies, selection;
    readonly TextBlock problem = Note("");
    static readonly string[] AppliesValues = { "both", "files", "folders" }, SelectionValues = { "any", "one", "several" };
    internal CustomAction Result { get; private set; }

    ActionDialog(Window owner, CustomAction a, string title) : base(owner, title, "Save", 520)
    {
        Body.Children.Add(Label("Name", 0)); name = Field(a.Name, "Name", out var f1); Body.Children.Add(f1);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel(); left.Children.Add(Label("Icon (Fluent glyph)")); glyph = Field(a.Glyph, "Icon glyph", out var f2); glyph.FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI"); left.Children.Add(f2);
        var right = new StackPanel(); right.Children.Add(Label("Submenu (optional)")); submenu = Field(a.Submenu, "Submenu", out var f3); right.Children.Add(f3);
        Grid.SetColumn(right, 2); row.Children.Add(left); row.Children.Add(right); Body.Children.Add(row);
        Body.Children.Add(Label("Program")); executable = Field(a.Executable, "Program", out var f4);
        var browse = Action("Browse…", () =>
        {
            var pick = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe;*.cmd;*.bat;*.ps1|All files|*.*" };
            if (pick.ShowDialog(this) == true) executable.Text = pick.FileName;
        });
        var exeRow = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); browse.Margin = new Thickness(6, 0, 0, 0); exeRow.Children.Add(browse); exeRow.Children.Add(f4); Body.Children.Add(exeRow);
        Body.Children.Add(Label("Arguments")); arguments = Field(a.Arguments, "Arguments", out var f5); Body.Children.Add(f5);
        Body.Children.Add(Note("%f first selected path · %F every selected path · %n / %N their names · %d current folder · %% a literal %. Each becomes one quoted argument."));
        Body.Children.Add(Label("Working folder (empty: the current folder)")); working = Field(a.WorkingFolder, "Working folder", out var f6); Body.Children.Add(f6);
        Body.Children.Add(Label("File patterns (folders ignore them)")); patterns = Field(a.Patterns, "File patterns", out var f7); Body.Children.Add(f7);
        var choices = new Grid(); choices.ColumnDefinitions.Add(new ColumnDefinition()); choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); choices.ColumnDefinitions.Add(new ColumnDefinition());
        var c1 = new StackPanel(); c1.Children.Add(Label("Applies to")); applies = Choice("Applies to", "Files and folders", "Files", "Folders"); applies.SelectedIndex = Math.Max(0, Array.IndexOf(AppliesValues, a.AppliesTo)); c1.Children.Add(applies);
        var c2 = new StackPanel(); c2.Children.Add(Label("Selection")); selection = Choice("Selection", "Any number", "Exactly one", "Two or more"); selection.SelectedIndex = Math.Max(0, Array.IndexOf(SelectionValues, a.Selection)); c2.Children.Add(selection);
        Grid.SetColumn(c2, 2); choices.Children.Add(c1); choices.Children.Add(c2); Body.Children.Add(choices);
        Body.Children.Add(problem);
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }

    protected override bool Validate()
    {
        string exe = executable.Text.Trim();
        problem.Text = name.Text.Trim().Length == 0 ? "Give the action a name."
            : exe.Length == 0 ? "Choose the program it starts."
            : glyph.Text.Trim().Length > 2 ? "The icon is one Segoe Fluent Icons character (or empty)."
            : "";
        if (problem.Text.Length > 0) return false;
        Result = new CustomAction
        {
            Name = name.Text.Trim(), Glyph = glyph.Text.Trim(), Submenu = submenu.Text.Trim(), Executable = exe,
            Arguments = arguments.Text.Trim(), WorkingFolder = working.Text.Trim(),
            Patterns = patterns.Text.Trim().Length == 0 ? "*" : patterns.Text.Trim(),
            AppliesTo = AppliesValues[Math.Max(0, applies.SelectedIndex)], Selection = SelectionValues[Math.Max(0, selection.SelectedIndex)],
        };
        return true;
    }

    internal static CustomAction Ask(Window owner, CustomAction action, string title)
    {
        var dialog = new ActionDialog(owner, action ?? new CustomAction(), title);
        return dialog.Present() ? dialog.Result : null;
    }
}
