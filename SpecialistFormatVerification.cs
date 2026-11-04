#if DEBUG
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FileExplorer.Preview;
using LiteDB;
using Microsoft.Data.Sqlite;
using OpenMcdf;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifySpecialistFormats13(string root,Action<bool,string> check)
    {
        var body=new ContentControl();var window=new Window{Content=body,Width=640,Height=700,ShowInTaskbar=false};HiddenTrialPresentation.Prepare(window);window.Show();
        using var session=new PreviewSession(this,body,_=>{});
        async Task Show(string path,string id,string state="ready")
        {session.Show(new Entry{Path=path,Name=Path.GetFileName(path)},id);await session.Completion;check(session.State==state,"Integrated format/protection outcome: "+Path.GetFileName(path)+" / "+session.State+" / "+(body.Content as TextBlock)?.Text);session.Clear();await PreviewSession.DrainAsync();}
        void Zip(string file,string entry,byte[] bytes)
        {using var archive=ZipFile.Open(file,ZipArchiveMode.Create);using var output=archive.CreateEntry(entry).Open();output.Write(bytes);}
        try
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            string live=Path.Combine(root,"wal-writer.sqlite"),snapshot=Path.Combine(root,"wal-records.sqlite");
            using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=live,Pooling=false}.ToString()))
            {
                connection.Open();using var query=connection.CreateCommand();query.CommandText="PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE records(value TEXT); INSERT INTO records VALUES ('committed WAL é 子')";query.ExecuteNonQuery();
                File.Copy(live,snapshot);File.Copy(live+"-wal",snapshot+"-wal");File.Copy(live+"-shm",snapshot+"-shm");
            }
            string[] members={snapshot,snapshot+"-wal",snapshot+"-shm"};var hashes=members.Select(file=>SHA256.HashData(File.ReadAllBytes(file))).ToArray();
            await Show(snapshot,"database");
            check(DatabaseViewer.ReadTable(snapshot,"records",true,CancellationToken.None).Rows.Single()[0]=="committed WAL é 子","SQLite preview includes committed WAL-only rows in its isolated snapshot");
            for(int index=0;index<members.Length;index++){check(hashes[index].SequenceEqual(SHA256.HashData(File.ReadAllBytes(members[index]))),"SQLite source and original sidecar remain byte-identical: "+Path.GetFileName(members[index]));File.Move(members[index],members[index]+".released");}
            string encrypted=Path.Combine(root,"protected.litedb");using(var database=new LiteDatabase(new ConnectionString{Filename=encrypted,Password="fixture-secret"}))database.GetCollection("records").Insert(new BsonDocument{{"value",42}});
            byte[] encryptedHash=SHA256.HashData(File.ReadAllBytes(encrypted));await Show(encrypted,"database","error");check(encryptedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(encrypted))),"Encrypted database rejection preserves the original");
            string large=Path.Combine(root,"large.sqlite");using(var file=File.Create(large)){file.Write("SQLite format 3\0"u8);file.SetLength(256L*1024*1024+1);}await Show(large,"database","error");
            string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
            string ipa=Path.Combine(root,"binary-plist.ipa");Zip(ipa,"Payload/Fixture.app/Info.plist",File.ReadAllBytes(Path.Combine(inputs,"identity.bplist")));await Show(ipa,"app");
            check(PackageInspection.Read(ipa,CancellationToken.None).Any(row=>row.Name=="CFBundleIdentifier"&&row.Value=="org.pathweft.binary"),"Real binary plist IPA identity is decoded from its object table");
            foreach(string extension in new[]{"appxbundle","msixbundle"})
            {string bundle=Path.Combine(root,"fixture."+extension);Zip(bundle,"AppxMetadata/AppxBundleManifest.xml",Encoding.UTF8.GetBytes("<Bundle><Identity Name='Pathweft.Bundle' Version='1.2.3.4' Publisher='CN=Fixture'/><Packages><Package FileName='Fixture.msix'/></Packages></Bundle>"));await Show(bundle,"app");}
            string pfx=Path.Combine(root,"protected.pfx");
            try{CertificateViewer.Read(pfx,"incorrect",CancellationToken.None);check(false,"Incorrect protected certificate password must fail");}catch(CryptographicException){check(true,"Incorrect protected certificate password fails explicitly");}
            string invalid=Path.Combine(root,"invalid.cer");File.WriteAllText(invalid,"invalid certificate");await Show(invalid,"certificate","error");
            foreach(bool compressed in new[]{false,true})
            {
                string msg=Path.Combine(root,compressed?"compressed-rtf.msg":"html-body.msg");
                using(var storage=RootStorage.Create(msg,OpenMcdf.Version.V3))
                {
                    using(var subject=storage.CreateStream("__substg1.0_0037001F"))subject.Write(Encoding.Unicode.GetBytes("Body variant\0"));
                    byte[] bytes;
                    if(compressed)
                    {
                        byte[] raw=Encoding.ASCII.GetBytes("{\\rtf1\\ansi Actual compressed body}");using var payload=new MemoryStream();
                        for(int at=0;at<raw.Length;at+=8){payload.WriteByte(0);payload.Write(raw,at,Math.Min(8,raw.Length-at));}
                        bytes=new byte[payload.Length+16];payload.ToArray().CopyTo(bytes,16);
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes,(uint)bytes.Length-4);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),(uint)raw.Length);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),0x75465a4c);
                        uint crc=0;foreach(byte value in bytes.AsSpan(16)){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12),crc);
                        check(MailViewer.DecompressRtf(bytes,CancellationToken.None).SequenceEqual(raw),"Compressed RTF literal run returns the actual formatted source");
                        var corrupt=(byte[])bytes.Clone();corrupt[^1]^=1;try{MailViewer.DecompressRtf(corrupt,CancellationToken.None);check(false,"Corrupted RTF must fail");}catch(InvalidDataException){check(true,"Compressed RTF checksum rejects corrupted body");}
                    }
                    else bytes=Encoding.UTF8.GetBytes("<h1>Actual MSG HTML body</h1><script>window.executed=1</script>");
                    using var stream=storage.CreateStream(compressed?"__substg1.0_10090102":"__substg1.0_10130102");stream.Write(bytes);
                }
                await Show(msg,"mail");check(MailViewer.Read(msg,CancellationToken.None).Html.Contains(compressed?"Actual compressed body":"Actual MSG HTML body"),"MSG structured body variant retains meaningful content");
            }
            session.Show(new Entry{Path=Path.Combine(root,"audio.wav"),Name="audio.wav"});await session.Completion;
            session.Menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var technical=session.Menu.Items.OfType<MenuItem>().Single(item=>item.Header.ToString()=="Media information");technical.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await session.Completion;
            check(session.ModuleId=="mediainfo"&&session.State=="ready","Actual preview menu changes playback into the technical MediaInfo view");session.Clear();await PreviewSession.DrainAsync();
            if(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_MEDIA_INPUTS") is {Length:>0} inventory)
            {
                using var mediaInputs=System.Text.Json.JsonDocument.Parse(File.ReadAllText(inventory));
                foreach(var input in mediaInputs.RootElement.EnumerateArray())
                {
                    string name=input.GetProperty("name").GetString(),path=input.GetProperty("path").GetString();
                    await Show(path,"mediainfo");var fields=InspectionViewer.Read("mediainfo",path,CancellationToken.None);
                    check(fields.Any(row=>row.Name=="Inspector library"&&row.Value.Contains("26.10")),"Actual native inspector reports its qualified26.10 identity");
                    if(name.StartsWith("p",StringComparison.Ordinal))check(fields.Any(row=>row.Name.EndsWith("HDR_Format")&&row.Value.Contains("Dolby Vision")),"Technical report identifies actual Dolby Vision metadata independently of playback: "+name);
                    File.WriteAllText(Path.Combine(root,name+"-mediainfo.json"),System.Text.Json.JsonSerializer.Serialize(fields.Select(row=>new{row.Name,row.Value})));
                }
            }
            var clsid=new Entry{Path="::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",Name="This PC"};session.Show(clsid);await session.Completion;Settings.DisabledPreviewModules.Add("clsid");
            try{session.Refresh();await session.Completion;check(session.ModuleId!="clsid","Disabling the active Shell inspector releases it without activating another class");}finally{Settings.DisabledPreviewModules.Remove("clsid");}
        }
        finally{session.Clear();await PreviewSession.DrainAsync();window.Close();}
    }
}
#endif
