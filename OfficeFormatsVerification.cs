#if DEBUG
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyOfficeFormats13(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var window=new Window{Content=body,Width=540,Height=640,ShowInTaskbar=false,Background=Hex("#161B24")};HiddenTrialPresentation.Prepare(window);window.Show();
        using var session=new PreviewSession(this,body,_=>{});
        try
        {
            foreach(string name in new[]{"fallback-changes-in-footnote.doc","fallback-hello.docm","fallback-Simple-TOC.odt","fallback-pivottable_empty_item.xls","fallback-shared_formula.xlsb","fallback-basic-cell-content.ods","fallback-tdf117266_macroButton.xlsm"})
            {
                string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
                var hash=SHA256.HashData(File.ReadAllBytes(path));
                session.Show(new Entry{Path=path,Name=name});await session.Completion;
                check(session.State=="ready","Actual remaining Office format renders: "+name+" / "+(body.Content as TextBlock)?.Text);
                var viewer=(OfficeViewer)session.ViewerForVerification;
                var image=PreviewChildren<Image>(body).FirstOrDefault(item=>item.Source is BitmapSource);
                check(image?.Source is BitmapSource {PixelWidth:>0,PixelHeight:>0}&&PreviewChildren<TextBlock>(body).Any(text=>text.Text.StartsWith("Page 1 of ")),"Office format produces an actual navigable raster: "+name+" / "+session.RendererId);
                var number=PreviewChildren<TextBox>(body).Single(box=>AutomationProperties.GetName(box)=="Page number");
                var next=PreviewChildren<Button>(body).Single(button=>AutomationProperties.GetName(button)=="Next page");
                if(next.IsEnabled){next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await viewer.Completion;check(number.Text=="2","The actual next-page control navigates "+name);}
                var sheet=PreviewChildren<ComboBox>(body).Single();
                if(sheet.IsVisible&&sheet.Items.Count>1){sheet.SelectedIndex=sheet.Items.Count-1;await viewer.Completion;check(sheet.SelectedIndex==sheet.Items.Count-1&&number.Text=="1","The actual sheet selector navigates "+name);}
                await S1Verification.Snapshot(window,root,name.Replace('.','-'));
                var workers=viewer.WorkersForVerification.ToArray();string snapshot=viewer.SourceForVerification;
                session.Clear();await PreviewSession.DrainAsync();
                check(workers.All(worker=>Exited(worker.Pid))&&!Directory.Exists(Path.GetDirectoryName(snapshot)),"All format workers exit and release their snapshots: "+name);
                check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))&&File.ReadAllText(path+":Zone.Identifier")=="[ZoneTransfer]\r\nZoneId=3\r\n","Format preview preserves original bytes and security marker: "+name);
            }
            string encrypted=Path.Combine(root,"encrypted.docx");File.Copy(Path.Combine(inputs,"fallback-Encrypted_MSO2013_abc.docx"),encrypted);
            var original=SHA256.HashData(File.ReadAllBytes(encrypted));
            session.Show(new Entry{Path=encrypted,Name="encrypted.docx"});await session.Completion;
            check(session.State=="error"&&body.Content is TextBlock {Text:var message}&&message.Length>0,"Password-protected Office content reports an explicit error without a password prompt");
            session.Clear();await PreviewSession.DrainAsync();
            check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(encrypted))),"Password-protected source is not modified or unblocked");
            check(HiddenTrialPresentation.ForegroundViolations==0,"Remaining format and protected-input workflows avoid foreground activation");
        }
        finally{session.Clear();await PreviewSession.DrainAsync();window.Close();}
    }
}
#endif
