// Selected package fields/manifest flow adapted from QuickLook AppViewer.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FileExplorer.Preview;

internal static class PackageInspection
{
    internal static List<(string Name,string Value)> Read(string path,CancellationToken token)
    {
        var info=new FileInfo(path);if(info.Length>2L*1024*1024*1024)throw new IOException("Package preview supports files up to2GiB.");
        string extension=Path.GetExtension(path).ToLowerInvariant();
        var rows=new List<(string,string)>{("Package",Path.GetFileName(path)),("Size",MainWindow.Fmt(info.Length)),("Inspection","Metadata only; package is not installed or executed")};
        void Add(string name,object value){string text=Convert.ToString(value);if(!string.IsNullOrEmpty(text))rows.Add((name,text));}
        if(extension==".apk")
        {
            using var input=OpenAndroid(path,token);var data=new ApkReader.ApkReader().Read(input);token.ThrowIfCancellationRequested();
            Add("Identity",data.PackageName);Add("Name",data.Label);Add("Version",data.VersionName);Add("Version code",data.VersionCode);Add("Minimum Android SDK",data.MinSdkVersion);Add("Target Android SDK",data.TargetSdkVersion);
            foreach(string permission in data.Permissions??Enumerable.Empty<string>())Add("Permission",permission);
            Add("Icon entry",data.Icons.Values.Concat(new[]{data.Icon}).FirstOrDefault(value=>value?.EndsWith(".png",StringComparison.OrdinalIgnoreCase)==true));
            Add("Architectures",string.Join(", ",data.Abis));
            if(string.IsNullOrEmpty(data.PackageName))throw new InvalidDataException("No Android package identity was found.");return rows;
        }
        if(extension==".aab")
        {
            using var input=OpenAndroid(path,token);var data=new ApkReader.AabReader().Read(input);token.ThrowIfCancellationRequested();
            Add("Identity",data.PackageName);Add("Name",data.Label);Add("Version",data.VersionName);Add("Version code",data.VersionCode);Add("Minimum Android SDK",data.MinSdkVersion);Add("Target Android SDK",data.TargetSdkVersion);
            foreach(string permission in data.Permissions??Enumerable.Empty<string>())Add("Permission",permission);
            Add("Icon entry",data.Icons.Values.Concat(new[]{data.Icon}).FirstOrDefault(value=>value?.EndsWith(".png",StringComparison.OrdinalIgnoreCase)==true));
            Add("Architectures",string.Join(", ",data.Abis));
            if(string.IsNullOrEmpty(data.PackageName))throw new InvalidDataException("No Android bundle identity was found.");return rows;
        }
        if(extension is ".msi" or ".msp"){ReadInstaller(path,extension==".msp",rows,token);return rows;}
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var archive=new ZipArchive(file,ZipArchiveMode.Read,false);
        if(archive.Entries.Count>100_000)throw new IOException("Package exceeds100,000 preview entries.");
        Add("Entries",archive.Entries.Count);
        byte[] Read(ZipArchiveEntry entry)
        {
            if(entry==null)throw new InvalidDataException("Required package manifest is missing.");
            if(entry.Length>16*1024*1024)throw new IOException("Package manifest exceeds16MiB.");
            using var input=entry.Open();return PreviewInput.Read(input,16*1024*1024,token);
        }
        XDocument Xml(byte[] bytes){using var reader=XmlReader.Create(new MemoryStream(bytes,false),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});return XDocument.Load(reader);}
        if(extension==".ipa")
        {
            var entry=archive.Entries.FirstOrDefault(e=>e.FullName.StartsWith("Payload/",StringComparison.Ordinal)&&e.FullName.Count(c=>c=='/')==2&&e.FullName.EndsWith(".app/Info.plist",StringComparison.Ordinal));
            var plist=PlistMetadata.Read(Read(entry));
            foreach(string key in new[]{"CFBundleIdentifier","CFBundleName","CFBundleDisplayName","CFBundleShortVersionString","CFBundleVersion","MinimumOSVersion","UIDeviceFamily","UIRequiredDeviceCapabilities"})
                if(plist.TryGetValue(key,out var value))Add(key,PlistMetadata.Value(value));return rows;
        }
        if(extension is ".nupkg" or ".snupkg")
        {
            var document=Xml(Read(archive.Entries.FirstOrDefault(e=>e.FullName.EndsWith(".nuspec",StringComparison.OrdinalIgnoreCase))));
            var metadata=document.Descendants().FirstOrDefault(e=>e.Name.LocalName=="metadata")??throw new InvalidDataException("NuGet package metadata is missing.");
            foreach(var element in metadata.Elements())if(!element.HasElements)Add(element.Name.LocalName,element.Value);
            Add("Icon entry",metadata.Elements().FirstOrDefault(e=>e.Name.LocalName=="icon")?.Value);
            foreach(var dependency in metadata.Descendants().Where(e=>e.Name.LocalName=="dependency").Take(1000))Add("Dependency",$"{dependency.Attribute("id")?.Value} {dependency.Attribute("version")?.Value}");
            return rows;
        }
        var manifest=archive.Entries.FirstOrDefault(e=>e.FullName.Equals("AppxManifest.xml",StringComparison.OrdinalIgnoreCase)||e.FullName.Equals("AppxMetadata/AppxBundleManifest.xml",StringComparison.OrdinalIgnoreCase));
        var xml=Xml(Read(manifest));var identity=xml.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Identity")??throw new InvalidDataException("Windows package identity is missing.");
        foreach(var attribute in identity.Attributes())Add(attribute.Name.LocalName,attribute.Value);
        string logo=xml.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Logo")?.Value;
        if(logo!=null)Add("Icon entry",logo.Replace('\\','/'));
        foreach(var property in xml.Descendants().Where(e=>e.Name.LocalName is "DisplayName" or "PublisherDisplayName" or "Description" or "Capability" or "DeviceCapability").Take(1000))Add(property.Name.LocalName,property.Attribute("Name")?.Value??property.Value);
        return rows;
    }
    static FileStream OpenAndroid(string path,CancellationToken token)
    {
        var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try
        {
            // The donor reads these resources into memory. Qualify their aggregate
            // uncompressed size before handing the same stable source to it.
            using(var zip=new ZipArchive(input,ZipArchiveMode.Read,true))
            {
                if(zip.Entries.Count>100_000)throw new IOException("Package exceeds 100,000 preview entries.");
                long total=0;
                foreach(var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();string name=entry.FullName;
                    if(name is "AndroidManifest.xml" or "resources.arsc" or "base/manifest/AndroidManifest.xml" or "base/resources.pb"||name.StartsWith("base/res/mipmap-",StringComparison.Ordinal))
                    {total=checked(total+entry.Length);if(entry.Length>32*1024*1024||total>64*1024*1024)throw new IOException("Android manifest/resources exceed the bounded preview budget.");}
                }
            }
            input.Position=0;return input;
        }
        catch{input.Dispose();throw;}
    }
    internal static byte[] Icon(string path,IReadOnlyList<(string Name,string Value)> rows,CancellationToken token)
    {
        string name=rows.FirstOrDefault(row=>row.Name=="Icon entry").Value;
        if(string.IsNullOrWhiteSpace(name))return null;
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        var entry=zip.GetEntry(name);
        if(entry==null||entry.Length>8*1024*1024)return null;
        using var image=entry.Open();return PreviewInput.Read(image,8*1024*1024,token);
    }
    static void ReadInstaller(string path,bool patch,List<(string,string)> rows,CancellationToken token)
    {
        uint database=0,view=0,record=0,summary=0;
        static void Check(uint result){if(result!=0)throw new IOException("Windows Installer metadata could not be read ("+result+").");}
        string Field(uint handle,uint field)
        {
            uint size=0;uint code=MsiRecordGetString(handle,field,null,ref size);if(code!=0&&code!=234)Check(code);
            if(size>1_048_576)throw new IOException("Installer metadata field exceeds its preview budget.");
            var text=new StringBuilder((int)size+1);size++;Check(MsiRecordGetString(handle,field,text,ref size));return text.ToString();
        }
        try
        {
            if(!patch)
            {
                Check(MsiOpenDatabase(path,IntPtr.Zero,out database)); // MSIDBOPEN_READONLY
                Check(MsiDatabaseOpenView(database,"SELECT `Property`,`Value` FROM `Property`",out view));Check(MsiViewExecute(view,0));
                for(int i=0;i<4096;i++)
                {
                    token.ThrowIfCancellationRequested();uint status=MsiViewFetch(view,out record);if(status==259)break;Check(status);
                    try{string name=Field(record,1);if(name is "ProductName" or "ProductVersion" or "Manufacturer" or "ProductCode" or "UpgradeCode" or "ProductLanguage")rows.Add((name,Field(record,2)));}
                    finally{MsiCloseHandle(record);record=0;}
                }
            }
            Check(MsiGetSummaryInformation(database,path,0,out summary));
            foreach(var (property,name) in new[]{(2u,"Title"),(3u,"Subject"),(4u,"Author"),(6u,"Comments"),(7u,"Template"),(9u,"Revision")})
            {
                token.ThrowIfCancellationRequested();uint length=8192;var text=new StringBuilder((int)length);
                uint status=MsiSummaryInfoGetProperty(summary,property,out _,out _,out _,text,ref length);if(status==0&&text.Length>0)rows.Add((name,text.ToString()));
            }
        }
        finally{if(record!=0)MsiCloseHandle(record);if(view!=0){MsiViewClose(view);MsiCloseHandle(view);}if(summary!=0)MsiCloseHandle(summary);if(database!=0)MsiCloseHandle(database);}
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiOpenDatabaseW")]static extern uint MsiOpenDatabase(string path,IntPtr persist,out uint database);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiDatabaseOpenViewW")]static extern uint MsiDatabaseOpenView(uint database,string query,out uint view);
    [DllImport("msi.dll")]static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")]static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll")]static extern uint MsiViewClose(uint view);
    [DllImport("msi.dll")]static extern uint MsiCloseHandle(uint handle);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiRecordGetStringW")]static extern uint MsiRecordGetString(uint record,uint field,StringBuilder text,ref uint length);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiGetSummaryInformationW")]static extern uint MsiGetSummaryInformation(uint database,string path,uint updateCount,out uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiSummaryInfoGetPropertyW")]static extern uint MsiSummaryInfoGetProperty(uint summary,uint property,out uint type,out int value,out System.Runtime.InteropServices.ComTypes.FILETIME fileTime,StringBuilder text,ref uint length);
}
