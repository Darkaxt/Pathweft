#if DEBUG
using FileExplorer.Preview;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Pkcs;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using LiteDB;
using OpenMcdf;
using JsonSerializer=System.Text.Json.JsonSerializer;
using System.Windows.Automation;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifySpecialists13(string root, Action<bool,string> check)
    {
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="performance"){await VerifyPerformance18(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="performance-ui"){await VerifyPerformancePresentation18(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="performance-native"){await VerifyPerformanceNative18(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="missing-tabs"){await VerifyMissingTabs23(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="transfer-differences"){await VerifyTransferDifferences16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="transfer-progress"){await VerifyTransferProgress24(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="preview-selection"){await VerifyPreviewSelection38(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="space-entry"){await VerifySpaceStartup23(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO") is "space-refresh" or "space-ui"){await VerifySpaceRefresh21(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="live-indexed-sizes"){await VerifyLiveIndexedSizes15(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="indexed-sizes"){await VerifyIndexedSizes15(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="space"){await VerifySpace15(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="menus-properties"){await DiagnosePropertyOwner9(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="menus-shell"){await VerifyShellMenus9(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="jump-lists"){await VerifyJumpLists11(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="windows"){await VerifyAuxiliaryWindows14(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="transfers"){await VerifyTransferDismiss14(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="transfers-clipboard"){await VerifyTransferClipboard15(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO") is "menus" or "menus-ui"){await VerifyMenus9(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="icons"){await VerifyCommandIcons13(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="sizes"){await VerifySizes18(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="pdf"){await VerifyPdfNavigation13(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="existing")
        {
            await VerifyPreview115(root,check);
            Settings.NativeMode="dark";Appearance.Apply();
            await VerifyMedia113(root,check,qualifyFolderRouting:false);return;
        }
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="office"){await VerifyOffice13(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="office-fallback"){await VerifyOfficeFallback13(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="office-formats"){await VerifyOfficeFormats13(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="office-extraction"){await VerifyOfficeExtraction16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="office-reference"){await VerifyOfficeReference16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="photo-core"){await VerifyPhotoCore16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="vector-core"){await VerifyVectorCore16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="fig-core"){await VerifyFigCore16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="pdf-core"){await VerifyPdfCore16(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="modes"){await VerifyPreviewModes13(root,check);return;}
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="runtime")
        {
            var runtimeBody=new ContentControl();using var runtimeSession=new PreviewSession(this,runtimeBody,_=>{});
            runtimeSession.Show(new Entry{Path=Path.Combine(root,"audio.wav"),Name="audio.wav"},"mediainfo");await runtimeSession.Completion;
            check(runtimeSession.State=="error"&&(runtimeBody.Content as TextBlock)?.Text.Contains("packaged MediaInfo runtime is missing")==true,"Missing native inspector runtime produces an explicit error in the actual preview");
            runtimeSession.Clear();await PreviewSession.DrainAsync();return;
        }
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="formats")
        {
            string source=Path.GetFullPath(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_FIXTURE_SOURCE"));
            if(!source.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned fixture source required.");
            foreach(string name in new[]{"audio.wav","protected.pfx"})File.Copy(Path.Combine(source,name),Path.Combine(root,name));
            await VerifySpecialistFormats13(root,check);return;
        }
        if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="policies")
        {
            string source=Path.GetFullPath(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_FIXTURE_SOURCE"));
            if(!source.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned fixture source required.");
            foreach(string name in new[]{"tab.tsv","records.sqlite","message.eml","public.cer","sample.apk","design.kra","audio.wav","bytes.bin","hello.elf","crash.dmp.released","windows10.pf","7-zip.chm","document.docx.released"})File.Copy(Path.Combine(source,name),Path.Combine(root,name));
            await VerifySpecialistPolicies13(root,check);return;
        }
        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var previewWindow=new Window{Content=body,Width=420,Height=700,ShowInTaskbar=false};HiddenTrialPresentation.Prepare(previewWindow);previewWindow.Show();
        try
        {
        using var session=new PreviewSession(this,body,_=>{});
        string path=Path.Combine(root,"quoted.csv");
        File.WriteAllText(path,"Name,Comment,Count\r\n\"é 子\",\"comma, and\r\nnewline\",2\r\nnext,plain,3",new UTF8Encoding(true));
        session.Show(new Entry{Path=path,Name=Path.GetFileName(path)});
        await session.Completion;
        check(session.State=="ready"&&session.ModuleId=="csv","CSV selection uses the specialist table module rather than raw text");
        body.Measure(new Size(380,600));body.Arrange(new Rect(0,0,380,600));body.UpdateLayout();
        check(body.Content is TabularPreview,"The CSV specialist publishes the actual navigable table");
        var table=(TabularPreview)body.Content;
        check(table.Grid.Columns.Select(column=>column.Header.ToString()).SequenceEqual(new[]{"Name","Comment","Count"}),"CSV headers are displayed as table column headings");
        var rows=table.Grid.Items.Cast<string[]>().ToArray();
        check(rows.Length==2&&rows[0][0]=="é 子"&&rows[0][1]=="comma, and\r\nnewline"&&rows[0][2]=="2","Quoted commas, embedded newlines and Unicode retain their actual cell values");
        string tsv=Path.Combine(root,"tab.tsv");File.WriteAllText(tsv,"one\ttwo\nvalue\tother");
        session.Show(new Entry{Path=tsv,Name="tab.tsv"});await session.Completion;
        check(session.State=="ready"&&((TabularPreview)body.Content).Grid.Columns.Count==2,"TSV uses an explicit tab delimiter on a short real input");
        string paged=Path.Combine(root,"paged.csv");File.WriteAllText(paged,"Index,Value\n"+string.Join('\n',Enumerable.Range(0,450).Select(i=>$"{i},row{i}")));
        session.Show(new Entry{Path=paged,Name="paged.csv"});await session.Completion;
        table=(TabularPreview)body.Content;
        var controls=(DockPanel)table.Children[0];var navigation=(StackPanel)controls.Children[0];
        ((IInvokeProvider)new ButtonAutomationPeer((Button)navigation.Children[1]).GetPattern(PatternInterface.Invoke)).Invoke();
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        check(table.Page==1&&((string[])table.Grid.Items[0])[0]=="200","The real Next rows action navigates to the next actual data page");
        session.Clear();await PreviewSession.DrainAsync();
        string zip=Path.Combine(root,"tables.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))archive.CreateEntryFromFile(tsv,"tables/tab.tsv");
        string virtualPath=zip+"\\tables\\tab.tsv";
        session.Show(new Entry{Path=virtualPath,Name="tab.tsv"});await session.Completion;
        check(session.State=="ready"&&session.ModuleId=="csv"&&body.Content is TabularPreview,"An actual compressed-folder selection uses the same CSV table workflow");
        session.Clear();await PreviewSession.DrainAsync();
        File.Move(path,path+".released");
        check(File.Exists(path+".released"),"Closing the CSV preview releases its source file");
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS")??throw new InvalidOperationException("Pinned upstream input directory required.");
        async Task Show(string file,string module,string choice=null)
        {
            session.Show(new Entry{Path=file,Name=Path.GetFileName(file)},choice);await session.Completion;
            check(session.State=="ready"&&session.ModuleId==module,$"Actual {module} selection renders {Path.GetFileName(file)} ({session.State}; {(body.Content as TextBlock)?.Text})");
            body.UpdateLayout();
        }
        async Task Released(string file,byte[] before)
        {
            session.Clear();await PreviewSession.DrainAsync();
            check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))),"Source bytes remain unchanged: "+Path.GetFileName(file));
            string renamed=file+".released";File.Move(file,renamed);File.Move(renamed,file);
        }
        string Copy(string name){string target=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),target);return target;}
        void Zip(string target,params(string Name,byte[] Bytes)[] entries)
        {
            using var archive=ZipFile.Open(target,ZipArchiveMode.Create);
            foreach(var item in entries){using var stream=archive.CreateEntry(item.Name).Open();stream.Write(item.Bytes);}
        }
        byte[] Utf(string value)=>Encoding.UTF8.GetBytes(value);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255},8)));
        using var imageBytes=new MemoryStream();png.Save(imageBytes);byte[] image=imageBytes.ToArray();
        foreach(var (extension,entry) in new[]{("kra","mergedimage.png"),("sketch","previews/preview.png"),("fig","thumbnail.png"),("cdr","previews/thumbnail.png"),("pix","document.thumb.png"),("pip","document.thumb.png"),("xd","preview.png"),("xmind","Thumbnails/thumbnail.png")})
        {
            string design=Path.Combine(root,"design."+extension);Zip(design,(entry,image));byte[] hash=SHA256.HashData(File.ReadAllBytes(design));
            await Show(design,"design");check(body.Content is Image{Source:BitmapSource{PixelWidth:2,PixelHeight:2}},"Design viewer displays the embedded bitmap: "+extension);await Released(design,hash);
        }
        string pdn=Path.Combine(root,"design.pdn");byte[] pdnXml=Utf("<pdnImage><custom><thumb png='"+Convert.ToBase64String(image)+"'/></custom></pdnImage>");
        using(var output=File.Create(pdn)){output.Write(new byte[]{80,68,78,(byte)pdnXml.Length,(byte)(pdnXml.Length>>8),(byte)(pdnXml.Length>>16)});output.Write(pdnXml);}
        await Show(pdn,"design");
        string sqlite=Path.Combine(root,"records.sqlite");
        SqliteRuntime.Ensure();
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=sqlite,Pooling=false}.ToString()))
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="CREATE TABLE people(id INTEGER, name TEXT, bytes BLOB); INSERT INTO people VALUES (42,'é 子',X'0102')";command.ExecuteNonQuery();
            command.CommandText="select sqlite_version()";check(System.Version.Parse((string)command.ExecuteScalar())>=new System.Version(3,50,2),"Actual OS SQLite runtime meets the qualified security floor");
        }
        byte[] databaseHash=SHA256.HashData(File.ReadAllBytes(sqlite));await Show(sqlite,"database");
        var actualTable=DatabaseViewer.ReadTable(sqlite,"people",true,CancellationToken.None);
        check(actualTable.Columns.SequenceEqual(new[]{"id","name","bytes"})&&actualTable.Rows[0].SequenceEqual(new[]{"42","é 子","0102"}),"Database table schema, typed values and bounded blob bytes match the actual database");await Released(sqlite,databaseHash);
        string lite=Path.Combine(root,"records.litedb");using(var db=new LiteDatabase(lite))db.GetCollection("people").Insert(new BsonDocument{{"name","é 子"},{"count",42}});
        byte[] liteHash=SHA256.HashData(File.ReadAllBytes(lite));await Show(lite,"database");
        check(DatabaseViewer.ReadTable(lite,"people",false,CancellationToken.None).Rows[0].Contains("é 子"),"LiteDB renders actual collection rows");await Released(lite,liteHash);
        string binary=Path.Combine(root,"bytes.bin");File.WriteAllBytes(binary,Enumerable.Range(0,9000).Select(i=>(byte)i).ToArray());
        await Show(binary,"binary","binary");check(BinaryViewer.ReadPage(binary,4096,CancellationToken.None).Text.StartsWith("000000001000  00 01 02"),"Hex navigation uses correct source offsets and bytes");
        string empty=Path.Combine(root,"empty.bin");File.WriteAllBytes(empty,Array.Empty<byte>());await Show(empty,"binary","binary");
        foreach(string name in new[]{"hello.elf","hello.dylib","kernel.uimage","windows7.pf","windows10.pf","sample.apk","sample.aab","7zip.msi"})
        {
            string file=Copy(name),module=name.EndsWith(".pf")?"prefetch":name.EndsWith(".apk")||name.EndsWith(".aab")||name.EndsWith(".msi")?"app":"elf";
            byte[] hash=SHA256.HashData(File.ReadAllBytes(file));await Show(file,module);
            var fields=InspectionViewer.Read(module,file,CancellationToken.None);
            check(fields.Count>5&&fields.All(row=>row.Value!=null),"Real upstream input supplies structured fields: "+name);await Released(file,hash);
        }
        foreach(var (extension,entry,markup) in new[]{
            ("msix","AppxManifest.xml","<Package><Identity Name='Pathweft.Test' Version='1.2.3.4' Publisher='CN=Fixture'/><Properties><DisplayName>Fixture</DisplayName><Logo>logo.png</Logo></Properties><Capabilities><Capability Name='internetClient'/></Capabilities></Package>"),
            ("nupkg","fixture.nuspec","<package><metadata><id>Fixture.Package</id><version>1.2.3</version><authors>Fixture</authors><description>Inert test package</description><icon>logo.png</icon></metadata></package>"),
            ("ipa","Payload/Fixture.app/Info.plist","<plist><dict><key>CFBundleIdentifier</key><string>org.pathweft.fixture</string><key>CFBundleVersion</key><string>42</string></dict></plist>")})
        {
            string file=Path.Combine(root,"fixture."+extension);Zip(file,(entry,Utf(markup)),("logo.png",image));await Show(file,"app");
            check(PackageInspection.Read(file,CancellationToken.None).Any(field=>field.Value.Contains("Fixture",StringComparison.OrdinalIgnoreCase)||field.Value.Contains("fixture",StringComparison.Ordinal)),"Package identity comes from its actual family manifest: "+extension);
        }
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=Pathweft fixture",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
        string cer=Path.Combine(root,"public.cer");File.WriteAllBytes(cer,certificate.Export(X509ContentType.Cert));await Show(cer,"certificate");
        check(CertificateViewer.Read(cer,null,CancellationToken.None).Any(field=>field.Name=="Subject"&&field.Value.Contains("Pathweft fixture")),"Certificate subject is read from the real public certificate");
        string csr=Path.Combine(root,"request.certSigningRequest");File.WriteAllText(csr,request.CreateSigningRequestPem());await Show(csr,"certificate");
        string pfx=Path.Combine(root,"protected.pfx");File.WriteAllBytes(pfx,certificate.Export(X509ContentType.Pfx,"fixture-secret"));await Show(pfx,"certificate");
        var password=PreviewChildren<PasswordBox>(body).Single();password.Password="fixture-secret";
        var unlock=PreviewChildren<Button>(body).Single(button=>AutomationProperties.GetName(button)=="Unlock certificate details");
        unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await ((CertificateViewer)session.ViewerForVerification).LoadingForVerification;
        check(password.Password.Length==0&&PreviewChildren<TextBox>(body).Any(text=>text.Text.Contains("Pathweft fixture")),"Actual password action unlocks certificate details and immediately clears its secret control");
        check(CertificateViewer.Read(pfx,"fixture-secret",CancellationToken.None).Any(field=>field.Name=="Private key"&&field.Value.StartsWith("Present")),"Password-protected store unlock inspects ephemeral public details without exporting its private key");
        var cms=new SignedCms(new ContentInfo(Utf("<plist><dict><key>Name</key><string>Fixture provision</string><key>UUID</key><string>fixture-uuid</string></dict></plist>")));cms.ComputeSignature(new CmsSigner(certificate));
        string provision=Path.Combine(root,"fixture.mobileprovision");File.WriteAllBytes(provision,cms.Encode());await Show(provision,"certificate");
        check(CertificateViewer.Read(provision,null,CancellationToken.None).Any(row=>row.Value=="Fixture provision"),"Signed provisioning metadata is parsed from the authenticated envelope payload");
        string eml=Path.Combine(root,"message.eml");File.WriteAllText(eml,"From: Sender <sender@example.invalid>\r\nTo: reader@example.invalid\r\nSubject: Fixture mail\r\nDate: Wed, 04 Nov 2026 10:00:00 +0000\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=fixture\r\n\r\n--fixture\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<h1>Actual mail body</h1><script>window.executed=1</script><img src='https://example.invalid/image.png'>\r\n--fixture\r\nContent-Type: text/plain\r\nContent-Disposition: attachment; filename=note.txt\r\n\r\nattachment\r\n--fixture--\r\n");
        byte[] mailHash=SHA256.HashData(File.ReadAllBytes(eml));await Show(eml,"mail");
        var mail=MailViewer.Read(eml,CancellationToken.None);check(mail.Subject=="Fixture mail"&&mail.Attachments.SequenceEqual(new[]{"note.txt"}),"EML headers and attachment inventory match the actual MIME message");
        var mailViewer=(MailViewer)session.ViewerForVerification;
        string rendered=await mailViewer.HtmlForVerification.CoreForVerification.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new{expression="document.querySelector('h1').textContent==='Actual mail body' && typeof window.executed==='undefined' && !document.querySelector('img').hasAttribute('src')",returnByValue=true}));
        using(var json=JsonDocument.Parse(rendered))check(json.RootElement.GetProperty("result").GetProperty("value").GetBoolean(),"Actual mail browser renders the body while scripts and remote resources remain inactive");await Released(eml,mailHash);
        string msg=Path.Combine(root,"message.msg");
        using(var storage=RootStorage.Create(msg,OpenMcdf.Version.V3))
        {
            void Text(Storage parent,string name,string text){using var stream=parent.CreateStream("__substg1.0_"+name+"001F");stream.Write(Encoding.Unicode.GetBytes(text+"\0"));}
            Text(storage,"0037","Compound fixture");Text(storage,"0C1A","Fixture sender");Text(storage,"0C1F","sender@example.invalid");Text(storage,"0E04","reader@example.invalid");Text(storage,"1000","Actual MSG body é 子");
            var attachment=storage.CreateStorage("__attach_version1.0_#00000000");Text(attachment,"3707","note.txt");
        }
        await Show(msg,"mail");check(MailViewer.Read(msg,CancellationToken.None).Attachments.Contains("note.txt"),"Actual MSG compound-file body and attachment directory are inspected without Outlook");session.Clear();await PreviewSession.DrainAsync();
        string wave=Path.Combine(root,"audio.wav");
        using(var output=new BinaryWriter(File.Create(wave))){output.Write("RIFF"u8);output.Write(16036);output.Write("WAVEfmt "u8);output.Write(16);output.Write((short)1);output.Write((short)1);output.Write(8000);output.Write(16000);output.Write((short)2);output.Write((short)16);output.Write("data"u8);output.Write(16000);output.Write(new byte[16000]);}
        await Show(wave,"mediainfo","mediainfo");check(InspectionViewer.Read("mediainfo",wave,CancellationToken.None).Any(row=>row.Value.Contains("PCM")),"Native MediaInfo identifies the actual PCM audio independently of playback");
        foreach(string id in new[]{"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}","::{00000000-0000-0000-0000-000000000001}"})await Show(id,"clsid");
        session.Show(new Entry{Path="::{invalid}",Name="Malformed identifier"});await session.Completion;check(session.State=="error","Malformed Shell identifiers report an explicit input error");
        string installer=Copy("7zip-installer.exe");var chmEntry=Archive.Read(installer).Single(item=>item.Path.EndsWith(".chm",StringComparison.OrdinalIgnoreCase));
        string chm=Path.Combine(root,"7-zip.chm");using(var output=File.Create(chm))Archive.CopyEntry(installer,chmEntry,output);
        byte[] helpHash=SHA256.HashData(File.ReadAllBytes(chm));await Show(chm,"chm");
        var help=(CompiledHelpViewer)session.ViewerForVerification;check(help.TopicsForVerification.Items.Count>2,"Actual CHM supplies navigable embedded topics");
        help.TopicsForVerification.SelectedIndex=1;await help.TopicCompletion;
        check(help.HtmlForVerification.CoreForVerification!=null,"Selecting another CHM topic renders its actual static page");await Released(chm,helpHash);
        check(HiddenTrialPresentation.ForegroundViolations==0,"Specialist workflows perform no foreground takeover");
        }
        finally{previewWindow.Close();}
        await VerifySpecialistFormats13(root,check);
        await VerifyOffice13(root,check);
        await VerifySpecialistPolicies13(root,check);
    }
    internal async Task VerifyOffice13(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var window=new Window{Content=body,Width=780,Height=700,ShowInTaskbar=false,Background=new SolidColorBrush(Appearance.Background),Foreground=(Brush)FindResource("Palette.E8EEF6")};HiddenTrialPresentation.Prepare(window);window.Show();
        using var session=new PreviewSession(this,body,_=>{});
        try
        {
            foreach(string name in new[]{"document.docx","document-pages.docx","spreadsheet.xlsx","navigation-distinct.pptx"})
            {
                string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);
                byte[] zone=System.Text.Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n");
                File.WriteAllBytes(path+":Zone.Identifier",zone);
                byte[] hash=SHA256.HashData(File.ReadAllBytes(path));
                session.Show(new Entry{Path=path,Name=name});await session.Completion;
                check(session.State=="ready"&&session.ViewerForVerification is OfficeViewer,"Actual Office selection uses the bundled read-only renderer: "+name+" / "+(body.Content as TextBlock)?.Text);
                await Dispatcher.InvokeAsync(body.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var office=(OfficeViewer)session.ViewerForVerification;
                check(!office.FallbackForVerification,"Supported Office fixture uses its actual borrowed core, without hiding a primary failure behind fallback: "+name);
                check(zone.SequenceEqual(File.ReadAllBytes(office.SourceForVerification+":Zone.Identifier")),"The owned rendering snapshot retains the document security marker: "+name);
                check(PreviewChildren<Image>(body).Any(image=>image.Source is BitmapSource),"The actual Office content is rendered inside the preview: "+name);
                if(name=="document-pages.docx")
                {
                    var page=PreviewChildren<TextBox>(body).Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="Page number");
                    page.Text="64";
                    page.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(page),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
                    await office.Completion;
                    check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 64 of 64"),"Actual page-number input reaches the final page of the64-page Word document");
                }
                if(name=="spreadsheet.xlsx")
                {
                    var sheets=PreviewChildren<ComboBox>(body).Single();
                    check(sheets.Items.Count==3,"Workbook navigation exposes all three actual sheets");
                    sheets.SelectedIndex=1;await office.Completion;
                    check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 1 of 1"),"Changing the actual sheet selector renders Sheet2's own content");
                    sheets.SelectedIndex=0;await office.Completion;
                    check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 1 of 2"),"Returning to Sheet1 retains its full two-page content");
                }
                if(name=="navigation-distinct.pptx")
                {
                    byte[] before=OfficePixels(body);
                    var next=PreviewChildren<Button>(body).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="Next page");
                    next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await office.Completion;
                    check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 2 of 5"),"Presentation navigation advances through the actual five rendered slides");
                    check(!before.SequenceEqual(OfficePixels(body)),"Advancing the presentation changes actual rendered slide pixels");
                }
                await S1Verification.Snapshot(window,root,name.Replace('.','-'));
                session.Clear();await PreviewSession.DrainAsync();
                check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Office preview preserves document bytes: "+name);
                check(zone.SequenceEqual(File.ReadAllBytes(path+":Zone.Identifier")),"Office preview preserves the original security marker: "+name);
                File.Move(path,path+".released");
            }
            string surface=Path.Combine(root,"surface.docx");File.Copy(Path.Combine(root,"document-pages.docx.released"),surface);
            var surfaceHash=SHA256.HashData(File.ReadAllBytes(surface));
            await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();Settings.Preview=true;
            Left.List.SelectedItem=Left.Items.Single(item=>item.Path==surface);ApplyPreview();await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="office"&&sidePreview.RendererId=="WordCraft","Main-pane selection renders the actual source-built Office core in the sidebar");
            foreach(var theme in new[]{("native-dark",true,"dark"),("native-light",true,"light"),("original",false,"dark")})
            {
                Settings.NativeTheme=theme.Item2;Settings.NativeMode=theme.Item3;
                foreach(double scale in new[]{1.0,1.5})
                {
                    Settings.TextScale=scale;ApplyAppearance();await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var pageControls=PreviewChildren<Button>(PreviewBody).ToArray();
                    check(pageControls.Length==2&&pageControls.All(button=>!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)))&&PreviewChildren<Image>(PreviewBody).Any(image=>image.ActualWidth>0),"Actual Office content/navigation remain visible and accessible in "+theme.Item1+" / "+scale);
                    await S1Verification.Snapshot(this,root,$"office-sidebar-{theme.Item1}-{scale*100:0}");
                }
            }
            Settings.TextScale=1;Settings.NativeTheme=true;Settings.NativeMode="dark";ApplyAppearance();
            ToggleQuickView(Left,this);await quickPreview.Completion;
            check(quickPreview.State=="ready"&&quickPreview.RendererId=="WordCraft","Actual Space opens the same integrated native Office viewer");
            var quickOffice=(OfficeViewer)quickPreview.ViewerForVerification;
            var quickPage=PreviewChildren<TextBox>(quickBody).Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="Page number");quickPage.Text="64";
            quickPage.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(quickPage),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});await quickOffice.Completion;
            check(PreviewChildren<TextBlock>(quickBody).Any(text=>text.Text=="Page 64 of 64"),"The actual Space surface navigates to the native document's final page");
            quick.Close();await PreviewSession.DrainAsync();
            var widget=new SearchWidgetWindow(this);
            try{
                widget.ShowSearch("");await S1Verification.Navigate(widget.Pane,root);widget.Pane.List.SelectedItem=widget.Pane.Items.Single(item=>item.Path==surface);
                widget.Preview();await quickPreview.Completion;
                check(quickPreview.State=="ready"&&quickPreview.RendererId=="WordCraft","Actual search-widget Preview renders the bundled Office core without an associated application");
                quick.Close();await PreviewSession.DrainAsync();
            }finally{widget.Shutdown();}
            Settings.Preview=false;ApplyPreview();await PreviewSession.DrainAsync();
            string officeZip=Path.Combine(root,"office.zip");using(var archive=System.IO.Compression.ZipFile.Open(officeZip,System.IO.Compression.ZipArchiveMode.Create))archive.CreateEntryFromFile(surface,"docs/surface.docx");
            await S1Verification.Navigate(Left,officeZip+"\\docs");SetActive(Left);Left.List.SelectedItem=Left.Items.Single(item=>item.Name=="surface.docx");
            ToggleQuickView(Left,this);await quickPreview.Completion;
            check(quickPreview.State=="ready"&&quickPreview.RendererId=="WordCraft"&&quickPreview.ArchiveDirectoryForVerification!=null,"Actual compressed Office member uses the native core with an owned archive input lease");
            string archiveLease=quickPreview.ArchiveDirectoryForVerification;var archivedOffice=(OfficeViewer)quickPreview.ViewerForVerification;var archiveWorkers=archivedOffice.WorkersForVerification.ToArray();
            quick.Close();await PreviewSession.DrainAsync();
            check(!Directory.Exists(archiveLease)&&archiveWorkers.All(worker=>Exited(worker.Pid)),"Closing compressed Office preview releases the native workers and archive lease");
            session.Show(new Entry{Path=surface,Name="surface.docx"});await session.Completion;var disabledOffice=(OfficeViewer)session.ViewerForVerification;
            Settings.DisabledPreviewModules.Add("office");PreviewSession.RefreshAll();await session.Completion;await PreviewSession.DrainAsync();
            check(session.ModuleId!="office"&&disabledOffice.WorkersForVerification.All(worker=>Exited(worker.Pid)),"Disabling active Office actually releases its native worker and selects only an enabled alternative");
            Settings.DisabledPreviewModules.Remove("office");session.Clear();await PreviewSession.DrainAsync();
            using(var locked=new FileStream(surface,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                session.Show(new Entry{Path=surface,Name="surface.docx"});await session.Completion;
                check(session.State=="error","Locked Office input reports unavailable without bypassing the source lock");
            }
            session.Clear();await PreviewSession.DrainAsync();check(surfaceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(surface))),"All Office surfaces preserve the source bytes");File.Move(surface,surface+".released");
            string dump=Path.Combine(root,"crash.dmp");File.Copy(Path.Combine(inputs,"crash.dmp"),dump);
            session.Show(new Entry{Path=dump,Name="crash.dmp"});await session.Completion;
            check(session.State=="ready"&&session.ModuleId=="dump","Actual upstream Windows minidump renders through the specialist module");
            var report=DumpInspection.Read(dump,CancellationToken.None);
            check(report.Any(row=>row.Name=="Exception code"&&row.Value.StartsWith("0x"))&&report.Any(row=>row.Name=="Module"&&!row.Value.StartsWith("Not recorded"))&&report.Any(row=>row.Name=="Thread ID"&&!row.Value.StartsWith("Not recorded")),"Real minidump exposes actual exception, modules and threads");
            session.Clear();await PreviewSession.DrainAsync();File.Move(dump,dump+".released");
            check(HiddenTrialPresentation.ForegroundViolations==0,"Office and dump workflows perform no foreground takeover");
        }
        finally{session.Clear();await PreviewSession.DrainAsync();window.Close();}
    }
    internal async Task VerifyPdfNavigation13(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        string path=Path.Combine(root,"navigation.pdf");
        File.Copy(Path.Combine(inputs,"../craft-baseline/wordcraft/pages-pdf.pdf"),path);
        byte[] hash=SHA256.HashData(File.ReadAllBytes(path));
        var body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        var window=new Window{Content=body,Width=420,Height=700,ShowInTaskbar=false,Background=new SolidColorBrush(Appearance.Background),Foreground=(Brush)FindResource("Palette.E8EEF6")};HiddenTrialPresentation.Prepare(window);window.Show();
        using var session=new PreviewSession(this,body,_=>{});
        try
        {
            session.Show(new Entry{Path=path,Name="navigation.pdf"});await session.Completion;
            check(session.State=="ready"&&session.ModuleId=="pdf","The shared PDF viewer renders the real64-page native export");
            var viewer=(BuiltinViewer)session.ViewerForVerification;
            var number=PreviewChildren<TextBox>(body).Single(box=>AutomationProperties.GetName(box)=="Page number");
            byte[] first=OfficePixels(body);
            void Enter(string value)
            {
                number.Text=value;
                number.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(number),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
            }
            Enter("64");await viewer.Ready;
            check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 64 of 64")&&!first.SequenceEqual(OfficePixels(body)),"Actual page-number input renders the last page's different content");
            check(!PreviewChildren<Button>(body).Single(button=>AutomationProperties.GetName(button)=="Next page").IsEnabled,"Next page is disabled at the actual document boundary");
            Enter("65");
            check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Enter a page from 1 to 64"),"Out-of-range navigation reports the valid page range");
            Enter("1");await viewer.Ready;
            check(first.SequenceEqual(OfficePixels(body)),"Returning to the first page restores its actual pixels");
            var next=PreviewChildren<Button>(body).Single(button=>AutomationProperties.GetName(button)=="Next page");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await viewer.Ready;
            check(PreviewChildren<TextBlock>(body).Any(text=>text.Text=="Page 2 of 64"),"The existing next-page icon retains navigation");
            await S1Verification.Snapshot(window,root,"pdf-page-navigation");
            session.Clear();await PreviewSession.DrainAsync();
            check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"PDF navigation preserves the document bytes");File.Move(path,path+".released");
            check(HiddenTrialPresentation.ForegroundViolations==0,"Shared PDF navigation performs no foreground takeover");
        }
        finally{session.Clear();await PreviewSession.DrainAsync();window.Close();}
    }
    static byte[] OfficePixels(DependencyObject body)
    {
        var bitmap=(BitmapSource)PreviewChildren<Image>(body).Single(image=>image.Source is BitmapSource).Source;
        int stride=(bitmap.PixelWidth*bitmap.Format.BitsPerPixel+7)/8;
        var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);return SHA256.HashData(pixels);
    }
}
#endif
