#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyMissingTabs23(string root,Action<bool,string> check)
    {
        string parent=Directory.CreateDirectory(Path.Combine(root,"version parent é 子")).FullName;
        string old=Directory.CreateDirectory(Path.Combine(parent,"1.0","portable")).FullName;
        File.WriteAllText(Path.Combine(parent,"parent-marker.txt"),"ancestor");
        string other=Directory.CreateDirectory(Path.Combine(root,"other-tab")).FullName;
        await S1Verification.Navigate(Left,other);
        Left.OpenInBackgroundTab(old);
        Directory.Move(Path.GetDirectoryName(old),Path.Combine(parent,"2.0"));
        var strip=(UniformGrid)Left.FindName("TabStrip");
        async Task Activate(Action action,string expected)
        {
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(){if(Left.Dir==expected&&Left.Listed)done.TrySetResult();}
            void Failed(string error)=>done.TrySetException(new IOException(error));
            Left.StatsChanged+=Ready;Left.Error+=Failed;
            try{Left.Listed=false;action();await done.Task;await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);}
            finally{Left.StatsChanged-=Ready;Left.Error-=Failed;}
        }
        await Activate(()=>((Button)strip.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),parent);
        check(Left.ActiveTab==1&&Left.OpenTabs[1]==parent&&((Button)strip.Children[1]).Tag as string=="on", "Deleted version tab click repairs the path and selected header across multiple missing levels");
        check(Left.Items.Any(e=>e.Name=="parent-marker.txt")&&!Left.Items.Any(e=>e.Path.StartsWith(other+"\\")),"Recovered tab lists the actual ancestor rather than the previous tab");
        await Activate(()=>Left.NextTab(-1),other);
        await Activate(()=>Left.NextTab(1),parent);
        check(Left.Dir==parent,"Keyboard tab cycling uses the repaired destination");
        await Activate(()=>Left.Restore(new(){other,old},1,new(){1}),parent);
        check(Left.OpenTabs.Count==2&&Left.ActiveTab==1&&Left.PinnedTabs.SequenceEqual(new[]{1}),"Restoring a missing pinned tab preserves its position and active identity");
        await Activate(()=>Left.ShowTab(0),other);
        await Activate(()=>Left.ShowTab(1),parent);
        check(Left.Dir==parent,"External tab activation uses the repaired path");
        Settings.Tabs["Left"]=Left.OpenTabs.ToList();Settings.Save();Settings.Load();
        check(Settings.Tabs["Left"].SequenceEqual(new[]{other,parent}),"Actual settings round-trip persists the repaired tab destination");
        string unc=@"\\pathweft-offline.invalid\share\old\folder";
        check(PaneView.RecoverMissingFolder(unc)==unc,"Offline UNC destinations are preserved without a network availability probe");
        string absentDrive=Enumerable.Range('D',23).Select(c=>((char)c)+@":\").FirstOrDefault(p=>!Directory.Exists(p));
        if(absentDrive!=null)
        {
            string unavailable=absentDrive+@"missing\folder";
            check(PaneView.RecoverMissingFolder(unavailable)==unavailable,"An unavailable drive is not reinterpreted as a missing descendant");
        }
        string zip=Path.Combine(parent,"preview.zip");
        using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))archive.CreateEntry("docs/");
        check(PaneView.RecoverMissingFolder(zip+@"\docs")==zip+@"\docs","Existing archive folder identities bypass filesystem recovery");
        await S1Verification.Snapshot(this,root,"missing-tab-recovered");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Tab recovery does not activate the host desktop");
    }
}
#endif
