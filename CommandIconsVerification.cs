#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyCommandIcons13(string root,Action<bool,string> check)
    {
        string source=Directory.CreateDirectory(Path.Combine(root,"source")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"target")).FullName;
        File.WriteAllText(Path.Combine(source,"copy.txt"),"Copy through the real compact command");
        File.WriteAllText(Path.Combine(source,"move.txt"),"Move through the real compact command");
        File.WriteAllText(Path.Combine(source,".hidden.txt"),"Hidden item");
        await S1Verification.Navigate(Left,source);await S1Verification.Navigate(Right,target);
        SetActive(Left);Settings.SinglePane=false;Settings.Preview=false;Settings.Stats=0;
        ApplyPanes();ApplyPreview();ApplyStats();
        async Task Layout()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var utilities=SidebarHeaderActions.Children.OfType<Button>().ToArray();
        check(utilities.Select(button=>button.Name).SequenceEqual(new[]{"UpdateButton","WhatsNewButton","HelpButton","SettingsButton"})&&utilities.All(button=>button.ToolTip is string&& !string.IsNullOrEmpty(AutomationProperties.GetName(button))),"Sidebar utilities retain their original actions, accessible names and hover labels at the top");
        check(SidebarFooter.Children.Count==1&&ReferenceEquals(SidebarFooter.Children[0],BinButton)&&FindName("VersionText")==null,"Only Drop bin remains at the bottom; the sidebar brand no longer shows a version");
        var buttons=CommandButtons.Children.OfType<Button>().ToArray();
        check(buttons.Select(button=>button.Name).SequenceEqual(new[]{"NewFolderCommand","NewFileCommand","OpenCommand","RenameCommand","DeleteCommand","CopyButton","MoveButton","TransfersButton","TerminalCommand","AnalyzeSpaceCommand","PerformanceCommand","MaintenanceCommand","HiddenToggle","AdministratorCommand","PanesCommand","PreviewCommand"})&&CommandButtons.Children.OfType<Border>().Count()==4,"The command row groups creation, selected-item actions, transfers, tools and view controls; administrator access sits beside Hidden");
        check(buttons.Length==16&&buttons.All(button=>button.Content is Image {Source:DrawingImage,Width:16,Height:16}),"All existing bottom commands use vector artwork at16 DIP without text labels");
        check(buttons.All(button=>!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))&&button.ToolTip is string {Length:>0}),"Icon-only commands retain accessible names and hover guidance");
        foreach(string key in new[]{"Rename","Open","Copy","Move","NewFolder","Delete","Hidden","Preview","DetailsPane","Sort","IconsXL","IconsL","IconsM","List","Details"})
            foreach(string variant in new[]{"contrast-standard/theme-light","contrast-standard/theme-dark","contrast-black","contrast-white"})
                check(ExplorerIcons.NativeForVerification(key,variant)!=null,"The actual Windows SVG is supported for "+key+" / "+variant);
        check(ExplorerIcons.Glyph("\uE8C8") is {IsFrozen:true}&&ExplorerIcons.Glyph("\uE8C8").Width==16,"The font compatibility path produces a frozen16-DIP vector without the Win11 asset set");

        foreach(var appearance in new[]{("native-dark",true,"dark"),("native-light",true,"light"),("original",false,"dark")})
        {
            Settings.NativeTheme=appearance.Item2;Settings.NativeMode=appearance.Item3;
            foreach(double scale in new[]{1.0,1.5})
            {
                Settings.TextScale=scale;Width=scale==1?1200:1500;Height=850;ApplyAppearance();await Layout();
                check(Grid.GetRow(SidebarHeaderActions)==0&&utilities.All(button=>button.TranslatePoint(new Point(button.ActualWidth,button.ActualHeight),SidebarHeader).X<=SidebarHeader.ActualWidth+0.1),"Utilities fit beside the brand at the usual sidebar width: "+appearance.Item1+" / "+scale);
                check(buttons.Where(button=>button.IsVisible).All(button=>button.ActualWidth<=30.1&&button.ActualHeight<=24.1),"Compact command dimensions stay at Pathweft scale: "+appearance.Item1+" / "+scale);
                check(CommandButtons.ActualWidth<=486.1&&CommandBar.ActualHeight<=24.1,"The grouped command row has only narrow separators, with no Explorer-sized spacing or extra toolbar: "+appearance.Item1+" / "+scale);
                foreach(var pane in new[]{Left,Right})
                {
                    var view=(Button)pane.FindName("ViewButton");var sort=(Button)pane.FindName("SortButton");
                    check(view.ActualWidth==28&&sort.ActualWidth==28&&view.Content is Image {Source:DrawingImage,Width:14},"Pane controls remain compact icon buttons: "+appearance.Item1+" / "+scale);
                    Click(view);await Layout();var menu=FindMenu(view);
                    check(menu.Items.OfType<MenuItem>().Count()==5&&menu.Items.OfType<MenuItem>().All(item=>item.Icon is Image {Source:DrawingImage,Width:14}&&item.InputGestureText.StartsWith("Ctrl+Shift+")),"The actual View menu uses distinct recovered SVGs and retains all five supported mode shortcuts");
                    var list=menu.Items.OfType<MenuItem>().Single(item=>(string)item.Header=="List");list.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;
                    check(pane.Mode==PaneView.ViewMode.List&&((string)view.ToolTip).Contains("List"),"A recovered View-menu icon remains connected to its actual mode and hover state");
                    pane.SetView(PaneView.ViewMode.Details);
                    Click(sort);await Layout();var sorting=FindMenu(sort);
                    sorting.Items.OfType<MenuItem>().Single(item=>(string)item.Header=="Modified").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));sorting.IsOpen=false;
                    check(pane.SortKey=="Modified"&&((string)sort.ToolTip).Contains("Modified, descending"),"The actual Sort menu keeps current key/direction in the compact control's tooltip");
                }
                // Detached validation stays outside the desktop; focus is exercised through WPF.
                RenameCommand.Focus();await Layout();
                check(RenameCommand.IsKeyboardFocused||RenameCommand.Focusable,"Icon controls remain keyboard focusable with an explicit focused-border template");
                await S1Verification.Snapshot(this,root,$"commands-{appearance.Item1}-{scale*100:0}");
            }
        }
        // Exercise the relocated buttons through their existing handlers, while
        // keeping network update checks and all settings mutations untouched.
        Click(HelpButton);await Layout();check(helpWindow?.IsVisible==true,"The header Help icon opens the actual Help window");helpWindow.Close();
        Click(WhatsNewButton);await Layout();check(whatsNewWindow?.IsVisible==true,"The header What's new icon opens its existing window");whatsNewWindow.Close();
        foreach(var appearance in new[]{("native-dark",true,"dark"),("native-light",true,"light"),("original",false,"dark")})
        {
            Settings.NativeTheme=appearance.Item2;Settings.NativeMode=appearance.Item3;Settings.TextScale=1.5;ApplyAppearance();
            Click(SettingsButton);await Layout();var settings=settingsWindow;
            check(settings?.IsVisible==true,"The header Settings icon opens the actual settings window");
            var categories=(ListBox)settings.FindName("SettingsCategories");categories.SelectedIndex=6;await Layout();settings.UpdateLayout();
            var about=(TextBlock)settings.FindName("AboutText");var card=(Border)settings.FindName("AboutCard");
            check(about.Text==$"Pathweft {Updater.Current} · Darkaxt"&&Descendants<TextBlock>(card).SelectMany(text=>text.Inlines.OfType<System.Windows.Documents.Hyperlink>()).All(link=>link.NavigateUri?.Host=="github.com"),"About retains Pathweft's version/project and removes upstream branding/contact/promotions");
            await S1Verification.Snapshot(settings,root,"about-"+appearance.Item1);
            foreach(var pair in new[]{(1,"StatsBox","StatsCompactBox"),(1,"AutoscrollBox","AutoscrollUpDownBox"),(3,"TaskbarSearchBox","TaskbarEmbedBox"),(3,"TrayBox","StartupBox")})
            {
                categories.SelectedIndex=pair.Item1;await Layout();settings.UpdateLayout();
                var parent=(CheckBox)settings.FindName(pair.Item2);var child=(CheckBox)settings.FindName(pair.Item3);
                double gap=child.TranslatePoint(new Point(),parent).Y-parent.ActualHeight;
                check(gap>=5.9&&child.TranslatePoint(new Point(),parent).X>=21.9,"Indented child toggles preserve standard row separation and indentation: "+pair.Item3+" / "+appearance.Item1);
            }
            categories.SelectedIndex=3;await Layout();await S1Verification.Snapshot(settings,root,"integration-spacing-"+appearance.Item1);
            settings.Close();
        }
        Settings.NativeTheme=true;Settings.NativeMode="dark";
        foreach(double scale in new[]{1.0,1.5})
        {
            Settings.TextScale=scale;Settings.SidebarScale=1.5;SidebarColumn.Width=new GridLength(160);ApplyAppearance();await Layout();
            check(Grid.GetRow(SidebarHeaderActions)==1&&utilities.All(button=>button.ActualWidth>=20&&button.TranslatePoint(new Point(button.ActualWidth,button.ActualHeight),SidebarHeader).X<=SidebarHeader.ActualWidth+0.1),"All sidebar utilities remain visible below the brand at minimum width and150% sidebar scale / "+scale);
        }
        await S1Verification.Snapshot(this,root,"sidebar-narrow-150");
        Settings.SidebarScale=1;SidebarColumn.Width=new GridLength(270);
        Settings.NativeTheme=true;Settings.NativeMode="dark";Settings.TextScale=1;Width=900;Height=650;ApplyAppearance();await Layout();
        foreach(double scale in new[]{1.0,1.5})
        {
            Settings.TextScale=scale;ApplyAppearance();await Layout();
            check(CommandButtons.ActualWidth<=CommandBar.ActualWidth&&buttons.Where(button=>button.IsVisible).All(button=>button.TranslatePoint(new Point(button.ActualWidth,0),CommandBar).X<=CommandBar.ActualWidth+0.1),"Grouped commands fit the narrow900-DIP window without hiding actions at "+scale);
        }
        Settings.TextScale=1;ApplyAppearance();await Layout();
        Click(HiddenToggle);await Layout();
        check(HiddenToggle.Tag as string==(Settings.ShowHidden?"on":null),"The hidden-files command exposes its toggled state");
        Click(PanesCommand);check(Settings.SinglePane&&PanesCommand.Tag==null,"The pane command keeps the one-pane action and state");
        Click(PanesCommand);check(!Settings.SinglePane&&PanesCommand.Tag as string=="on","The pane command restores two panes through the original action");
        Click(PreviewCommand);check(Settings.Preview&&PreviewCommand.Tag as string=="on","The compact Preview button retains its action and active state");
        Click(PreviewCommand);check(!Settings.Preview&&PreviewCommand.Tag==null,"The same Preview button turns the pane off");

        SetActive(Left);Left.List.SelectedItem=Left.Items.Single(entry=>entry.Name=="copy.txt");
        Click(CopyButton);var copy=jobs.First();await copy.Completion;
        check(File.ReadAllText(Path.Combine(target,"copy.txt"))==File.ReadAllText(Path.Combine(source,"copy.txt")),"The compact Copy icon executes a real transfer through the unchanged command handler");
        Left.List.SelectedItem=Left.Items.Single(entry=>entry.Name=="move.txt");Click(MoveButton);var move=jobs.First();await move.Completion;
        check(File.Exists(Path.Combine(target,"move.txt"))&&!File.Exists(Path.Combine(source,"move.txt")),"The compact Move icon executes the real move action");
        string launched=null;bool internalEditor=Settings.InternalEditor;Settings.InternalEditor=false;
        Left.ExternalOpenForVerification=start=>launched=start.FileName;
        try{Left.List.SelectedItem=Left.Items.Single(entry=>entry.Name=="copy.txt");Click(OpenCommand);check(launched==Path.Combine(source,"copy.txt"),"Open reaches the existing Windows-association dispatch path");}
        finally{Left.ExternalOpenForVerification=null;Settings.InternalEditor=internalEditor;}
        await S1Verification.Snapshot(this,root,"commands-narrow-actions");
        HiddenTrialPresentation.AllowNativelyHiddenMonitorMoves(this);var dpi=new HashSet<uint>();
        foreach(var display in System.Windows.Forms.Screen.AllScreens)
        {
            var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;var area=display.WorkingArea;
            check(TaskbarSearchHost.Native.SetWindowPos(handle,IntPtr.Zero,area.Left+40,area.Top+40,Math.Min(1600,area.Width-80),Math.Min(1000,area.Height-80),0x0094),"Compact command HWND follows a real monitor transition");
            await Layout();dpi.Add(TaskbarSearchHost.Native.GetDpiForWindow(handle));
            check(HiddenTrialPresentation.IsNativelyHidden(this),"DPI verification leaves the native app hidden");
        }
        check(dpi.Contains(96)&&dpi.Contains(144),"Compact command layout qualifies actual96/144-DPI HWND transitions");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Compact command verification caused no foreground activation or desktop input: "+System.Text.Json.JsonSerializer.Serialize(HiddenTrialPresentation.ForegroundEvents));
        static ContextMenu FindMenu(Button button)
        {
            // WPF keeps open popup roots in its presentation sources, not button.ContextMenu.
            foreach(var source in PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>())
                if(source.RootVisual is {} root)
                    foreach(var menu in Descendants<ContextMenu>(root))if(menu.IsOpen&&menu.PlacementTarget==button)return menu;
            throw new InvalidOperationException("The actual command popup did not open");
        }
        static IEnumerable<T> Descendants<T>(DependencyObject node)where T:DependencyObject
        {
            if(node is T match)yield return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in Descendants<T>(VisualTreeHelper.GetChild(node,i)))yield return item;
        }
    }
}
#endif
