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
        await VerifyCancelledIndexDispatch();
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
            var activity=(ProgressBar)pane.FindName("SpaceActivity");
            Check(!startup.IsCompleted&&((GridView)list.View).Columns.Any(c=>c.Header as string=="Space used")&&activity.Visibility==Visibility.Visible&&((TextBlock)pane.FindName("Footer")).Text=="Preparing space analysis…","Analysis presents its mode and loading feedback immediately and owns pending admission in its completion task");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check((bool)Get(pane,"SpaceAnalyzing")&&!startup.IsCompleted&&admissions==1,"Repeated clicks cannot create a newer entry while the original admission is draining");
            held.TrySetResult();await startup;
            await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(!(bool)Get(pane,"SpaceAnalyzing")&&((GridView)list.View).Columns.SequenceEqual(columns)&&list.Items.Count==1,"Cancelling during admission restores the original rows and exact columns after the original task completes");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await (Task)Get(pane,"SpaceCompletion");
            Check((bool)Get(pane,"SpaceAnalyzing")&&list.Items.Count==1&&pane.FolderTotal==3&&ReferenceEquals(list.SelectedItem,pane.Items.First())&&activity.Visibility==Visibility.Collapsed,"Re-entering after cancelled admission shows the current tree, exact total and first selection, and ends loading feedback");
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

    async Task VerifyCancelledIndexDispatch()
    {
        var type=Type("Search.EverythingSearchClient");var client=type.GetProperty("Shared",Flags).GetValue(null);
        var dispatcher=await ((TaskCompletionSource<Dispatcher>)type.GetField("ready",Flags).GetValue(client)).Task;
        using var release=new ManualResetEventSlim();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held=dispatcher.InvokeAsync(()=>{entered.TrySetResult();release.Wait();});
        await entered.Task;
        using var cancel=new CancellationTokenSource();
        var query=(Task)Call(type,client,"QueryAsync","folder:","",0u,1u,"Name",false,cancel.Token);
        bool cancelledWithoutDispatch=false;
        try
        {
            cancel.Cancel();
            // Observation bound only: never cancels or abandons the native owner.
            // The held dispatcher is released and the original task drained below.
            await Task.WhenAny(query,Task.Delay(1000));
            cancelledWithoutDispatch=query.IsCanceled;
        }
        finally
        {
            release.Set();await held;
            try{await query;}catch(OperationCanceledException){}
        }
        Check(cancelledWithoutDispatch,"Cancelling an index request completes its caller while the SDK dispatcher is occupied, without releasing or abandoning the original dispatcher operation");
    }
}
