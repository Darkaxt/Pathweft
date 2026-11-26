using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using static FileExplorer.EmbeddedMetadata;

namespace FileExplorer;

/// OPUS-09: Edit metadata… for one or many files. A field showing "(multiple values)" keeps each file's own
/// value unless it is edited; an emptied field removes that detail. Apply shows what will change first, then
/// writes file by file (Stop takes effect between files) and lists what happened to each.
internal sealed class MetadataDialog : AuxDialog
{
    internal const string Mixed = "(multiple values)";
    internal readonly List<Item> Items, Editable;
    internal readonly Dictionary<EmbeddedMetadata.Field, TextBox> Boxes = new();
    readonly Dictionary<EmbeddedMetadata.Field, string> initial = new();
    readonly Button[] stars = new Button[5];
    int rating; internal bool RatingTouched;
    internal readonly ComboBox Dates;
    internal readonly TextBox When;
    internal readonly CheckBox Modified, Created, Taken, KeepModified;
    readonly StackPanel edit = new(), review = new(), work = new();
    readonly TextBlock error = Note(""), summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0) };
    readonly ProgressBar bar = new() { Height = 4, Margin = new Thickness(2, 10, 2, 4) };
    readonly TextBlock current = Note("");
    readonly ListBox results = new() { MaxHeight = 300, Margin = new Thickness(0, 8, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly Button back, stopButton;
    readonly Action<List<Outcome>, Change> finished;
    CancellationTokenSource stop;
    internal int Phase { get; private set; } // 0 edit, 1 review, 2 writing, 3 done
    internal Change Planned { get; private set; }
    internal List<Outcome> Outcomes { get; private set; }
    internal Task Writing { get; private set; } = Task.CompletedTask;

    internal MetadataDialog(Window owner, List<Item> items, Action<List<Outcome>, Change> finished)
        : base(owner, items.Count == 1 ? "Edit metadata · " + Path.GetFileName(items[0].Path) : $"Edit metadata · {items.Count:N0} files", "Review", 500)
    {
        Items = items; Editable = items.Where(i => i.Problem == null).ToList(); this.finished = finished;
        Body.Children.Add(edit); Body.Children.Add(review); Body.Children.Add(work);
        review.Visibility = work.Visibility = Visibility.Collapsed;
        stopButton = Footer.Children.OfType<Button>().First(b => b.IsCancel);
        back = Action("Back", () => Go(0)); back.Visibility = Visibility.Collapsed; DockPanel.SetDock(back, Dock.Right);
        Footer.Children.Insert(2, back);

        var kinds = Editable.GroupBy(i => i.Family).Select(g => $"{g.Count():N0} {FamilyName(g.Key.Value)}{(g.Count() == 1 ? "" : "s")}");
        edit.Children.Add(Note(Editable.Count == 0 ? "None of these files can be changed." : string.Join(", ", kinds) + ". The details are written into the files themselves."));
        if (items.Count > Editable.Count) edit.Children.Add(Note(Unchanged(items.Where(i => i.Problem != null).ToList())));
        var fields = new StackPanel();
        foreach (var field in All)
        {
            var with = Editable.Where(i => i.Fields.Contains(field)).ToList();
            if (with.Count == 0) continue;
            string label = field.Label + (field.Kind == Kind.List ? " (separated by semicolons)" : "") + (with.Count < Editable.Count ? $" · {with.Count:N0} of {Editable.Count:N0} files" : "");
            fields.Children.Add(Label(label, fields.Children.Count == 0 ? 0 : 8));
            var shown = with.Select(i => i.Values.TryGetValue(field.Name, out var v) ? string.Join("; ", v) : "").Distinct().ToArray();
            if (field.Kind == Kind.Rating)
            {
                var ratings = with.Select(i => Stars(i.Values.TryGetValue(field.Name, out var v) && uint.TryParse(v[0], out uint r) ? r : 0)).Distinct().ToArray();
                rating = ratings.Length == 1 ? ratings[0] : 0;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                for (int s = 0; s < 5; s++)
                {
                    int value = s + 1;
                    var star = stars[s] = new Button { FontSize = 18, Padding = new Thickness(4, 0, 4, 0), MinWidth = 0 }; star.SetResourceReference(StyleProperty, "Ghost");
                    System.Windows.Automation.AutomationProperties.SetName(star, $"{value} star{(value > 1 ? "s" : "")}");
                    star.Click += (_, _) => { rating = rating == value ? 0 : value; RatingTouched = true; ShowStars(); };
                    row.Children.Add(star);
                }
                fields.Children.Add(row); ShowStars();
                if (ratings.Length > 1) fields.Children.Add(Note("The files have different ratings; choosing one sets it for all."));
                continue;
            }
            string text = shown.Length == 1 ? shown[0] : Mixed;
            var box = Field(text, field.Label, out var frame, multiline: field == Comment);
            Boxes[field] = box; initial[field] = text; fields.Children.Add(frame);
        }
        if (fields.Children.Count > 0) edit.Children.Add(new ScrollViewer { Content = fields, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });

        edit.Children.Add(Label("Dates", 12));
        Dates = Choice("Dates", "Keep the dates", "Set to", "Shift by");
        When = Field("", "Date", out var whenFrame);
        var whenHint = Note("");
        bool photos = Editable.Any(i => i.TakenWritable);
        Modified = Check("Modified", true); Created = Check("Created", false); Taken = Check("Date taken (photos)", photos);
        KeepModified = Check("Keep each file's modified date (Windows otherwise updates it on a change)", false);
        var targets = new WrapPanel(); targets.Children.Add(Modified); targets.Children.Add(Created); if (photos) targets.Children.Add(Taken);
        foreach (var c in new[] { Modified, Created, Taken }) c.Margin = new Thickness(2, 6, 14, 0);
        void Mode()
        {
            var mode = (DateMode)Dates.SelectedIndex;
            whenFrame.Visibility = whenHint.Visibility = targets.Visibility = mode == DateMode.Keep ? Visibility.Collapsed : Visibility.Visible;
            KeepModified.Visibility = mode == DateMode.Keep || Modified.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
            whenHint.Text = mode == DateMode.Set ? "A date and time, as " + DateTime.Now.ToString("g", CultureInfo.CurrentCulture) + "."
                          : "Hours:minutes, or days.hours:minutes; a minus sign moves earlier (-1.00:00 is a day earlier).";
            if (mode == DateMode.Set && (When.Text.Length == 0 || TryShift(When.Text, out _))) When.Text = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
            if (mode == DateMode.Shift && !TryShift(When.Text, out _)) When.Text = "+1:00";
        }
        Dates.SelectionChanged += (_, _) => Mode(); Modified.Click += (_, _) => Mode();
        edit.Children.Add(Dates); edit.Children.Add(whenFrame); edit.Children.Add(whenHint); edit.Children.Add(targets); edit.Children.Add(KeepModified);
        edit.Children.Add(error);
        Mode();

        review.Children.Add(summary);
        work.Children.Add(bar); work.Children.Add(current); work.Children.Add(results);
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        Accept.IsEnabled = Editable.Count > 0;
        Closing += (_, e) => { if (Phase == 2) { e.Cancel = true; stop?.Cancel(); stopButton.IsEnabled = false; current.Text = "Stopping after this file…"; } };
        Loaded += (_, _) => { if (Boxes.Values.FirstOrDefault() is { } first) { first.Focus(); first.SelectAll(); } };
    }

    void ShowStars() { for (int i = 0; i < 5; i++) if (stars[i] != null) stars[i].Content = i < rating ? "★" : "☆"; }

    static string Unchanged(List<Item> left)
    {
        var named = left.Take(4).Select(i => $"{Path.GetFileName(i.Path.TrimEnd('\\'))} ({i.Problem})");
        return $"Left as they are ({left.Count:N0}): " + string.Join("; ", named) + (left.Count > 4 ? $"; and {left.Count - 4:N0} more" : "");
    }

    /// The edit as chosen, or null with the reason shown.
    internal Change Build()
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (field, box) in Boxes)
        {
            string text = box.Text.Trim();
            if (box.Text == initial[field] || text == Mixed) continue;
            if (field.Kind == Kind.Number && text.Length > 0 && !uint.TryParse(text, NumberStyles.None, CultureInfo.CurrentCulture, out _)) { error.Text = field.Label + " takes a whole number."; return null; }
            fields[field.Name] = field.Kind == Kind.List ? text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : text.Length == 0 ? Array.Empty<string>() : new[] { text };
        }
        if (RatingTouched) fields[EmbeddedMetadata.Rating.Name] = rating == 0 ? Array.Empty<string>() : new[] { FromStars(rating).ToString(CultureInfo.InvariantCulture) };
        var mode = (DateMode)Dates.SelectedIndex;
        DateTime set = default; TimeSpan shift = default;
        if (mode == DateMode.Set && !DateTime.TryParse(When.Text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out set)) { error.Text = "That isn't a date and time Pathweft understands."; return null; }
        if (mode == DateMode.Shift && !TryShift(When.Text, out shift)) { error.Text = "A shift is hours:minutes or days.hours:minutes, with a minus sign for earlier."; return null; }
        var change = new Change(fields, mode, set, shift, Modified.IsChecked == true, Created.IsChecked == true, Taken.IsChecked == true && Editable.Any(i => i.TakenWritable),
                                KeepModified.IsChecked == true && KeepModified.Visibility == Visibility.Visible);
        if (change.Empty) { error.Text = "Nothing is changed yet."; return null; }
        error.Text = "";
        return change;
    }

    internal static bool TryShift(string text, out TimeSpan shift)
    {
        text = text.Trim(); bool minus = text.StartsWith('-');
        if (text.StartsWith('+') || minus) text = text[1..];
        bool ok = TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out shift) && shift > TimeSpan.Zero;
        if (minus) shift = -shift;
        return ok;
    }

    /// What Apply will do, one line per change.
    internal string Describe(Change change)
    {
        var lines = new List<string> { $"{Editable.Count:N0} file{(Editable.Count == 1 ? "" : "s")} will be changed:" };
        foreach (var field in All.Where(f => change.Fields.ContainsKey(f.Name)))
        {
            var value = change.Fields[field.Name];
            int count = Editable.Count(i => i.Fields.Contains(field));
            string shown = field.Kind == Kind.Rating ? value.Length == 0 ? "removed" : "→ " + new string('★', Stars(uint.Parse(value[0], CultureInfo.InvariantCulture)))
                         : value.Length == 0 ? "removed" : "→ " + string.Join("; ", value);
            lines.Add($"• {field.Label} {shown}" + (count < Editable.Count ? $" ({count:N0} files)" : ""));
        }
        if (change.Dates != DateMode.Keep)
        {
            var which = new[] { change.Modified ? "modified" : null, change.Created ? "created" : null, change.Taken ? "date taken" : null }.Where(w => w != null);
            string how = change.Dates == DateMode.Set ? "set to " + change.Set.ToString("g", CultureInfo.CurrentCulture) : "shifted by " + (change.Shift < TimeSpan.Zero ? "-" : "+") + change.Shift.Duration().ToString(@"d\.hh\:mm", CultureInfo.InvariantCulture);
            lines.Add($"• Dates ({string.Join(", ", which)}) {how}");
        }
        else if (change.KeepModified) lines.Add("• Modified dates kept");
        if (Items.Count > Editable.Count) lines.Add(Unchanged(Items.Where(i => i.Problem != null).ToList()));
        lines.Add("Undo (Ctrl+Z, or History → Operation history) puts the earlier details and dates back.");
        return string.Join(Environment.NewLine, lines);
    }

    void Go(int phase)
    {
        Phase = phase;
        edit.Visibility = phase == 0 ? Visibility.Visible : Visibility.Collapsed;
        review.Visibility = phase == 1 ? Visibility.Visible : Visibility.Collapsed;
        work.Visibility = phase >= 2 ? Visibility.Visible : Visibility.Collapsed;
        back.Visibility = phase == 1 ? Visibility.Visible : Visibility.Collapsed;
        Accept.Content = phase switch { 0 => "Review", 1 => "Apply", _ => "Close" };
        Accept.IsEnabled = phase != 2;
        stopButton.Content = phase == 2 ? "Stop" : "Cancel";
        stopButton.Visibility = phase == 3 ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override bool Validate()
    {
        switch (Phase)
        {
            case 0:
                if (Build() is not { } change) return false;
                Planned = change; summary.Text = Describe(change); Go(1); return false;
            case 1: Writing = Write(); return false;
            case 3: return true;
            default: return false;
        }
    }

    async Task Write()
    {
        stop = new CancellationTokenSource(); Go(2);
        bar.Maximum = Items.Count; bar.Value = 0;
        var progress = new Progress<(int, string)>(p => { bar.Value = p.Item1; current.Text = $"{p.Item1 + 1:N0} of {Items.Count:N0}: {Path.GetFileName(p.Item2)}"; });
        Outcomes = await ApplyAsync(Items, Planned, (i, path) => ((IProgress<(int, string)>)progress).Report((i, path)), stop.Token);
        int changed = Outcomes.Count(o => o.Changed), failed = Outcomes.Count(o => o.Error != null), notStarted = Outcomes.Count(o => o.Note?.StartsWith("not started") == true);
        bar.Value = Items.Count;
        current.Text = $"Changed {changed:N0}" + (failed > 0 ? $", failed {failed:N0}" : "") + (notStarted > 0 ? $", not started {notStarted:N0} (stopped)" : "")
                     + (Outcomes.Count - changed - failed - notStarted is > 0 and var other ? $", left as they are {other:N0}" : "") + ".";
        foreach (var o in Outcomes.OrderBy(o => o.Changed ? 1 : 0))
            results.Items.Add(new ListBoxItem { Content = new TextBlock { Text = Path.GetFileName(o.Path) + ": " + (o.Error != null ? "failed: " + o.Error : o.Changed ? "changed" + (o.Note != null ? " (" + o.Note + ")" : "") : o.Note), TextWrapping = TextWrapping.Wrap } });
        finished?.Invoke(Outcomes, Planned);
        Go(3);
    }

    /// For verification: moves to the given phase as the buttons do.
    internal void Press() => Accept.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    internal void PressStop() => stop?.Cancel();
    internal void PressStar(int value) => stars[value - 1]?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
}

