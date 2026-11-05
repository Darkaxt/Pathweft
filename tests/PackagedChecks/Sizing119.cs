using System.Collections;
using System.IO;
using Expression = System.Linq.Expressions.Expression;
using System.Windows.Controls;
using System.Windows;
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
            Check((string)Get(update,"CheckState")=="current"&&((TextBlock)update.FindName("Heading")).Text.Contains("1.1.19"),"Delivered update checker presents an explicit up-to-date confirmation");
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
        void Observe(){if(pane.Dir==folder&&(bool)typeof(PaneView).GetField("Listed",Flags).GetValue(pane))listed.TrySetResult();}
        pane.StatsChanged+=Observe;
        try{pane.Navigate(folder);Observe();await listed.Task;}finally{pane.StatsChanged-=Observe;}
        await (Task)Get(pane,"SizingCompletion");
        var list=(ListView)pane.FindName("List");var columns=((GridView)list.View).Columns.ToArray();
        await (Task)Call(typeof(PaneView),pane,"ToggleSpaceAsync");await (Task)Get(pane,"SpaceCompletion");
        Check((bool)Get(pane,"SpaceAnalyzing")&&pane.FolderTotal==579&&((GridView)list.View).Columns.Select(column=>column.Header?.ToString()).SequenceEqual(new[]{"Name","Space used","Of this folder","Status"}),"Delivered current-pane toggle exposes actual tree/size/usage/status with the correct recursive total");
        await (Task)Call(typeof(PaneView),pane,"EndSpaceAsync");
        Check(!(bool)Get(pane,"SpaceAnalyzing")&&((GridView)list.View).Columns.SequenceEqual(columns)&&pane.Dir==folder,"Delivered toggle restores the original pane and exact normal column identities");
        await (Task)Call(typeof(PaneView),pane,"StopSizingAsync");
        DateTime stamp=Directory.GetLastWriteTimeUtc(nested);var cache=typeof(SizeCache);
        int limit=(int)cache.GetField("HotLimit",Flags).GetRawConstantValue();
        for(int i=0;i<limit+1;i++)SizeCache.Set(Path.Combine(folder,"cache-record",i.ToString()),stamp,i);
        Check((int)cache.GetProperty("HotCount",Flags).GetValue(null)==limit&&SizeCache.Get(Path.Combine(folder,"cache-record","0"),stamp)==0,"Delivered hot cache is bounded and evicted records remain available from SQLite");
        Call(cache,null,"Invalidate",Path.Combine(folder,"cache-record"),true);
        Check(SizeCache.Get(Path.Combine(folder,"cache-record","0"),stamp)<0,"Delivered targeted subtree invalidation removes persistent descendants");
    }
}
