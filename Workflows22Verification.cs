#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileExplorer;

/// S22 (OPUS-01..08) on owned fixtures below the trial root, in the private profile: workspaces, linked panes,
/// expandable folders with Keep structure / Flatten, flat view, collections, operation history, listing export,
/// aliases and image marks. No clipboard (CopySink), no desktop input, no file outside the trial root.
public partial class MainWindow
{
    internal async Task VerifyWorkflows22(string root, Action<bool, string> check)
    {
        async Task Settled() => await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle);
        // a real condition, bounded so a broken path fails the check instead of hanging the trial
        async Task<bool> Until(Func<bool> ready, int seconds = 30)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!ready()) { if (clock.Elapsed.TotalSeconds > seconds) return false; await Task.Delay(25); }
            await Settled();
            return true;
        }
        // answers the next modal question by its button, as the user would click it
        Task<bool> Answer(string button) => Dispatcher.InvokeAsync(() =>
        {
            var dialog = Application.Current.Windows.Cast<Window>().LastOrDefault(win => win != this && win.IsVisible && win.Title == "Pathweft");
            var pressed = dialog == null ? null : PreviewChildren<Button>(dialog).FirstOrDefault(x => x.Content as string == button);
            pressed?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return pressed != null;
        }, DispatcherPriority.ApplicationIdle).Task;
        static void Png(string file, Color color)
        {
            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, Enumerable.Range(0, 16).SelectMany(_ => new[] { color.B, color.G, color.R, (byte)255 }).ToArray(), 16);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(file); encoder.Save(stream);
        }

        // ---- fixtures ----
        string w = Directory.CreateDirectory(Path.Combine(root, "workflows-22")).FullName;
        string a = Directory.CreateDirectory(Path.Combine(w, "project a")).FullName;
        string aSub = Directory.CreateDirectory(Path.Combine(a, "sub1")).FullName;
        string aDeep = Directory.CreateDirectory(Path.Combine(aSub, "sub2")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(w, "project b")).FullName;
        string bSub = Directory.CreateDirectory(Path.Combine(b, "sub1")).FullName;
        string elsewhere = Directory.CreateDirectory(Path.Combine(w, "elsewhere")).FullName;
        string top = Path.Combine(a, "top.txt"), one = Path.Combine(aSub, "one.txt"), deep = Path.Combine(aDeep, "deep.txt");
        string comma = Path.Combine(a, "comma, name é.txt");
        foreach (var f in new[] { top, one, deep, comma }) File.WriteAllText(f, Path.GetFileName(f));
        string img1 = Path.Combine(a, "img1.png"), img2 = Path.Combine(a, "img2.png");
        Png(img1, Colors.SteelBlue); Png(img2, Colors.Goldenrod);
        check(!Settings.SinglePane, "Both panes are shown for the paired checks");

        // ---- OPUS-01: workspaces ----
        await S1Verification.Navigate(Right, b);
        await S1Verification.Navigate(Left, a);
        Left.OpenInBackgroundTab(aSub);
        SetActive(Left);
        var saved = CaptureWorkspace("Work α");
        check(Workspaces.Store.Write(Workspaces.All.Append(saved).ToList()) == null, "A workspace is written to workspaces.json in the profile");
        Workspaces.Store.Reload();
        var read = Workspaces.Find("work α");
        check(read != null && read.Left.Tabs.SequenceEqual(new[] { a, aSub }) && read.Right.Tabs.SequenceEqual(new[] { b }) && read.ActivePane == "Left",
            "A saved workspace reads back with both panes' tabs and the active pane");
        await S1Verification.Navigate(Left, elsewhere);
        Left.Listed = false;
        OpenWorkspace(read, replace: true);
        check(await Until(() => Left.Listed && Left.Dir == a) && Left.OpenTabs.SequenceEqual(new[] { a, aSub }),
            "Opening a workspace replaces the unpinned tabs with its own and shows its active tab");
        await S1Verification.Navigate(Left, elsewhere);
        Left.Listed = false;
        OpenWorkspace(read, replace: false);
        check(await Until(() => Left.Listed && Left.Dir == a) && Left.OpenTabs.SequenceEqual(new[] { elsewhere, aSub, a }),
            "Adding a workspace keeps the open tabs, skips one already open and adds the rest");
        var missing = new Workspace { Name = "Gone", Left = new() { Tabs = new() { Path.Combine(w, "gone", "deeper") } }, Right = new() { Tabs = new() { b } } };
        Left.Listed = false;
        OpenWorkspace(missing, replace: true);
        check(await Until(() => Left.Listed) && Left.NoticeText.Contains("missing") && Left.Dir.Equals(w, StringComparison.OrdinalIgnoreCase),
            "A workspace folder that is gone opens at the nearest folder that exists and says so");
        await S1Verification.Navigate(Left, a);
        await S1Verification.Navigate(Right, b);

        // ---- OPUS-02: linked relative navigation ----
        LinkPanes();
        check(PanesLinked && paneLink.Value == (a, b), "Link panes records both current folders as roots");
        Right.Listed = false;
        await S1Verification.Navigate(Left, aSub);
        check(await Until(() => Right.Listed && Right.Dir.Equals(bSub, StringComparison.OrdinalIgnoreCase)), "Going into a folder takes the linked pane to the same relative folder");
        await S1Verification.Navigate(Left, aDeep);
        await Settled();
        check(Right.Dir.Equals(bSub, StringComparison.OrdinalIgnoreCase) && Right.NoticeText.StartsWith("No matching folder") && !Directory.Exists(Path.Combine(bSub, "sub2")),
            "A missing counterpart is said in the other pane, which stays and creates nothing");
        await S1Verification.Navigate(Left, elsewhere);
        check(Right.NoticeText.Contains("paused") && Right.Dir.Equals(bSub, StringComparison.OrdinalIgnoreCase), "Leaving the linked root pauses following without moving the other pane, which says why");
        Right.Listed = false;
        await S1Verification.Navigate(Left, a);
        check(await Until(() => Right.Listed && Right.Dir.Equals(b, StringComparison.OrdinalIgnoreCase)), "Coming back below the root resumes following");
        var linkedSave = CaptureWorkspace("Linked");
        UnlinkPanes();
        check(!PanesLinked && linkedSave.LinkLeft == a && linkedSave.LinkRight == b, "Unlink ends the link; a workspace saved while linked keeps both roots");

        // ---- OPUS-03: expandable folders, Keep folder structure and Flatten ----
        Left.ToggleNesting();
        await Settled();
        var sub1Row = Left.Items.Single(e => e.Name == "sub1");
        await Left.ToggleNest(sub1Row);
        var sub2Row = Left.Items.SingleOrDefault(e => e.Name == "sub2");
        check(sub1Row.NestOpen && sub2Row?.NestDepth == 1 && Left.Items.Any(e => e.Name == "one.txt" && e.NestParent == sub1Row), "Expanding a folder lists its members indented below it");
        await Left.ToggleNest(sub2Row);
        await Settled();
        var rows = Left.ListingRows.ToList();
        int at = rows.IndexOf(sub1Row);
        check(Left.Items.Any(e => e.Name == "deep.txt" && e.NestDepth == 2) && rows.Skip(at + 1).Take(3).All(e => e.NestDepth > 0),
            "Nested members are listed right below their folder, two levels deep");
        var oneRow = Left.Items.Single(e => e.Name == "one.txt");
        Left.List.SelectedItems.Clear(); Left.List.SelectedItems.Add(sub1Row); Left.List.SelectedItems.Add(oneRow);
        check(Left.Selected.Count == 1 && Left.Selected[0] == sub1Row, "A folder and an item inside it both selected count once (the folder carries it)");
        var deepRow = Left.Items.Single(e => e.Name == "deep.txt");
        var topRow = Left.Items.Single(e => e.Name == "top.txt");
        var picked = new List<Entry> { topRow, oneRow, deepRow };
        string keep = Path.Combine(w, "copy keep"), flat = Path.Combine(w, "copy flat");
        Directory.CreateDirectory(keep); Directory.CreateDirectory(flat);
        var keepAnswer = Answer("Keep folder structure");
        SubmitShaped(Left, picked, keep, move: false);
        check(await keepAnswer, "Copying items from below the folder asks Keep folder structure or Flatten");
        check(await Until(() => File.Exists(Path.Combine(keep, "top.txt")) && File.Exists(Path.Combine(keep, "sub1", "one.txt")) && File.Exists(Path.Combine(keep, "sub1", "sub2", "deep.txt"))),
            "Keep folder structure recreates the folders between the shown folder and each item");
        var flatAnswer = Answer("Flatten");
        SubmitShaped(Left, picked, flat, move: false);
        check(await flatAnswer && await Until(() => new[] { "top.txt", "one.txt", "deep.txt" }.All(n => File.Exists(Path.Combine(flat, n)))) && !Directory.Exists(Path.Combine(flat, "sub1")),
            "Flatten puts every item directly in the destination");
        check(File.Exists(top) && File.Exists(one) && File.Exists(deep), "Copying from expanded folders leaves the originals in place");
        await Left.ToggleNest(sub1Row);
        check(!Left.Items.Any(e => e.NestDepth > 0), "Collapsing a folder removes its members, deeper ones too");
        Left.ToggleNesting();

        // ---- OPUS-03: flat view ----
        Left.ShowFlat();
        check(await Until(() => Left.FlatView && Left.ListingRows.Any(e => e.Name == "deep.txt")), "Flat view lists every file below the folder");
        var deepFlat = Left.ListingRows.Single(e => e.Name == "deep.txt");
        check(deepFlat.Location == Path.Combine("sub1", "sub2") && !Left.ListingRows.Any(e => e.IsDir), "Flat view shows each file's location and no folder rows");
        Left.SetSort("Location", false);
        await Settled();
        var flatRows = Left.ListingRows.Select(e => e.Location ?? "").ToList();
        check(flatRows.SequenceEqual(flatRows.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)), "Flat view sorts by Location");
        Left.SetSort("Name", false);
        Left.Listed = false;
        Left.ClearFilter();
        check(await Until(() => !Left.InSearch && !Left.FlatView && Left.Dir == a && Left.Listed), "Esc (Clear) returns from the flat view to the folder");

        // ---- OPUS-04: collections ----
        string gone = Path.Combine(w, "deleted later.txt");
        File.WriteAllText(gone, "x");
        check(Collections.Add("Trip", new[] { top, one, gone }) == 3 && Collections.Add("trip", new[] { top }) == 0, "A collection takes references once each");
        File.Delete(gone);
        Collections.Store.Reload();
        check(Collections.Find("TRIP")?.Items.Count == 3, "A collection reads back from collections.json");
        Left.ShowCollection("Trip");
        check(await Until(() => Left.ShownCollection == "Trip" && Left.ListingRows.Count == 3), "Showing a collection lists its members where they are");
        var goneRow = Left.ListingRows.Single(e => e.Path == gone);
        check(goneRow.Message?.StartsWith("Missing") == true && Left.ListingRows.Single(e => e.Path == one).Location == aSub,
            "A member that is gone stays listed as missing; the others show their folder");
        RemoveFromCollection(Left, new() { Left.ListingRows.Single(e => e.Path == top) });
        check(await Until(() => Left.ListingRows.Count == 2) && File.Exists(top) && !Collections.Contains("Trip", top), "Remove from collection forgets the reference and leaves the file");
        ForgetMissing(Collections.Find("Trip"));
        check(await Until(() => !Collections.Contains("Trip", gone)) && Collections.Contains("Trip", one), "Forget missing items drops only the members that are gone");
        var trip = Collections.Find("Trip");
        Collections.Store.Put(new FileCollection { Name = "Trip 2026", Items = trip.Items }, trip);
        check(Collections.Find("Trip") == null && Collections.Find("Trip 2026")?.Items.SequenceEqual(new[] { one }) == true, "Renaming a collection keeps its members");
        check(await Until(() => !Left.InSearch), "A pane showing a collection that is renamed away returns to its folder");
        await S1Verification.Navigate(Left, a);

        // ---- OPUS-05: operation history ----
        await FinishUndoWork();
        check(await Until(() => OperationHistory.Store.All.Count(r => r.Destination != null && r.Destination.StartsWith(w)) >= 4),
            "Finished copies are recorded in the operation history");
        var flatRecord = OperationHistory.Store.All.First(r => r.Destination?.TrimEnd('\\') == flat);
        check(flatRecord.Result == "done" && flatRecord.Items == 3 && flatRecord.Sources.Contains(deep), "A record holds what, where to, how many and how it ended");
        OperationHistory.Store.Reload();
        check(File.Exists(Path.Combine(DataFolder.Bulk, "history.json")) && OperationHistory.Store.All.Any(r => r.Destination?.TrimEnd('\\') == flat), "The history reads back after a restart");
        OperationHistory.Clear();
        check(OperationHistory.Store.All.Count == 0 && File.Exists(Path.Combine(keep, "top.txt")), "Clear history forgets the records and touches no file");

        // ---- OPUS-06: Copy as table and Export listing ----
        string copied = null;
        CopySink = text => copied = text;
        try
        {
            Left.List.SelectedItems.Clear();
            CopyAsTable(Left);
            var lines = copied?.Split(Environment.NewLine) ?? Array.Empty<string>();
            check(lines.Length == Left.ListingRows.Count + 1 && lines[0].Split('\t')[0] == "Name" && lines.Skip(1).Select(l => l.Split('\t')[0]).SequenceEqual(Left.ListingRows.Select(e => e.Name)),
                "Copy as table writes a header and every row in the listing's order");
            Left.List.SelectedItems.Clear(); Left.List.SelectedItems.Add(Left.Items.Single(e => e.Path == top));
            CopyAsTable(Left);
            check(copied.Split(Environment.NewLine).Length == 2, "With a selection, Copy as table writes the selected rows");
        }
        finally { CopySink = null; }
        string csvFile = Path.Combine(w, "export.csv");
        check(WriteListing(csvFile, ListingTable(Left, Left.ListingRows, "csv")) == null, "Export listing writes the file");
        var bytes = File.ReadAllBytes(csvFile);
        var csv = File.ReadAllText(csvFile);
        check(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && csv.Contains("\"comma, name é.txt\"") && csv.Split('\n').Length == Left.ListingRows.Count + 1,
            "The CSV has a byte order mark, one line per row and quotes a name with a comma");

        // ---- OPUS-07: aliases ----
        check(Aliases.Problem("my project") != null && Aliases.Problem("proj") == null, "An alias name with a space is refused with the reason");
        Aliases.Store.Put(new PathAlias { Name = "proj", Target = a });
        Aliases.Store.Put(new PathAlias { Name = "nowhere", Target = Path.Combine(w, "no such folder") });
        Aliases.Store.Put(new PathAlias { Name = "子项目", Target = aSub });
        Aliases.Store.Put(new PathAlias { Name = "nas", Target = @"\\pathweft-offline.invalid\share" });
        Aliases.Store.Reload();
        check(Aliases.Problem("PROJ") is { } taken && taken.Contains("already exists") && Aliases.Expand(@"@proj\sub1")?.Path == aSub, "Aliases read back and expand with a path below them");
        check(Aliases.Expand("@子项目")?.Path == aSub && Aliases.Expand(@"@nas\photos")?.Path == @"\\pathweft-offline.invalid\share\photos" && Aliases.Expand("@unknown") == null,
            "Unicode names and UNC targets expand without a network probe; an unknown @name is not an alias");
        var address = (TextBox)Left.FindName("FilterBox");
        async Task Go(string text) { address.Text = text; await Left.SubmitAddressAsync(); await Settled(); }
        await S1Verification.Navigate(Left, elsewhere);
        Left.Listed = false;
        await Go(@"@proj\sub1");
        check(await Until(() => Left.Listed && Left.Dir.Equals(aSub, StringComparison.OrdinalIgnoreCase)), "Typing @name\\below in the address goes to the aliased folder");
        string real = Directory.CreateDirectory(Path.Combine(elsewhere, "@proj")).FullName;
        await S1Verification.Navigate(Left, elsewhere);
        Left.Listed = false;
        await Go("@proj");
        check(await Until(() => Left.Listed && Left.Dir.Equals(real, StringComparison.OrdinalIgnoreCase)), "A real folder named @proj wins over the alias");
        await S1Verification.Navigate(Left, elsewhere);
        string error = null;
        void Failed(string e) => error = e;
        Left.Error += Failed;
        try { await Go("@nowhere"); }
        finally { Left.Error -= Failed; }
        check(error?.Contains("missing or offline") == true && Left.Dir == elsewhere && address.Text == "@nowhere", "An alias whose folder is gone says so and goes nowhere");

        // ---- OPUS-08: marks ----
        await S1Verification.Navigate(Left, a);
        SetActive(Left);
        bool preview = Settings.Preview;
        Settings.Preview = true; ApplyPreview();
        try
        {
            async Task Show(string path)
            {
                Left.List.SelectedItems.Clear();
                Left.List.SelectedItem = Left.Items.Single(e => e.Path == path); ApplyPreview();
                await sidePreview.Completion; await Settled();
            }
            await Show(img1);
            check(PreviewMarkButton.Visibility == Visibility.Visible && PreviewMarkButton.Tag == null, "The preview offers Mark for an image");
            check(ToggleMark() && Collections.Contains(Collections.Marked, img1) && PreviewMarkButton.Tag as string == "on", "Mark puts the image in Marked images");
            await Show(img2);
            ToggleMark();
            await Show(top);
            check(PreviewMarkButton.Visibility == Visibility.Collapsed, "Mark is not offered for a file that isn't an image");
            Left.List.SelectedItem = Left.Items.Single(e => e.Path == img1);
            check(StepMarked(1) && (Left.List.SelectedItem as Entry)?.Path == img2 && StepMarked(1) && (Left.List.SelectedItem as Entry)?.Path == img1,
                "Next marked image goes through the marked images in the listing's order, wrapping around");
            await Show(img2);
            foreach (var host in new[] { PreviewMode.PictureInPicture, PreviewMode.Windowed })
            {
                MovePreviewHost(host); await Settled();
                check(PreviewMarkButton.Visibility == Visibility.Visible && PreviewMarkButton.Tag as string == "on", $"The mark shows in the {host} preview host");
            }
            check(ToggleMark() && PreviewMarkButton.Tag == null, "Unmarking works in the windowed preview host");
            MovePreviewHost(PreviewMode.Sidebar); await Settled();
            Collections.Store.Reload();
            check(Collections.Contains(Collections.Marked, img1) && !Collections.Contains(Collections.Marked, img2) && File.Exists(img2), "Unmarking takes the image out; marks persist; files are untouched");
        }
        finally { Settings.Preview = preview; ApplyPreview(); }

        await S1Verification.Snapshot(this, root, "workflows-22");
        check(HiddenTrialPresentation.ForegroundViolations == 0, "S22 workflows do not activate the host desktop");
    }
}
#endif
