#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows.Media;

namespace FileExplorer;

public partial class MainWindow
{
    // Diagnostic reproduction of the shared Open route; no associated user app
    // is launched and no media content is read.
    async Task VerifyOpen1111(string root, Action<bool,string> check)
    {
        var folder=Directory.CreateDirectory(Path.Combine(root,"open-routing")).FullName;
        var dispatches=new List<ProcessStartInfo>();
        bool editor=Settings.InternalEditor;
        try
        {
            Left.ExternalOpenForVerification=dispatches.Add;
            foreach(string name in new[]{"video.mp4","video.mkv","video.ts","sound.mp3","image.png","document.pdf","workbook.xlsx","unknown.pw1111","program.exe","script.cmd"})
                File.WriteAllBytes(Path.Combine(folder,name),name.EndsWith(".ts")?new byte[]{0,1,2,3}:Array.Empty<byte>());
            await S1Verification.Navigate(Left,folder);
            foreach(bool enabled in new[]{false,true})
            {
                Settings.InternalEditor=enabled;
                foreach(var entry in Left.Items.Where(e=>!e.IsDir&&!e.IsUp).ToArray())
                {
                    Left.List.SelectedItem=entry;
                    await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Left.List.ScrollIntoView(entry);Left.List.UpdateLayout();
                    var row=(ListViewItem)Left.List.ItemContainerGenerator.ContainerFromItem(entry);
                    check(row!=null,"Generated file row: "+entry.Name);
                    int before=dispatches.Count;
                    Left.List.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});
                    check(dispatches.Count==before+1&&dispatches[^1].FileName==entry.Path,"Double-click dispatches "+entry.Name+"; editor="+enabled);
                    // The actual custom menu command uses the same method.
                    var menu=ItemMenu(Left,new(){entry},null);
                    try{((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}
                    finally{menu.IsOpen=false;menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));}
                    check(dispatches.Count==before+2&&dispatches[^1].FileName==entry.Path,"Context Open dispatches "+entry.Name+"; editor="+enabled);
                    SetActive(Left);
                    OnPreviewKeyDown(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.F3){RoutedEvent=Keyboard.PreviewKeyDownEvent,Source=Left.List});
                    check(dispatches.Count==before+3&&dispatches[^1].FileName==entry.Path,"Shared Enter/F3 command dispatches "+entry.Name+"; editor="+enabled);
                }
            }
            check(dispatches.All(p=>p.UseShellExecute&&p.WorkingDirectory==folder),"All ordinary file types use Windows association dispatch with their containing directory");
            var failed=Left.Items.Single(e=>e.Name=="video.mp4");Left.List.SelectedItem=failed;
            bool HasMessage()=>((GridView)Left.List.View).Columns.Any(c=>Equals(c.Header,"Message")||colText(c.Header)=="Message");
            string colText(object header)=>header is Panel panel?string.Join("",panel.Children.OfType<TextBlock>().Select(t=>t.Text)):header?.ToString();
            IEnumerable<DependencyObject> Tree(DependencyObject node){for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var descendant in Tree(child))yield return descendant;}}
            check(!HasMessage(),"Message column is absent while no row has a failure");
            string previousStatus=Status.Text;
            Left.ExternalOpenForVerification=_=>throw new System.ComponentModel.Win32Exception(unchecked((int)0x800401F5),"The registered application could not be found. Fixture launch error, with enough detail to test clipping and the complete tooltip.");
            Left.OpenSelected();await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(failed.Message.StartsWith("Could not open:")&&HasMessage()&&Status.Text==previousStatus,"A failed shared Open adds its row message and dynamic column without replacing the bottom path/status");
            Left.List.ScrollIntoView(failed);Left.List.UpdateLayout();
            check(Tree(Left.List).OfType<TextBlock>().Any(t=>t.Text==failed.Message&&Equals(t.ToolTip,failed.Message)),"The rendered Message cell binds full error text and complete tooltip");
            await S1Verification.Snapshot(this,root,"open-message-native-dark");
            Left.ExternalOpenForVerification=dispatches.Add;Left.OpenSelected();
            await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(failed.Message==""&&!HasMessage(),"Successful retry clears the row error and removes the now-empty Message column");
            string key=Guid.NewGuid().ToString("N"),extension=".pw1111"+key,progId="Pathweft.OpenProbe."+key;
            string classes=@"Software\Classes\";
            check(Registry.CurrentUser.OpenSubKey(classes+extension)==null&&Registry.CurrentUser.OpenSubKey(classes+progId)==null,"Unique synthetic association absent before trial");
            try
            {
                using(var association=Registry.CurrentUser.CreateSubKey(classes+extension))association.SetValue("",progId);
                using(var command=Registry.CurrentUser.CreateSubKey(classes+progId+@"\shell\open\command"))command.SetValue("","\""+Environment.ProcessPath+"\" \"--open1111-receiver=%1\"");
                foreach(string basename in new[]{"ordinary","name with spaces","unicode-ñ-文件"})
                {
                    string path=Path.Combine(folder,basename+extension);File.WriteAllText(path,"Synthetic association receiver fixture");
                }
                await S1Verification.Navigate(Left,folder);
                foreach(var entry in Left.Items.Where(e=>Path.GetExtension(e.Path)==extension).ToArray())
                {
                    Left.List.SelectedItem=entry;
                    Process receiver=null;
                    Left.ExternalOpenForVerification=start=>receiver=Process.Start(start)??throw new InvalidOperationException("Shell returned no receiver process");
                    Left.OpenSelected();
                    check(receiver!=null,"Real ShellExecute accepted synthetic file "+entry.Name);
                    using(receiver){await receiver.WaitForExitAsync();check(receiver.ExitCode==0,"Association receiver exited successfully");}
                    using var receipt=JsonDocument.Parse(File.ReadAllText(entry.Path+".receipt.json"));
                    check(receipt.RootElement.GetProperty("path").GetString()==entry.Path&&receipt.RootElement.GetProperty("directory").GetString()==folder,"Actual Windows association delivers intact path and working directory: "+entry.Name);
                }
            }
            finally{Registry.CurrentUser.DeleteSubKeyTree(classes+extension,false);Registry.CurrentUser.DeleteSubKeyTree(classes+progId,false);}
            check(Registry.CurrentUser.OpenSubKey(classes+extension)==null&&Registry.CurrentUser.OpenSubKey(classes+progId)==null,"Synthetic association removed without modifying user associations");
            Left.ExternalOpenForVerification=dispatches.Add;
            Directory.CreateDirectory(Path.Combine(folder,"child"));await S1Verification.Navigate(Left,folder);
            Left.List.SelectedItem=Left.Items.Single(e=>e.Name=="child");Left.OpenSelected();
            await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(Left.Dir==Path.Combine(folder,"child"),"Ordinary directories still navigate internally");
            string html=Path.Combine(folder,"plain.html");File.WriteAllText(html,"<html><body>Opening contract fixture</body></html>");
            Settings.InternalEditor=true;check(EditorWindow.TryOpen(html)&&EditorWindow.OwnsArchiveCopy(html),"Readable supported text retains the enabled editor contract");
            await EditorWindow.CaptureForVerification(Path.Combine(root,"open-editor-html"),light:false);
            await EditorWindow.CloseForVerification();Settings.InternalEditor=false;
            string zip=Path.Combine(root,"open-fixture.zip");
            using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive,Path.Combine(folder,"image.png"),"image.png");
            await S1Verification.Navigate(Left,zip);var archived=Left.Items.Single(e=>e.Name=="image.png");
            ArchiveConsumerForVerification=_=>throw new System.ComponentModel.Win32Exception(2,"Fixture archive consumer unavailable");
            await OpenArchiveEntry(Left,archived);await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(archived.Message.StartsWith("Could not open archive entry:")&&HasMessage(),"Archive working-copy launch failures use the same row message column");
            string consumed=null;ArchiveConsumerForVerification=path=>{consumed=path;return null;};
            await OpenArchiveEntry(Left,archived);await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(archived.Message==""&&!HasMessage()&&File.Exists(consumed),"Successful archive retry clears its failure and preserves the extracted consumer copy");
            check(ArchiveContent.Root.StartsWith(DataFolder.Bulk+"\\",StringComparison.OrdinalIgnoreCase),"New archive working copies use the explicit non-Roaming bulk root");
            Left.List.SelectedItem=archived;
            var cancelled=Left.CreateDragData();
            var exported=(string[])cancelled.GetData(DataFormats.FileDrop);
            check(exported.Length==1&&File.Exists(exported[0]),"External drag materializes the selected entry in bulk storage");
            Left.CompleteDrag(DragDropEffects.None);
            check(!File.Exists(exported[0]),"Cancelled or failed drag automatically discards its materialized copy");
            var accepted=Left.CreateDragData();var handed=(string[])accepted.GetData(DataFormats.FileDrop);
            Left.CompleteDrag(DragDropEffects.Copy);
            check(File.Exists(handed[0]),"An accepted external drag preserves the actual consumer copy");
            var acceptedRecord=ArchiveContent.Retained().Single(r=>ArchiveContent.Location(r)==Path.GetDirectoryName(Path.GetDirectoryName(handed[0])));
            ArchiveContent.Release(acceptedRecord);
            string legacy=Path.Combine(Settings.Dir,"archive-content",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(legacy,"data"));
            File.WriteAllText(Path.Combine(legacy,"data","legacy.txt"),"Retained external consumer");
            var legacyRecord=new ArchiveContent.Record(Path.GetFileName(legacy),zip,"opened file",new[]{@"data\legacy.txt"},new[]{"data"});
            File.WriteAllText(Path.Combine(legacy,"lease.json"),JsonSerializer.Serialize(legacyRecord));
            var retainedLegacy=ArchiveContent.Retained().Single(r=>r.Id==legacyRecord.Id);
            check(ArchiveContent.Location(retainedLegacy)==legacy&&File.Exists(Path.Combine(legacy,"data","legacy.txt")),"Legacy external copies keep their original paths after bulk storage changes");
            ArchiveContent.Release(retainedLegacy);
            check(!Directory.Exists(legacy),"Explicit release addresses the correct legacy copy rather than the new bulk root");
            check(DataFolder.ResolveBulk(root,true)==Path.Combine(root,"Runtime"),"Isolated profiles remain self-contained");
            string configured=Path.Combine(root,"storage-choice");Directory.CreateDirectory(configured);
            File.WriteAllText(Path.Combine(configured,"data-root.txt"),Path.Combine(root,"chosen-data"));
            check(DataFolder.ResolveBulk(configured,false)==Path.Combine(root,"chosen-data"),"A small setting selects a non-system bulk directory");
            File.WriteAllText(Path.Combine(configured,"data-root.txt"),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            bool systemRefused=false;try{_ = DataFolder.ResolveBulk(configured,false);}catch(IOException){systemRefused=true;}
            check(systemRefused,"Unavailable/invalid bulk configuration never silently falls back to system storage");
            var cacheFolder=Directory.CreateDirectory(Path.Combine(root,"cached-folder"));
            SizeCache.Set(cacheFolder.FullName,cacheFolder.LastWriteTimeUtc,12345);SizeCache.Save();
            check(File.ReadLines(SizeCache.FilePath).Any(line=>line.EndsWith("|"+cacheFolder.FullName))&&SizeCache.FilePath.StartsWith(DataFolder.Cache+"\\",StringComparison.OrdinalIgnoreCase),"Folder-size persistence writes actual data under the bulk cache root");
            check(Directory.Exists(DataFolder.WebView2)&&DataFolder.WebView2.StartsWith(DataFolder.Bulk+"\\",StringComparison.OrdinalIgnoreCase),"The actual served editor creates its WebView2 profile under bulk storage");
            check(HiddenTrialPresentation.ForegroundViolations==0,"Opening/error/editor checks did not activate a host test window");
            Environment.SetEnvironmentVariable("PATHWEFT_DESKTOP_TEST_PROFILE",Settings.Profile);
            try{check(DesktopLaunch.FromSetup(true)==0,"Desktop-context resume helper returns its actual completion through the current-user result channel without touching host settings");}
            finally{Environment.SetEnvironmentVariable("PATHWEFT_DESKTOP_TEST_PROFILE",null);}
        }
        finally{Left.ExternalOpenForVerification=null;ArchiveConsumerForVerification=null;Settings.InternalEditor=editor;if(EditorWindow.HasOpenWindow)await EditorWindow.CloseForVerification();}
    }
}
#endif
