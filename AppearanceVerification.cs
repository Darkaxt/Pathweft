#if DEBUG
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace FileExplorer;

public partial class MainWindow
{
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out uint value,int size);
    internal async Task VerifyAppearanceCorrection(string root)
    {
        if(!HiddenTrialPresentation.Enabled||!Path.GetFullPath(root).StartsWith(@"D:\Temp\pathweft-s8-delivery\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned hidden fixture required.");
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var checks=new List<object>(); int result=1; SettingsWindow settings=null;SearchWidgetWindow widget=null;
        void Check(bool passed,string criterion){checks.Add(new{criterion,passed});if(!passed)throw new InvalidOperationException(criterion);}
        async Task Settled()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        try
        {
            if(Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_CLOSE_ONLY")=="1")
            {await S1Verification.Navigate(Left,root);result=0;return;}
            var automaticEditor=typeof(EditorWindow).GetMethod("TryOpen",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
            Check(automaticEditor!=null,"Automatic opening can decline an editor-incompatible file before creating an editor or showing an error");
            Check(Settings.NativeTheme&&Settings.WindowsAccent,"Real startup loads Native and Windows accent from the isolated persisted profile");
            settings=new SettingsWindow(this);settings.Show();await Settled();settings.UpdateLayout();
            var body=(ScrollViewer)settings.FindName("Body");
            var first=(Border)((StackPanel)body.Content).Children[0];
            Check(first.Name=="AppearanceCard","Settings starts with actionable Appearance controls instead of the search qualification report");
            var hwnd=new WindowInteropHelper(this).Handle;
            var neutral=Appearance.Background;
            uint neutralRef=(uint)(neutral.R|neutral.G<<8|neutral.B<<16);
            var apply=typeof(Appearance).GetMethod("ApplyFrame",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Check(apply!=null,"Native frame has one activation-state update path");
            foreach(bool active in new[]{true,false})
            {
                var frame=((uint Border,uint Caption,int BorderResult,int CaptionResult))apply.Invoke(null,new object[]{this,active});
                using var dwm=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                var color=new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                uint expected=active&&dwm?.GetValue("ColorPrevalence") is int enabled&&enabled!=0?(uint)(color.R|color.G<<8|color.B<<16):0xFFFFFFFF;
                checks.Add(new{active,frame.Border,frame.Caption,frame.BorderResult,frame.CaptionResult,expected});
                Check(frame.BorderResult==0&&frame.CaptionResult==0&&frame.Border==expected&&frame.Caption==neutralRef,$"DWM accepts the actual Windows accent/neutral caption for active={active}; explicit state input does not activate the desktop window");
                int nativeFrame=DwmGetWindowAttribute(hwnd,37,out uint thickness,4);
                checks.Add(new{nativeFrame,thickness,limit="DWM caption/border colors are set-only on this host; offscreen PrintWindow yielded no composed pixels. Native call results and frame geometry are verified; this is not a desktop focus/pixel claim."});
                Check(nativeFrame==0&&thickness>0,"The real HWND retains the native DWM outer frame");
            }
            var address=(TextBox)Left.FindName("FilterBox");
            async Task Enter(string text)
            {
                Left.EditPath();address.Text=text;
                address.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
                await Left.AddressCompletion;
                await Settled();
            }
            async Task Located(string text,string folder,string selected=null)
            {
                var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(){if(Left.Dir==folder&&Left.Listed&&!Left.InSearch&&(selected==null||Left.Selected.Any(e=>e.Name==selected)))ready.TrySetResult();}
                void Failed(string error)=>ready.TrySetException(new IOException(error));
                Left.StatsChanged+=Changed;Left.Error+=Failed;
                try{await Enter(text);Changed();await ready.Task;}
                finally{Left.StatsChanged-=Changed;Left.Error-=Failed;}
            }
            string folder=Directory.CreateDirectory(Path.Combine(root,"Path with spaces ä")).FullName;
            File.WriteAllText(Path.Combine(folder,"sample file.txt"),"address path resolution");
            await Located('"'+folder+'"',folder);
            Check(Left.Dir==folder&&!Left.InSearch,"The real address Enter route resolves a quoted Unicode folder path before search");
            await Located("sample file.txt",folder,"sample file.txt");
            Check(Left.Selected.Single().Name=="sample file.txt","A relative existing file resolves as a path and is revealed instead of executed or searched");
            await Located("%USERPROFILE%",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            await Located(@"D:\Projects\Pathweft",@"D:\Projects\Pathweft");
            Left.EditPath();address.Text=folder;var superseded=Left.SubmitAddressAsync();
            address.Text=@"D:\Projects\Pathweft";var latest=Left.SubmitAddressAsync();
            await Task.WhenAll(superseded,latest);await Settled();
            Check(Left.Dir==@"D:\Projects\Pathweft"&&!Left.InSearch,"A superseded asynchronous path resolution cannot navigate over the newer submission");
            await Enter("README*.md");
            Check(Left.InSearch&&Left.Items.Any(e=>e.Path.Equals(@"D:\Projects\Pathweft\README.md",StringComparison.OrdinalIgnoreCase))&&Left.SearchRequest.Scope==0,"A non-path address queries the real Everything index scoped to the current folder");
            async Task Toggle(PaneView pane,string name)
            {
                var button=(System.Windows.Controls.Primitives.ToggleButton)pane.FindName(name);
                ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
                await Settled();await pane.SearchCompletion;
                checks.Add(new{toggle=name,options=pane.MatchOptions.ToString(),query=pane.SearchRequest.Query,expression=typeof(PaneView).GetField("indexedExpression",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pane),notice=pane.SearchNotice,paths=pane.Items.Take(4).Select(e=>e.Path).ToArray()});
            }
            bool HasReadme(PaneView pane)=>pane.Items.Any(e=>e.Path.Equals(@"D:\Projects\Pathweft\README.md",StringComparison.OrdinalIgnoreCase));
            await Enter("readme*.md");Check(HasReadme(Left),"Default Everything matching is case insensitive");
            await Toggle(Left,"MatchCase");Check(!HasReadme(Left),"The real Match case toggle changes actual indexed results");
            await Toggle(Left,"MatchCase");
            await Enter("READ");Check(HasReadme(Left),"Default Everything matching accepts partial words");
            await Toggle(Left,"MatchWord");Check(!HasReadme(Left),"The real Match whole word toggle excludes partial-name matches");
            await Toggle(Left,"MatchWord");
            await Enter("Projects");Check(!HasReadme(Left),"Name matching does not include a parent folder name");
            await Toggle(Left,"MatchPath");Check(Left.Items.Any(e=>!e.Name.Contains("Projects",StringComparison.OrdinalIgnoreCase)&&e.Path.StartsWith(@"D:\Projects\Pathweft\",StringComparison.OrdinalIgnoreCase)),"The real Match path toggle returns scoped hits whose parent path, rather than their name, matches");
            await Toggle(Left,"MatchPath");await Toggle(Left,"MatchRegex");
            await Enter(@"^README\.md$");
            Check(HasReadme(Left)&&!((Control)Left.FindName("MatchWord")).IsEnabled,"Regex searches the real index while retaining folder scope and disabling whole-word matching");
            Check(Right.MatchOptions==FileExplorer.Search.SearchMatchOptions.None,"Match options do not leak into the other pane");
            await S1Verification.Snapshot(this,root,"native-inline-search-options");
            await Toggle(Left,"MatchRegex");
            await Enter("This PC");
            Check(Left.Dir==PaneView.ThisPC,"The same input navigates to This PC for global search");
            await Enter("path:\"D:\\Projects\\Pathweft\" \"README.md\"");
            Check(Left.InSearch&&Left.SearchRequest.Scope==1&&Left.Items.Any(e=>e.Path.Equals(@"D:\Projects\Pathweft\README.md",StringComparison.OrdinalIgnoreCase)),"Searching from This PC queries the real global Everything index");
            Check(Left.FindName("SearchBar")==null&&Left.FindName("SearchScope")==null&&Left.FindName("SearchOptions")==null&&Left.FindName("SearchSubmit")==null,"The second row, mode selector, funnel and search icon are absent");
            widget=new SearchWidgetWindow(this);
            widget.ShowSearch(PaneView.ThisPC,@"^D:\\Projects\\Pathweft\\README\.md$");
            await Toggle(widget.Pane,"MatchCase");await Toggle(widget.Pane,"MatchPath");await Toggle(widget.Pane,"MatchRegex");
            Check(HasReadme(widget.Pane),"Taskbar widget searches with its independent inline match options");
            var match=widget.Pane.MatchOptions;
            ((IInvokeProvider)new ButtonAutomationPeer(widget.HandoffButton).GetPattern(PatternInterface.Invoke)).Invoke();await Settled();await Left.SearchCompletion;
            Check(Left.MatchOptions==match&&HasReadme(Left)&&Left.SearchRequest.Scope==1,"Actual widget-to-pane handoff preserves query, global location and all match options");
            widget.Shutdown();widget=null;
            Check(((SolidColorBrush)Root.Background).Color==Color.FromRgb(20,20,20)&&SidebarPanel.Background==Brushes.Transparent,"Native dark outer background exactly matches the sidebar while panes retain gray surfaces");
            await S1Verification.Snapshot(this,root,"native-unified-header");
            foreach(var scale in new[]{1.0,1.5})
            {
                Settings.TextScale=scale;settings.Width=scale==1?560:660;settings.Height=scale==1?640:900;
                settings.GetType().GetMethod("ApplyTextSize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(settings,null);
                await Settled();await S1Verification.Snapshot(settings,root,$"settings-native-{scale*100:0}");
                Check(first.TranslatePoint(new Point(),settings).Y<40,"Appearance preferences start at the top without a large introductory report");
            }
            var help=(Button)settings.FindName("SearchHelp");
            Check(help!=null,"Settings provides a deliberate Help entry for search documentation");
            ((IInvokeProvider)new ButtonAutomationPeer(help).GetPattern(PatternInterface.Invoke)).Invoke();await Settled();
            Check(Application.Current.Windows.OfType<HelpWindow>().Any(),"The Settings Help action opens the actual searchable Help window");
            bool closed=false,canceled=false;
            settings.Closed+=(_,_)=>closed=true;
            settings.Closing+=(_,args)=>canceled=args.Cancel;
            bool animations=Settings.Animations;
            try
            {
                Settings.Animations=true;
                settings.Close();
                Check(closed&&!canceled,"A Native window closes through Windows without cancelling close to fade its client into a colored empty DWM surface");
                settings=null;
            }
            finally{Settings.Animations=animations;}
            bool internalEditor=Settings.InternalEditor;
            try
            {
                Settings.InternalEditor=true;
                string editorFolder=Directory.CreateDirectory(Path.Combine(root,"editor-routing")).FullName;
                string binaryHtml=Path.Combine(editorFolder,"binary.html"),largeHtml=Path.Combine(editorFolder,"large.html"),plainHtml=Path.Combine(editorFolder,"plain.html"),unsupported=Path.Combine(editorFolder,"unsupported.bin");
                File.WriteAllBytes(binaryHtml,new byte[]{0,1,2,3});File.WriteAllBytes(unsupported,new byte[]{0,1,2,3});
                using(var large=File.Create(largeHtml))large.SetLength(20L*1024*1024+1);
                File.WriteAllText(plainHtml,"<!doctype html><html><body>Editor routing fixture</body></html>");
                foreach(var path in new[]{binaryHtml,largeHtml,unsupported})
                    Check(!EditorWindow.TryOpen(path)&&!EditorWindow.HasOpenWindow,"Automatic editor declines "+Path.GetFileName(path)+" without an error dialog or empty editor");
                var external=new List<System.Diagnostics.ProcessStartInfo>();Left.ExternalOpenForVerification=external.Add;
                await S1Verification.Navigate(Left,editorFolder);
                async Task DoubleClick(string name)
                {
                    var list=(ListView)Left.FindName("List");
                    list.SelectedItem=Left.Items.Single(e=>e.Name==name);await Settled();
                    list.ScrollIntoView(Left.Selected.Single());await Settled();
                    var row=(ListViewItem)list.ItemContainerGenerator.ContainerFromItem(Left.Selected.Single());
                    Check(row!=null,"Real file row generated for double-click: "+name);
                    list.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});
                    await Settled();
                }
                foreach(var path in new[]{binaryHtml,largeHtml,unsupported})await DoubleClick(Path.GetFileName(path));
                Check(external.Count==3&&external.All(p=>p.UseShellExecute&&p.WorkingDirectory==editorFolder)&&!EditorWindow.HasOpenWindow,"Actual file-row double-clicks reach the existing Windows-association launch boundary for rejected files; test captures dispatch without launching user apps");
                await DoubleClick("plain.html");
                Check(external.Count==3&&EditorWindow.OwnsArchiveCopy(plainHtml),"Supported HTML still opens internally when automatic editing is enabled");
                await EditorWindow.CaptureForVerification(Path.Combine(root,"editor-supported-html"),light:false);
                await EditorWindow.CloseForVerification();
                Settings.InternalEditor=false;await DoubleClick("plain.html");
                Check(external.Count==4&&external[^1].FileName==plainHtml&&!EditorWindow.HasOpenWindow,"Disabling the internal editor sends ordinary HTML double-clicks to the Windows association");
                Settings.InternalEditor=true;
                string archive=Path.Combine(root,"editor-routing.zip");
                using(var zip=System.IO.Compression.ZipFile.Open(archive,System.IO.Compression.ZipArchiveMode.Create))System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip,binaryHtml,"binary.html");
                await S1Verification.Navigate(Left,archive);
                string archivedDispatch=null;ArchiveConsumerForVerification=path=>{archivedDispatch=path;return null;};
                await OpenArchiveEntry(Left,Left.Items.Single(e=>e.Name=="binary.html"));
                Check(archivedDispatch!=null&&File.ReadAllBytes(archivedDispatch).SequenceEqual(File.ReadAllBytes(binaryHtml))&&!EditorWindow.HasOpenWindow,"Archive automatic open hands a rejected HTML extracted copy to the external consumer with its bytes preserved");
                // Explicit editing still accepts HTML; it does not inherit the automatic fallback decision.
                EditorWindow.Open(plainHtml);Check(EditorWindow.OwnsArchiveCopy(plainHtml),"Explicit editor opening remains available for HTML");
                await EditorWindow.CaptureForVerification(Path.Combine(root,"editor-explicit-html"),light:false);
                await EditorWindow.CloseForVerification();
            }
            finally{Settings.InternalEditor=internalEditor;Left.ExternalOpenForVerification=null;ArchiveConsumerForVerification=null;}
            Check(HiddenTrialPresentation.ForegroundViolations==0,"Focused appearance checks caused zero foreground activation");
            result=0;
        }
        catch(Exception error){checks.Add(new{failure=error.ToString()});}
        finally
        {
            widget?.Shutdown();
            foreach(var help in Application.Current.Windows.OfType<HelpWindow>().ToArray())help.Close();
            settings?.Close();
            var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool didClose=false,closeCancelled=false;
            Closed+=(_,_)=>{didClose=true;closed.TrySetResult();};Closing+=(_,args)=>closeCancelled=args.Cancel;
            Quit();
            await closed.Task; // preview work owns the first cancelled Close; wait for the actual Closed event
            foreach(var pane in new[]{Left,Right})
            {
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var watch=(FileSystemWatcher)typeof(PaneView).GetField("watcher",flags).GetValue(pane);
                var pending=((System.Windows.Threading.DispatcherTimer)typeof(PaneView).GetField("debounce",flags).GetValue(pane)).IsEnabled;
                checks.Add(new{close=didClose,cancelled=closeCancelled,pane=pane.Name,watch=watch?.Path,pending});
                if(watch!=null||pending)
                {result=1;checks.Add(new{failure="Closing left a folder watcher/refresh timer alive after view resource teardown."});}
            }
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{result,pid=Environment.ProcessId,checks},new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown(result);
        }
    }
}
#endif
