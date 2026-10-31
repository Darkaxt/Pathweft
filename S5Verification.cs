#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;

public partial class MainWindow
{
    // Finite hidden scenario for the actual pane/widget/archive and transfer-card
    // callers. No clipboard mutation, remote listener or Release test endpoint.
    internal async Task VerifyS5(string root)
    {
        root=Path.GetFullPath(root);
        if(!root.StartsWith(@"D:\Temp\pathweft-s5-native\ui-",StringComparison.OrdinalIgnoreCase)||!HiddenTrialPresentation.Enabled) throw new ArgumentException("Owned hidden S5 profile required.");
        string evidence=Directory.CreateDirectory(Path.Combine(root,"evidence")).FullName;
        var checks=new List<object>(); int result=1; SearchWidgetWindow widget=null;
        void Check(bool pass,string criterion)
        {
            HiddenTrialPresentation.Phase=criterion; checks.Add(new{criterion,passed=pass});
            File.WriteAllText(Path.Combine(evidence,"phase.json"),JsonSerializer.Serialize(checks[^1]));
            if(!pass) throw new IOException(criterion);
        }
        IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject
        {
            if(node is T found) yield return found;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) foreach(var child in Children<T>(VisualTreeHelper.GetChild(node,i))) yield return child;
        }
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Select(PaneView pane,string name) {SetActive(pane);pane.ListForVerification.SelectedItem=pane.Items.Single(e=>e.Name==name);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
        async Task Render() {foreach(var job in jobs)job.Refresh();UpdateTransfers();await Dispatcher.InvokeAsync(()=>transfersWindow.UpdateLayout(),DispatcherPriority.ApplicationIdle);}
        async Task<Job> Action(System.Action action,Choice? decision=null)
        {
            var old=jobs.ToHashSet(); var conflict=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(decision!=null) Ferry.VerificationBoundary=(phase,_,_,_)=>{if(phase=="conflicts")conflict.TrySetResult();};
            try
            {
                action(); var job=jobs.Single(j=>!old.Contains(j));
                if(decision is {} choice)
                {
                    await Task.WhenAny(conflict.Task,job.Completion);Check(job.HasConflicts,"Actual caller enters the shared conflict card");await Render();
                    Click(Children<Button>(transfersWindow).Single(b=>b.DataContext==job && b.CommandParameter?.ToString()==choice.ToString()));
                }
                await job.Completion;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                checks.Add(new{callerOutcome=job.Title,phase=job.Phase.ToString(),job.TotalFiles,job.DoneFiles,job.VerifiedFiles,job.RenamedFiles,job.SkippedFiles,errors=job.Errors.ToArray(),notes=job.Notes.ToArray()});
                return job;
            }
            finally{Ferry.VerificationBoundary=null;}
        }
        string Folder(string name)=>Directory.CreateDirectory(Path.Combine(root,name)).FullName;
        string Write(string folder,string name,string text) {string file=Path.Combine(folder,name);File.WriteAllText(file,text);return file;}
        try
        {
            Check(HiddenTrialPresentation.IsOutsideDisplays(this),"S5 main window stays outside every display");
            Width=1280;Height=820;Settings.Verify=true;
            string source=Folder("source"),target=Folder("target");
            string file=Write(source,"pane.txt","pane copy data");
            await S1Verification.Navigate(Left,source);await S1Verification.Navigate(Right,target);await Select(Left,"pane.txt");
            var copy=await Action(()=>Click(CopyButton));
            Check(copy.Successful&&copy.VerifiedFiles==1&&File.ReadAllText(Path.Combine(target,"pane.txt"))=="pane copy data"&&Right.Selected.Any(e=>e.Name=="pane.txt"),"Pane Copy button verifies, publishes and selects the actual arrival");
            int undoBefore=undo.Count; await Select(Left,"pane.txt");
            var skip=await Action(()=>Click(CopyButton),Choice.Skip);
            uint clipboard=GetClipboardSequenceNumber();await CompleteCut(skip,clipboard);
            Check(skip.Partial&&!skip.Successful&&skip.SkippedFiles==1&&skip.TotalFiles==1&&undo.Count==undoBefore&&clipboard==GetClipboardSequenceNumber(),"Skipped transfer stays Partial, preserves cut ownership and adds no unsafe undo");
            await Render();await S1Verification.Snapshot(transfersWindow,evidence,"partial-card");
            string merge=Directory.CreateDirectory(Path.Combine(source,"merge")).FullName;Write(merge,"new.txt","new");
            string existing=Directory.CreateDirectory(Path.Combine(target,"merge")).FullName;Write(existing,"kept.txt","keep");
            await S1Verification.Navigate(Left,source);await Select(Left,"merge");var merged=await Action(()=>Click(CopyButton));
            Check(merged.Successful&&merged.Merged&&File.ReadAllText(Path.Combine(existing,"kept.txt"))=="keep"&&File.ReadAllText(Path.Combine(existing,"new.txt"))=="new","Pane folder merge retains unrelated existing contents");
            await Select(Left,"pane.txt");File.WriteAllText(Path.Combine(target,"pane.txt"),"old");var replaced=await Action(()=>Click(CopyButton),Choice.Overwrite);
            Check(replaced.Successful&&replaced.VerifiedFiles==1&&File.ReadAllText(Path.Combine(target,"pane.txt"))=="pane copy data","Real conflict Replace button publishes a verified overwrite");
            string moveTarget=Folder("moved");await S1Verification.Navigate(Right,moveTarget);await Select(Left,"pane.txt");var renamed=await Action(()=>Click(MoveButton));
            Check(renamed.Successful&&renamed.RenamedFiles==1&&!File.Exists(file),"Pane Move button uses the same-volume identity path");
            string cross=Directory.CreateDirectory(Path.Combine(@"E:\Temp\pathweft-s5-review",Path.GetFileName(root)+"-move")).FullName;
            string moving=Write(source,"cross.txt","cross caller");await S1Verification.Navigate(Left,source);await S1Verification.Navigate(Right,cross);await Select(Left,"cross.txt");Settings.Verify=false;
            var moved=await Action(()=>Click(MoveButton));Settings.Verify=true;
            Check(moved.Successful&&moved.Verify&&moved.VerifiedFiles==1&&!File.Exists(moving)&&File.ReadAllText(Path.Combine(cross,"cross.txt"))=="cross caller","Real pane cross-volume Move verifies even with optional copy verification off");

            await S1Verification.Navigate(Left,root);await S1Verification.Navigate(Right,target);await Select(Left,"storage-source");var storageCopy=await Action(()=>Click(CopyButton));
            Check(storageCopy.Successful&&storageCopy.VerifiedFiles==2&&storageCopy.Notes.Any(note=>note.Contains("Compression"))&&storageCopy.Notes.Any(note=>note.Contains("Sparse"))&&storageCopy.StateGroup=="Done","Pane storage copy verifies contents and reports destination policy without a false failure");
            await Render();
            var storageNote=Children<TextBlock>(transfersWindow).Single(text=>text.DataContext==storageCopy&&text.Text==storageCopy.ErrorSummary);
            Check(!storageCopy.HasTransferErrors&&storageNote.TextWrapping==TextWrapping.Wrap&&storageNote.Foreground.ToString()==FindResource("Muted").ToString()&&storageNote.ToolTip.ToString().Contains("Sparse"),"Storage notes wrap in the existing neutral theme and expose their complete detail");
            await S1Verification.Snapshot(transfersWindow,evidence,"storage-policy-card");
            await S1Verification.Navigate(Left,source);

            // The compact1.1.2 widget has no dedicated transfer/destination UI.
            // Pane/archive/drop-bin callers remain qualified in this workflow.

            string archive=Path.Combine(root,"fixture.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("entry.txt").Open());writer.Write("archive payload");}
            await S1Verification.Navigate(Left,archive);await S1Verification.Navigate(Right,target);await Select(Left,"entry.txt");var extracted=await Action(()=>Click(CopyButton));
            Check(extracted.Successful&&extracted.ArchiveOperation&&File.ReadAllText(Path.Combine(target,"entry.txt"))=="archive payload","Real archive pane Copy reaches the same transfer owner and publishes checked content");
            string archiveInput=Write(source,"archive-cut.txt","archive cut payload");await S1Verification.Navigate(Left,source);await S1Verification.Navigate(Right,archive);await Select(Left,"archive-cut.txt");var inserted=await Action(()=>Click(MoveButton));
            using(var zip=ZipFile.OpenRead(archive)){using var reader=new StreamReader(zip.GetEntry("archive-cut.txt")!.Open());Check(inserted.Successful&&!File.Exists(archiveInput)&&reader.ReadToEnd()=="archive cut payload","Filesystem-to-archive Move removes only the exact verified source");}
            string ads=Write(source,"ads.txt","main");File.WriteAllText(ads+":fixture","retained stream");await S1Verification.Navigate(Left,source);await Select(Left,"ads.txt");int safeUndo=undo.Count;var refused=await Action(()=>Click(MoveButton));
            Check(refused.Phase==Phase.Failed&&File.ReadAllText(ads+":fixture")=="retained stream"&&undo.Count==safeUndo,"Archive caller refuses unsupported streams without unsafe source removal or undo");

            await S1Verification.Navigate(Right,target);string pause=Write(source,"pause.txt",new string('p',8*1024*1024));await S1Verification.Navigate(Left,source);await Select(Left,"pause.txt");
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int entered=0;
            Ferry.VerificationBoundary=(phase,job,path,_)=>{if(phase=="copy-chunk"&&path==pause&&Interlocked.CompareExchange(ref entered,1,0)==0){job.Pause();ready.TrySetResult();}};
            Click(CopyButton);var paused=jobs[0];await Task.WhenAny(ready.Task,paused.Completion);Check(ready.Task.IsCompleted&&paused.Paused,"Copy enters an actual paused progress boundary");await Render();
            await S1Verification.Snapshot(transfersWindow,evidence,"paused-before-control");
            var resume=Children<Button>(transfersWindow).Single(b=>b.DataContext==paused&&Children<TextBlock>(b).Any(t=>new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text.Contains("Resume")));
            Check(resume.IsVisible&&resume.IsEnabled&&paused.Rate==0&&!paused.Stats.Contains("/s"),"Actual card exposes Resume and suppresses stale throughput while paused");await S1Verification.Snapshot(transfersWindow,evidence,"paused-card");Click(resume);await paused.Completion;Check(paused.Successful,"Actual Resume control completes the paused copy");Ferry.VerificationBoundary=null;

            string cancelling=Write(source,"cancel.txt",new string('c',8*1024*1024));await S1Verification.Navigate(Left,source);await Select(Left,"cancel.txt");
            var cancelReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int cancelEntered=0;
            Ferry.VerificationBoundary=(phase,job,path,_)=>{if(phase=="copy-chunk"&&path==cancelling&&Interlocked.CompareExchange(ref cancelEntered,1,0)==0){job.Pause();cancelReady.TrySetResult();}};
            Click(CopyButton);var cancelled=jobs[0];await Task.WhenAny(cancelReady.Task,cancelled.Completion);Check(cancelReady.Task.IsCompleted,"Actual direct copy reaches the cancellation control fixture");await Render();
            Click(Children<Button>(transfersWindow).Single(b=>b.DataContext==cancelled&&Children<TextBlock>(b).Any(t=>new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text.Contains("Cancel"))));
            await cancelled.Completion;Ferry.VerificationBoundary=null;
            Check(cancelled.Phase==Phase.Cancelled&&cancelled.Errors.IsEmpty&&File.Exists(cancelling)&&!File.Exists(Path.Combine(target,"cancel.txt")),"Actual Cancel control removes incomplete direct output and retains the source");

            foreach(double scale in new[]{1.0,1.5})
            {
                transfersWindow.FontSize=12*scale;transfersWindow.Width=640*scale;await Render();
                Check(Children<TextBlock>(transfersWindow).Any(t=>t.Text.Contains("Failed"))&&Children<TextBlock>(transfersWindow).Any(t=>t.Text=="Partial"),"Rendered cards distinguish failure and partial outcomes at scale "+scale);
                await S1Verification.Snapshot(transfersWindow,evidence,"outcomes-scale-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach(Window window in Application.Current.Windows) Check(HiddenTrialPresentation.IsOutsideDisplays(window),"Hidden native geometry: "+window.Title);
            Check(HiddenTrialPresentation.ForegroundViolations==0,"No owned test window acquired the foreground");result=0;
        }
        catch(Exception ex){checks.Add(new{failure=ex.ToString()});}
        finally
        {
            Ferry.VerificationBoundary=null;foreach(var job in jobs.Where(j=>j.Active))job.Cancel();await Task.WhenAll(jobs.Select(j=>j.Completion));widget?.Shutdown();
            File.WriteAllText(Path.Combine(evidence,"results.json"),JsonSerializer.Serialize(new{result,pid=Environment.ProcessId,HiddenTrialPresentation.ForegroundViolations,checks},new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown(result);
        }
    }
}
#endif
