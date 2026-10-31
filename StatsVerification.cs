#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyStatsVisibility(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        Border Card(string name)=>(Border)FindName(name);
        Settings.Stats=2;ApplyStats();SetSinglePane(true);SetActive(Left);
        RefreshDrives();await driveRefreshWork;
        await S1Verification.Navigate(Left,PaneView.ThisPC);await Settle();
        check(StatsRow.Visibility==Visibility.Collapsed&&StatsRow.ActualHeight==0,"This PC collapses the complete placeholder statistics strip and its layout height");
        check(Settings.Stats==2&&((TextBlock)Left.FindName("Footer")).Text.EndsWith("locations"),"Automatic collapse preserves the full-card preference and This PC counts locations without file/byte totals");
        double expandedPaneHeight=PaneArea.ActualHeight;
        Left.List.SelectedItem=Left.Items.First();await Settle();
        check(StatsRow.Visibility==Visibility.Collapsed,"Selecting a drive does not restore duplicate empty folder statistics");
        string folder=Directory.CreateDirectory(Path.Combine(root,"stats-folder")).FullName;
        string empty=Directory.CreateDirectory(Path.Combine(root,"stats-empty")).FullName;
        File.WriteAllText(Path.Combine(folder,"payload.txt"),"statistics payload");
        await S1Verification.Navigate(Left,folder);await Settle();
        check(StatsRow.Visibility==Visibility.Visible&&StatsRow.ActualHeight>0&&expandedPaneHeight-PaneArea.ActualHeight>40,"Navigating to useful folder data restores cards and This PC reclaimed their actual height");
        check(Card("FolderCard").Visibility==Visibility.Visible&&Card("SelectionCard").Visibility==Visibility.Visible,"Populated folder totals and actual latest-change data remain available");
        Left.List.SelectedItem=Left.Items.Single(e=>e.Name=="payload.txt");await Settle();
        check(SelTitle.Text=="Selected"&&SelValue.Text!="—","Selecting a real file restores its actual selection summary");
        await S1Verification.Navigate(Left,empty);await Settle();
        check(Card("FolderCard").Visibility==Visibility.Collapsed&&Card("SelectionCard").Visibility==Visibility.Collapsed,"An empty folder omits empty folder/latest-change cards");
        check(DriveCard.Visibility==Visibility.Visible&&DriveCard.ActualWidth>=StatsRow.ActualWidth-1&&StatsRow.ColumnDefinitions.Skip(1).All(c=>c.ActualWidth==0),"A useful capacity card fills the strip without empty columns or gaps");
        string archive=Path.Combine(root,"stats.zip");ZipFile.CreateFromDirectory(folder,archive);
        await S1Verification.Navigate(Left,archive);await Settle();
        check(Card("FolderCard").Visibility==Visibility.Visible&&StatsRow.Visibility==Visibility.Visible,"Virtual compressed folders retain meaningful content statistics");
        await S1Verification.Navigate(Left,PaneView.ThisPC);
        var query=(TextBox)Left.FindName("FilterBox");query.Text="path:\"D:\\Projects\\Pathweft\" \"README.md\"";
        await Left.SubmitAddressAsync();await Settle();
        check(Left.InSearch&&Left.Items.Any()&&StatsRow.Visibility==Visibility.Visible,"Global Everything results from This PC restore useful search statistics");
        Settings.Stats=0;ApplyStats();await S1Verification.Navigate(Left,folder);await Settle();
        check(StatsRow.Visibility==Visibility.Collapsed&&Settings.Stats==0,"Navigation never overrides the user's disabled statistics preference");
        Settings.Stats=1;ApplyStats();await Settle();
        check(StatsRow.Visibility==Visibility.Visible&&DriveIcon.Visibility==Visibility.Collapsed,"Compact mode restores useful data with its existing compact styling");
        await S1Verification.Navigate(Left,PaneView.ThisPC);await Settle();
        check(StatsRow.Visibility==Visibility.Collapsed&&Settings.Stats==1,"Compact mode also collapses empty This PC statistics without changing its preference");
        SetSinglePane(false);await S1Verification.Navigate(Right,folder);SetActive(Right);await Settle();
        check(StatsRow.Visibility==Visibility.Visible,"The active populated pane restores statistics in split view");
        SetActive(Left);await Settle();check(StatsRow.Visibility==Visibility.Collapsed,"Switching to the This PC pane collapses the shared strip");
        SetSinglePane(true);Settings.Stats=2;ApplyStats();
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1200;Height=820;
            await S1Verification.Navigate(Left,PaneView.ThisPC);await Settle();
            check(StatsRow.Visibility==Visibility.Collapsed&&StatsRow.ActualHeight==0&&PaneArea.ActualHeight>400,"Themed drive layout has no reserved statistics space: "+theme+scale);
            await S1Verification.Snapshot(this,root,$"stats-thispc-{theme}-{scale*100:0}");
            await S1Verification.Navigate(Left,empty);await Settle();
            check(DriveCard.ActualWidth>=StatsRow.ActualWidth-1,"The remaining card uses the complete themed strip: "+theme+scale);
            await S1Verification.Snapshot(this,root,$"stats-empty-{theme}-{scale*100:0}");
        }
        check(HiddenTrialPresentation.ForegroundViolations==0,"Statistics verification never activates or controls a desktop window");
    }
}
#endif
