#if DEBUG
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileExplorer.Preview;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task DiagnosePropertyOwner9(string root,Action<bool,string> check)
    {
        check(MenuPropertyProbe.Station()==Environment.GetEnvironmentVariable("PATHWEFT_PRIVATE_MENU_DESKTOP"),"Native property diagnosis is bound to the private station");
        File.WriteAllText(Path.Combine(root,"coverage.json"),JsonSerializer.Serialize(new{diagnosticOnly=true,purpose="Observe actual native property-sheet root/parent/owner identity; no ownership acceptance claim"}));
        string folder=Directory.CreateDirectory(Path.Combine(root,"folder é 子")).FullName;
        string file=Path.Combine(root,"document é 子.txt");File.WriteAllText(file,"Native property owner diagnosis");
        string link=Path.Combine(root,"shortcut é 子.lnk");SaveShortcut(link,folder,root);
        var widget=new SearchWidgetWindow(this);widget.Show();
        try
        {
            foreach(string path in new[]{file,folder,link})
            {
                await S1Verification.Navigate(widget.Pane,root);widget.Pane.List.SelectedItem=widget.Pane.Items.Single(entry=>entry.Path==path);
                using var probe=new MenuPropertyProbe(widget,path,_=>widget.Dismiss());widget.PropertiesSelection();var observed=await probe.Completion;
                File.WriteAllText(Path.Combine(root,"properties-"+Path.GetFileName(path)+".json"),JsonSerializer.Serialize(observed));
                check(observed.Title.Contains(Path.GetFileNameWithoutExtension(path),StringComparison.OrdinalIgnoreCase),"Actual property sheet identity recorded for "+Path.GetFileName(path));
            }
        }finally{widget.Shutdown();}
    }
    internal async Task VerifyMenus9(string root,Action<bool,string> check)
    {
        string station=Environment.GetEnvironmentVariable("PATHWEFT_PRIVATE_MENU_DESKTOP");
        bool native=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="menus";
        if(native)check(!string.IsNullOrEmpty(station)&&station!="WinSta0"&&MenuPropertyProbe.Station()==station,
            "Menu checks run on the actual private window station, never the interactive clipboard");
        else File.WriteAllText(Path.Combine(root,"coverage.json"),JsonSerializer.Serialize(new{coverage="Independent rendered menus, archive actions and widget dispatch only; native clipboard/property-sheet/live Shell execution acceptance remains pending"}));
        Settings.NativeMenu=false;Settings.SinglePane=false;Settings.InternalEditor=false;Settings.Preview=false;Settings.Stats=0;
        ApplyPanes();ApplyPreview();ApplyStats();
        string source=Directory.CreateDirectory(Path.Combine(root,"source é 子")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"target")).FullName;
        string folder=Directory.CreateDirectory(Path.Combine(source,"folder 子")).FullName;
        string file=Path.Combine(source,"copy é 子.txt");File.WriteAllText(file,"S9 real menu clipboard content");
        string move=Path.Combine(source,"move.txt");File.WriteAllText(move,"S9 cut and paste");
        string shortcut=Path.Combine(source,"folder shortcut.lnk");SaveShortcut(shortcut,folder,source);
        string probe=Path.Combine(source,"open fixture.exe");if(native)File.Copy(Environment.GetEnvironmentVariable("PATHWEFT_MENU_OPEN_PROBE"),probe);
        else File.WriteAllText(probe,"Not executable; UI-only eligibility fixture");
        string zip=Path.Combine(source,"menu.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){archive.CreateEntryFromFile(file,"first.txt");archive.CreateEntryFromFile(move,"second.txt");}
        await S1Verification.Navigate(Left,source);await S1Verification.Navigate(Right,target);SetActive(Left);
        async Task Layout()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T:DependencyObject
        {
            if(node is T value)yield return value;
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)
                foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(node,index)))yield return child;
        }
        async Task Select(PaneView pane,params string[] names)
        {
            pane.List.SelectedItems.Clear();foreach(string name in names)pane.List.SelectedItems.Add(pane.Items.Single(entry=>entry.Name==name));
            if(names.Length>0)pane.List.ScrollIntoView(pane.List.SelectedItem);SetActive(pane);await Layout();
        }
        ContextMenu Menu(PaneView pane)
        {
            SetActive(pane);captureS4Menu=true;
            try{pane.S4ContextAction();return archiveMenuForVerification??throw new InvalidOperationException("Menu not captured");}
            finally{captureS4Menu=false;}
        }
        void Click(ContextMenu menu,string name)
        {
            var item=MenuCommands(menu).Single(item=>item.Header?.ToString()==name);
            check(item.IsEnabled,"The actual menu command is eligible: "+name);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;
        }
        void Inventory(ContextMenu menu,string context)
        {
            var commands=MenuCommands(menu).ToArray();
            check(commands.All(item=>item.Header!=null),"Every grouped command retains a readable label: "+context);
            void Separators(ItemsControl parent)
            {
                var items=parent.Items.Cast<object>().ToArray();
                check(items.Length==0||items[0] is not Separator&&items[^1] is not Separator&&!items.Zip(items.Skip(1)).Any(pair=>pair.First is Separator&&pair.Second is Separator),"No empty separator groups: "+context);
                foreach(var child in parent.Items.OfType<MenuItem>())if(child.Items.Count>0)Separators(child);
            }
            Separators(menu);
            File.WriteAllText(Path.Combine(root,"menu-"+context+".json"),JsonSerializer.Serialize(commands.Select(item=>new{label=item.Header.ToString(),item.InputGestureText,item.IsEnabled,children=item.Items.Count,tag=item.Tag?.ToString()})));
        }
        await Select(Left,Path.GetFileName(file));var single=Menu(Left);Inventory(single,"single");
        string script=Path.Combine(source,"launch pilot.ps1");File.WriteAllText(script,"exit 0");await S1Verification.Navigate(Left,source);
        await Select(Left,"launch pilot.ps1");var scriptMenu=Menu(Left);
        check(scriptMenu.Items.OfType<MenuItem>().Any(item=>item.Header as string=="Run with PowerShell")&&MenuCommands(scriptMenu).Single(item=>item.Header as string=="Run as administrator").Parent is MenuItem{Header:"Tools"}&&
            !MenuCommands(single).Any(item=>item.Header as string=="Run with PowerShell"),"A PowerShell script offers Run with PowerShell at the root and Run as administrator under Tools; other files do not");
        var run=ScriptStart(script,false);var elevated=ScriptStart(script,true);
        check(File.Exists(run.FileName)&&Path.GetFileName(run.FileName) is "pwsh.exe" or "powershell.exe"&&run.Arguments.StartsWith("-NoProfile ")&&run.Arguments.EndsWith("-File \""+script+"\"")&&
            run.WorkingDirectory==source&&run.Verb==""&&elevated.Verb=="runas"&&elevated.Arguments==run.Arguments,
            "Run with PowerShell starts "+run.FileName+" "+run.Arguments+" in the script's folder; Run as administrator asks for elevation with the same command");
        File.Delete(script);await S1Verification.Navigate(Left,source);
        await Select(Left,Path.GetFileName(file));
        check(MenuCommands(single).Where(item=>item.Header as string is "Open" or "Cut" or "Copy" or "Paste" or "Rename" or "Delete").All(item=>item.Icon is Image{Source:DrawingImage}),"Every native-art primary command has an actual bound vector, including Cut and Paste");
        check(new[]{"Open","Cut","Copy","Rename","Delete","Windows menu…","Properties"}.All(name=>single.Items.OfType<MenuItem>().Any(item=>item.Header as string==name)),"Frequent file actions remain at the root");
        check(single.Items.OfType<MenuItem>().Where(item=>item.Items.Count>0).Select(item=>item.Header as string).Contains("Transfer")&&MenuCommands(single).Single(item=>item.Header as string=="Copy path").Parent is MenuItem {Header:"Copy details"},"Secondary commands are grouped without losing their callbacks");
        if(native){
        Click(single,"Copy");await clipboardOperation;
        check(System.Windows.Forms.Clipboard.GetFileDropList().Cast<string>().SequenceEqual(new[]{file}),"Menu Copy publishes the real Windows file-drop clipboard on the isolated station");
        await Select(Right);Click(Menu(Right),"Paste");await jobs[0].Completion;
        check(jobs[0].Successful&&File.ReadAllText(Path.Combine(target,Path.GetFileName(file)))==File.ReadAllText(file),"Grouped background Paste copies the actual clipboard file through Ferry");
        await S1Verification.Navigate(Left,source);await Select(Left,"move.txt");Click(Menu(Left),"Cut");await clipboardOperation;
        check(System.Windows.Forms.Clipboard.GetData("Preferred DropEffect") is MemoryStream effect&&(BitConverter.ToInt32(effect.ToArray(),0)&2)!=0,"Menu Cut retains the native move effect");
        await Select(Right);Click(Menu(Right),"Paste");await jobs[0].Completion;await clipboardOperation;
        check(jobs[0].Successful&&!File.Exists(move)&&File.Exists(Path.Combine(target,"move.txt")),"Cut/Paste moves the real file and completes clipboard ownership normally");
        }
        await S1Verification.Navigate(Left,source);await Select(Left,Path.GetFileName(file));Click(Menu(Left),"Rename");await Layout();
        var editor=Descendants<TextBox>(Left).Single(box=>box.Tag is Entry {Renaming:true});editor.Text="renamed é 子.txt";
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        file=Path.Combine(source,"renamed é 子.txt");await S1Verification.Navigate(Left,source);
        check(File.Exists(file)&&Left.Items.Any(entry=>entry.Name==Path.GetFileName(file)),"Menu Rename commits through the actual inline editor");
        if(native){await Select(Left,"open fixture.exe");
        {
            string opened=Path.Combine(root,"open-receipt.json");
            Process child=null;
            // Observe the real Process.Start boundary through the existing debug
            // seam; no substitute launcher, simulated success or event deadline.
            Left.ExternalOpenForVerification=start=>child=Process.Start(start);
            try
            {
                Click(Menu(Left),"Open");check(child!=null,"Shell Open returns the actual owned receiver process");
                await child.WaitForExitAsync();using var receipt=JsonDocument.Parse(File.ReadAllText(opened));
                check(child.ExitCode==0&&receipt.RootElement.GetProperty("pid").GetInt32()==child.Id&&receipt.RootElement.GetProperty("opened").GetBoolean(),"Menu Open reaches Windows Shell execution and the actual owned executable");
            }finally{Left.ExternalOpenForVerification=null;child?.Dispose();}
        }
        }
        await Select(Left,Path.GetFileName(file),Path.GetFileName(folder));var mixed=Menu(Left);Inventory(mixed,"mixed");
        check(!MenuCommands(mixed).Single(item=>item.Header as string=="Quick View").IsEnabled&&MenuCommands(mixed).Any(item=>item.Header as string=="Rename all…"),"Mixed selection keeps batch actions and honestly disables single-item preview");
        Settings.SinglePane=true;ApplyPanes();await Select(Left,Path.GetFileName(folder));var onePane=Menu(Left);
        check(!MenuCommands(onePane).Single(item=>item.Header as string=="Copy to other pane").IsEnabled&&!MenuCommands(onePane).Single(item=>item.Header as string=="Open in other pane").IsEnabled,"Other-pane commands are disabled when no destination pane is shown");
        Settings.SinglePane=false;ApplyPanes();
        foreach(var appearance in new[]{("native-dark",true,"dark"),("native-light",true,"light"),("original",false,"dark")})
        foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=appearance.Item2;Settings.NativeMode=appearance.Item3;Settings.TextScale=scale;Width=900;Height=650;ApplyAppearance();
            await Select(Left,Path.GetFileName(file));var menu=Menu(Left);menu.IsOpen=true;await Layout();
            var transfer=menu.Items.OfType<MenuItem>().Single(item=>item.Header as string=="Transfer");
            transfer.Focus();
            transfer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),Environment.TickCount,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});
            await Layout();
            var container=MenuCommands(menu).Single(item=>item.Header as string=="Copy to other pane");
            check(container.IsVisible&&container.Focusable&&transfer.IsSubmenuOpen,"The actual Right-key menu handler opens the grouped submenu: "+appearance.Item1+" / "+scale);
            container.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(container),Environment.TickCount,Key.Left){RoutedEvent=Keyboard.KeyDownEvent});await Layout();
            check(!transfer.IsSubmenuOpen,"The actual Left-key menu handler returns to the parent menu: "+appearance.Item1+" / "+scale);
            check(menu.ActualHeight*scale<=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Height&&menu.ActualWidth*scale<=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Width,"The real popup fits the work area at narrow900-DIP layout: "+appearance.Item1+" / "+scale);
            transfer.IsSubmenuOpen=false;await Layout();CaptureMenu(menu,root,$"context-{appearance.Item1}-{scale*100:0}");menu.IsOpen=false;
            await Select(Left);var background=Menu(Left);Inventory(background,$"background-{appearance.Item1}-{scale*100:0}");
            check(background.Items.OfType<MenuItem>().Any(item=>(item.Header as string)?.StartsWith("View  ·")==true)&&MenuCommands(background).Any(item=>item.Header as string=="Open terminal here"),"Background View/Sort and grouped Tools remain available");
        }
        Settings.TextScale=1;Settings.NativeTheme=true;Settings.NativeMode="dark";ApplyAppearance();
        await S1Verification.Navigate(Left,zip);await Select(Left,"first.txt");var archiveMenu=Menu(Left);Inventory(archiveMenu,"archive");
        Click(archiveMenu,"Preview");await quickPreview.Completion;check(quickPreview.State=="ready"&&quickPreview.ModuleId=="text","Grouped archive Preview renders the actual selected member");quick.Close();await PreviewSession.DrainAsync();
        Click(Menu(Left),"Rename");await Layout();var rename=Descendants<TextBox>(Left).Single(box=>box.Tag is Entry{Renaming:true});rename.Text="renamed.txt";
        rename.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});await lastArchiveJob.Completion;
        check(lastArchiveJob.Successful&&Archive.Read(zip).Any(entry=>entry.Path=="renamed.txt"),"Archive Rename uses the actual transactional native edit");
        await S1Verification.Navigate(Left,zip);await Select(Left,"renamed.txt");
        _=Dispatcher.BeginInvoke(()=>
        {
            var prompt=Application.Current.Windows.Cast<Window>().Single(window=>window!=this&&window.Title=="Pathweft"&&window.Content is Grid);
            Descendants<Button>(prompt).Single(button=>button.Content as string=="Delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Click(Menu(Left),"Delete from archive");await lastArchiveJob.Completion;
        check(lastArchiveJob.Successful&&Archive.Read(zip).All(entry=>entry.Path!="renamed.txt"),"Archive Delete confirms and commits through its real grouped menu action");
        await S1Verification.Navigate(Left,source);File.SetAttributes(zip,File.GetAttributes(zip)|FileAttributes.ReadOnly);
        try
        {
            await S1Verification.Navigate(Left,zip);await Select(Left,"second.txt");var readOnly=Menu(Left);Inventory(readOnly,"readonly-archive");
            check(MenuCommands(readOnly).Where(item=>item.Header as string is "Cut" or "Paste" or "Rename" or "Delete from archive").All(item=>!item.IsEnabled&&item.ToolTip?.ToString()==Archive.ChangeReason(zip)),"Read-only archive mutations retain the actual disabled-state explanation");
            Click(readOnly,"Extract to other pane");await lastArchiveJob.Completion;
            check(lastArchiveJob.Successful&&File.ReadAllText(Path.Combine(target,"second.txt"))=="S9 cut and paste","The grouped Extract action reads a real read-only archive into the destination pane");
        }finally{File.SetAttributes(zip,File.GetAttributes(zip)&~FileAttributes.ReadOnly);}
        await S1Verification.Navigate(Left,source);
        await VerifyWidgetMenu9(root,source,file,folder,shortcut,native,check);
        await Select(Left,Path.GetFileName(file));
        var actualNative=new List<object>();
        ShellMenu.InspectForVerification=(paths,verbs,own)=>
        {
            actualNative.Add(new{paths,verbs,own});check(verbs.Contains("properties")&&verbs.Contains("copy"),"The real Windows IContextMenu retains native Properties and clipboard verbs");
        };
        try{Settings.NativeMenu=true;Left.S4ContextAction();}finally{Settings.NativeMenu=false;ShellMenu.InspectForVerification=null;}
        File.WriteAllText(Path.Combine(root,"native-menu.json"),JsonSerializer.Serialize(actualNative));
        check(!ShellMenu.CanShowItems(new[]{file,Path.Combine(target,"move.txt")})&&ShellMenu.CanShowItems(new[]{file,shortcut}),"Native Shell eligibility refuses unrelated child PIDLs while preserving same-folder mixed selections");
        check(!SupportsProperties(new[]{file,@"C:\\synthetic-unavailable.txt"})&&SupportsProperties(new[]{file,Path.Combine(target,"move.txt")}),"Combined Properties eligibility matches the supported shared-drive boundary");
        check(!ArchiveContent.Retained().Any(entry=>entry.Purpose=="preview"),"Menu preview lifecycle releases the actual archive working copy");
        check(HiddenTrialPresentation.ForegroundObserverActive&&HiddenTrialPresentation.ForegroundViolations==0,"The complete independent menu workflow never activates a host-desktop window");
    }
    internal async Task VerifyShellMenus9(string root,Action<bool,string> check)
    {
        check(Environment.UserInteractive&&MenuPropertyProbe.Station()=="WinSta0"&&Environment.GetEnvironmentVariable("PATHWEFT_PRIVATE_MENU_DESKTOP")=="WinSta0","Recycle checks use the normal Windows station on an exact never-switched private desktop; no clipboard operations");
        Settings.NativeMenu=false;Settings.SinglePane=false;Settings.InternalEditor=false;Settings.Preview=false;Settings.Stats=0;ApplyPanes();ApplyPreview();ApplyStats();
        string source=Directory.CreateDirectory(Path.Combine(root,"source é 子")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"target")).FullName;
        string file=Path.Combine(source,"document é 子.txt");File.WriteAllText(file,"Native Shell properties");
        string folder=Directory.CreateDirectory(Path.Combine(source,"folder 子")).FullName;
        string shortcut=Path.Combine(source,"shortcut é 子.lnk");SaveShortcut(shortcut,folder,source);
        await S1Verification.Navigate(Left,source);await S1Verification.Navigate(Right,target);
        await VerifyWidgetMenu9(root,source,file,folder,shortcut,true,check);
        string deleting=Path.Combine(source,"open fixture.exe");
        File.Copy(Environment.GetEnvironmentVariable("PATHWEFT_MENU_OPEN_PROBE"),deleting);
        await S1Verification.Navigate(Left,source);SetActive(Left);Left.List.SelectedItem=Left.Items.Single(entry=>entry.Path==deleting);
        ContextMenu menu;captureS4Menu=true;try{Left.S4ContextAction();menu=archiveMenuForVerification;}finally{captureS4Menu=false;}
        byte[] original=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(deleting));
        var recycled=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        RecycleFinishedForVerification=(paths,error)=>{if(paths.SequenceEqual(new[]{deleting}))recycled.TrySetResult(error);};
        try
        {
            var command=MenuCommands(menu).Single(item=>item.Header as string=="Delete");check(command.IsEnabled,"The actual regular Delete menu is eligible");command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));string error=await recycled.Task;
            check(error==null&&!File.Exists(deleting),"Menu Delete completes the actual Windows recycle operation on the owned fixture");
            await RestoreRecycledMenuFixture(deleting,original);
            check(File.Exists(deleting)&&System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(deleting)).SequenceEqual(original),"The exact recycled fixture is restored for reviewed task cleanup without changing other Recycle Bin items");
        }finally{RecycleFinishedForVerification=null;}
        check(HiddenTrialPresentation.ForegroundObserverActive&&HiddenTrialPresentation.ForegroundViolations==0,"The complete independent menu workflow never activates a host-desktop window");
    }
    static async Task RestoreRecycledMenuFixture(string path,byte[] expectedHash)
    {
        // Restore only the fixture whose unique original directory and bytes match.
        // Windows removes its own recycle metadata; ordinary trial cleanup still
        // goes through the reviewed transactional-cleanup helper after exit.
        object shell=null,bin=null,items=null,match=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("13709620-C279-11CE-A49E-444553540000")));
            bin=((dynamic)shell).NameSpace(10);items=((dynamic)bin).Items();
            for(int index=0;index<(int)((dynamic)items).Count;index++)
            {
                object item=((dynamic)items).Item(index);
                bool keep=false;
                try
                {
                    string parent=Convert.ToString(((dynamic)item).ExtendedProperty("System.Recycle.DeletedFrom"));
                    if(!string.Equals(parent,Path.GetDirectoryName(path),StringComparison.OrdinalIgnoreCase))continue;
                    string recycledPath=((dynamic)item).Path;
                    if(!File.Exists(recycledPath)||!System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(recycledPath)).SequenceEqual(expectedHash))continue;
                    if(match!=null)throw new InvalidOperationException("Multiple recycled fixtures match the exact trial directory and bytes.");
                    match=item;keep=true;
                }finally{if(!keep)Marshal.ReleaseComObject(item);}
            }
            if(match==null)throw new InvalidOperationException("The exact recycled menu fixture was not found.");
            using var watcher=new FileSystemWatcher(Path.GetDirectoryName(path));
            var restored=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(string changed){if(string.Equals(changed,path,StringComparison.OrdinalIgnoreCase))restored.TrySetResult();}
            watcher.Created+=(_,args)=>Observe(args.FullPath);watcher.Renamed+=(_,args)=>Observe(args.FullPath);watcher.EnableRaisingEvents=true;
            ((dynamic)match).InvokeVerb("undelete");
            if(File.Exists(path))restored.TrySetResult();
            await restored.Task;
        }
        finally
        {
            foreach(object item in new[]{match,items,bin,shell})if(item!=null)Marshal.ReleaseComObject(item);
        }
    }
    async Task VerifyWidgetMenu9(string root,string source,string file,string folder,string shortcut,bool native,Action<bool,string> check)
    {
        var widget=new SearchWidgetWindow(this);widget.Show();
        try
        {
            async Task Select(string path){if(!widget.IsVisible)widget.Show();await S1Verification.Navigate(widget.Pane,source);widget.Pane.List.SelectedItem=widget.Pane.Items.Single(entry=>entry.Path==path);await Dispatcher.InvokeAsync(widget.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
            foreach(string path in new[]{file,folder,shortcut})
            {
                await Select(path);var menu=widget.ResultMenu();
                check(MenuCommands(menu).Where(item=>item.Header as string is "Open containing folder" or "Properties").All(item=>item.IsEnabled)&&MenuCommands(menu).Single(item=>item.Header as string=="Properties").InputGestureText=="Alt+Enter","Widget actions are available with shortcuts for "+Path.GetFileName(path));
                string[] dispatched=null;Window owner=null;PropertiesForVerification=(w,paths)=>{owner=w;dispatched=paths.ToArray();};
                try
                {
                    MenuCommands(menu).Single(item=>item.Header as string=="Properties").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    check(ReferenceEquals(owner,this)&&dispatched?.SequenceEqual(new[]{path})==true&&!widget.IsVisible,"Widget menu Properties preserves its own selection and dismisses search on successful dispatch");
                    await Select(path);dispatched=null;check(widget.HandleResultShortcut(Key.Enter,ModifierKeys.Alt,widget.Pane.List)&&dispatched?.SequenceEqual(new[]{path})==true,"The Alt+Enter handler reaches the same actual selected object");
                }
                finally{PropertiesForVerification=null;}
                check(ReferenceEquals(owner,this)&&dispatched?.SequenceEqual(new[]{path})==true,"Widget Properties dispatches its exact selected object through the application, preserving .lnk identity");
                await Select(path);
                if(native)using(var probe=new MenuPropertyProbe(widget,path,_=>widget.Dismiss()))
                {
                    var propertyRow=widget.Pane.Selected.Single();
                    widget.PropertiesSelection();var observed=await probe.Completion;
                    File.WriteAllText(Path.Combine(root,"properties-"+Path.GetFileName(path)+".json"),JsonSerializer.Serialize(observed));
                    check(propertyRow.Message.Length==0&&!widget.IsVisible&&observed.SurvivesPopupHide==true&&(!Environment.UserInteractive||observed.VisibleBeforeHide)&&observed.Title.Contains(Path.GetFileNameWithoutExtension(path),StringComparison.OrdinalIgnoreCase),"The actual Windows property sheet launches successfully, survives search dismissal, closes and describes "+Path.GetFileName(path));
                }
                await Select(path);
                var listed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Revealed(){if(new[]{Left,Right}.Any(pane=>pane.Dir==source&&pane.Selected.Any(entry=>entry.Path==path)))listed.TrySetResult();}
                Left.StatsChanged+=Revealed;Right.StatsChanged+=Revealed;
                try
                {
                    if(path==folder)check(widget.HandleResultShortcut(Key.Enter,ModifierKeys.Control|ModifierKeys.Shift,widget.Pane.List),"The containing-folder keyboard shortcut uses the same selection-bound reveal action");
                    else MenuCommands(menu).Single(item=>item.Header as string=="Open containing folder").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Revealed();await listed.Task;
                }
                finally{Left.StatsChanged-=Revealed;Right.StatsChanged-=Revealed;}
                check(!widget.IsVisible,"Reveal returns to Pathweft and highlights the selected result itself");widget.Show();
            }
            await Select(file);widget.Pane.List.SelectedItems.Clear();var empty=widget.ResultMenu();
            check(MenuCommands(empty).All(item=>!item.IsEnabled),"An empty widget selection never dispatches a file action");
            widget.Pane.List.SelectedItems.Add(widget.Pane.Items.Single(entry=>entry.Path==file));widget.Pane.List.SelectedItems.Add(widget.Pane.Items.Single(entry=>entry.Path==folder));
            var multiple=widget.ResultMenu();check(!MenuCommands(multiple).Single(item=>item.Header as string=="Open containing folder").IsEnabled&&MenuCommands(multiple).Single(item=>item.Header as string=="Properties").IsEnabled,"Widget multiple selection preserves combined Properties and disables single-result reveal");
            await Select(shortcut);ShellMenu.InspectForVerification=(paths,verbs,top)=>check(paths.SequenceEqual(new[]{shortcut})&&verbs.Contains("properties"),"Widget Windows menu uses the shortcut itself and actual Shell verbs");
            try{MenuCommands(widget.ResultMenu()).Single(item=>item.Header as string=="Windows menu…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}finally{ShellMenu.InspectForVerification=null;}
            await S1Verification.Snapshot(widget,root,"widget-result-menu-owner");
        }finally{PropertiesForVerification=null;widget.Shutdown();}
    }
    static void CaptureMenu(ContextMenu menu,string root,string label)
    {
        var dpi=VisualTreeHelper.GetDpi(menu);double scale=Math.Max(1,Settings.TextScale);
        var image=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth*dpi.DpiScaleX*scale),(int)Math.Ceiling(menu.ActualHeight*dpi.DpiScaleY*scale),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);image.Render(menu);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(root,label+".png"));encoder.Save(output);
    }
}

