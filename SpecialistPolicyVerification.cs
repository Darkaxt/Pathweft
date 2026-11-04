#if DEBUG
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifySpecialistPolicies13(string root,Action<bool,string> check)
    {
        string[] specialists={"csv","database","mail","certificate","app","design","mediainfo","binary","elf","dump","prefetch","chm","clsid","office"};
        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var window=new Window{Content=body,Width=640,Height=700,ShowInTaskbar=false};HiddenTrialPresentation.Prepare(window);window.Show();
        using var session=new PreviewSession(this,body,_=>{});
        var disabled=Settings.DisabledPreviewModules.ToArray();bool native=Settings.NativeTheme;string mode=Settings.NativeMode;
        try
        {
            var samples=new[]{("csv","tab.tsv"),("database","records.sqlite"),("mail","message.eml"),("certificate","public.cer"),("app","sample.apk"),("design","design.kra"),("mediainfo","audio.wav"),("binary","bytes.bin"),("elf","hello.elf"),("dump","crash.dmp.released"),("prefetch","windows10.pf"),("chm","7-zip.chm"),("office","document.docx.released")};
            string zip=Path.Combine(root,"specialists.zip");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
                foreach(var (id,file) in samples)archive.CreateEntryFromFile(Path.Combine(root,file),file.Replace(".released",""));
            foreach(var (id,file) in samples)
            {
                string entry=file.Replace(".released","");
                session.Show(new Entry{Path=zip+"\\"+entry,Name=entry},id=="mediainfo"?id:null);await session.Completion;
                check(session.State=="ready"&&session.ModuleId==id,"Actual archive entry uses the integrated specialist: "+id);
                int initializations=session.Initializations.GetValueOrDefault(id);
                Settings.DisabledPreviewModules.Add(id);session.Refresh();await session.Completion;await PreviewSession.DrainAsync();
                check(session.ModuleId!=id&&session.Initializations.GetValueOrDefault(id)==initializations,"Disabling an active specialist releases it and excludes construction: "+id);
                Settings.DisabledPreviewModules.Remove(id);session.Clear();await PreviewSession.DrainAsync();
            }
            Settings.DisabledPreviewModules.UnionWith(specialists);Settings.Save();
            string receipt=Path.Combine(Settings.Profile,"specialist-settings.json");
            var start=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            start.ArgumentList.Add("--verify-specialist-settings="+receipt);
            using(var process=Process.Start(start)){await process.WaitForExitAsync();check(process.ExitCode==0,"Fresh independent process reads the specialist settings profile");}
            var persisted=JsonSerializer.Deserialize<string[]>(File.ReadAllText(receipt));
            check(specialists.All(persisted.Contains),"All fourteen module toggles persist across an actual process restart");
            Settings.DisabledPreviewModules.Clear();Settings.DisabledPreviewModules.UnionWith(disabled);Settings.Save();
            string csv=Path.Combine(root,"tab.tsv");session.Show(new Entry{Path=csv,Name="tab.tsv"});await session.Completion;
            var header=PreviewChildren<CheckBox>(body).Single();header.IsChecked=false;
            check(((TabularPreview)body.Content).Grid.Columns[0].Header.ToString()=="Column 1"&&((string[])((TabularPreview)body.Content).Grid.Items[0])[0]=="one","Actual header toggle preserves the first source row as data");
            header.IsChecked=true;
            foreach(bool theme in new[]{false,true})foreach(string appearance in theme?new[]{"dark","light"}:new[]{"dark"})foreach(double scale in new[]{1d,1.5})foreach(double width in new[]{360d,680d})
            {
                Settings.NativeTheme=theme;Settings.NativeMode=appearance;Appearance.Apply();window.Background=new SolidColorBrush(Appearance.Background);window.Foreground=(Brush)FindResource("Palette.E8EEF6");
                window.Width=width;body.LayoutTransform=new ScaleTransform(scale,scale);
                await Dispatcher.InvokeAsync(window.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var table=(TabularPreview)body.Content;
                var actions=PreviewChildren<Button>(body).Where(button=>button.ToolTip is string).ToArray();
                check(table.ActualWidth>0&&table.Grid.ActualHeight>0&&actions.Length==2&&actions.All(button=>AutomationProperties.GetName(button).Length>0),"Themed table retains readable rows and named controls at "+theme+"/"+appearance+"/"+scale+"/"+width);
                await S1Verification.Snapshot(window,root,$"table-{theme}-{appearance}-{scale}-{width}");
            }
            body.LayoutTransform=null;Settings.NativeTheme=native;Settings.NativeMode=mode;Appearance.Apply();
            HiddenTrialPresentation.AllowNativelyHiddenMonitorMoves(window);var observedDpi=new HashSet<uint>();
            foreach(var display in System.Windows.Forms.Screen.AllScreens)
            {
                var handle=new WindowInteropHelper(window).Handle;var area=display.WorkingArea;
                check(TaskbarSearchHost.Native.SetWindowPos(handle,IntPtr.Zero,area.Left+40,area.Top+40,680,600,0x0094),"Owned hidden preview follows a real monitor transition");
                await Dispatcher.InvokeAsync(window.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                observedDpi.Add(TaskbarSearchHost.Native.GetDpiForWindow(handle));
                check(HiddenTrialPresentation.IsNativelyHidden(window),"DPI qualification keeps the native preview hidden");
            }
            check(observedDpi.Contains(96)&&observedDpi.Contains(144),"Actual specialist HWND qualifies 100% and150% monitor DPI");
            foreach(var (id,extension) in new[]{("database","sqlite"),("mail","msg"),("app","apk"),("design","kra"),("elf","elf"),("dump","dmp"),("prefetch","pf"),("chm","chm")})
            {
                string invalid=Path.Combine(root,"invalid."+extension);File.WriteAllBytes(invalid,new byte[]{1,2,3,4});
                session.Show(new Entry{Path=invalid,Name=Path.GetFileName(invalid)});await session.Completion;
                check(session.State=="error","Malformed specialist input reports an explicit error: "+id);
            }
            string locked=Path.Combine(root,"records.sqlite");byte[] sourceHash=SHA256.HashData(File.ReadAllBytes(locked));
            using(var writer=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {session.Show(new Entry{Path=locked,Name="records.sqlite"});await session.Completion;check(session.State=="error","Locked database reports unavailable without touching a writer");}
            check(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(locked))),"Locked database bytes remain unchanged");
            string huge=Path.Combine(root,"oversize.csv");using(var file=File.Create(huge))file.SetLength(16*1024*1024+1);
            session.Show(new Entry{Path=huge,Name="oversize.csv"});await session.Completion;check(session.State=="error","CSV size boundary is explicit before decoding");
            var missing=PreviewHost.For(Path.Combine(root,"no-handler.pathweft-office-missing"));check(missing==null,"Missing Office handler is detected without launching an application");
            foreach(var (id,file) in samples)
            {
                session.Show(new Entry{Path=Path.Combine(root,file),Name=file},id);
                session.Show(new Entry{Path=csv,Name="tab.tsv"});await session.Completion;await PreviewSession.DrainAsync();
                check(session.ModuleId=="csv"&&session.State=="ready","Rapid replacement prevents stale publication and drains the previous specialist: "+id);
            }
            session.Clear();await PreviewSession.DrainAsync();
            await S1Verification.Navigate(Left,root);SetActive(Left);Settings.Preview=true;
            Left.List.SelectedItem=Left.Items.Single(item=>item.Path.Equals(csv,StringComparison.OrdinalIgnoreCase));ApplyPreview();await sidePreview.Completion;
            check(sidePreview.ModuleId=="csv"&&sidePreview.State=="ready","Actual main-pane selection renders the specialist in the side preview");
            ToggleQuickView(Left,this);await quickPreview.Completion;check(quickPreview.ModuleId=="csv"&&quickPreview.State=="ready","Actual Space preview uses the same specialist workflow");quick.Close();await PreviewSession.DrainAsync();
            var widget=new SearchWidgetWindow(this);
            try
            {
                widget.ShowSearch("");await S1Verification.Navigate(widget.Pane,root);widget.Pane.List.SelectedItem=widget.Pane.Items.Single(item=>item.Path.Equals(csv,StringComparison.OrdinalIgnoreCase));
                widget.Preview();await quickPreview.Completion;check(quickPreview.ModuleId=="csv"&&quickPreview.State=="ready","Actual widget Preview action renders the specialist");quick.Close();await PreviewSession.DrainAsync();
            }
            finally{widget.Shutdown();}
            InspectShellClass(Left,new Entry{Path="::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",Name="Shell class"});await quickPreview.Completion;
            check(quickPreview.ModuleId=="clsid"&&quickPreview.State=="ready","Actual Shell namespace input opens the governed virtual preview");quick.Close();await PreviewSession.DrainAsync();
            check(HiddenTrialPresentation.ForegroundViolations==0,"All specialist policy and surface checks avoid foreground takeover");
        }
        finally
        {
            Settings.DisabledPreviewModules.Clear();Settings.DisabledPreviewModules.UnionWith(disabled);Settings.NativeTheme=native;Settings.NativeMode=mode;Settings.Save();Appearance.Apply();
            session.Clear();await PreviewSession.DrainAsync();window.Close();
        }
    }
}
#endif
