#if DEBUG
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyAuxiliaryWindows14(string root,Action<bool,string> check)
    {
        var saved=(Settings.NativeTheme,Settings.NativeMode,Settings.TextScale);
        string oldFeed=Updater.Feed;
        var measurements=new List<object>();
        const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Instance;
        async Task Settle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        async Task Render(Window window,string name,bool capture)
        {
            HiddenTrialPresentation.Phase="aux-"+name+"-"+Settings.NativeMode+"-"+Settings.TextScale;
            if(!window.IsLoaded)HiddenTrialPresentation.Prepare(window);
            window.Show();await Settle();window.UpdateLayout();
            double normal=window.Width,normalHeight=window.Height;
            if(Settings.NativeTheme&&Settings.NativeMode=="dark"&&Settings.TextScale==1.5)
            {
                HiddenTrialPresentation.AllowNativelyHiddenMonitorMoves(window);
                var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var dpis=new HashSet<uint>();
                foreach(var display in System.Windows.Forms.Screen.AllScreens)
                {
                    var area=display.WorkingArea;
                    check(TaskbarSearchHost.Native.SetWindowPos(handle,IntPtr.Zero,area.Left+40,area.Top+40,Math.Min(1600,area.Width-80),Math.Min(1000,area.Height-80),0x0094),"The actual auxiliary HWND follows its monitor transition: "+name);
                    await Settle();window.UpdateLayout();dpis.Add(TaskbarSearchHost.Native.GetDpiForWindow(handle));
                    check(HiddenTrialPresentation.IsNativelyHidden(window),"DPI qualification leaves the native auxiliary HWND hidden: "+name);
                }
                check(dpis.Contains(96)&&dpis.Contains(144),"Actual auxiliary HWND qualifies96/144-DPI transitions: "+name);
                window.Width=normal;window.Height=normalHeight;window.Show();await Settle();
            }
            foreach(bool narrow in window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip?new[]{false,true}:new[]{false})
            {
                window.Width=narrow?window.MinWidth:normal;await Settle();window.UpdateLayout();
                string label=$"aux-{name}-{(Settings.NativeTheme?Settings.NativeMode:"original")}-{Settings.TextScale*100:0}-{(narrow?"narrow":"normal")}";
                check(window.Content is FrameworkElement body&&body.ActualWidth>0&&body.ActualHeight>0,"The actual auxiliary surface lays out at its supported viewport: "+label);
                check(window.Style==FindResource("AuxiliaryWindow"),"The owned window uses the shared typography/frame contract: "+label);
                foreach(var button in PreviewChildren<Button>(window).Where(b=>b.IsVisible&&b.IsEnabled))
                {
                    // Vertically scrolled-off navigation/content is intentional.
                    // Commands outside scroll viewers must fit their window.
                    var parent=VisualTreeHelper.GetParent(button);bool scrolling=false;
                    while(parent!=null&&parent!=window){if(parent is ScrollViewer){scrolling=true;break;}parent=VisualTreeHelper.GetParent(parent);}
                    if(scrolling)continue;
                    var position=button.TranslatePoint(new Point(),window);
                    check(button.ActualWidth>0&&button.ActualHeight>0&&position.X>=-1&&position.Y>=-1&&position.X+button.ActualWidth<=window.ActualWidth+1&&position.Y+button.ActualHeight<=window.ActualHeight+1,"Visible auxiliary actions remain in bounds: "+label+" "+(AutomationProperties.GetName(button) is {Length:>0} accessible?accessible:button.Content));
                }
                measurements.Add(new{surface=name,label,width=window.ActualWidth,height=window.ActualHeight,window.MinWidth,window.MinHeight});
                if(capture)await S1Verification.Snapshot(window,root,label);
            }
            window.Width=normal;await Settle();
            check(HiddenTrialPresentation.ForegroundViolations==0,"This auxiliary surface did not activate a host window: "+HiddenTrialPresentation.Phase+" "+JsonSerializer.Serialize(HiddenTrialPresentation.ForegroundEvents));
        }
        try
        {
            string fixture=Directory.CreateDirectory(Path.Combine(root,"auxiliary folder é 子")).FullName;
            string input=Path.Combine(fixture,"rename-me.txt");File.WriteAllText(input,"A real auxiliary-window fixture.");
            var entries=new List<Entry>{new(){Name="rename-me.txt",Path=input,Size=new FileInfo(input).Length}};
            foreach(var theme in new[]{("dark",true),("light",true),("original",false)})foreach(double scale in new[]{1d,1.5})
            {
                Settings.NativeTheme=theme.Item2;Settings.NativeMode=theme.Item1;Settings.TextScale=scale;ApplyAppearance();
                var settings=new SettingsWindow(this);
                try
                {
                    await Render(settings,"settings",true);
                    var categories=(ListBox)settings.FindName("SettingsCategories");
                    categories.SelectedIndex=categories.Items.Count-1;await Settle();
                    check(((Border)((ListBoxItem)categories.SelectedItem).Tag).IsVisible,"Settings still switches to the actual About category: "+theme+scale);
                }finally{settings.Close();}
                var help=new HelpWindow(this);
                try
                {
                    await Render(help,"help",true);
                    var scroll=(ScrollViewer)help.FindName("Scroller");scroll.ScrollToBottom();await Settle();
                    var topics=(StackPanel)help.FindName("Topics");
                    check(topics.Children.OfType<Button>().Last().Tag as string=="on"&&topics.Children.OfType<Button>().Last().FontWeight==FontWeights.Bold,"Help's actual scroll still highlights and bolds the section being read: "+theme+scale);
                    ((TextBox)help.FindName("Find")).Text="Ctrl+T";await Settle();
                    check(((StackPanel)help.FindName("Sections")).Children.OfType<Grid>().Any(),"Help filtering still renders matching real shortcut rows: "+theme+scale);
                }finally{help.Close();}
                var notes=new WhatsNewWindow(this);
                try{await Render(notes,"notes",true);check(PreviewChildren<Button>(notes).Any(b=>b.Tag as string=="on"),"What's new retains its actual selected release: "+theme+scale);}
                finally{notes.Close();}
                var update=new UpdateDialog(this);
                try
                {
                    update.ShowCheckResult(false,$"Pathweft {Updater.Current} is the latest version.");
                    await Render(update,"update",true);
                    check(update.CheckState=="current"&&((TextBlock)update.FindName("Heading")).Text.Contains(Updater.Current.ToString()),"The actual checker exposes installed-version confirmation: "+theme+scale);
                    update.ShowRelease(new(new Version(999,14,0),"v999.14.0","Verification release notes.","",1024,""));
                    update.BeginDownload();update.ReportDownload(new("Downloading",256,1024));await Settle();
                    check(((ProgressBar)update.FindName("Progress")).Value==.25&&((TextBlock)update.FindName("Status")).Text.Contains("25%"),"The same themed surface retains measured download progress: "+theme+scale);
                    update.ReportDownload(new("Checking update",0,0));check(((ProgressBar)update.FindName("Progress")).IsIndeterminate,"Hash verification retains its explicit indeterminate phase: "+theme+scale);
                }finally{update.Close();}
                var prompt=(Window)Activator.CreateInstance(typeof(PromptDialog),fields,null,new object[]{this,"Rename — Pathweft","Name","rename-me.txt","Rename",false},null);
                try{await Render(prompt,"prompt",scale==1.5);}
                finally{prompt.Close();}
                var rename=(Window)Activator.CreateInstance(typeof(MultiRenameDialog),fields,null,new object[]{this,entries},null);
                try{await Render(rename,"multiple-rename",scale==1.5);}
                finally{rename.Close();}
                var create=new NewFileDialog(this);
                try{await Render(create,"new-file",scale==1.5);}
                finally{create.Close();}
                var duplicates=new DuplicatesWindow(this,new[]{fixture});
                try
                {
                    HiddenTrialPresentation.Prepare(duplicates);duplicates.Show();await Settle();
                    var search=(Button)duplicates.FindName("SearchButton");
                    if(search.Content as string=="Stop")
                    {
                        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var descriptor=DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty,typeof(Button));
                        EventHandler changed=(_,_)=>{if(search.Content as string!="Stop")done.TrySetResult();};
                        descriptor.AddValueChanged(search,changed);
                        try{changed(null,EventArgs.Empty);await done.Task;}finally{descriptor.RemoveValueChanged(search,changed);}
                    }
                    await Render(duplicates,"duplicates",scale==1.5);
                }finally{duplicates.Close();}
                var widget=new SearchWidgetWindow(this);
                try
                {
                    widget.ApplyTextScale();await Render(widget,"search",true);
                    var field=(Border)widget.Pane.FindName("AddressField");
                    check(field.CornerRadius.TopLeft==6&&field.MinHeight==34,"Search uses the same field radius/compact height as other auxiliary inputs: "+theme+scale);
                }finally{widget.Shutdown();}
                // Read-only enumeration; no restore/delete command is invoked and
                // user recycle-bin names are not retained in screenshots.
                var recycle=new RecycleBinWindow(this,()=>{});
                try{await Render(recycle,"recycle",false);}finally{recycle.Close();}

                string destination=Directory.CreateDirectory(Path.Combine(fixture,$"copy-{theme.Item1}-{scale}")).FullName;
                // Recreate the actual completed transfer surface at each scale;
                // the normal close/hide/reuse path is verified before disposal.
                if(transfersWindow!=null)
                {
                    bool wasQuitting=quitting;quitting=true;
                    try{transfersWindow.Close();transfersWindow=null;}finally{quitting=wasQuitting;}
                }
                var job=Submit(new(){input},destination,false);
                await job.Completion;UpdateTransfers();
                check(job.Successful&&File.ReadAllText(Path.Combine(destination,"rename-me.txt"))==File.ReadAllText(input),"The actual transfer retains its file-operation owner and completes the copy");
                await Render(transfersWindow,"transfers",scale==1.5);
                var transfer=transfersWindow;transfer.Close();
                check(!transfer.IsVisible&&ReferenceEquals(transfer,transfersWindow)&&job.Completion.IsCompletedSuccessfully,"Closing Transfers hides its reusable surface without cancelling the completed operation");
                jobs.Remove(job);

                EditorWindow.Open(input);
                try
                {
                    await EditorWindow.AppearanceReadyForVerification;
                    var editor=Application.Current.Windows.OfType<EditorWindow>().Single();
                    await Render(editor,"editor",false);
                    await EditorWindow.CaptureForVerification(Path.Combine(root,$"aux-editor-{theme.Item1}-{scale*100:0}"),theme.Item2&&theme.Item1=="light");
                }finally{await EditorWindow.CloseForVerification();}
            }
            // Exercise the actual modal choice path without desktop input or a
            // destructive operation; the existing debug presentation is hidden.
            bool choiceObserved=false;
            var inspectChoice=Dispatcher.InvokeAsync(async()=>
            {
                var dialog=Application.Current.Windows.Cast<Window>().Single(w=>w.Title=="Pathweft"&&w!=this);
                await S1Verification.Snapshot(dialog,root,"aux-choice");
                var cancel=PreviewChildren<Button>(dialog).Single(b=>b.Content as string=="Cancel");
                choiceObserved=cancel.Style==FindResource("AuxiliaryAction");cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },DispatcherPriority.ApplicationIdle);
            int decision=PromptDialog.Choose(this,"Confirm operation","Verification only; no file action is dispatched.","Cancel","Continue");
            await inspectChoice.Task.Unwrap();check(choiceObserved&&decision==0,"The real modal question renders shared actions and returns its actual cancellation choice");
            File.WriteAllText(Path.Combine(root,"auxiliary-window-metrics.json"),JsonSerializer.Serialize(measurements));
            await VerifyUpdateChecks14(root,check);
            await VerifyDownload114(root,check);
            string[] arguments=Updater.InstallationArguments(123,456,@"D:\Tools\Pathweft\");
            check(arguments.Contains("/WAITPID=123")&&arguments.Contains("/WAITCREATED=00000000000001C8")&&arguments.Contains("/RESTARTAPP=1")&&arguments.Any(value=>value.StartsWith("/UPDATEACCENT=")),"The actual deployment handoff preserves process identity, restart intent and the selected palette");
            check(HiddenTrialPresentation.ForegroundViolations==0,"Auxiliary window/check/retry rendering did not activate a host-desktop window");
        }
        finally
        {
            File.WriteAllText(Path.Combine(root,"auxiliary-activation.json"),JsonSerializer.Serialize(new{events=HiddenTrialPresentation.ForegroundEvents,messages=HiddenTrialPresentation.ActivationMessages}));
            updateDialog?.Close();Updater.Feed=oldFeed;
            (Settings.NativeTheme,Settings.NativeMode,Settings.TextScale)=saved;ApplyAppearance();
        }
    }

    async Task VerifyUpdateChecks14(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;
            UpdateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var work=updateCheck;var surface=updateDialog;
            check(work!=null&&!work.IsCompleted&&surface is{IsVisible:true,CheckState:"checking"},"Clicking the actual sidebar check immediately shows Checking before the held HTTP response");
            await feed.RequestSeen.Task;
            var settings=new SettingsWindow(this);HiddenTrialPresentation.Prepare(settings);settings.Show();await Settle();
            try
            {
                ((ListBox)settings.FindName("SettingsCategories")).SelectedIndex=6;
                ((Button)settings.FindName("UpdateCheck")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(ReferenceEquals(work,updateCheck)&&ReferenceEquals(surface,updateDialog),"Actual Settings and sidebar actions share the original HTTP task and visible surface");
                feed.Respond(200,Updater.Current);await work;await feed.Completion;await Settle();
                check(surface.CheckState=="current"&&((TextBlock)surface.FindName("Heading")).Text.Contains(Updater.Current.ToString()),"A real current-version HTTP result displays confirmation in the same themed window");
                await S1Verification.Snapshot(surface,root,"update-check-current");
            }finally{settings.Close();surface.Close();}
        }
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;var automatic=CheckForUpdate(false);await feed.RequestSeen.Task;
            check(updateDialog==null,"An automatic in-flight check stays quiet");
            var manual=CheckForUpdate(true);check(ReferenceEquals(automatic,manual)&&updateDialog?.CheckState=="checking","A manual caller joining the actual automatic HTTP request immediately gets its visible state");
            feed.Respond(200,Updater.Current);await manual;await feed.Completion;
            check(updateDialog?.CheckState=="current","The joined manual caller receives a visible current-version result");updateDialog.Close();
        }
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;var work=CheckForUpdate(true);await feed.RequestSeen.Task;feed.Respond(503,null);await work;await feed.Completion;
            check(updateDialog?.CheckState=="failed"&&((Button)updateDialog.FindName("UpdateButton")) is{IsEnabled:true,Content:"Retry"},"A real HTTP503 is a visible failure with explicit Retry, never an up-to-date result");
            await S1Verification.Snapshot(updateDialog,root,"update-check-failed");
        }
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;var surface=updateDialog;
            // HiddenTrial deliberately clears native ownership to prevent owner
            // activation. Restore the real owner for this routed retry action;
            // both HWNDs retain the test's native no-activation protection.
            surface.Owner=this;
            ((Button)surface.FindName("UpdateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var work=updateCheck;
            check(surface.CheckState=="checking"&&work!=null,"The real Retry action starts a new observable check in the same window");
            await feed.RequestSeen.Task;feed.Respond(200,new Version(999,14,0));await work;await feed.Completion;
            check(ReferenceEquals(surface,updateDialog)&&surface.CheckState=="available"&&surface.OfferedRelease.Version==new Version(999,14,0)&&((Button)surface.FindName("UpdateButton")).Content as string=="Update now","The actual newer-release response exposes its version, notes and update action in the same surface");
            await S1Verification.Snapshot(surface,root,"update-check-available");surface.Close();
        }
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;var work=CheckForUpdate(false);await feed.RequestSeen.Task;feed.Respond(200,new Version(999,14,0));await work;await feed.Completion;
            check(updateDialog==null,"The actual automatic response respects a dismissed offer without another popup");
        }
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;var work=CheckForUpdate(true);await feed.RequestSeen.Task;updateDialog.Close();
            feed.Respond(200,Updater.Current);await work;await feed.Completion;
            check(updateDialog==null,"Closing a checker does not resurrect a result window after its actual HTTP completion");
        }
        await using(var feed=new UpdateFeed14())
        {
            Updater.Feed=feed.Url;var work=CheckForUpdate(false);await feed.RequestSeen.Task;
            await StopUpdateChecks();check(work.IsCompletedSuccessfully&&updateCheck==null&&updateDialog==null,"Normal shutdown cancellation observes the original pending HTTP check and drains its managed task");
            feed.Respond(200,Updater.Current);await feed.Completion;
        }
    }
    sealed class UpdateFeed14:IAsyncDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);
        readonly CancellationTokenSource cancellation=new();
        readonly TaskCompletionSource<(int status,Version version)> response=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RequestSeen=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Completion{get;}
        internal string Url{get;}
        internal UpdateFeed14(){listener.Start();Url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/latest";Completion=Serve();}
        internal void Respond(int status,Version version)=>response.TrySetResult((status,version));
        async Task Serve()
        {
            try
            {
                using var client=await listener.AcceptTcpClientAsync(cancellation.Token);using var stream=client.GetStream();
                using var request=new MemoryStream();var one=new byte[1];
                while(await stream.ReadAsync(one,cancellation.Token)>0)
                {
                    request.WriteByte(one[0]);
                    if(request.Length>=4&&Encoding.ASCII.GetString(request.GetBuffer(),(int)request.Length-4,4)=="\r\n\r\n")break;
                }
                RequestSeen.TrySetResult();var reply=await response.Task.WaitAsync(cancellation.Token);
                string body=JsonSerializer.Serialize(new{tag_name="v"+reply.version,body="Actual HTTP fixture release notes.",assets=new[]{new{name="Pathweft-setup.exe",browser_download_url=Url.Replace("/latest","/setup.exe"),size=1024,digest="sha256:"+new string('0',64)}}});
                byte[] bytes=Encoding.UTF8.GetBytes(body),header=Encoding.ASCII.GetBytes($"HTTP/1.1 {reply.status} {(reply.status==200?"OK":"Service Unavailable")}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header,cancellation.Token);await stream.WriteAsync(bytes,cancellation.Token);
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
            catch(IOException)when(cancellation.IsCancellationRequested||response.Task.IsCompleted){}
        }
        public async ValueTask DisposeAsync(){cancellation.Cancel();listener.Stop();await Completion;cancellation.Dispose();}
    }
}
#endif
