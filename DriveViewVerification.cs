#if DEBUG
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyDriveViews(string root,Action<bool,string> check)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        string[] Columns()=>((GridView)Left.List.View).Columns.Select(c=>(string)((Dictionary<GridViewColumn,string>)typeof(PaneView).GetField("colKey",flags).GetValue(Left))[c]).ToArray();
        async Task Show(IList<Entry> entries)
        {typeof(PaneView).GetMethod("ShowItems",flags).Invoke(Left,new object[]{entries,null,false,false});await Settle();}
        SetSinglePane(true);SetActive(Left);
        await S1Verification.Navigate(Left,PaneView.ThisPC);await Left.driveListingWork;await Settle();
        RefreshDrives();await driveRefreshWork;await Settle();
        check(Left.Items.Any(e=>e.IsDrive&&e.HasDriveCapacity),"This PC obtains actual drive capacity/free values");
        foreach(var drive in shownDrives)
        {
            var entry=Left.Items.Single(e=>e.Path==drive.Name);
            check(entry.DriveTotal==drive.Total&&entry.DriveFree==drive.Free&&entry.DriveUsedPercent>=0&&entry.DriveUsedPercent<=100,"This PC and sidebar share the same refreshed volume data: "+drive.Name);
            if(drive.Network)check(entry.DriveLocation==drive.RemotePath&&entry.DriveLocation.Length>0,"Mapped network location includes its actual UNC target: "+drive.Name);
        }
        check(!Columns().Intersect(new[]{"Version","Type","Modified","Size"}).Any()&&Columns().Contains("Capacity")&&Columns().Contains("Free space"),"Drive view replaces empty file columns with populated drive columns");
        var links=await Task.Run(()=>DriveConnections.Read(Left.Items.Where(e=>e.IsDrive&&e.HasDriveCapacity).Select(e=>e.Path).ToArray()));
        File.WriteAllText(Path.Combine(root,"usb-links.json"),JsonSerializer.Serialize(links,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"drive-inventory.json"),JsonSerializer.Serialize(shownDrives,new JsonSerializerOptions{WriteIndented=true}));
        check(DriveConnections.LaneBits((10u<<16)|(3u<<4),0)==10e9&&DriveConnections.LaneBits((10u<<16)|(3u<<4),1)==20e9,"USB rates decode operating lane speed and actual lane count without treating capability as throughput");
        foreach(string letter in new[]{"E:\\","G:\\"})
        {
            var entry=Left.Items.Single(e=>e.Path==letter);
            check(links.TryGetValue(letter,out var link)&&entry.DriveConnection.StartsWith("USB"),"External drive follows native disk-to-USB hub topology: "+letter);
            check(link.ReceiveBits==10e9&&link.TransmitBits==10e9,"Actual external connection negotiates 10 Gbps in both directions: "+letter);
        }
        foreach(string theme in new[]{"dark","light"})
        {
            Settings.NativeMode=theme;ApplyAppearance();await Settle();
            foreach(double scale in new[]{1.0,1.5})
            {
                Settings.TextScale=scale;ApplyAppearance();Width=1200;Height=820;await Settle();
                check(Columns().Contains("Connection")&&Left.List.ActualWidth>300,"Populated drive columns survive theme/text scale: "+theme+scale);
                await S1Verification.Snapshot(this,root,$"drives-{theme}-{scale*100:0}");
            }
        }
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();
        Left.SetSort("Free space",true);await Settle();
        var free=Left.List.Items.Cast<Entry>().Select(e=>e.DriveFree).ToArray();
        check(free.SequenceEqual(free.OrderDescending()),"Drive free-space sorting uses numeric bytes");
        var snapshot=shownDrives.First(d=>!d.Network);
        Left.RefreshThisPC(new[]{snapshot with{Free=snapshot.Free-1},new DriveRow("Z:\\","Empty reader","",0,0,false,"",DriveType.Removable)});await Settle();
        check(Left.Items.Count()==2&&Left.Items.Single(e=>e.Path==snapshot.Name).DriveFree==snapshot.Free-1&&Left.Items.Single(e=>e.Path=="Z:\\").CapacityText=="No media or unavailable","Drive refresh updates free space, preserves unavailable-media rows and removes disconnected entries");
        await Show(new List<Entry>{new(){Name="Z: Empty reader",Path="Z:\\",IsDir=true,IsDrive=true,Size=-2}});
        check(Columns().SequenceEqual(new[]{"Name"}),"Unavailable media does not invent capacity or show empty metadata columns");

        string empty=Path.Combine(root,"empty");Directory.CreateDirectory(empty);
        await S1Verification.Navigate(Left,empty);await Settle();
        check(Columns().SequenceEqual(new[]{"Name"}),"Empty folder has only the Name column after leaving This PC");
        var entryData=new Entry{Name="sample",Path=Path.Combine(empty,"sample"),Size=-2};
        await Show(new List<Entry>{entryData});
        check(Columns().SequenceEqual(new[]{"Name"}),"A view with no meaningful file metadata has no empty columns");
        entryData.Version="1.2.3";entryData.Size=42;entryData.Modified=DateTime.Now;await Settle();
        check(Columns().SequenceEqual(new[]{"Name","Version","Size","Modified"}),"Asynchronous version/size/date values restore their columns");
        var grid=(GridView)Left.List.View;var version=grid.Columns.Single(c=>c!=Left.NameColumn&&Columns()[grid.Columns.IndexOf(c)]=="Version");
        version.Width=113;entryData.Version="";await Settle();check(!Columns().Contains("Version"),"Clearing the last populated value hides that column");
        entryData.Version="2.0";await Settle();check(grid.Columns.Contains(version)&&version.Width==113,"Restoring a hidden column preserves its object and user width");
        var streamed=new System.Collections.ObjectModel.ObservableCollection<Entry>();await Show(streamed);
        streamed.Add(new Entry{Name="data.txt",Path=Path.Combine(empty,"data.txt"),Size=0});await Settle();
        check(Columns().SequenceEqual(new[]{"Name","Type","Size"}),"Incoming result rows expose populated type and zero-byte size columns");
        streamed.Clear();await Settle();check(Columns().SequenceEqual(new[]{"Name"}),"Removing all results removes unused metadata columns");
        string executableFolder=Path.Combine(root,"versioned");Directory.CreateDirectory(executableFolder);
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Pathweft.exe"),Path.Combine(executableFolder,"versioned.exe"));
        await S1Verification.Navigate(Left,executableFolder);
        var versioned=Left.Items.Single(e=>e.Name=="versioned.exe");
        var loaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void VersionArrived(object sender,System.ComponentModel.PropertyChangedEventArgs change){if(versioned.Version.Length>0)loaded.TrySetResult();}
        versioned.PropertyChanged+=VersionArrived;
        try{if(versioned.Version.Length>0)loaded.TrySetResult();await loaded.Task;}finally{versioned.PropertyChanged-=VersionArrived;}
        await Settle();check(Columns().Contains("Version"),"Actual asynchronous executable metadata restores the Version column");
        await S1Verification.Navigate(Left,PaneView.ThisPC);await Left.driveListingWork;await Settle();
        check(Columns().Contains("Capacity")&&!Columns().Contains("Version"),"Returning to This PC restores its drive-specific columns");
        Left.Search("path:\"D:\\Projects\\Pathweft\" \"README.md\"");await Left.SearchCompletion;await Settle();
        File.WriteAllText(Path.Combine(root,"search-columns.json"),JsonSerializer.Serialize(new{notice=Left.SearchNotice,columns=Columns(),sort=Left.SortKey,items=Left.Items.Select(e=>e.Name).ToArray()}));
        check(Left.Items.Any(e=>e.Name=="README.md")&&!Columns().Intersect(new[]{"Capacity","Connection","Version"}).Any()&&Columns().Contains("Size"),"Actual Everything results replace drive columns with populated file metadata");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Drive and dynamic-column verification activates no desktop window");
    }
}
#endif
