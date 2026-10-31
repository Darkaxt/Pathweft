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
            if(Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_WIDGET_ONLY")=="1")
            {
                widget=new SearchWidgetWindow(this);widget.ShowSearch();await Settled();
                var input=(TextBox)widget.Pane.FindName("FilterBox");
                var list=widget.Pane.ListForVerification;
                string known=@"D:\Projects\Pathweft\README.md";
                bool Known()=>widget.Pane.Items.Any(e=>e.Path.Equals(known,StringComparison.OrdinalIgnoreCase));
                async Task Query(string text){input.Text=text;await widget.Pane.SearchCompletion;await Settled();}
                async Task Flip(string name)
                {
                    var toggle=(System.Windows.Controls.Primitives.ToggleButton)widget.Pane.FindName(name);
                    ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
                    await widget.Pane.SearchCompletion;await Settled();
                }
                Check(widget.WindowStyle==WindowStyle.None&&widget.Pane.Dir==PaneView.ThisPC&&!widget.Pane.Items.Any(),"The compact widget opens to an empty global query without a caption/header or drive listing");
                Check(((DockPanel)widget.Content).Children.Count==2&&widget.Notice.Visibility==Visibility.Collapsed&&((DockPanel)widget.Pane.FindName("PathBar")).Children.OfType<Button>().All(b=>!b.IsVisible)&&!((FrameworkElement)((TextBlock)widget.Pane.FindName("Footer")).Parent).IsVisible,"Only search/match controls and results are visible; navigation, destination and action/view/sort bars are absent");
                Check(((GridView)list.View).Columns.Select(c=>c.Header is Panel header?header.Children.OfType<TextBlock>().Last().Text:c.Header.ToString()).SequenceEqual(new[]{"Name","Location","Size","Modified"}),"The real result view has the four restrained reference columns");
                input.Text="no_such_widget_result";input.Text="path:\"D:\\Projects\\Pathweft\" \"README.md\"";
                await widget.Pane.SearchCompletion;await Settled();
                Check(Known()&&widget.Pane.SearchRequest.Scope==1&&widget.Pane.SearchRequest.Query==input.Text,"Actual text changes query the global Everything index and a superseded query cannot overwrite newer results");
                Check(ReferenceEquals(list.ItemContainerStyle,widget.Pane.FindResource("WidgetResultRow")),"Live query results retain their plain popup row template without file-pane stripes or age bars");
                widget.Pane.SearchInstance="Pathweft-missing-widget-verification-instance";
                await Query("README");
                Check(!widget.Pane.Items.Any()&&((FrameworkElement)widget.Pane.FindName("SearchCoverage")).IsVisible&&!string.IsNullOrWhiteSpace(widget.Pane.SearchNotice),"An unavailable index shows a concise error and clears stale results");
                widget.Pane.SearchInstance="";
                await Query(@"D:\Projects\Pathweft");
                Check(widget.Pane.Dir==PaneView.ThisPC,"Typing an existing path stays a global search rather than navigating a mini file browser");
                await Query("path:\"D:\\Projects\\Pathweft\" readme.md");Check(Known(),"Default widget matching is case insensitive");
                await Flip("MatchCase");Check(!Known(),"The compact Match case control changes actual indexed results");await Flip("MatchCase");
                await Query(@"^D:\\Projects\\Pathweft\\README\.md$");await Flip("MatchPath");await Flip("MatchRegex");await Flip("MatchCase");
                Check(Known()&&!((Control)widget.Pane.FindName("MatchWord")).IsEnabled,"Regex, path and case controls retain their actual Everything semantics in the compact input");
                var options=widget.Pane.MatchOptions;widget.Handoff();await Left.SearchCompletion;await Settled();
                Check(!widget.IsVisible&&Left.MatchOptions==options&&Left.Items.Any(e=>e.Path.Equals(known,StringComparison.OrdinalIgnoreCase)),"The Ctrl+Enter handoff command preserves the real query/options and dismisses the popup");
                widget.ShowSearch();await widget.Pane.SearchCompletion;
                await Flip("MatchRegex");await Flip("MatchPath");await Flip("MatchCase");
                await Query("path:\"C:\\Windows\\System32\" ext:dll");
                int pageCount=widget.Pane.Items.Count();Check(pageCount>0&&widget.Pane.IndexedTotal>pageCount,"The live global query has more real results than one index page");
                list.ScrollIntoView(widget.Pane.Items.Last());await Settled();await widget.Pane.SearchCompletion;await Settled();
                Check(widget.Pane.Items.Count()>pageCount&&!((Control)widget.Pane.FindName("MoreResults")).IsVisible,"Scrolling the actual results loads the next page without a paging action bar");
                foreach(double scale in new[]{1.0,1.5})foreach(double width in new[]{520d,720d})
                {
                    Settings.TextScale=scale;widget.ApplyTextScale();widget.Width=width*scale;widget.Height=520*scale;await Settled();
                    Check(((GridView)list.View).Columns.Sum(c=>c.Width)<=list.ActualWidth&&input.ActualWidth>160,"Query and result columns fit the compact width "+width+" at text scale "+scale);
                    await S1Verification.Snapshot(widget,root,$"compact-widget-{scale*100:0}-{width:0}");
                }
                Settings.TextScale=1;widget.ApplyTextScale();
                widget.Width=720;widget.Height=520;
                foreach(string theme in new[]{"light","original"})
                {
                    Settings.NativeTheme=theme!="original";Settings.NativeMode=theme=="original"?"dark":theme;ApplyAppearance();await Settled();
                    Check(ReferenceEquals(widget.Background,Root.Background),"The compact popup follows the main "+theme+" theme background");
                    await S1Verification.Snapshot(widget,root,"compact-widget-theme-"+theme);
                }
                Settings.NativeTheme=true;Settings.NativeMode="dark";ApplyAppearance();await Settled();
                await Query("path:\"D:\\Projects\\Pathweft\" \"README.md\"");
                list.SelectedItem=widget.Pane.Items.First(e=>e.Path.Equals(known,StringComparison.OrdinalIgnoreCase));
                var opened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                widget.Pane.ExternalOpenForVerification=start=>{Check(start.FileName==known&&start.UseShellExecute,"Enter reaches the existing Windows file-open boundary for the selected result");opened.TrySetResult();};
                bool editor=Settings.InternalEditor;Settings.InternalEditor=false;
                try{input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(widget),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});await opened.Task;}
                finally{Settings.InternalEditor=editor;widget.Pane.ExternalOpenForVerification=null;}
                await Query("");Check(!widget.Pane.Items.Any()&&input.Visibility==Visibility.Visible&&((FrameworkElement)widget.Pane.FindName("Crumbs")).Visibility!=Visibility.Visible,"Clearing the input empties results without showing drives or breadcrumbs");
                await Query("path:\"D:\\Projects\\Pathweft\" folder: docs");
                string docs=@"D:\Projects\Pathweft\docs";list.SelectedItem=widget.Pane.Items.First(e=>e.Path.Equals(docs,StringComparison.OrdinalIgnoreCase));
                var navigated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void FolderLocated(){if(new[]{Left,Right}.Any(p=>p.Dir==docs&&p.Listed&&!p.InSearch))navigated.TrySetResult();}
                Left.StatsChanged+=FolderLocated;Right.StatsChanged+=FolderLocated;
                try{widget.Pane.OpenSelected();FolderLocated();await navigated.Task;}finally{Left.StatsChanged-=FolderLocated;Right.StatsChanged-=FolderLocated;}
                Check(!widget.IsVisible&&widget.Pane.Dir==PaneView.ThisPC,"Folder results open in the main manager and dismiss the global search popup");
                string archive=@"D:\Projects\Pathweft\dist\1.1.1\Pathweft-1.1.1-source.zip";
                widget.ShowSearch("path:\"D:\\Projects\\Pathweft\\dist\\1.1.1\" \"Pathweft-1.1.1-source.zip\"");await widget.Pane.SearchCompletion;
                list.SelectedItem=widget.Pane.Items.Single(e=>e.Path.Equals(archive,StringComparison.OrdinalIgnoreCase));
                var archiveOpened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ArchiveLocated(){if(new[]{Left,Right}.Any(p=>p.ArchiveRoot?.Archive==archive&&p.Listed&&p.Items.Any()))archiveOpened.TrySetResult();}
                Left.StatsChanged+=ArchiveLocated;Right.StatsChanged+=ArchiveLocated;
                try{widget.Pane.OpenSelected();ArchiveLocated();await archiveOpened.Task;}finally{Left.StatsChanged-=ArchiveLocated;Right.StatsChanged-=ArchiveLocated;}
                Check(!widget.IsVisible&&widget.Pane.Dir==PaneView.ThisPC,"An indexed ZIP result opens its real contents in the main manager without turning the popup into a file browser");
                widget.ShowSearch("README.md");await widget.Pane.SearchCompletion;
                widget.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(widget),Environment.TickCount,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
                Check(!widget.IsVisible&&HiddenTrialPresentation.ForegroundViolations==0,"Escape dismisses the popup and the complete hidden workflow causes zero foreground activation");
                result=0;return;
            }
            if(Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_SETTINGS_ACTION_ONLY")=="1")
            {
                settings=new SettingsWindow(this);settings.Show();await Settled();
                var action=(Button)settings.FindName("RestoreFolderHandlers");
                action.Visibility=Visibility.Visible; // real action/style; never invoke registry recovery
                IEnumerable<DependencyObject> Children(DependencyObject parent)
                {
                    for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
                    {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Children(child))yield return descendant;}
                }
                foreach(double scale in new[]{1.0,1.5})foreach(double width in new[]{440d,660d})
                {
                    Settings.TextScale=scale;settings.Width=width;settings.Height=640;
                    settings.GetType().GetMethod("ApplyTextSize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(settings,null);
                    await Settled();action.BringIntoView();await Settled();
                    var text=Children(action).OfType<TextBlock>().Single(t=>t.Text=="Restore previous folder handlers");
                    var last=text.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
                    var end=text.TranslatePoint(new Point(last.Right,last.Bottom),action);
                    checks.Add(new{scale,width,actionWidth=action.ActualWidth,textWidth=text.ActualWidth,textHeight=text.ActualHeight,last=last.ToString(),end});
                    await S1Verification.Snapshot(settings,root,$"restore-action-{scale*100:0}-{width:0}");
                    Check(!last.IsEmpty&&end.X<=action.ActualWidth+0.5&&end.Y<=action.ActualHeight+0.5,"The full restore-handler action label fits at width "+width+" and text scale "+scale);
                }
                Check(HiddenTrialPresentation.ForegroundViolations==0,"Restore-action layout checks caused zero foreground activation and never invoked recovery");
                result=0;return;
            }
            if(Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_SIDEBAR_ONLY")=="1")
            {
                Check(SidebarEdgeMask(200,false,false,false)==null&&SidebarEdgeMask(200,true,true,true)==null,"Unclipped content and high-contrast mode retain fully opaque content");
                foreach(double scale in new[]{1.0,1.5})
                {
                    Settings.TextScale=scale;Height=680;Width=1200;ApplyAppearance();await Settled();
                    var viewport=(ScrollContentPresenter)SidebarScroll.Template.FindName("PART_ScrollContentPresenter",SidebarScroll);
                    Check(viewport!=null&&SidebarScroll.ScrollableHeight>0,"Actual sidebar content overflows its viewport at "+scale+" text scale");
                    foreach(string position in new[]{"top","middle","bottom"})
                    {
                        SidebarScroll.ScrollToVerticalOffset(position=="top"?0:position=="bottom"?SidebarScroll.ScrollableHeight:SidebarScroll.ScrollableHeight/2);
                        await Settled();UpdateSidebarFade();
                        var mask=(LinearGradientBrush)viewport.OpacityMask;
                        Check(mask!=null&&mask.GradientStops[0].Color.A==(position=="top"?255:0)&&mask.GradientStops[^1].Color.A==(position=="bottom"?255:0),"Only clipped "+position+" viewport edges fade at "+scale+" text scale");
                        Check(SidebarScroll.OpacityMask==null&&SidebarPanel.OpacityMask==null&&viewport.IsHitTestVisible,"Scrollbar, title/footer and sidebar input remain outside the content-only fade");
                        await S1Verification.Snapshot(this,root,$"sidebar-fade-{scale*100:0}-{position}");
                        if(position=="middle")
                        {
                            using var file=File.OpenRead(Path.Combine(root,$"sidebar-fade-{scale*100:0}-{position}.png"));
                            var image=System.Windows.Media.Imaging.BitmapDecoder.Create(file,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                            var bitmap=new System.Windows.Media.Imaging.FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
                            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                            var point=viewport.TranslatePoint(new Point(viewport.ActualWidth/2,0),this);
                            int x=(int)Math.Round(point.X*bitmap.PixelWidth/ActualWidth),y=(int)Math.Ceiling(point.Y*bitmap.PixelHeight/ActualHeight);
                            int at=(y*bitmap.PixelWidth+x)*4;
                            checks.Add(new{edgePixel=new[]{pixels[at],pixels[at+1],pixels[at+2]},point,viewport.ActualHeight,descendantBounds=VisualTreeHelper.GetDescendantBounds(viewport).ToString()});
                            Check(Math.Abs(pixels[at]-20)<=3&&Math.Abs(pixels[at+1]-20)<=3&&Math.Abs(pixels[at+2]-20)<=3,"The actual clipped edge fades into the Native background pixels at "+scale+" text scale");
                        }
                    }
                    Height=(SidebarScroll.ExtentHeight+160)*scale;await Settled();UpdateSidebarFade();
                    Check(SidebarScroll.ScrollableHeight==0&&viewport.OpacityMask==null,"Resizing to fit the actual content removes the fade at "+scale+" text scale");
                }
                Check(HiddenTrialPresentation.ForegroundViolations==0,"Sidebar fade verification caused zero foreground activation");
                result=0;return;
            }
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
            widget.ShowSearch(@"^D:\\Projects\\Pathweft\\README\.md$");
            await Toggle(widget.Pane,"MatchCase");await Toggle(widget.Pane,"MatchPath");await Toggle(widget.Pane,"MatchRegex");
            Check(HasReadme(widget.Pane),"Taskbar widget searches with its independent inline match options");
            var match=widget.Pane.MatchOptions;
            widget.Handoff();await Settled();await Left.SearchCompletion;
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