public partial class MainWindow
{
    /// OPUS-09: the item menu's Tools → Edit metadata….
    internal async void EditMetadata(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (paths.Count > EmbeddedMetadata.Limit) { Status.Text = $"Edit metadata handles up to {EmbeddedMetadata.Limit:N0} files at a time"; return; }
        Status.Text = "Reading the files' details…";
        List<EmbeddedMetadata.Item> items;
        try { items = await EmbeddedMetadata.InspectAsync(paths); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException) { Status.Text = "Can't read the details: " + ex.Message; return; }
        Status.Text = "";
        var dialog = new MetadataDialog(this, items, (outcomes, change) => RecordMetadata(outcomes));
        dialog.Present();
    }

    /// The changed files go into Undo as one entry (their earlier values and dates), and into Operation history with the real outcome.
    internal UndoEntry RecordMetadata(List<EmbeddedMetadata.Outcome> outcomes)
    {
        var changed = outcomes.Where(o => o.Changed).ToList();
        int failed = outcomes.Count(o => o.Error != null);
        bool stopped = outcomes.Any(o => o.Note?.StartsWith("not started") == true);
        string what = "metadata edit of " + (outcomes.Count == 1 ? Path.GetFileName(outcomes[0].Path) : $"{outcomes.Count:N0} files");
        string result = changed.Count == 0 ? stopped ? "cancelled" : "failed" : failed > 0 || stopped ? "partial" : "done";
        UndoEntry entry = null;
        if (changed.Count > 0)
        {
            rememberingJob = true; // recorded below with its outcome
            try { entry = Remember(what, changed.Select(o => new UndoStep("meta", o.Path, o.Undo, o.Stamp)), null); }
            finally { rememberingJob = false; }
        }
        OperationHistory.Add(new OperationRecord
        {
            What = Capital(what), Result = result,
            When = DateTime.Now, Sources = outcomes.Select(o => o.Path).Take(50).ToList(), Items = outcomes.Count,
            Error = outcomes.FirstOrDefault(o => o.Error != null) is { } bad ? Path.GetFileName(bad.Path) + ": " + bad.Error : null,
            UndoWhen = entry?.When, UndoWhat = entry?.What,
        });
        Status.Text = $"Metadata changed in {changed.Count:N0} of {outcomes.Count:N0} files" + (failed > 0 ? $"; {failed:N0} failed" : "") + (stopped ? "; stopped" : "");
        Left.Refresh(); Right.Refresh();
        return entry;
    }
}
