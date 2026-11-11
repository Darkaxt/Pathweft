using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer;

sealed partial class PackageProbe
{
    async Task VerifyRefinements124(PaneView left)
    {
        string images=Directory.CreateDirectory(Path.Combine(root,"following previews")).FullName;
        foreach(var color in new[]{Colors.Red,Colors.Blue})
        {
            byte[] pixel={color.B,color.G,color.R,255};
            var image=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,pixel,4);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using var file=File.Create(Path.Combine(images,color==Colors.Red?"red.png":"blue.png"));encoder.Save(file);
        }
        var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Listed(){if(left.Dir==images&&left.Listed&&left.Items.Count(e=>!e.IsUp)==2)arrived.TrySetResult();}
        left.StatsChanged+=Listed;
        try{left.Navigate(images);Listed();await arrived.Task;}finally{left.StatsChanged-=Listed;}
        Call(typeof(MainWindow),owner,"SetActive",left);Settings.Preview=true;Call(typeof(MainWindow),owner,"ApplyPreview");
        var preview=typeof(MainWindow).GetField("sidePreview",Flags).GetValue(owner);
        var list=(ListView)left.FindName("List");
        async Task Select(string name)
        {
            list.SelectedItem=left.Items.Single(e=>e.Name==name);
            await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            await (Task)Get(preview,"Completion");
        }
        await Select("red.png");
        var mode=typeof(MainWindow).GetNestedType("PreviewMode",BindingFlags.NonPublic);
        Call(typeof(MainWindow),owner,"MovePreviewHost",Enum.Parse(mode,"PictureInPicture"));
        await Select("blue.png");
        Check(((Entry)Get(preview,"Entry")).Name=="blue.png"&&typeof(MainWindow).GetField("previewMode",Flags).GetValue(owner).ToString()=="PictureInPicture","Delivered PiP follows real list selection without changing host mode");
        var pin=(Button)owner.FindName("PreviewPinButton");pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Select("red.png");
        Check(((Entry)Get(preview,"Entry")).Name=="blue.png"&&Equals(pin.Tag,"on")&&AutomationProperties.GetItemStatus(pin)=="Pinned","Delivered explicit pin retains the item and exposes its state");
        Call(typeof(MainWindow),owner,"MovePreviewHost",Enum.Parse(mode,"Sidebar"));
        Check(((Entry)Get(preview,"Entry")).Name=="blue.png"&&Equals(pin.Tag,"on"),"Delivered host-only transition preserves the pinned renderer");
        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await (Task)Get(preview,"Completion");
        Check(((Entry)Get(preview,"Entry")).Name=="red.png"&&pin.Tag==null,"Delivered unpin catches up immediately");
        list.UnselectAll();await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(Get(preview,"Entry")==null,"Delivered unpinned empty selection releases the previous renderer");
        var close=(Button)owner.FindName("PreviewCloseButton");
        var detach=(Button)owner.FindName("PreviewWindowButton");
        Check(detach.FontSize<=10&&pin.Width==32&&pin.Height==32,"Delivered preview controls use small glyphs with retained hit targets");

        var stamp=new DateTime(2026,1,5,12,0,0,DateTimeKind.Utc);
        foreach(var choice in new[]{Choice.Overwrite,Choice.Newer,Choice.Rename,Choice.Skip})
        {
            string source=Directory.CreateDirectory(Path.Combine(root,"differences "+choice+" incoming")).FullName;
            string target=Directory.CreateDirectory(Path.Combine(root,"differences "+choice+" existing")).FullName;
            foreach(string name in new[]{"matched.txt","newer existing.txt"})
            {
                File.WriteAllText(Path.Combine(source,name),"left");File.WriteAllText(Path.Combine(target,name),"rght");
                File.SetLastWriteTimeUtc(Path.Combine(source,name),stamp);File.SetLastWriteTimeUtc(Path.Combine(target,name),name.StartsWith("newer")?stamp.AddTicks(1):stamp);
            }
            var job=Ferry.Submit(Directory.GetFiles(source),target,false);job.Resolve(choice);await job.Completion;
            Check(job.Errors.IsEmpty&&job.Conflicts.Count==2&&job.ConflictRows.Count==1&&job.ConflictRows[0].ExistingNewer&&job.ConflictRows[0].IncomingSize==""&&job.ConflictRows[0].IncomingTime!=job.ConflictRows[0].ExistingTime,"Delivered metadata review filters matches, orders newer destination and retains timestamp precision: "+choice);
            job.ShowMatchingConflicts=true;
            Check(job.ConflictRows.Count==2&&job.ConflictScope.Contains("all 2")&&job.DoneFiles+job.SkippedFiles==2,"Delivered choices account for the complete conflict set: "+choice);
            Check(File.ReadAllText(Path.Combine(target,"matched.txt"))==(choice is Choice.Overwrite or Choice.Rename?"left":"rght"),"Delivered hidden metadata match still obeys the actual choice: "+choice);
            if(choice==Choice.Rename)Check(Directory.GetFiles(target).Length==4&&File.ReadAllText(Path.Combine(target,"matched.old.txt"))=="rght"&&File.ReadAllText(Path.Combine(target,"newer existing.old.txt"))=="rght","Delivered Keep both copies metadata matches too, keeping the existing files as name.old.ext");
        }
    }
}
