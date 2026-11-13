#if DEBUG
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;

// PREVIEW-39: shortcut details instead of a hex dump, from real .lnk and .url files.
public partial class MainWindow
{
    internal async Task VerifyShortcutPreview39(string root,Action<bool,string> check)
    {
        string target=Path.Combine(root,"target folder","notes.txt"),folder=Path.GetDirectoryName(target);
        Directory.CreateDirectory(folder);File.WriteAllText(target,"notes");
        string file=Path.Combine(root,"notes.lnk"),toFolder=Path.Combine(root,"folder.lnk"),missing=Path.Combine(root,"missing.lnk");
        var link=(IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);link.SetArguments("-p work");link.SetWorkingDirectory(folder);link.SetDescription("Fixture comment");
            link.SetHotkey((short)(0x0600|0x46));link.SetShowCmd(3);link.SetIconLocation(@"C:\Windows\System32\shell32.dll",4);
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(file,true);
        }
        finally{Marshal.ReleaseComObject(link);}
        SaveShortcut(toFolder,folder,folder);
        SaveShortcut(missing,Path.Combine(root,"gone.txt"),root);
        string web=Path.Combine(root,"site.url"),local=Path.Combine(root,"local.url");
        File.WriteAllText(web,"[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,11\r\n[InternetShortcut]\r\nIDList=\r\nURL=https://example.com/page\r\nIconFile=C:\\Windows\\System32\\url.dll\r\nIconIndex=2\r\n");
        File.WriteAllText(local,"[InternetShortcut]\r\nURL="+new Uri(target).AbsoluteUri+"\r\n");
        string Value(ShortcutDetails details,string name)=>details.Rows.FirstOrDefault(r=>r.Name==name).Value;

        var lnk=ShortcutInspection.Read(file,CancellationToken.None);
        check(lnk.Kind==ShortcutKind.File&&Value(lnk,"Target")==target&&Value(lnk,"Arguments")=="-p work"&&Value(lnk,"Start in")==folder&&
            Value(lnk,"Run")=="Maximized"&&Value(lnk,"Shortcut key")=="Ctrl+Alt+F"&&Value(lnk,"Icon")==@"C:\Windows\System32\shell32.dll, 4"&&
            Value(lnk,"Comment")=="Fixture comment"&&Value(lnk,"Target state")=="File found",
            "A file shortcut shows its target, arguments, start folder, run state, key, icon, comment and state: "+string.Join(" | ",lnk.Rows.Select(r=>r.Name+"="+r.Value)));
        var dir=ShortcutInspection.Read(toFolder,CancellationToken.None);
        check(dir.Kind==ShortcutKind.Folder&&Value(dir,"Target state")=="Folder found"&&Value(dir,"Run")=="Normal window"&&Value(dir,"Arguments")==null,"A folder shortcut reads as a found folder and leaves out empty fields");
        var gone=ShortcutInspection.Read(missing,CancellationToken.None);
        check(gone.Kind==ShortcutKind.Missing&&Value(gone,"Target state")=="Not found at this location","A shortcut to a missing file says so without repairing it");
        var url=ShortcutInspection.Read(web,CancellationToken.None);
        check(url.Kind==ShortcutKind.Address&&Value(url,"Address")=="https://example.com/page"&&Value(url,"Icon")==@"C:\Windows\System32\url.dll, 2"&&Value(url,"Target state")=="Web address","An Internet shortcut shows its address and icon");
        var fileUrl=ShortcutInspection.Read(local,CancellationToken.None);
        check(fileUrl.Kind==ShortcutKind.File&&fileUrl.Target==target,"An Internet shortcut to a local file reads as that file");
        check(ShortcutInspection.Hotkey(0x0131)=="Shift+1"&&ShortcutInspection.Hotkey(0)=="","Shortcut keys format digits and an unset key");
        byte[] before=File.ReadAllBytes(file);

        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var window=new Window{Content=body,Width=420,Height=600,ShowInTaskbar=false};HiddenTrialPresentation.Prepare(window);window.Show();
        try
        {
            using var session=new PreviewSession(this,body,_=>{});
            async Task<Button[]> Show(string path)
            {
                session.Show(new Entry{Path=path,Name=Path.GetFileName(path)});await session.Completion;body.UpdateLayout();
                check(session.State=="ready"&&session.ModuleId=="shortcut","The preview uses Shortcuts, not Hex and text, for "+Path.GetFileName(path)+$" ({session.ModuleId})");
                return PreviewChildren<Button>(body).ToArray();
            }
            Button Named(Button[] buttons,string name)=>buttons.Single(b=>AutomationProperties.GetName(b)==name);
            var buttons=await Show(file);
            check(PreviewChildren<TextBox>(body).Any(t=>AutomationProperties.GetName(t)=="Target"&&t.Text==target&&t.IsReadOnly),"The preview lists the target as selectable read-only text");
            check(Named(buttons,"Open").IsEnabled&&Named(buttons,"Show target in its folder").IsEnabled,"A found file target can be opened or shown in its folder");
            var pane=active;var listed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Listed(){if(pane.Listed&&string.Equals(pane.Dir,folder,StringComparison.OrdinalIgnoreCase))listed.TrySetResult();}
            pane.StatsChanged+=Listed;
            try{Named(buttons,"Show target in its folder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.WhenAny(listed.Task,Task.Delay(10_000));}
            finally{pane.StatsChanged-=Listed;}
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            check(string.Equals(active.Dir,folder,StringComparison.OrdinalIgnoreCase)&&active.Selected.Any(e=>e.Name=="notes.txt"),"Show target in its folder selects the target in a pane: "+active.Dir);
            buttons=await Show(toFolder);
            check(Named(buttons,"Open target folder").IsEnabled,"A folder target opens in Pathweft");
            buttons=await Show(missing);
            check(!Named(buttons,"Open").IsEnabled&&!Named(buttons,"Show target in its folder").IsEnabled,"A missing target offers neither Open nor Show in folder");
            buttons=await Show(web);
            check(Named(buttons,"Open").IsEnabled&&!Named(buttons,"Show target in its folder").IsEnabled,"A web address can be opened but has no folder");
            session.Clear();await PreviewSession.DrainAsync();
            check(before.SequenceEqual(File.ReadAllBytes(file)),"Previewing leaves the shortcut file unchanged");
            File.Move(file,file+".released");check(File.Exists(file+".released"),"Closing the shortcut preview releases the file");
        }
        finally{window.Close();}
    }
}
#endif
