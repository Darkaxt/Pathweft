#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyOfficeFallback13(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var window=new Window{Content=body,Width=680,Height=700,ShowInTaskbar=false};HiddenTrialPresentation.Prepare(window);window.Show();
        using var session=new PreviewSession(this,body,_=>{});
        async Task<OfficeViewer> Show(string path)
        {
            session.Show(new Entry{Path=path,Name=Path.GetFileName(path)});await session.Completion;
            check(session.State=="ready","Actual Office fallback renders "+Path.GetFileName(path)+": "+(body.Content as TextBlock)?.Text);
            return (OfficeViewer)session.ViewerForVerification;
        }
        async Task Released(string path,byte[] hash,OfficeViewer office)
        {
            var workers=office.WorkersForVerification.ToArray();string snapshot=office.SourceForVerification;
            session.Clear();await PreviewSession.DrainAsync();
            check(workers.All(worker=>Exited(worker.Pid)),"Every owned Office worker exits before cleanup: "+Path.GetFileName(path));
            check(!Directory.Exists(Path.GetDirectoryName(snapshot)),"The preview workspace is removed after its actual consumers exit");
            check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Fallback preserves original bytes: "+Path.GetFileName(path));
            File.Move(path,path+".released");
        }
        try
        {
            foreach(string name in new[]{"fallback-FillPatterns.ppt","fallback-closed-shapes.odp","fallback-fdo57117-1.vsd","fallback-Visio2013drawing.vsdx"})
            {
                string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);var hash=SHA256.HashData(File.ReadAllBytes(path));
                var office=await Show(path);
                check(office.FallbackForVerification&&office.WorkersForVerification.Count==1&&office.WorkersForVerification[0].Engine=="libreoffice","Unsupported primary format goes directly to the bundled fallback: "+name);
                check(PreviewChildren<Image>(body).Any(image=>image.Source!=null)&&PreviewChildren<TextBlock>(body).Any(text=>text.Text.StartsWith("Page 1 of ")),"Fallback exposes actual rendered content and authoritative page count: "+name);
                var imported=(OfficeDocument)typeof(OfficeViewer).GetField("document",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(office);
                var text=await imported.ExtractText(null,CancellationToken.None);
                check(text.Status=="unsupported"&&text.Records.Length==0&&text.Next==null&&text.Coverage.Contains("LibreOffice"),"Fallback explicitly reports unsupported Craft extraction without pretending to have indexed its rendered pixels: "+name);
                await S1Verification.Snapshot(window,root,name.Replace('.','-'));
                await Released(path,hash,office);
            }
            string document=Path.Combine(root,"recovery.docx");File.Copy(Path.Combine(inputs,"document-pages.docx"),document);
            byte[] original=SHA256.HashData(File.ReadAllBytes(document));var recovering=await Show(document);
            int primary=recovering.WorkerForVerification;
            // Fault injection at the real process boundary: terminate only the
            // exact test-owned renderer, leaving the browser and snapshot alive.
            using(var process=Process.GetProcessById(primary)){process.Kill();await process.WaitForExitAsync();}
            var number=PreviewChildren<TextBox>(body).Single(box=>AutomationProperties.GetName(box)=="Page number");
            number.Text="64";number.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(number),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
            await recovering.Completion;
            check(recovering.FallbackForVerification&&recovering.WorkersForVerification.Select(w=>w.Engine).SequenceEqual(new[]{"wordcraft","libreoffice"})&&Exited(primary),"Abnormal primary-worker exit recovers once with LibreOffice after the original process exits");
            check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 64 of 64")&&PreviewChildren<Image>(body).Any(image=>image.Source!=null),"Crash recovery preserves the requested final page and uses actual fallback pagination");
            check(session.RendererId=="LibreOffice"&&session.RendererDescription.Contains("wordcraft failed"),"Fallback identifies its actual engine and recovery reason in preview metadata");
            await Released(document,original,recovering);
            string broken=Path.Combine(root,"broken.docx");
            using(var archive=System.IO.Compression.ZipFile.Open(broken,System.IO.Compression.ZipArchiveMode.Create))
            {using var text=new StreamWriter(archive.CreateEntry("unrelated.txt").Open());text.Write("Not an Office package");}
            session.Show(new Entry{Path=broken,Name="broken.docx"});await session.Completion;
            check(session.State=="error"&&(body.Content as TextBlock)?.Text.Contains("LibreOffice fallback failed")==true,"Both engines failing produces an explicit combined error: "+session.State+" / "+(body.Content as TextBlock)?.Text);session.Clear();await PreviewSession.DrainAsync();
            string huge=Path.Combine(root,"oversize.docx");using(var file=File.Create(huge))file.SetLength((64L<<20)+1);
            await using(var policy=new OfficeDocument())
            {
                try{await policy.Open(huge,CancellationToken.None);check(false,"Oversized Office input must be rejected");}
                catch(OfficePolicyException){check(policy.WorkersForVerification.Count==0&&!policy.FallbackForVerification,"Input policy rejection launches neither primary nor fallback");}
            }
            using var cancel=new CancellationTokenSource();await using(var cancelled=new OfficeDocument())
            {
                await cancelled.Open(Path.Combine(inputs,"document.docx"),cancel.Token);cancel.Cancel();
                try{await cancelled.RenderPage(0,0,cancel.Token);check(false,"Cancelled Office work must not render");}
                catch(OperationCanceledException){check(cancelled.WorkersForVerification.Count==1&&!cancelled.FallbackForVerification,"Lifetime cancellation does not start a fallback worker");}
            }
            check(HiddenTrialPresentation.ForegroundViolations==0,"Fallback, recovery and policy workflows perform no foreground takeover");
        }
        finally{session.Clear();await PreviewSession.DrainAsync();window.Close();}
    }
    static bool Exited(int pid)
    {try{using var process=Process.GetProcessById(pid);return process.HasExited;}catch(ArgumentException){return true;}}
}
#endif
