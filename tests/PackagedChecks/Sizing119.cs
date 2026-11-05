using System.Collections;
using System.IO;
using Expression = System.Linq.Expressions.Expression;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using FileExplorer;

sealed partial class PackageProbe
{
    void VerifyWindows119()
    {
        foreach(string name in new[]{"SettingsWindow","HelpWindow","WhatsNewWindow"})
        {
            var window=(Window)Activator.CreateInstance(Type(name),new object[]{owner});
            try{Check(ReferenceEquals(window.Style,Application.Current.Resources["AuxiliaryWindow"])&&!window.IsVisible,"Delivered "+name+" uses the shared auxiliary style without opening a host window");}
            finally{window.Close();}
        }
        var update=(Window)Activator.CreateInstance(Type("UpdateDialog"),new object[]{owner,null});
        try
        {
            Check((string)Get(update,"CheckState")=="checking"&&((ProgressBar)update.FindName("Progress")).IsIndeterminate&&((TextBlock)update.FindName("Heading")).Text.Contains("Checking"),"Delivered update checker immediately contains its visible progress and installed-version state");
            Call(update.GetType(),update,"ShowCheckResult",false,"The installed version is current.");
            Check((string)Get(update,"CheckState")=="current"&&((TextBlock)update.FindName("Heading")).Text.Contains(Product.GetName().Version.ToString(3)),"Delivered update checker presents an explicit up-to-date confirmation");
            Call(update.GetType(),update,"ShowCheckResult",true,"An actual check failed.");
            Check((string)Get(update,"CheckState")=="failed"&&((Button)update.FindName("UpdateButton")).Content.ToString()=="Retry","Delivered failed update state retains its explicit Retry action");
        }
        finally{update.Close();}
    }
    async Task VerifySizing119()
    {
        string folder=Directory.CreateDirectory(Path.Combine(root,"delivered analysis é 子")).FullName;
        string nested=Directory.CreateDirectory(Path.Combine(folder,"nested")).FullName;
        File.WriteAllBytes(Path.Combine(nested,"payload.bin"),new byte[579]);
        var updates=new List<object>();var updateType=Type("SpaceUpdate");
        var parameter=Expression.Parameter(updateType);
        var collect=Expression.Lambda(typeof(Action<>).MakeGenericType(updateType),Expression.Call(Expression.Constant(updates),typeof(List<object>).GetMethod("Add"),Expression.Convert(parameter,typeof(object))),parameter).Compile();
        await (Task)Call(Type("SpaceScan"),null,"RunAsync",folder,false,"",collect,CancellationToken.None);
        var terminal=updates.Last(update=>(string)Get(update,"Path")==folder);
        Check((long)Get(terminal,"Bytes")==579&&Get(terminal,"State").ToString()=="Complete","Delivered Diskweft-derived metadata traversal reaches the exact real recursive total");
        var pane=(PaneView)owner.FindName("Left");
        var listed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(){if(pane.Dir==folder&&(bool)Get(pane,"CanAnalyzeSpace"))listed.TrySetResult();}
        pane.StatsChanged+=Observe;
        try{pane.Navigate(folder);Observe();await listed.Task;}finally{pane.StatsChanged-=Observe;}
        await (Task)Get(pane,"SizingCompletion");
        var analyze=(Button)owner.FindName("AnalyzeSpaceCommand");
        Check(analyze.IsEnabled,"Delivered toolbar becomes enabled after asynchronous folder readiness without reactivating its pane");
        var list=(ListView)pane.FindName("List");var columns=((GridView)list.View).Columns.ToArray();
        var invoke=(System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(analyze).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
        invoke.Invoke();await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await (Task)Get(pane,"SpaceCompletion");
        Check((bool)Get(pane,"SpaceAnalyzing")&&pane.FolderTotal==579&&((GridView)list.View).Columns.Select(column=>column.Header?.ToString()).SequenceEqual(new[]{"Name","Space used","Of this folder","Status"}),"Delivered current-pane toggle exposes actual tree/size/usage/status with the correct recursive total");
        Check(ReferenceEquals(list.SelectedItem,pane.Items.First()),"Delivered analysis selects its first available row on entry");
        var selected=list.SelectedItem;
        bool analysisNoise=false;
        void AnalysisChanged(){analysisNoise|=pane.Items.Any(entry=>Get(entry,"SpaceStatus").ToString() is "Pending" or "Measuring…");}
        pane.StatsChanged+=AnalysisChanged;
        try
        {
            var replacement=SizeReady(()=>pane.FolderTotal==591&&!pane.SizesPending);
            await Task.Run(()=>{using var stream=File.OpenWrite(Path.Combine(nested,"payload.bin"));stream.SetLength(591);});await replacement;await (Task)Get(pane,"SpaceCompletion");
            Check(!analysisNoise&&ReferenceEquals(list.SelectedItem,selected),"Delivered watched analysis replaces a completed branch without flashing states or losing selection");
            replacement=SizeReady(()=>pane.FolderTotal==579&&!pane.SizesPending);
            await Task.Run(()=>{using var stream=File.OpenWrite(Path.Combine(nested,"payload.bin"));stream.SetLength(579);});await replacement;await (Task)Get(pane,"SpaceCompletion");
        }
        finally{pane.StatsChanged-=AnalysisChanged;}
        foreach(string name in new[]{"HiddenToggle","PanesCommand","PreviewCommand","AnalyzeSpaceCommand"})
        {
            var button=(Button)owner.FindName(name);button.ApplyTemplate();
            bool on=button.Tag as string=="on";
            var border=(Border)button.Template.FindName("Bd",button);
            Check(!on||border.BorderBrush.ToString()==((System.Windows.Media.Brush)Application.Current.Resources["Accent"]).ToString(),"Delivered active toggle has its persistent accent outline: "+name);
            Check(!string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetItemStatus(button)),"Delivered toggle exposes current accessible state: "+name);
        }
        await (Task)Call(typeof(PaneView),pane,"EndSpaceAsync");
        Check(!(bool)Get(pane,"SpaceAnalyzing")&&((GridView)list.View).Columns.SequenceEqual(columns)&&pane.Dir==folder,"Delivered toggle restores the original pane and exact normal column identities");
        await (Task)Call(typeof(PaneView),pane,"StopSizingAsync");
        pane.EditPath();
        var address=(TextBox)pane.FindName("FilterBox");
        string draft=Path.Combine(folder,"draft address é 子");address.Text=draft;address.Select(7,3);
        var focus=System.Windows.Input.FocusManager.GetFocusedElement(owner);
        list.SelectedItems.Clear();
        bool noise=false,editLost=false;
        void Editing()
        {
            noise|=((TextBlock)pane.FindName("Footer")).Text.Contains("measuring")||((TextBlock)owner.FindName("StatFolder")).Text.EndsWith('+')||((TextBlock)owner.FindName("StatFolderSub")).Text.Contains("measuring");
            editLost|=address.Text!=draft||address.Visibility!=Visibility.Visible||address.SelectionStart!=7||address.SelectionLength!=3||System.Windows.Input.FocusManager.GetFocusedElement(owner)!=focus;
        }
        async Task SizeReady(Func<bool> predicate)
        {
            var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(){if(predicate())complete.TrySetResult();}
            pane.StatsChanged+=Changed;
            try{Changed();await complete.Task;await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
            finally{pane.StatsChanged-=Changed;}
        }
        pane.StatsChanged+=Editing;
        try
        {
            var grown=SizeReady(()=>pane.FolderTotal==580&&!pane.SizesPending);
            await Task.Run(()=>{using var stream=File.OpenWrite(Path.Combine(nested,"payload.bin"));stream.SetLength(580);});await grown;
            for(int i=0;i<3;i++)
            {
                var pending=SizeReady(()=>pane.SizesPending);
                await Task.Run(()=>{using var stream=File.OpenWrite(Path.Combine(nested,"payload.bin"));stream.WriteByte((byte)i);});
                await pending;await SizeReady(()=>!pane.SizesPending);
            }
            var replacement=SizeReady(()=>pane.Items.Any(e=>e.Name=="new row.txt"));
            await Task.Run(()=>File.WriteAllText(Path.Combine(folder,"new row.txt"),""));await replacement;
            Check(!noise&&!editLost,"Delivered real watcher updates retain known totals quietly and preserve draft text, selection, visibility and logical focus through row replacement");
        }
        finally{pane.StatsChanged-=Editing;}
        address.Text=nested;
        var submit=(Task)Call(typeof(PaneView),pane,"SubmitAddressAsync");pane.Refresh();await submit;
        Check(pane.Dir==nested,"Delivered Enter submission survives a concurrent listing refresh");
        DateTime stamp=Directory.GetLastWriteTimeUtc(nested);var cache=typeof(SizeCache);
        int limit=(int)cache.GetField("HotLimit",Flags).GetRawConstantValue();
        for(int i=0;i<limit+1;i++)SizeCache.Set(Path.Combine(folder,"cache-record",i.ToString()),stamp,i);
        Check((int)cache.GetProperty("HotCount",Flags).GetValue(null)==limit&&SizeCache.Get(Path.Combine(folder,"cache-record","0"),stamp)==0,"Delivered hot cache is bounded and evicted records remain available from SQLite");
        Call(cache,null,"Invalidate",Path.Combine(folder,"cache-record"),true);
        Check(SizeCache.Get(Path.Combine(folder,"cache-record","0"),stamp)<0,"Delivered targeted subtree invalidation removes persistent descendants");
    }
}
