using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FileExplorer;
using Microsoft.Data.Sqlite;

sealed partial class PackageProbe
{
    async Task VerifySpecialists118(ContentControl target,object preview)
    {
        var parameters=new System.Windows.Interop.HwndSourceParameters("Pathweft hidden specialist qualification")
        {Width=720,Height=540,PositionX=(int)(SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth+8192),WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080};
        using var presentation=new System.Windows.Interop.HwndSource(parameters);presentation.RootVisual=target;
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS")??throw new InvalidOperationException("Pinned specialist fixture input directory required");
        string work=Directory.CreateDirectory(Path.Combine(root,"specialists")).FullName;
        var sessionType=Type("Preview.PreviewSession");
        string Copy(string name){string path=Path.Combine(work,name);File.Copy(Path.Combine(inputs,name),path);return path;}
        async Task Show(string path,string module,bool directory=false)
        {
            Call(sessionType,preview,"Show",new Entry{Path=path,Name=Path.GetFileName(path),IsDir=directory},module);await (Task)Get(preview,"Completion");
            Check((string)Get(preview,"State")=="ready"&&(string)Get(preview,"ModuleId")==module,"Delivered specialist renders actual "+module+" input: "+Path.GetFileName(path)+"; "+(target.Content as TextBlock)?.Text);
            target.Measure(new Size(720,540));target.Arrange(new Rect(0,0,720,540));target.UpdateLayout();
        }
        async Task Clear(){Call(sessionType,preview,"Clear");await (Task)Call(sessionType,null,"DrainAsync");}
        string csv=Path.Combine(work,"table.csv");File.WriteAllText(csv,"Name,Value\n\"é 子\",42");await Show(csv,"csv");
        Check(Descendants<DataGrid>(target).Any(grid=>grid.Columns.Count==2),"Delivered CSV displays its actual table columns");await Clear();
        string database=Path.Combine(work,"data.sqlite");SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
        using(var connection=new SqliteConnection("Data Source="+database+";Pooling=False"))
        {connection.Open();using var command=connection.CreateCommand();command.CommandText="CREATE TABLE people(name TEXT);INSERT INTO people VALUES('é 子')";command.ExecuteNonQuery();}
        byte[] databaseHash=SHA256.HashData(File.ReadAllBytes(database));await Show(database,"database");await Clear();
        Check(SHA256.HashData(File.ReadAllBytes(database)).SequenceEqual(databaseHash),"Delivered database preview leaves actual source bytes unchanged");
        string mail=Path.Combine(work,"message.eml");File.WriteAllText(mail,"From: sender@example.invalid\r\nTo: reader@example.invalid\r\nSubject: Packaged message\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nActual packaged mail body");await Show(mail,"mail");await Clear();
        using(var rsa=RSA.Create(2048))
        using(var certificate=new CertificateRequest("CN=Pathweft packaged fixture",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1)))
        {string path=Path.Combine(work,"public.cer");File.WriteAllBytes(path,certificate.Export(X509ContentType.Cert));await Show(path,"certificate");await Clear();}
        await Show(Copy("sample.apk"),"app");await Clear();
        string design=Path.Combine(work,"design.kra");using(var zip=ZipFile.Open(design,ZipArchiveMode.Create))
        {
            using var bitmap=Application.GetResourceStream(new Uri("/Pathweft;component/app.ico",UriKind.Relative)).Stream;
            var decoder=BitmapDecoder.Create(bitmap,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
            using var png=new MemoryStream();encoder.Save(png);png.Position=0;
            using var output=zip.CreateEntry("mergedimage.png").Open();png.CopyTo(output);
        }
        await Show(design,"design");Check(target.Content is Image{Source:BitmapSource},"Delivered design preview publishes the embedded bitmap");await Clear();
        await Show(Copy("audio.wav"),"mediainfo");await Clear();
        string binary=Path.Combine(work,"bytes.bin");File.WriteAllBytes(binary,Enumerable.Range(0,4097).Select(index=>(byte)index).ToArray());await Show(binary,"binary");await Clear();
        foreach(var item in new[]{("hello.elf","elf"),("crash.dmp","dump"),("windows10.pf","prefetch")}){await Show(Copy(item.Item1),item.Item2);await Clear();}
        await Show(PaneView.ThisPC,"clsid");await Clear();
        string installer=Copy("7zip-installer.exe");var archiveType=Type("Archive");
        var chmEntry=((IEnumerable)Call(archiveType,null,"Read",installer)).Cast<object>().Single(entry=>((string)Get(entry,"Path")).EndsWith(".chm",StringComparison.OrdinalIgnoreCase));
        string chm=Path.Combine(work,"7-zip.chm");using(var output=File.Create(chm))Call(archiveType,null,"CopyEntry",installer,chmEntry,output);
        await Show(chm,"chm");Check(Descendants<ComboBox>(target).Single().Items.Count>1,"Delivered compiled help exposes multiple real topics");await Clear();

        foreach(var item in new[]{("document-pages.docx","wordcraft"),("spreadsheet.xlsx","gridcraft"),("navigation-distinct.pptx","deckcraft")})
        {
            string path=Copy(item.Item1);byte[] hash=SHA256.HashData(File.ReadAllBytes(path));await Show(path,"office");
            Check(string.Equals((string)Get(preview,"RendererId"),item.Item2,StringComparison.OrdinalIgnoreCase),"Delivered Office preview uses its actual bundled primary core: "+item.Item2+"; actual "+Get(preview,"RendererDescription"));
            object viewer=preview.GetType().GetField("viewer",Flags).GetValue(preview);
            var next=Descendants<Button>(target).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="Next page");
            if(item.Item2=="gridcraft")
            {
                var sheets=Descendants<ComboBox>(target).Single();Check(sheets.Items.Count==3,"Delivered workbook exposes all three real sheets");sheets.SelectedIndex=1;
            }
            else next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await (Task)Get(viewer,"Completion");
            Check(Descendants<TextBlock>(target).Any(text=>text.Text==(item.Item2=="wordcraft"?"Page 2 of 64":item.Item2=="deckcraft"?"Page 2 of 5":"Page 1 of 1")),"Delivered Office controls navigate the actual page, sheet or slide");
            if(item.Item2=="wordcraft")
            {
                object document=viewer.GetType().GetField("document",Flags).GetValue(viewer);
                var rendered=(Task)Call(document.GetType(),document,"RenderPage",0,63,CancellationToken.None);await rendered;
                Check((byte[])Get(Get(Get(rendered,"Result"),"Raster"),"Pixels") is {Length:>0},"Delivered Word core directly renders the final page beyond earlier row/page caps");
                object worker=document.GetType().GetField("worker",Flags).GetValue(document);
                using var primary=Process.GetProcessById((int)Get(worker,"ProcessId"));primary.Kill();await primary.WaitForExitAsync();
                var recovered=(Task)Call(document.GetType(),document,"RenderPage",0,63,CancellationToken.None);await recovered;
                Check(string.Equals((string)Get(Get(document,"Info"),"Engine"),"libreoffice",StringComparison.OrdinalIgnoreCase)&&(byte[])Get(Get(Get(recovered,"Result"),"Raster"),"Pixels") is {Length:>0},"Delivered app-owned worker crash recovers through actual bundled LibreOffice after primary process exit");
            }
            await Clear();Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Delivered Office navigation/recovery preserves the original input: "+item.Item1);
        }
        foreach(string name in new[]{"fallback-FillPatterns.ppt","fallback-closed-shapes.odp","fallback-fdo57117-1.vsd","fallback-Visio2013drawing.vsdx"})
        {
            await Show(Copy(name),"office");Check(string.Equals((string)Get(preview,"RendererId"),"libreoffice",StringComparison.OrdinalIgnoreCase)&&Descendants<Image>(target).Any(image=>image.Source is BitmapSource),"Delivered fallback renders actual legacy presentation/Visio content: "+name);await Clear();
        }
        var widgetType=Type("SearchWidgetWindow");var widget=Activator.CreateInstance(widgetType,Flags,null,new object[]{owner},null);
        try
        {
            var pane=(PaneView)Get(widget,"Pane");var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Listed(){if(pane.Dir==work&&pane.Listed)ready.TrySetResult();}pane.StatsChanged+=Listed;
            try{pane.Navigate(work);Listed();await ready.Task;}finally{pane.StatsChanged-=Listed;}
            ((ListView)pane.FindName("List")).SelectedItem=pane.Items.Single(entry=>entry.Path==csv);
            var menu=(ContextMenu)Call(widgetType,widget,"ResultMenu");
            Check(menu.Items.OfType<MenuItem>().Any(item=>item.Header as string=="Open containing folder"&&item.IsEnabled)&&menu.Items.OfType<MenuItem>().Any(item=>item.Header as string=="Properties"&&item.InputGestureText=="Alt+Enter"&&item.IsEnabled),"Delivered widget exposes the real selection-bound reveal and native Properties menu with keyboard hints");
        }finally{Call(widgetType,widget,"Shutdown");}
        Check(Type("Program").GetProperty("Executable",Flags).GetValue(null) as string==Path.Combine(AppContext.BaseDirectory,"Pathweft.exe"),"Delivered worker and destination links use the bundled application host, including from an independent assembly consumer");
        var identity=Type("ShellIdentity");
        Check(((string)identity.GetProperty("Current",Flags).GetValue(null)).StartsWith("Darkaxt.Pathweft.Copy.",StringComparison.Ordinal),"Delivered Shell verification is scoped to this explicit profile's separate application identity");
        Call(identity,null,"Apply");
        var jumpType=Type("FolderJumpList");
        var publisher=Activator.CreateInstance(jumpType,Flags,null,Array.Empty<object>(),null);
        try
        {
            Call(jumpType,publisher,"Publish");
            var destinations=((IEnumerable)Get(publisher,"Published")).Cast<System.Windows.Shell.JumpTask>().ToArray();
            var shellList=(System.Windows.Shell.JumpList)jumpType.GetField("list",Flags).GetValue(publisher);
            Check(((string[])Get(publisher,"Rejections")).Length==0&&destinations.Length==0&&shellList.ShowFrequentCategory&&!shellList.ShowRecentCategory,"Actual delivered publisher uses native Frequent/Pinned without custom categories or folder tasks");
            Call(jumpType,publisher,"Visit",work);
        }
        finally{((IDisposable)publisher).Dispose();Call(Type("NativeJumpList"),null,"RemoveOwned");}
    }
}
