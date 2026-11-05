using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer;

sealed partial class PackageProbe
{
    async Task DiagnoseSpaceEntry(App app)
    {
        Settings.Load();Call(Type("Appearance"),null,"Initialize");
        owner=new MainWindow();new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        await VerifySpaceEntry122();
        File.WriteAllText(Path.Combine(root,"entry-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Closed+=(_,_)=>closed.TrySetResult();owner.Quit();await closed.Task;app.Shutdown(0);
    }
    async Task VerifySpaceEntry122()
    {
        var pane=(PaneView)owner.FindName("Left");var list=(ListView)pane.FindName("List");
        var admission=typeof(PaneView).GetField("SpaceAdmission",Flags);
        var previous=admission.GetValue(pane);
        string path=Directory.CreateDirectory(Path.Combine(root,"entry tree")).FullName;
        File.WriteAllBytes(Path.Combine(path,"payload.bin"),new byte[]{1,2,3});
        var listed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Ready(){if(pane.Dir==path&&(bool)Get(pane,"CanAnalyzeSpace"))listed.TrySetResult();}
        pane.StatsChanged+=Ready;pane.Navigate(path);Ready();await listed.Task;pane.StatsChanged-=Ready;
        await (Task)Get(pane,"SizingCompletion");
        var columns=((GridView)list.View).Columns.ToArray();
        var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int admissions=0;
        admission.SetValue(pane,(Func<Task>)(()=>{if(++admissions==1){entered.SetResult();return held.Task;}return Task.CompletedTask;}));
        var button=(Button)owner.FindName("AnalyzeSpaceCommand");
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await entered.Task;
            var startup=(Task)Get(pane,"SpaceCompletion");
            Check(!startup.IsCompleted&&((GridView)list.View).Columns.Any(c=>c.Header as string=="Space used"),"Analysis presents its actual mode immediately and owns pending admission in its completion task");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check((bool)Get(pane,"SpaceAnalyzing")&&!startup.IsCompleted&&admissions==1,"Repeated clicks cannot create a newer entry while the original admission is draining");
            held.TrySetResult();await startup;
            await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(!(bool)Get(pane,"SpaceAnalyzing")&&((GridView)list.View).Columns.SequenceEqual(columns)&&list.Items.Count==1,"Cancelling during admission restores the original rows and exact columns after the original task completes");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await (Task)Get(pane,"SpaceCompletion");
            Check((bool)Get(pane,"SpaceAnalyzing")&&list.Items.Count==1&&pane.FolderTotal==3&&ReferenceEquals(list.SelectedItem,pane.Items.First()),"Re-entering after cancelled admission shows the current tree, exact total and first selection without an obsolete entry clearing rows");
            await (Task)Call(typeof(PaneView),pane,"EndSpaceAsync");
            await (Task)Get(pane,"SizingCompletion");
            var navigationHeld=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var navigationEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            admission.SetValue(pane,(Func<Task>)(()=>{navigationEntered.TrySetResult();return navigationHeld.Task;}));
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await navigationEntered.Task;
                var owned=(Task)Get(pane,"SpaceCompletion");
                string destination=Directory.CreateDirectory(Path.Combine(root,"entry navigation")).FullName;
                pane.Navigate(destination);
                Check(pane.Dir==path&&!owned.IsCompleted,"Navigation during admission retains the original operation until its actual cancellation/drain");
                navigationHeld.TrySetResult();
                await (Task)typeof(PaneView).GetField("spaceTransition",Flags).GetValue(pane);
                Check(pane.Dir==destination&&!(bool)Get(pane,"SpaceAnalyzing")&&((GridView)list.View).Columns.SequenceEqual(columns),"Navigation after admission cancellation restores standard view and cannot be overwritten by obsolete startup");
            }
            finally{navigationHeld.TrySetResult();}
        }
        finally{held.TrySetResult();admission.SetValue(pane,previous);}
    }
}
