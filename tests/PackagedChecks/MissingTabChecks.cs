using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer;

sealed partial class PackageProbe
{
    async Task VerifyMissingTabs125()
    {
        var pane=(PaneView)owner.FindName("Left");
        string parent=Directory.CreateDirectory(Path.Combine(root,"tab-parent é 子")).FullName;
        string old=Directory.CreateDirectory(Path.Combine(parent,"old","portable")).FullName;
        string other=Directory.CreateDirectory(Path.Combine(root,"tab-other")).FullName;
        File.WriteAllText(Path.Combine(parent,"ancestor.txt"),"ancestor");
        async Task Activate(Action action,string expected,bool expectError=false)
        {
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(){if(pane.Listed&&pane.Dir==expected)done.TrySetResult();}
            void Failed(string message){if(expectError&&pane.Dir==expected)done.TrySetResult();else done.TrySetException(new IOException(message));}
            pane.StatsChanged+=Ready;pane.Error+=Failed;
            try{pane.Listed=false;action();await done.Task;await owner.Dispatcher.InvokeAsync(owner.UpdateLayout,DispatcherPriority.ApplicationIdle);}
            finally{pane.StatsChanged-=Ready;pane.Error-=Failed;}
        }
        await Activate(()=>pane.Navigate(other),other);
        pane.OpenInBackgroundTab(old);
        Directory.Move(Path.GetDirectoryName(old),Path.Combine(parent,"new"));
        var strip=(Panel)pane.FindName("TabStrip");
        await Activate(()=>((Button)strip.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),parent);
        Check(pane.ActiveTab==1&&pane.OpenTabs[1]==parent&&((Button)strip.Children[1]).Tag as string=="on"&&pane.Items.Any(e=>e.Name=="ancestor.txt"),"Delivered stale tab click repairs multiple missing parents, selected header and actual listing");
        await Activate(()=>pane.Restore(new(){other,old},1,new(){1}),parent);
        Check(pane.OpenTabs.Count==2&&pane.ActiveTab==1&&pane.PinnedTabs.SequenceEqual(new[]{1}),"Delivered restore retains stale tab position, pin and active identity");
        await Activate(()=>pane.NextTab(-1),other);
        await Activate(()=>pane.ShowTab(1),parent);
        Settings.Tabs["Left"]=pane.OpenTabs.ToList();Settings.Save();Settings.Load();
        Check(Settings.Tabs["Left"].SequenceEqual(new[]{other,parent}),"Delivered keyboard/external activation and settings persist the recovered destination");
        string unc=@"\\pathweft-offline.invalid\share\old\folder";
        Check((string)Call(typeof(PaneView),null,"RecoverMissingFolder",unc)==unc,"Delivered recovery preserves UNC paths without probing the network");
        string zip=Path.Combine(parent,"tabs.zip");
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))archive.CreateEntry("docs/");
        string archivePath=zip+@"\docs";
        await Activate(()=>pane.Navigate(archivePath),archivePath);
        Check(pane.ArchiveRoot?.Archive==zip,"Delivered existing archive tabs retain their virtual folder identity");
        var denied=Directory.CreateDirectory(Path.Combine(parent,"denied"));
        var original=denied.GetAccessControl();
        var restricted=denied.GetAccessControl();
        restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.ReadAttributes|FileSystemRights.ListDirectory,AccessControlType.Deny));
        try
        {
            denied.SetAccessControl(restricted);
            await Activate(()=>pane.Navigate(denied.FullName),denied.FullName,expectError:true);
            Check(pane.Dir==denied.FullName&&pane.Items.Count==0,"Delivered access-denied tab stays selected at its original destination with an empty error listing");
        }
        finally{denied.SetAccessControl(original);}
        await Activate(()=>pane.Restore(new(){root},0),root);
    }
}
