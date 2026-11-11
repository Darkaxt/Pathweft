#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Controls;

namespace FileExplorer;

// CRC32 column: an archive's stored values without extraction, and the opt-in lazy folder hashing.
public partial class MainWindow
{
    async Task VerifyCrcColumn(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var keys=(Dictionary<GridViewColumn,string>)typeof(PaneView).GetField("colKey",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Left);
        bool Shown()=>((GridView)Left.List.View).Columns.Any(c=>keys.GetValueOrDefault(c)=="CRC32");
        string Crc(string name)=>Left.Items.Single(e=>e.Name==name).CrcText;
        string folder=Path.Combine(root,"crc"),zip=Path.Combine(root,"crc.zip");
        Directory.CreateDirectory(Path.Combine(folder,"sub"));
        File.WriteAllText(Path.Combine(folder,"check.txt"),"123456789",Encoding.ASCII); // the CRC-32 check value: CBF43926
        File.WriteAllText(Path.Combine(folder,"hello.txt"),"hello",Encoding.ASCII);     // 3610A686
        File.WriteAllBytes(Path.Combine(folder,"empty.bin"),Array.Empty<byte>());       // 00000000
        ZipFile.CreateFromDirectory(folder,zip);
        var listed=Archive.List(zip,"").ToDictionary(i=>i.Name);
        check(listed["check.txt"].Crc==0xCBF43926&&listed["hello.txt"].Crc==0x3610A686&&listed["empty.bin"].Crc==0&&listed["sub"].Crc==null,"Archive listing carries each entry's stored CRC32; folders have none");
        SetSinglePane(true);SetActive(Left);Settings.ArchiveCrc=true;Settings.FolderCrc=false;
        await S1Verification.Navigate(Left,zip);await Settle();
        check(Shown()&&Crc("check.txt")=="CBF43926"&&Crc("empty.bin")=="00000000"&&Crc("sub")=="","Archive view shows the CRC32 column by default as 8 uppercase hex digits, blank for folders");
        Left.SetSort("CRC32",false);await Settle();
        check(Left.List.Items.Cast<Entry>().Where(e=>!e.IsDir).Select(e=>e.Name).SequenceEqual(new[]{"empty.bin","hello.txt","check.txt"}),"CRC32 sorts numerically");
        Left.SetSort("Name",false);Left.ShowCrc(false);await Settle();
        check(!Shown()&&!Settings.ArchiveCrc,"Removing CRC32 inside an archive is remembered");
        Left.ShowCrc(true);await Settle();
        await S1Verification.Navigate(Left,folder);await Settle();
        check(!Shown()&&Left.Items.All(e=>e.Crc==null),"Folders hide CRC32 by default and hash nothing");
        Left.ShowCrc(true);await Settle();await Left.CrcCompletion;await Settle();
        check(Shown()&&Crc("check.txt")=="CBF43926"&&Crc("hello.txt")=="3610A686"&&Crc("empty.bin")=="00000000"&&Crc("sub")=="","Opted-in folder files are hashed in the background and match the archive's stored values");
        File.WriteAllText(Path.Combine(folder,"hello.txt"),"hello!",Encoding.ASCII);File.SetLastWriteTimeUtc(Path.Combine(folder,"hello.txt"),DateTime.UtcNow.AddMinutes(1));
        check(FileCrc.Get(Path.Combine(folder,"hello.txt"),CancellationToken.None)==0x9A86C960u,"A rewritten file is not served its cached CRC32");
        Left.ShowCrc(false);await Settle();
        check(!Shown()&&!Settings.FolderCrc,"Hiding CRC32 in folders stops the column");
    }
}
#endif
