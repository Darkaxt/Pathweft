#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyTransferDifferences16(string root, Action<bool,string> check)
    {
        var stamp = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        foreach (var choice in new[] { Choice.Overwrite, Choice.Newer, Choice.Rename, Choice.Skip })
        {
            string incoming = Directory.CreateDirectory(Path.Combine(root, choice + " incoming é 子")).FullName;
            string existing = Directory.CreateDirectory(Path.Combine(root, choice + " existing")).FullName;
            void Pair(string name, string source, string target, DateTime sourceTime, DateTime targetTime)
            {
                File.WriteAllText(Path.Combine(incoming, name), source);
                File.WriteAllText(Path.Combine(existing, name), target);
                File.SetLastWriteTimeUtc(Path.Combine(incoming, name), sourceTime);
                File.SetLastWriteTimeUtc(Path.Combine(existing, name), targetTime);
            }
            Pair("metadata match.txt", "left", "rght", stamp, stamp);
            Pair("size only.txt", new string('a',10001), new string('b',10002), stamp, stamp);
            Pair("date only.txt", "left", "rght", stamp, stamp.AddDays(-1));
            Pair("newer existing.txt", "left", "rght", stamp, stamp.AddDays(1));
            Pair("both.txt", "left", "longer target", stamp, stamp.AddDays(-1));
            Pair("tick only.txt", "left", "rght", stamp, stamp.AddTicks(1));
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Ferry.VerificationBoundary = (phase,_,_,_) => { if (phase == "conflicts") reached.TrySetResult(); };
            Job job = null;
            try
            {
                job = Submit(Directory.GetFiles(incoming).ToList(), existing, false);
                await Task.WhenAny(reached.Task, job.Completion);
                check(reached.Task.IsCompleted && job.HasConflicts, "Actual transfer reaches metadata review: " + choice);
                job.Refresh(); UpdateTransfers();
                await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout, DispatcherPriority.ApplicationIdle);
                var rows = job.ConflictRows;
                check(job.Conflicts.Count == 6 && rows.Count == 5 && job.MatchingConflictCount == 1 && !rows.Any(c=>c.MetadataMatches), "Only differing rows shown while complete conflict plan survives: " + choice);
                check(rows.Take(2).All(c=>c.ExistingNewer) && rows.Skip(2).All(c=>!c.ExistingNewer) && rows[0].ReviewHint == "Newer existing file", "Newer destination files lead with a noncolor cue: " + choice);
                var size = rows.Single(c=>c.Name == "size only.txt");
                var date = rows.Single(c=>c.Name == "date only.txt");
                var tick = rows.Single(c=>c.Name == "tick only.txt");
                check(size.IncomingTime == "" && size.ExistingTime == "" && size.IncomingSize != size.ExistingSize && size.IncomingSize.Contains("bytes"), "Size-only comparison hides dates and distinguishes rounded-equal sizes: " + choice);
                check(date.IncomingSize == "" && date.ExistingSize == "" && date.IncomingTime != date.ExistingTime && tick.IncomingTime != tick.ExistingTime, "Date-only and 100ns differences hide matching sizes and remain distinguishable: " + choice);
                check(rows.All(c=>c.Detail.Contains(incoming)&&c.Detail.Contains(existing)) && job.ConflictScope.Contains("all 6"), "Full paths and complete action scope remain available: " + choice);
                var table = PreviewChildren<ListView>(transfersWindow).Single(view=>view.Name == "ConflictTable");
                var toggle = PreviewChildren<CheckBox>(transfersWindow).Single(view=>view.Name == "ShowMatchingConflicts");
                check(table.Items.Count == 5 && toggle.IsVisible, "Actual table and matching-file control bind to filtered metadata: " + choice);
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                toggle.GetBindingExpression(ToggleButton.IsCheckedProperty).UpdateSource();
                await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout, DispatcherPriority.ApplicationIdle);
                check(job.ShowMatchingConflicts && table.Items.Count == 6 && job.Conflicts.Count == 6 && job.ConflictRows.Single(c=>c.MetadataMatches).IncomingSize == "", "Show matching files uses the real binding without adding matching property values: " + choice);
                toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                toggle.GetBindingExpression(ToggleButton.IsCheckedProperty).UpdateSource();
                if (choice == Choice.Overwrite)
                {
                    foreach (string theme in new[] { "dark", "light" }) foreach (double scale in new[] { 1.0, 1.5 })
                    {
                        Settings.NativeMode = theme; Settings.TextScale = scale; ApplyAppearance();
                        transfersWindow.Width = 640*scale;
                        ((FrameworkElement)transfersWindow.Content).LayoutTransform = scale>1 ? new ScaleTransform(scale,scale) : null;
                        await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout, DispatcherPriority.ApplicationIdle);
                        table.SelectedItem = null;
                        var warning = (ListViewItem)table.ItemContainerGenerator.ContainerFromIndex(0);
                        check(warning != null && ((SolidColorBrush)warning.Foreground).Color == ((SolidColorBrush)FindResource("PreviewWarning")).Color && AutomationProperties.GetHelpText(warning).Contains("Newer existing file"), "Orange warning and accessible explanation use actual rendered rows: " + theme + scale);
                        check(table.ActualHeight <= 240.1 && transfersWindow.ActualHeight < 680*scale, "Differences review stays compact and scrollable: " + theme + scale);
                        table.SelectedIndex = 0;
                        await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout, DispatcherPriority.ApplicationIdle);
                        check(((SolidColorBrush)warning.Foreground).Color == ((SolidColorBrush)FindResource("AccentSelectText")).Color, "Selected warning row retains selection text contrast: " + theme + scale);
                        table.SelectedItem = null;
                        await S1Verification.Snapshot(transfersWindow, root, $"differences-{theme}-{scale*100:0}");
                    }
                    Settings.TextScale = 1; ApplyAppearance();
                    ((FrameworkElement)transfersWindow.Content).LayoutTransform = null;
                }
                PreviewChildren<Button>(transfersWindow).Single(button=>ReferenceEquals(button.DataContext,job)&&Equals(button.CommandParameter,choice.ToString())).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await job.Completion;
                check(job.Errors.IsEmpty && job.DoneFiles + job.SkippedFiles == 6, "Actual resolution accounts for every planned file: " + choice);
                string matched = File.ReadAllText(Path.Combine(existing,"metadata match.txt"));
                check(matched == (choice == Choice.Overwrite ? "left" : "rght"), "Hidden metadata match with different bytes obeys complete resolution choice: " + choice);
                if (choice == Choice.Newer) check(job.DoneFiles == 2 && job.SkippedFiles == 4 && File.ReadAllText(Path.Combine(existing,"newer existing.txt")) == "rght", "Replace if newer uses the same timestamp comparison as review warnings");
                if (choice == Choice.Rename) check(job.DoneFiles == 6 && Directory.GetFiles(existing).Length == 12, "Keep both also retains all filtered metadata matches");
                DismissJob(job);
            }
            finally { Ferry.VerificationBoundary = null; if(job?.Active==true){job.Cancel();await job.Completion;} }
        }
        string allSource=Directory.CreateDirectory(Path.Combine(root,"all matching incoming")).FullName;
        string allTarget=Directory.CreateDirectory(Path.Combine(root,"all matching existing")).FullName;
        File.WriteAllText(Path.Combine(allSource,"same.txt"),"left");File.WriteAllText(Path.Combine(allTarget,"same.txt"),"rght");
        File.SetLastWriteTimeUtc(Path.Combine(allSource,"same.txt"),stamp);File.SetLastWriteTimeUtc(Path.Combine(allTarget,"same.txt"),stamp);
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Ferry.VerificationBoundary=(phase,_,_,_)=>{if(phase=="conflicts")ready.TrySetResult();};
        var all=Submit(Directory.GetFiles(allSource).ToList(),allTarget,false);
        try
        {
            await Task.WhenAny(ready.Task,all.Completion);check(ready.Task.IsCompleted,"All-matching transfer reaches real conflict review");
            all.Refresh();UpdateTransfers();await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(all.NoConflictDifferences&&all.Conflicts.Count==1&&PreviewChildren<TextBlock>(transfersWindow).Any(text=>text.IsVisible&&text.Text.StartsWith("No size or date differences.")),"All-matching state explains the empty review without claiming identical contents");
            await S1Verification.Snapshot(transfersWindow,root,"differences-all-matching");
            PreviewChildren<Button>(transfersWindow).Single(button=>ReferenceEquals(button.DataContext,all)&&Equals(button.CommandParameter,"Overwrite")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await all.Completion;check(all.Successful&&File.ReadAllText(Path.Combine(allTarget,"same.txt"))=="left","Replace from the empty review still transfers different bytes with matching metadata");
        }
        finally{Ferry.VerificationBoundary=null;if(all.Active){all.Cancel();await all.Completion;}DismissJob(all);Settings.TextScale=1;ApplyAppearance();}
        await VerifyTransferDesign15(root,check);
    }
}
#endif
