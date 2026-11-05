#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifySpace15(string root,Action<bool,string> check)
    {
        var measurements=new List<object>();
        string folder=Directory.CreateDirectory(Path.Combine(root,"space tree é 子")).FullName;
        long expected=0;
        await Task.Run(()=>
        {
            for(int directory=0;directory<80;directory++)
            {
                string branch=Directory.CreateDirectory(Path.Combine(folder,$"branch-{directory:000}","nested")).FullName;
                for(int file=0;file<64;file++)
                {using var stream=File.Create(Path.Combine(branch,$"item-{file:000}.bin"));long bytes=(directory+1)*100+file;stream.SetLength(bytes);expected+=bytes;}
            }
            File.WriteAllText(Path.Combine(folder,"root.txt"),"root");expected+=4;
        });
        await S1Verification.Navigate(Left,folder);await S1Verification.Navigate(Right,folder);
        await VerifySpaceBoundaries15(root,check);
        await Sizes(Left,()=>Left.Items.All(e=>e.Size>=0));await Left.StopSizingAsync();
        SetActive(Left);Left.SetSort("Name",false);
        Left.ListForVerification.SelectedItem=Left.Items.First(entry=>entry.Name=="branch-010");
        var previousColumns=((GridView)Left.ListForVerification.View).Columns.ToArray();
        var previousWidths=previousColumns.Select(column=>column.Width).ToArray();
        string previousSelection=Left.Selected.Single().Path;
        var oldSettings=Settings.ColumnWidths.ToDictionary(pair=>pair.Key,pair=>pair.Value);
        int progress=0;void Progress(){if(Left.SpaceAnalyzing&&Left.SizesPending&&Left.FolderTotal>0)progress++;}
        Left.StatsChanged+=Progress;
        var watch=Stopwatch.StartNew();long before=GC.GetTotalMemory(false);
        AnalyzeSpaceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        // F5 is the actual metadata-refresh route, independent of this machine's
        // optional indexed coverage; the separate live-index scenario qualifies
        // the fast entry path and its explicit coverage label.
        Left.Refresh(force:true);await Left.SpaceCompletion;
        watch.Stop();Left.StatsChanged-=Progress;
        check(Left.SpaceAnalyzing&&Left.Dir==folder&&Left.FolderTotal==expected,"Actual Analyze space command morphs the current pane and reaches exact recursive logical totals");
        check(Left.SpaceSummary.Folders==160&&Left.SpaceSummary.Files==5121,"Root summary does not double-count expanded ancestors or visible rows");
        check(progress>0,"The rendered analysis publishes useful recursive totals before worker completion");
        check(Left.ListForVerification.Items.OfType<SpaceEntry>().Select(row=>row.DisplaySize).SequenceEqual(Left.ListForVerification.Items.OfType<SpaceEntry>().Select(row=>row.DisplaySize).OrderDescending()),"Root siblings are largest first");
        check(Left.Items.Count()==81,"The collapsed analysis does not retain a visible object for every nested file");
        measurements.Add(new{fixture="5121 files,160 directories",bytes=expected,milliseconds=watch.Elapsed.TotalMilliseconds,managedBytesBefore=before,managedBytesAfter=GC.GetTotalMemory(false),hotCacheRecords=SizeCache.HotCount,visibleRows=Left.Items.Count()});
        await CompareDiskweft15(root,folder,expected,measurements,check);
        // Invoke the actual row expander routed event and await its owned read.
        var branch=(SpaceEntry)Left.Items.First(entry=>entry.Name=="branch-079");
        Left.ListForVerification.SelectedItem=branch;Left.ListForVerification.ScrollIntoView(branch);UpdateLayout();
        var expand=PreviewChildren<Button>(Left.ListForVerification).First(button=>ReferenceEquals(button.DataContext,branch)&&AutomationProperties.GetName(button)=="Expand or collapse folder");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Left.SpaceCompletion;
        var nested=(SpaceEntry)Left.Items.First(entry=>entry.Path==Path.Combine(branch.Path,"nested"));
        Left.ListForVerification.ScrollIntoView(nested);UpdateLayout();
        expand=PreviewChildren<Button>(Left.ListForVerification).First(button=>ReferenceEquals(button.DataContext,nested)&&AutomationProperties.GetName(button)=="Expand or collapse folder");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Left.SpaceCompletion;
        check(Left.Items.Count(entry=>!entry.IsDir)==65&&ReferenceEquals(Left.Selected.Single(),branch)&&Left.FolderTotal==expected,"Nested expansion uses real metadata and preserves selection and the nonduplicated root total");
        Left.ListForVerification.SelectedItem=nested;
        foreach(var key in new[]{System.Windows.Input.Key.Left,System.Windows.Input.Key.Right})
        {
            var input=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(Left.ListForVerification),Environment.TickCount,key){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};
            Left.ListForVerification.RaiseEvent(input);await Left.SpaceCompletion;
            check(input.Handled&&nested.Expanded==(key==System.Windows.Input.Key.Right),"Real routed tree arrow keys collapse and expand the selected folder: "+key);
        }
        foreach(var theme in new[]{(true,"dark"),(true,"light"),(false,"dark")})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=theme.Item1;Settings.NativeMode=theme.Item2;Settings.TextScale=scale;ApplyAppearance();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
            check(Left.ListForVerification.View is GridView grid&&grid.Columns.Count==4&&AnalyzeSpaceCommand.ActualWidth<=30,"The analysis uses compact native/theme/scale layout: "+theme+scale);
            if(scale==1.0||theme.Item1&&theme.Item2=="dark")
            {
                string path=Path.Combine(root,$"space-{(theme.Item1?theme.Item2:"original")}-{scale*100:0}.png");
                var body=(FrameworkElement)Content;var bitmap=new RenderTargetBitmap((int)body.ActualWidth,(int)body.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(body);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);
            }
        }
        Settings.NativeTheme=true;Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();
        AnalyzeSpaceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Left.DrainSpaceAndSizesAsync();
        check(!Left.SpaceAnalyzing&&Left.Dir==folder&&Left.SortKey=="Name"&&!Left.SortDescending&&Left.Selected.Single().Path==previousSelection,"Toggle off restores the exact folder/sort/selection");
        check(((GridView)Left.ListForVerification.View).Columns.SequenceEqual(previousColumns)&&previousColumns.Select(column=>column.Width).SequenceEqual(previousWidths)&&oldSettings.All(pair=>Settings.ColumnWidths.GetValueOrDefault(pair.Key)==pair.Value),"Temporary tree columns do not change normal column identities, order, widths or persisted settings");
        foreach(var mode in new[]{PaneView.ViewMode.List,PaneView.ViewMode.IconsM,PaneView.ViewMode.Details})
        {
            Left.SetView(mode);await Left.ToggleSpaceAsync();await Left.SpaceCompletion;await Left.EndSpaceAsync();
            check(Left.Mode==mode&&!Left.SpaceAnalyzing&&Left.Dir==folder,"Analysis restores the actual standard view mode: "+mode);
        }
        // Actual Everything IPC, no live settings changes. A disabled index is
        // an observed unavailable capability, never reported as zero/complete.
        var client=Search.EverythingSearchClient.Shared;
        var page=await client.QueryAsync(Search.EverythingSearchClient.FolderExpression(new[]{@"C:\Windows"}),"",0,2,"Name",false,CancellationToken.None);
        var indexed=await client.FolderSizesAsync(new[]{@"C:\Windows",Path.Combine(folder,"not indexed and absent")},"",CancellationToken.None);
        check(page.Hits.Any(hit=>hit.Path.Equals(@"C:\Windows",StringComparison.OrdinalIgnoreCase)),"The real existing Everything index returns an exact known folder using the production size-query expression");
        check(!indexed.ContainsKey(Path.Combine(folder,"not indexed and absent")),"An absent index result never turns into a zero-byte folder");
        if(page.FolderSizesIndexed)check(indexed.TryGetValue(@"C:\Windows",out long indexedBytes)&&indexedBytes>=0,"Production folder-size query retrieves the real enabled indexed total");
        else check(indexed.Count==0,"Disabled folder-size indexing is detected and leaves actual traversal available");
        measurements.Add(new{engine=page.Engine,folderSizesIndexed=page.FolderSizesIndexed,knownFolderSize=indexed.GetValueOrDefault(@"C:\Windows",-1)});
        // Successful completed results persist; invalidated/cancelled snapshots
        // cannot overwrite them, including after a branch rename/removal.
        string cached=Path.Combine(folder,"branch-000");DateTime stamp=Directory.GetLastWriteTimeUtc(cached);
        SizeCache.Set(cached,stamp,999);using(var obsolete=new SizeCache.Measurement(cached))
        {SizeCache.Invalidate(Path.Combine(cached,"nested","item-000.bin"));check(!obsolete.Complete(stamp,123),"SQLite invalidation rejects an in-flight obsolete publication");}
        for(int i=0;i<SizeCache.HotLimit*2;i++)SizeCache.Set(Path.Combine(root,"cache-contract",i.ToString()),stamp,i);
        check(SizeCache.HotCount==SizeCache.HotLimit&&SizeCache.Get(Path.Combine(root,"cache-contract","0"),stamp)==0,"Evicted hot records remain available from the database while RAM stays bounded");
        long allocated=GC.GetAllocatedBytesForCurrentThread();watch.Restart();SizeCache.Invalidate(Path.Combine(root,"cache-contract","100"),true);watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(allocated<1_000_000,"One targeted invalidation avoids the previous whole-map allocation");
        measurements.Add(new{invalidationMilliseconds=watch.Elapsed.TotalMilliseconds,allocatedBytes=allocated,hotCacheRecords=SizeCache.HotCount});
        // Two independent panes and navigation cancellation, with the ordinary
        // mutation/preview owners untouched.
        await S1Verification.Navigate(Right,folder);
        await Left.ToggleSpaceAsync();await Right.ToggleSpaceAsync();await Task.WhenAll(Left.SpaceCompletion,Right.SpaceCompletion);
        File.WriteAllText(Path.Combine(root,"space-two-pane.json"),JsonSerializer.Serialize(new{expected,left=new{Left.Dir,Left.SpaceAnalyzing,total=Left.FolderTotal,state=Left.SpaceAnalyzing?Left.SpaceSummary.Status:"standard"},right=new{Right.Dir,Right.SpaceAnalyzing,total=Right.FolderTotal,state=Right.SpaceAnalyzing?Right.SpaceSummary.Status:"standard"}},new JsonSerializerOptions{WriteIndented=true}));
        check(Left.SpaceAnalyzing&&Right.SpaceAnalyzing&&Left.FolderTotal==expected&&Right.FolderTotal==expected,"Two panes own independent real scan roots/sessions");
        for(int rewrite=1;rewrite<=4;rewrite++)
        {
            long newTotal=expected+rewrite;
            var leftChanged=Sizes(Left,()=>Left.SpaceAnalyzing&&Left.FolderTotal==newTotal&&!Left.SizesPending);
            var rightChanged=Sizes(Right,()=>Right.SpaceAnalyzing&&Right.FolderTotal==newTotal&&!Right.SizesPending);
            await Task.Run(()=>File.AppendAllText(Path.Combine(folder,"root.txt"),"x"));await Task.WhenAll(leftChanged,rightChanged);
        }
        check(Left.FolderTotal==expected+4&&Right.FolderTotal==expected+4,"Repeated external writes refresh both owned trees to exact new totals");
        string target=Directory.CreateDirectory(Path.Combine(root,"copy-target")).FullName;
        await S1Verification.Navigate(Right,target);SetActive(Left);
        Left.ListForVerification.SelectedItem=Left.Items.Single(entry=>entry.Name=="root.txt");
        CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var copy=jobs.First();await copy.Completion;
        check(File.ReadAllText(Path.Combine(target,"root.txt"))=="rootxxxx"&&Left.SpaceAnalyzing,"Analysis selection uses the existing real Ferry copy owner");
        await S1Verification.Navigate(Right,folder);await Right.ToggleSpaceAsync();await Right.SpaceCompletion;
        Left.Navigate(Path.Combine(folder,"branch-001"));await Left.DrainSpaceAndSizesAsync();
        check(!Left.SpaceAnalyzing&&Right.SpaceAnalyzing,"Navigation drains only its original analysis and preserves the other pane");
        await Right.EndSpaceAsync();await Task.WhenAll(Left.StopSizingAsync(),Right.StopSizingAsync());
        await S1Verification.Navigate(Left,folder);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool stopped=false;int once=0;
        SpaceScan.BeforeEnumeration=(path,token)=>{if(path==folder&&Interlocked.Exchange(ref once,1)==0){entered.TrySetResult();token.WaitHandle.WaitOne();stopped=true;}};
        try
        {
            await Left.ToggleSpaceAsync();await entered.Task;
            Left.Navigate(Path.Combine(folder,"branch-002"));await Left.DrainSpaceAndSizesAsync();
            check(stopped&&!Left.SpaceAnalyzing&&Left.Dir==Path.Combine(folder,"branch-002"),"Navigation waits for the actual held worker's cancellation signal and exit before restoring/leaving analysis");
        }
        finally{SpaceScan.BeforeEnumeration=null;}
        string archivePath=Path.Combine(root,"analysis-boundary.zip");
        using(var archive=System.IO.Compression.ZipFile.Open(archivePath,System.IO.Compression.ZipArchiveMode.Create))
        {using var writer=new StreamWriter(archive.CreateEntry("nested/item.txt").Open());writer.Write("archive-owned bytes");}
        foreach(string location in new[]{PaneView.ThisPC,archivePath})
        {
            await S1Verification.Navigate(Right,location);await Right.SizingCompletion;var originalSizing=Right.SizingCompletion;
            await Left.ToggleSpaceAsync();await Left.SpaceCompletion;await Left.EndSpaceAsync();
            check(ReferenceEquals(originalSizing,Right.SizingCompletion),"Analysis exit does not start filesystem sizing for the other pane's virtual owner: "+(location==PaneView.ThisPC?"This PC":"archive"));
        }
        File.WriteAllText(Path.Combine(root,"space-metrics.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
        check(HiddenTrialPresentation.ForegroundViolations==0,"Space analysis did not activate the host desktop");
    }

    static async Task VerifySpaceBoundaries15(string root,Action<bool,string> check)
    {
        string fixture=Directory.CreateDirectory(Path.Combine(root,"scan-boundaries")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"link-target")).FullName;File.WriteAllBytes(Path.Combine(target,"outside.bin"),new byte[1000]);
        string link=Path.Combine(fixture,"junction");
        var start=new ProcessStartInfo("cmd.exe","/d /c mklink /J \""+link+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var process=Process.Start(start)){await process.WaitForExitAsync();check(process.ExitCode==0,"The test creates only its owned directory junction without administrator changes");}
        string longPath=fixture;for(int i=0;i<24;i++)longPath=Path.Combine(longPath,"long é 子 segment"+i);Directory.CreateDirectory(longPath);File.WriteAllBytes(Path.Combine(longPath,"end.bin"),new byte[23]);
        var updates=new System.Collections.Concurrent.ConcurrentDictionary<string,SpaceUpdate>(StringComparer.OrdinalIgnoreCase);
        await SpaceScan.RunAsync(fixture,false,"",update=>updates[update.Path]=update,CancellationToken.None);
        File.WriteAllText(Path.Combine(root,"space-boundaries.json"),JsonSerializer.Serialize(updates.Values,new JsonSerializerOptions{WriteIndented=true}));
        check(updates[fixture].State==SpaceState.Complete&&updates[fixture].Bytes==23&&updates[link].State==SpaceState.Reparse,"Real Unicode/long-path traversal skips junction contents and measures exact logical lengths");
        string denied=Directory.CreateDirectory(Path.Combine(fixture,"denied")).FullName;File.WriteAllBytes(Path.Combine(denied,"hidden.bin"),new byte[99]);
        var info=new DirectoryInfo(denied);var security=System.IO.FileSystemAclExtensions.GetAccessControl(info,System.Security.AccessControl.AccessControlSections.Access);
        var original=new System.Security.AccessControl.DirectorySecurity();original.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm(),System.Security.AccessControl.AccessControlSections.Access);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User,System.Security.AccessControl.FileSystemRights.ListDirectory,System.Security.AccessControl.AccessControlType.Deny));
        try
        {
            System.IO.FileSystemAclExtensions.SetAccessControl(info,security);updates.Clear();
            await SpaceScan.RunAsync(fixture,false,"",update=>updates[update.Path]=update,CancellationToken.None);
            File.WriteAllText(Path.Combine(root,"space-denied.json"),JsonSerializer.Serialize(new{root=updates[fixture],denied=updates[denied]},new JsonSerializerOptions{WriteIndented=true}));
            check(updates[denied].State==SpaceState.Denied&&updates[fixture].State==SpaceState.Incomplete&&SizeCache.Get(fixture,Directory.GetLastWriteTimeUtc(fixture))<0,"A real denied child marks ancestors incomplete and cannot promote a complete cached total");
        }
        finally{System.IO.FileSystemAclExtensions.SetAccessControl(info,original);}
        using var cancel=new CancellationTokenSource();bool cancelled=false;
        try{await SpaceScan.RunAsync(fixture,false,"",update=>{updates[update.Path]=update;if(update.Path==fixture&&update.State==SpaceState.Scanning)cancel.Cancel();},cancel.Token);}
        catch(OperationCanceledException)when(cancel.IsCancellationRequested){cancelled=true;}
        check(cancelled&&updates[fixture].State==SpaceState.Cancelled,"Actual traversal cancellation reaches a terminal cancelled state");
    }
    static async Task CompareDiskweft15(string root,string fixture,long expected,List<object> measurements,Action<bool,string> check)
    {
        const string donor=@"C:\Program Files (x86)\Darkaxt\Diskweft\Diskweft.exe";
        string output=Path.Combine(root,"donor.csv");
        var start=new ProcessStartInfo(donor){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in new[]{fixture,"--csv",output})start.ArgumentList.Add(argument);
        start.Environment["DISKWEFT_TEST_ROOT"]=Path.Combine(root,"donor-profile");start.Environment["SPACESCAN_TEST_ROOT"]=Path.Combine(root,"donor-profile");start.Environment["TEMP"]=Path.Combine(root,"temp");start.Environment["TMP"]=start.Environment["TEMP"];
        var clock=Stopwatch.StartNew();using var process=Process.Start(start);
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        long observedPeak=0;using var sampling=new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        var exited=process.WaitForExitAsync();
        while(!exited.IsCompleted)
        {process.Refresh();try{observedPeak=Math.Max(observedPeak,process.WorkingSet64);}catch(InvalidOperationException){}if(await Task.WhenAny(exited,sampling.WaitForNextTickAsync().AsTask())==exited)break;}
        await exited;clock.Stop();File.WriteAllText(Path.Combine(root,"donor-stdout.txt"),await stdout);File.WriteAllText(Path.Combine(root,"donor-stderr.txt"),await stderr);
        check(process.ExitCode==0&&File.ReadLines(output).Skip(1).First().Split(';')[1]==expected.ToString(),"The actual installed donor CLI reaches the same terminal byte total on the identical warm filesystem fixture");
        measurements.Add(new{donorVersion=FileVersionInfo.GetVersionInfo(donor).FileVersion,donorHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(donor))),milliseconds=clock.Elapsed.TotalMilliseconds,observedPeakWorkingSet=observedPeak,sampling="20ms observation; not a guaranteed instantaneous process peak",sameFixtureBytes=expected});
    }
}
#endif
