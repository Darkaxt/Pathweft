#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileExplorer.Search;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifySpaceStartup23(string root,Action<bool,string> check)
    {
        Settings.Preview=false;ApplyPreview();SetActive(Left);
        string folder=Directory.CreateDirectory(Path.Combine(root,"occupied SDK é 子")).FullName;
        string nested=Directory.CreateDirectory(Path.Combine(folder,"nested")).FullName;
        File.WriteAllBytes(Path.Combine(folder,"direct.bin"),new byte[17]);
        File.WriteAllBytes(Path.Combine(nested,"nested.bin"),new byte[304]);
        Left.SetSizingSuspended(true);Right.SetSizingSuspended(true);
        await Task.WhenAll(Left.StopSizingAsync(),Right.StopSizingAsync());
        await S1Verification.Navigate(Left,folder);
        // CORE-32: a held index service (it reads requests and never answers) is waited for, never replaced by a
        // disk walk on a timer; Ctrl+R stops the wait and measures on disk.
        using var slow=new FakeIndexService{Hold=true};
        var service=IndexedSearch.ServiceOverride;
        IndexedSearch.ServiceOverride=new IndexServiceClient(slow.Name);
        var visits=new System.Collections.Concurrent.ConcurrentBag<string>();
        try
        {
            SpaceScan.BeforeEnumeration=(path,token)=>visits.Add(path);
            AnalyzeSpaceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(Left.SpaceActivity.Visibility==Visibility.Visible&&!Left.SpaceActivity.IsIndeterminate,"Reduced-motion initial loading remains visible while startup is active");
            await Task.Delay(TimeSpan.FromSeconds(6)); // longer than the 5-second wait that used to start a disk walk
            check(!Left.SpaceCompletion.IsCompleted&&visits.IsEmpty&&!slow.Requests.IsEmpty,"While the index service does not answer, the analysis waits for it and reads no folder on disk ("+visits.Count+" read)");
            Left.Refresh(force:true,measure:true);
            await Left.SpaceCompletion;
            check(Left.FolderTotal==321&&Left.Items.Count()==2&&Left.Items.OfType<SpaceEntry>().All(row=>row.State==SpaceState.Complete),"Ctrl+R stops waiting for the index and measures the nested fixture on disk to exact totals");
            check(Left.SpaceActivity.Visibility==Visibility.Collapsed,"Actual completion ends the initial loading indicator");
        }
        finally
        {
            SpaceScan.BeforeEnumeration=null;IndexedSearch.ServiceOverride=service;slow.Hold=false;
            await Left.EndSpaceAsync();
        }
        var previous=Left.SpaceAdmission;
        var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Left.SpaceAdmission=()=>held.Task;
        try
        {
            Settings.Animations=true;
            AnalyzeSpaceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(Left.SpaceActivity.Visibility==Visibility.Visible&&Left.SpaceActivity.IsIndeterminate==Motion.On,"Initial loading follows application and Windows motion preferences during real held admission");
            await VerifyCapacityColors23(root,check);
        }
        finally{held.TrySetResult();await Left.SpaceCompletion;await Left.EndSpaceAsync();Left.SpaceAdmission=previous;Settings.Animations=false;}
        string reported=@"D:\Tools\Modern VLC";
        if(Directory.Exists(reported))
        {
            await S1Verification.Navigate(Left,reported);
            AnalyzeSpaceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Left.SpaceCompletion;
            check(Left.Items.Any()&&Left.SpaceSummary.Status is "Measured" or "Indexed"&&Left.SpaceActivity.Visibility==Visibility.Collapsed,"Read-only analysis of the reported Modern VLC path reaches a populated terminal tree");
            await Left.EndSpaceAsync();
        }
    }
    async Task VerifyCapacityColors23(string root,Action<bool,string> check)
    {
        Settings.WindowsAccent=false;Settings.Accent="#D45D82";
        foreach(string theme in new[]{"dark","light"})
        {
            Settings.NativeMode=theme;ApplyAppearance();
            foreach(long free in new[]{201L,200L,100L})
            {
                var row=new DriveRow("D:\\","Test","NTFS",1000,free,false,"Test",DriveType.Fixed);
                var expected=DrivePresentation.Fill(1000,free);
                var button=DriveButton(row);
                var fill=((Grid)button.Content).Children.OfType<Grid>().Single(grid=>grid.Height==2).Children.OfType<Border>().Last();
                shownDrives=new(){row};UpdateStats();
                check(ReferenceEquals(fill.Background,expected)&&ReferenceEquals(DriveFill.Background,expected),"Sidebar and summary disk bars share capacity colors without a duplicate address strip: "+theme+"/"+free);
            }
            Settings.TextScale=1;ApplyAppearance();
            await S1Verification.Snapshot(this,root,"space-starting-"+theme);
        }
        check(((SolidColorBrush)DrivePresentation.Fill(79.9)).Color==Color.FromRgb(0,120,212)&&((SolidColorBrush)new Entry{DriveTotal=1000,DriveFree=201}.DriveColor).Color==Color.FromRgb(0,120,212),"Healthy blue and the This PC binding remain independent of the custom accent");
    }
}
#endif