internal sealed class MenuPropertyProbe : IDisposable
{
    internal sealed record Observation(string Title,bool Owned,string[] Labels,long Window,long Root,long Parent,long ExpectedOwner,long ExpectedParent,long ExpectedRootOwner,object[] Owners,uint StationFlags,bool VisibleBeforeHide,bool ExistsAfterHide,uint StyleBefore,uint StyleAfter,bool? SurvivesPopupHide);
    readonly TaskCompletionSource<Observation> complete=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly IntPtr owner;readonly string path;readonly IntPtr hook;readonly Action<IntPtr> onShown;
    IntPtr observedWindow;Observation observed;
    static readonly Dictionary<IntPtr,MenuPropertyProbe> probes=new();
    static readonly EventCallback callback=(h,e,w,o,c,t,time)=>{if(probes.TryGetValue(h,out var probe))probe.OnShow(h,e,w,o,c,t,time);};
    internal Task<Observation> Completion=>complete.Task;
    internal MenuPropertyProbe(Window window,string file,Action<IntPtr> shown=null)
    {
        owner=new WindowInteropHelper(window).Handle;path=file;onShown=shown;
        hook=SetWinEventHook(0x8001,0x8002,IntPtr.Zero,callback,(uint)Environment.ProcessId,0,0);
        if(hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        probes.Add(hook,this);
    }
    void OnShow(IntPtr h,uint e,IntPtr window,int obj,int child,uint thread,uint time)
    {
        if(obj!=0||child!=0)return;
        if(e==0x8001){if(window==observedWindow&&observed!=null)complete.TrySetResult(observed);return;}
        if(observedWindow!=IntPtr.Zero||Class(window)!="#32770")return;
        string title=Text(window);if(!title.Contains(Path.GetFileNameWithoutExtension(path),StringComparison.OrdinalIgnoreCase))return;
        var labels=new List<string>();EnumChildWindows(window,(item,_)=>{string text=Text(item);if(text.Length>0)labels.Add(text);return true;},IntPtr.Zero);
        var owners=new List<long>();for(var current=GetWindow(window,4);current!=IntPtr.Zero;current=GetWindow(current,4))owners.Add(current.ToInt64());
        bool owned=owners.Contains(owner.ToInt64());
        var details=owners.Select(handle=>
        {
            var native=new IntPtr(handle);uint ownerThread=GetWindowThreadProcessId(native,out uint process);
            return (object)new{handle,@class=Class(native),title=Text(native),process,thread=ownerThread};
        }).ToArray();
        bool visibleBefore=IsWindowVisible(window);uint styleBefore=GetWindowLong(window,-16);
        onShown?.Invoke(window);
        // A private station has no visible display surface. Check Windows' own
        // shown style before/after the hide, plus actual HWND destruction; keep
        // IsWindowVisible separately rather than claiming on-screen visibility.
        bool? survives=onShown==null?null:IsWindow(window)&&(styleBefore&0x10000000)!=0&&(GetWindowLong(window,-16)&0x10000000)!=0;
        observedWindow=window;
        observed=new Observation(title,owned,labels.ToArray(),window.ToInt64(),GetAncestor(window,2).ToInt64(),GetParent(window).ToInt64(),owner.ToInt64(),GetWindow(owner,4).ToInt64(),GetAncestor(owner,3).ToInt64(),details,StationFlags(),visibleBefore,IsWindow(window),styleBefore,GetWindowLong(window,-16),survives);
        PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
    }
    static string Text(IntPtr window){var value=new StringBuilder(2048);GetWindowText(window,value,value.Capacity);return value.ToString();}
    static string Class(IntPtr window){var value=new StringBuilder(256);GetClassName(window,value,value.Capacity);return value.ToString();}
    internal static string Station(){var text=new StringBuilder(256);if(!GetUserObjectInformation(GetProcessWindowStation(),2,text,512,out _))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return text.ToString();}
    static uint StationFlags(){if(!GetUserObjectInformation(GetProcessWindowStation(),1,out UserObjectFlags flags,Marshal.SizeOf<UserObjectFlags>(),out _))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return flags.Flags;}
    [StructLayout(LayoutKind.Sequential)]struct UserObjectFlags{internal int Inherit,Reserved;internal uint Flags;}
    public void Dispose()
    {
        if(!UnhookWinEvent(hook))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        probes.Remove(hook);
    }
    delegate void EventCallback(IntPtr hook,uint e,IntPtr window,int obj,int child,uint thread,uint time);
    delegate bool ChildCallback(IntPtr window,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,EventCallback callback,uint pid,uint tid,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint command);
    [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern uint GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int size);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr parent,ChildCallback callback,IntPtr data);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,int message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder buffer,int bytes,out int needed);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(IntPtr handle,int index,out UserObjectFlags buffer,int bytes,out int needed);
}
#endif
