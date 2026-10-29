using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Diagnostics;
using FileExplorer;

// Diagnostic reproductions during implementation. Complete S5 acceptance also
// requires the connected WPF/action/archive callers and preservation workflows.
if(args.Length==2 && args[0]=="--crash-child") return TransferChecks.CrashChild(args[1]);
if (args.Length != 2 || args[0] is not ("--probe-outcomes" or "--contracts" or "--full-volume" or "--restart" or "--allocation-probe" or "--volume-alias" or "--allocation-breakdown" or "--release-audit" or "--storage-probe" or "--storage-fixture")) return 2;
string root = Path.GetFullPath(args[1]);
if (!root.StartsWith(@"D:\Temp\pathweft-s5-native\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root)) return 2;
Directory.CreateDirectory(root);
if(args[0]=="--storage-probe") return TransferChecks.StorageProbe(root);
if(args[0]=="--storage-fixture"){TransferChecks.StorageFixture(root);return 0;}
if(args[0]=="--restart") return await TransferChecks.Restart(root);
if(args[0]=="--release-audit")
{
    var assembly=typeof(Ferry).Assembly;
    string product=@"D:\Temp\pathweft-s5-native\release-build\artifacts\bin\FileExplorer\release";
    bool noHooks=assembly.GetType("FileExplorer.S5Benchmark")==null&&assembly.GetType("FileExplorer.HiddenTrialPresentation")==null&&typeof(Ferry).GetField("VerificationBoundary",BindingFlags.NonPublic|BindingFlags.Static)==null&&typeof(MainWindow).GetMethod("VerifyS5",BindingFlags.NonPublic|BindingFlags.Instance)==null;
    var forbidden=new HashSet<string>(new[]{"TeraCopy.exe","TeraCopy.dll","BTCR.dll","Blake3.dll","xxHashAVX2.dll","xxHashSSE2.dll"},StringComparer.OrdinalIgnoreCase);
    var donorAssets=Directory.GetFiles(product,"*",SearchOption.AllDirectories).Where(file=>forbidden.Contains(Path.GetFileName(file))).ToArray();
    var references=assembly.GetReferencedAssemblies().Select(reference=>reference.Name).ToArray();
    string target=Path.Combine(product,"Pathweft.dll");bool matching=SHA256.HashData(File.ReadAllBytes(assembly.Location)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(target)));
    bool clean=matching&&noHooks&&donorAssets.Length==0&&!references.Any(name=>name!.Contains("TeraCopy",StringComparison.OrdinalIgnoreCase));
    File.WriteAllText(Path.Combine(root,"release-audit.json"),JsonSerializer.Serialize(new{passed=clean,noTestHooks=noHooks,actualReleaseAssemblyMatches=matching,donorAssets,references,releaseSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))},new JsonSerializerOptions{WriteIndented=true}));return clean?0:1;
}
if(args[0]=="--allocation-breakdown")
{
    string input=Path.Combine(root,"item.txt");File.WriteAllText(input,"a small file");
    var type=typeof(Ferry).Assembly.GetType("FileExplorer.TransferFile",true)!;var measurements=new List<object>();
    foreach(string methodName in new[]{"NamedStreams","Streams","Inspect"})
    {
        var method=type.GetMethod(methodName,BindingFlags.Static|BindingFlags.NonPublic)!;object[] parameters=methodName=="Streams"?new object[]{input,false}:new object[]{input};
        method.Invoke(null,parameters);long before=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
        for(int i=0;i<1000;i++)method.Invoke(null,parameters);clock.Stop();
        measurements.Add(new{method=methodName,calls=1000,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before,wallMilliseconds=clock.Elapsed.TotalMilliseconds});
    }
    File.WriteAllText(Path.Combine(root,"breakdown.json"),JsonSerializer.Serialize(measurements));return 0;
}
if(args[0]=="--volume-alias")
{
    string physical=Directory.CreateDirectory(Path.Combine(@"E:\Temp\pathweft-s5-review",Path.GetFileName(root))).FullName;
    string link=Path.Combine(root,"alias"), target=Directory.CreateDirectory(Path.Combine(root,"target")).FullName;
    Directory.CreateSymbolicLink(link,physical);string aliasSource=Path.Combine(link,"item.txt");File.WriteAllText(aliasSource,"other volume behind a selected parent path");
    var job=Ferry.Submit(new[]{aliasSource},target,true);await job.Completion;
    bool pass=job.Successful&&job.VerifiedFiles==1&&!File.Exists(aliasSource)&&File.ReadAllText(Path.Combine(target,"item.txt"))=="other volume behind a selected parent path";
    File.WriteAllText(Path.Combine(root,"volume-alias.json"),JsonSerializer.Serialize(new{passed=pass,sourceRoot=Path.GetPathRoot(aliasSource),targetRoot=Path.GetPathRoot(target),phase=job.Phase.ToString(),job.VerifiedFiles,errors=job.Errors.ToArray()}));return pass?0:1;
}
if(args[0]=="--allocation-probe")
{
    Settings.Verify=true;Settings.ParallelCopy=true;
    long before=GC.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();
    var measured=Ferry.Submit(new[]{@"D:\Temp\pathweft-s5-native\workloads\small"},root,false);await measured.Completion;clock.Stop();
    File.WriteAllText(Path.Combine(root,"allocation.json"),JsonSerializer.Serialize(new{allocatedBytes=GC.GetTotalAllocatedBytes(true)-before,wallMilliseconds=clock.Elapsed.TotalMilliseconds,measured.TotalFiles,measured.DoneFiles,phase=measured.Phase.ToString(),errors=measured.Errors.ToArray()}));
    return measured.Successful?0:1;
}
if(args[0]=="--full-volume") return await TransferChecks.FullVolume(root);
if (args[0] == "--contracts") return await TransferChecks.Run(root);
string source = Path.Combine(root, "source"), destination = Path.Combine(root, "destination");
Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
File.WriteAllText(Path.Combine(source,"item.txt"),"new content");
File.WriteAllText(Path.Combine(destination,"item.txt"),"existing content");
Settings.Verify = true;
var skip = Ferry.Submit(new[] { Path.Combine(source,"item.txt") },destination,false);
skip.Resolve(Choice.Skip);
await skip.Completion;
var failed = Ferry.Submit(new[] { Path.Combine(source,"absent.txt") },destination,false);
await failed.Completion;
var results = new[] {
    new { contract="Skipped member remains in planned total and explicit skipped outcome", passed=skip.SkippedFiles==1 && skip.TotalFiles==1 && skip.DoneFiles==0 && skip.Title.Contains("skipped",StringComparison.OrdinalIgnoreCase), phase=skip.Phase.ToString(),skip.Title,skip.TotalFiles,skip.DoneFiles,skip.SkippedFiles,errors=skip.Errors.ToArray() },
    new { contract="Unreadable/missing source is failed, not a wholly successful copy", passed=failed.Phase.ToString()=="Failed" && !failed.Title.StartsWith("Copied",StringComparison.OrdinalIgnoreCase), phase=failed.Phase.ToString(),failed.Title,failed.TotalFiles,failed.DoneFiles,failed.SkippedFiles,errors=failed.Errors.ToArray() }
};
File.WriteAllText(Path.Combine(root,"outcomes.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(results));
return results.All(result=>result.passed)?0:1;

internal static class TransferChecks
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLinkW(string name,string existing,IntPtr security);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle file,uint code,byte[] input,int inputBytes,IntPtr output,int outputBytes,out int returned,IntPtr overlapped);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetCompressedFileSizeW(string path,out uint high);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    delegate uint CopyProgress(long total,long transferred,long streamSize,long streamTransferred,uint stream,uint reason,IntPtr source,IntPtr target,IntPtr state);
    delegate uint Copy2Progress(IntPtr message,IntPtr context);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CopyFileExW(string source,string target,CopyProgress progress,IntPtr state,IntPtr cancel,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushFileBuffers(IntPtr file);
    [StructLayout(LayoutKind.Sequential)] struct CopyParameters{internal uint Size,Flags;internal IntPtr Cancel,Callback,Context;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int CopyFile2(string source,string target,ref CopyParameters parameters);
    static void StorageState(FileStream file,uint control,byte[] input)
    {
        if(!DeviceIoControl(file.SafeFileHandle,control,input,input?.Length??0,IntPtr.Zero,0,out _,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    static long Allocated(string file){uint low=GetCompressedFileSizeW(file,out uint high);if(low==uint.MaxValue)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return ((long)high<<32)|low;}
    internal static (string Compressed,string Sparse) StorageFixture(string root)
    {
        string compressed=Path.Combine(root,"compressed.bin"),sparse=Path.Combine(root,"sparse.bin");
        using(var file=new FileStream(compressed,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){StorageState(file,0x9c040,new byte[]{1,0});file.Write(Enumerable.Repeat((byte)73,2*1024*1024+17).ToArray());file.Flush(true);}
        using(var file=new FileStream(sparse,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){StorageState(file,0x900c4,null!);file.SetLength(8*1024*1024+31);file.WriteByte(11);file.Position=file.Length-1;file.WriteByte(29);file.Flush(true);}
        if(!File.GetAttributes(compressed).HasFlag(FileAttributes.Compressed)||!File.GetAttributes(sparse).HasFlag(FileAttributes.SparseFile)||Allocated(sparse)>=new FileInfo(sparse).Length/2)throw new IOException("Storage fixture was not qualified.");
        return (compressed,sparse);
    }
    internal static int StorageProbe(string root)
    {
        string source=Directory.CreateDirectory(Path.Combine(root,"source")).FullName,target=Directory.CreateDirectory(Path.Combine(root,"target")).FullName;
        var files=StorageFixture(source);string empty=Path.Combine(source,"empty.bin");
        using(var file=new FileStream(empty,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){StorageState(file,0x9c040,new byte[]{1,0});}
        File.SetAttributes(files.Compressed,File.GetAttributes(files.Compressed)|FileAttributes.ReadOnly);
        var rows=new List<object>();
        foreach(var item in new[]{("compressed-default",files.Compressed,1u,false),("compressed-callback",files.Compressed,1u,true),("compressed-empty",empty,1u,true),("sparse-default",files.Sparse,1u,false),("sparse-callback",files.Sparse,1u,true),("sparse-enabled",files.Sparse,0x20000001u,false),("sparse-copy2",files.Sparse,0x20000001u,false)})
        {
            string output=Path.Combine(target,item.Item1+".bin");int callbackError=0,callbacks=0,controls=0;bool configured=false;var messages=new List<object>();bool? sourceSharing=null;
            CopyProgress callback=(total,transferred,streamSize,streamTransferred,stream,reason,src,dst,state)=>
            {
                callbacks++;
                if(item.Item4&&stream==1&&!configured&&!dst.Equals(IntPtr.Zero))
                {
                    configured=true;controls++;
                    using var handle=new Microsoft.Win32.SafeHandles.SafeFileHandle(dst,false);
                    byte[] input=item.Item2==files.Sparse?null!:new byte[]{1,0};uint control=item.Item2==files.Sparse?0x900c4u:0x9c040u;
                    if(!DeviceIoControl(handle,control,input,input?.Length??0,IntPtr.Zero,0,out _,IntPtr.Zero)){callbackError=Marshal.GetLastWin32Error();return 1;}
                }
                // Diagnostic allocation qualification only; never a product flush policy.
                if(transferred==total&&!FlushFileBuffers(dst)){callbackError=Marshal.GetLastWin32Error();return 1;}
                return 0;
            };
            bool ok;int error;
            if(item.Item1=="sparse-copy2")
            {
                Copy2Progress progress=(message,_)=>
                {
                    int type=Marshal.ReadInt32(message);long total=type==2?Marshal.ReadInt64(message,72):type==4?Marshal.ReadInt64(message,56):-1;
                    messages.Add(new{type,total,thread=Environment.CurrentManagedThreadId});callbacks++;
                    if(type==3&&sourceSharing==null){try{using var shared=new FileStream(item.Item2,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);sourceSharing=true;}catch(IOException){sourceSharing=false;}}
                    return 0;
                };
                var parameters=new CopyParameters{Size=(uint)Marshal.SizeOf<CopyParameters>(),Flags=item.Item3,Callback=Marshal.GetFunctionPointerForDelegate(progress)};
                int result=CopyFile2(item.Item2,output,ref parameters);ok=result>=0;error=result;GC.KeepAlive(progress);
                if(ok){using var flush=new FileStream(output,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);flush.Flush(true);}
            }
            else {ok=CopyFileExW(item.Item2,output,callback,IntPtr.Zero,IntPtr.Zero,item.Item3);error=ok?0:Marshal.GetLastWin32Error();}
            GC.KeepAlive(callback);
            bool exists=File.Exists(output),same=false;if(exists){using var input=File.OpenRead(item.Item2);using var result=File.OpenRead(output);same=SHA256.HashData(input).SequenceEqual(SHA256.HashData(result));}
            rows.Add(new{test=item.Item1,ok,error,callbackError,callbacks,controls,sourceSharing,messages,sourceAttributes=File.GetAttributes(item.Item2).ToString(),sourceAllocated=Allocated(item.Item2),destinationAttributes=exists?File.GetAttributes(output).ToString():null,destinationAllocated=exists?Allocated(output):-1,dataMatches=same});
        }
        File.WriteAllText(Path.Combine(root,"storage-probe.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(rows));return 0;
    }
    sealed record Result(string Contract, bool Passed, string Error = null);
    internal static int CrashChild(string root)
    {
        root=Path.GetFullPath(root);
        if(!root.StartsWith(@"D:\Temp\pathweft-s5-native\restart-",StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(root,"source","item.bin")))return 2;
        typeof(Ferry).GetField("VerificationBoundary",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,(Action<string,Job,string,string>)((phase,_,_,_)=>{if(phase=="copy-chunk")Environment.Exit(23);}));
        var job=Ferry.Submit(new[]{Path.Combine(root,"source","item.bin")},Path.Combine(root,"target"),false);job.Resolve(Choice.Overwrite);job.Completion.GetAwaiter().GetResult();return 1;
    }
    internal static async Task<int> Restart(string root)
    {
        string source=Directory.CreateDirectory(Path.Combine(root,"source")).FullName,target=Directory.CreateDirectory(Path.Combine(root,"target")).FullName;
        string file=Path.Combine(source,"item.bin"), old=Path.Combine(target,"item.bin");File.WriteAllBytes(file,Enumerable.Repeat((byte)123,8*1024*1024).ToArray());File.WriteAllText(old,"existing");
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--crash-child");start.ArgumentList.Add(root);
        using var child=Process.Start(start)!;await child.WaitForExitAsync();
        string[] orphaned=Directory.GetFiles(target,".pathweft-transfer-*.partial");
        bool sourcePreserved=child.ExitCode==23&&File.ReadAllBytes(file).All(value=>value==123);
        bool incomplete=File.Exists(old)&&!File.ReadAllBytes(old).SequenceEqual(File.ReadAllBytes(file))&&orphaned.Length==0;
        Settings.Verify=true;var fresh=Ferry.Submit(new[]{file},target,false);fresh.Resolve(Choice.Overwrite);await fresh.Completion;
        using var a=File.OpenRead(file);using var b=File.OpenRead(old);bool same=SHA256.HashData(a).SequenceEqual(SHA256.HashData(b));
        bool resumed=fresh.Successful&&fresh.DoneBytes==fresh.TotalBytes&&same;
        File.WriteAllText(Path.Combine(root,"restart.json"),JsonSerializer.Serialize(new{childPid=child.Id,childExit=child.ExitCode,sourcePreserved,incompleteDestinationObserved=incomplete,freshSubmissionPassed=resumed,orphanedStagePaths=orphaned,phase=fresh.Phase.ToString(),fresh.DoneBytes,fresh.TotalBytes,fresh.VerifiedFiles,guarantee="A crash can leave incomplete direct output. No automatic recovery or offset continuation; manual resubmission overwrites from zero. Source data is retained."},new JsonSerializerOptions{WriteIndented=true}));
        return sourcePreserved&&incomplete&&resumed?0:1;
    }
    internal static async Task<int> FullVolume(string root)
    {
        string volume=Environment.GetEnvironmentVariable("PATHWEFT_S5_FULL_VOLUME")??"";
        var drive=new DriveInfo(volume);
        if(!drive.VolumeLabel.Equals("PathweftS5",StringComparison.OrdinalIgnoreCase) || drive.TotalSize>128L*1024*1024 || drive.DriveFormat!="FAT32") return 2;
        string target=Directory.CreateDirectory(Path.Combine(volume,"fixture")).FullName;
        string source=Path.Combine(root,"item.txt"), old=Path.Combine(target,"item.txt");
        File.WriteAllText(source,new string('s',2*1024*1024)); File.WriteAllText(old,"existing");
        Settings.Verify=true;
        string streams=Path.Combine(root,"streams.txt"); File.WriteAllText(streams,"main"); File.WriteAllText(streams+":fixture","stream");
        var unsupported=Ferry.Submit(new[]{streams},target,true); await unsupported.Completion;
        bool streamsKept=unsupported.Phase==Phase.Failed && File.ReadAllText(streams+":fixture")=="stream" && !File.Exists(Path.Combine(target,"streams.txt"));
        string plain=Path.Combine(root,"timestamps.txt"); File.WriteAllText(plain,"precision"); var timestamp=new DateTime(2020,1,2,3,4,5,123,DateTimeKind.Utc); File.SetLastWriteTimeUtc(plain,timestamp);
        var precision=Ferry.Submit(new[]{plain},target,false); await precision.Completion;
        bool rounded=precision.Successful && precision.Notes.Any(n=>n.Contains("precision")) && File.GetLastWriteTimeUtc(Path.Combine(target,"timestamps.txt"))!=timestamp;
        int fullError=0;
        using(var filler=new FileStream(Path.Combine(volume,"fill.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            byte[] block=new byte[4096];
            try {while(true) filler.Write(block);} catch(IOException ex) {fullError=ex.HResult&0xffff;}
        }
        var job=Ferry.Submit(new[]{source},target,true); job.Resolve(Choice.Overwrite); await job.Completion;
        bool retained=fullError==112 && job.Phase==Phase.Failed && File.ReadAllText(source).Length==2*1024*1024 && !Directory.EnumerateFiles(target,".pathweft-transfer-*.partial").Any();
        File.WriteAllText(Path.Combine(root,"full-volume.json"),JsonSerializer.Serialize(new{fullError,sourceRetained=retained,streamsKept,rounded,destinationExists=File.Exists(old),destinationBytes=File.Exists(old)?new FileInfo(old).Length:0,overwriteRollback=false,filesystem=drive.DriveFormat,drive.TotalSize,remaining=drive.AvailableFreeSpace,phase=job.Phase.ToString(),errors=job.Errors.ToArray(),streamErrors=unsupported.Errors.ToArray(),precisionNotes=precision.Notes.ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        return retained&&streamsKept&&rounded?0:1;
    }
    internal static async Task<int> Run(string root)
    {
        var results = new List<Result>();
        var boundary = typeof(Ferry).GetField("VerificationBoundary", BindingFlags.NonPublic | BindingFlags.Static)!;
        void Hook(Action<string, Job, string, string> action) => boundary.SetValue(null, action);
        void Check(bool condition, string message) { if (!condition) throw new IOException(message); }
        string Folder(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        string Write(string folder, string name, string content) { string path = Path.GetFullPath(Path.Combine(folder,name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,content); return path; }
        string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        bool NoStages(string destination) => !Directory.EnumerateFiles(destination,".pathweft-transfer-*.partial",SearchOption.AllDirectories).Any();
        async Task<Job> Copy(string path,string target,bool move=false,Choice choice=Choice.Overwrite)
        {
            var job=Ferry.Submit(new[]{path},target,move); job.Resolve(choice); await job.Completion; return job;
        }
        void Complete(Job job) => Check(job.Successful && job.Errors.IsEmpty, job.Title+"\n"+string.Join("\n",job.Errors));
        object Identity(string path)
        {
            var type=typeof(Ferry).Assembly.GetType("FileExplorer.TransferFile",true)!;
            var stamp=type.GetMethod("Inspect",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{path})!;
            return (stamp.GetType().GetProperty("Volume")!.GetValue(stamp)!,stamp.GetType().GetProperty("Id")!.GetValue(stamp)!);
        }
        async Task Case(string contract,Func<Task> run)
        {
            try { await run(); results.Add(new(contract,true)); }
            catch(Exception ex) { results.Add(new(contract,false,ex.ToString())); }
            finally { Hook(null!); }
            Console.WriteLine(JsonSerializer.Serialize(results[^1]));
        }
        Settings.Verify=true; Settings.ParallelCopy=true;
        await Case("Plain copies hash during copying and read only the destination for verification",async()=>
        {
            string source=Folder("stream-copy-input"),target=Folder("stream-copy-target"),file=Write(source,"item.bin",new string('h',2*1024*1024+17));
            var job=await Copy(file,target);Complete(job);
            Check(Hash(file)==Hash(Path.Combine(target,"item.bin"))&&job.VerifiedFiles==1&&job.VerificationBytes==job.TotalBytes,"Plain copy used incorrect verification data/accounting.");
            Settings.Verify=false;
            try {var uncheckedJob=await Copy(file,Folder("stream-copy-unchecked"));Complete(uncheckedJob);Check(uncheckedJob.VerifiedFiles==0&&uncheckedJob.VerificationBytes==0,"Unchecked copy claimed or performed verification reads.");}
            finally{Settings.Verify=true;}
        });
        await Case("Plain copies preserve timestamps and apply changed attributes on new and overwritten files",async()=>
        {
            string source=Folder("plain-metadata-input"),target=Folder("plain-metadata-target"),file=Write(source,"item.txt","plain metadata");
            string output=Path.Combine(target,"item.txt");
            var created=new DateTime(2020,3,4,5,6,8,DateTimeKind.Utc);var modified=created.AddDays(1);var accessed=created.AddDays(2);
            File.SetCreationTimeUtc(file,created);File.SetLastWriteTimeUtc(file,modified);File.SetLastAccessTimeUtc(file,accessed);
            const FileAttributes mask=FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive|FileAttributes.NotContentIndexed|FileAttributes.Temporary;
            var wanted=FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive|FileAttributes.NotContentIndexed;
            File.SetAttributes(file,wanted);
            var job=await Copy(file,target);Complete(job);
            Check((File.GetAttributes(output)&mask)==wanted&&File.GetCreationTimeUtc(output)==created&&File.GetLastWriteTimeUtc(output)==modified&&File.GetLastAccessTimeUtc(output)==accessed,"New plain file lost basic metadata.");
            File.SetAttributes(file,FileAttributes.Archive);
            File.SetAttributes(output,FileAttributes.NotContentIndexed|FileAttributes.Temporary|FileAttributes.Archive);
            var overwritten=await Copy(file,target);Complete(overwritten);
            Check((File.GetAttributes(output)&mask)==FileAttributes.Archive&&Hash(file)==Hash(output)&&File.GetCreationTimeUtc(output)==created&&File.GetLastWriteTimeUtc(output)==modified,"Plain overwrite retained old attributes or lost content/timestamps.");
        });
        await Case("Read-only, Hidden and System overwrite refusals retain both existing files",async()=>
        {
            foreach(var attribute in new[]{FileAttributes.ReadOnly,FileAttributes.Hidden,FileAttributes.System})
            {
                string source=Folder("refused-attribute-input-"+attribute),target=Folder("refused-attribute-target-"+attribute),file=Write(source,"item.txt","new data"),output=Write(target,"item.txt","existing data");
                File.SetAttributes(output,attribute|FileAttributes.Archive);
                var job=await Copy(file,target);
                Check(job.Phase==Phase.Failed&&job.DoneFiles==0&&File.ReadAllText(file)=="new data"&&File.ReadAllText(output)=="existing data"&&File.GetAttributes(output).HasFlag(attribute),"Refused overwrite changed an input or claimed success.");
            }
        });
        await Case("A source truncated during copying is retained and its incomplete output removed",async()=>
        {
            string source=Folder("short-copy-input"),target=Folder("short-copy-target"),file=Write(source,"item.bin",new string('t',2*1024*1024));bool changed=false;
            Hook((phase,_,path,_)=>{if(phase=="copy-chunk"&&path==file&&!changed){changed=true;using var edit=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);edit.SetLength(0);}});
            var job=await Copy(file,target);Check(changed&&job.Phase==Phase.Failed&&File.Exists(file)&&new FileInfo(file).Length==0&&!File.Exists(Path.Combine(target,"item.bin")),"A short source was accepted, removed or left an incomplete output.");
        });
        await Case("Ordinary Unicode copies preserve contents through long source and destination paths",async()=>
        {
            string source=Folder("long-input"),target=Folder("long-target");
            for(int i=0;i<4;i++){source=Directory.CreateDirectory(Path.Combine(source,new string('s',70)+i)).FullName;target=Directory.CreateDirectory(Path.Combine(target,new string('d',70)+i)).FullName;}
            string file=Write(source,"文件 ç.txt","long path content");var job=await Copy(file,target);Complete(job);
            Check(Hash(file)==Hash(Path.Combine(target,"文件 ç.txt"))&&job.VerifiedFiles==1,"Long Unicode path changed or omitted copied content.");
        });
        await Case("Native verified tree copy preserves named streams, timestamps, attributes and empty folders",async()=>
        {
            string source=Folder("metadata-input"), target=Folder("metadata-target");
            string file=Write(source,"nested/文件 ç.txt","payload · Unicode");
            File.WriteAllText(file+":fixture","named stream content");
            string empty=Directory.CreateDirectory(Path.Combine(source,"empty")).FullName;
            var created=new DateTime(2020,2,3,4,5,6,DateTimeKind.Utc); var modified=created.AddDays(1);var accessed=created.AddDays(2);
            File.SetCreationTimeUtc(file,created); File.SetLastWriteTimeUtc(file,modified);File.SetLastAccessTimeUtc(file,accessed);
            Directory.SetCreationTimeUtc(empty,created);Directory.SetLastWriteTimeUtc(empty,modified);Directory.SetLastAccessTimeUtc(empty,accessed);
            File.SetAttributes(file,FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.Archive);
            Directory.SetLastWriteTimeUtc(source,modified);
            var job=await Copy(source,target); Complete(job);
            string output=Path.Combine(target,"metadata-input","nested","文件 ç.txt");
            string emptyOutput=Path.Combine(target,"metadata-input","empty");
            Check(File.GetCreationTimeUtc(output)==created && File.GetLastWriteTimeUtc(output)==modified && File.GetLastAccessTimeUtc(output)==accessed,"File's creation/access/write timestamps differ.");
            Check(Directory.GetCreationTimeUtc(emptyOutput)==created && Directory.GetLastWriteTimeUtc(emptyOutput)==modified && Directory.GetLastAccessTimeUtc(emptyOutput)==accessed,"Empty folder's creation/access/write timestamps differ.");
            Check(File.ReadAllText(output)=="payload · Unicode" && File.ReadAllText(output+":fixture")=="named stream content","Main/named stream differs.");
            Check((File.GetAttributes(output)&(FileAttributes.ReadOnly|FileAttributes.Hidden))==(FileAttributes.ReadOnly|FileAttributes.Hidden),"File attributes differ.");
            Check(Directory.Exists(Path.Combine(target,"metadata-input","empty")) && Directory.GetLastWriteTimeUtc(Path.Combine(target,"metadata-input"))==modified,"Directory metadata/empty folder differs.");
            Check(job.VerifiedFiles==1 && job.DoneFiles==1 && job.DoneBytes==job.TotalBytes && job.VerificationBytes==2*job.TotalBytes && NoStages(target),"Stream-aware progress/verification/staging contract differs.");
        });
        await Case("Overwriting a target with an old alternate stream uses complete native replacement",async()=>
        {
            string source=Folder("old-stream-input"),target=Folder("old-stream-target"),file=Write(source,"item.txt","new main data"),output=Write(target,"item.txt","old main data");
            File.WriteAllText(output+":stale","old alternate data");
            var job=await Copy(file,target);Complete(job);
            bool stale=true;try{using var old=File.OpenRead(output+":stale");}catch(FileNotFoundException){stale=false;}
            Check(!stale&&Hash(file)==Hash(output)&&job.VerificationBytes==2*job.TotalBytes,"Native replacement retained an old stream or plain truncation bypassed stream preservation.");
        });
        await Case("Compressed and sparse contents verify while destination storage policy is reported",async()=>
        {
            string source=Folder("storage-input"),target=Folder("storage-target");var (compressed,sparse)=StorageFixture(source);
            File.SetAttributes(compressed,File.GetAttributes(compressed)|FileAttributes.ReadOnly);
            Check(File.GetAttributes(compressed).HasFlag(FileAttributes.Compressed)&&File.GetAttributes(sparse).HasFlag(FileAttributes.SparseFile)&&Allocated(sparse)<new FileInfo(sparse).Length/2,"Storage fixture was not qualified.");
            var job=await Copy(source,target);Complete(job);
            string copiedCompressed=Path.Combine(target,"storage-input","compressed.bin"),copiedSparse=Path.Combine(target,"storage-input","sparse.bin");
            Check(!File.GetAttributes(copiedCompressed).HasFlag(FileAttributes.Compressed)&&!File.GetAttributes(copiedSparse).HasFlag(FileAttributes.SparseFile)&&File.GetAttributes(copiedCompressed).HasFlag(FileAttributes.ReadOnly),"Destination storage policy or basic read-only attribute differs.");
            Check(Hash(compressed)==Hash(copiedCompressed)&&Hash(sparse)==Hash(copiedSparse)&&job.VerificationBytes==job.TotalBytes&&!job.HasTransferErrors&&job.ErrorDetail.Contains("Sparse")&&job.Notes.Any(note=>note.Contains("Compression"))&&job.Notes.Any(note=>note.Contains("Sparse")),"Storage contents, verification accounting or explicit notes differ.");
        });
        await Case("Compressed directory contents follow destination policy with explicit reporting",async()=>
        {
            string source=Folder("compressed-directory-input"),target=Folder("compressed-directory-target");
            using(var handle=CreateFileW(source,0xc0000000,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero)){Check(!handle.IsInvalid,"Could not open owned directory.");Check(DeviceIoControl(handle,0x9c040,new byte[]{1,0},2,IntPtr.Zero,0,out _,IntPtr.Zero),"Could not mark owned directory compressed.");}
            string file=Write(source,"item.txt","compressed directory contents");Check(File.GetAttributes(source).HasFlag(FileAttributes.Compressed)&&File.GetAttributes(file).HasFlag(FileAttributes.Compressed),"Compressed inheritance fixture was not qualified.");
            var job=await Copy(source,target);Complete(job);string output=Path.Combine(target,"compressed-directory-input","item.txt");
            Check(Hash(file)==Hash(output)&&job.Notes.Any(note=>note.Contains("Compression"))&&!File.GetAttributes(output).HasFlag(FileAttributes.Compressed),"Compressed directory was refused, lost data or hid policy reporting.");
        });
        await Case("Cross-volume compressed and sparse moves verify contents before removing sources",async()=>
        {
            string source=Folder("storage-move-input"),target=Path.Combine(@"E:\Temp\pathweft-s5-review",Path.GetFileName(root)+"-storage-move");Directory.CreateDirectory(target);var (compressed,sparse)=StorageFixture(source);string first=Hash(compressed),second=Hash(sparse);
            Settings.Verify=false;
            try {var job=await Copy(source,target,true);Complete(job);Check(!Directory.Exists(source)&&Hash(Path.Combine(target,"storage-move-input","compressed.bin"))==first&&Hash(Path.Combine(target,"storage-move-input","sparse.bin"))==second&&job.Verify&&job.VerificationBytes==job.TotalBytes&&job.Notes.Count>=2,"Copied storage move removed unchecked sources or hid normalization.");}
            finally{Settings.Verify=true;}
        });
        await Case("Compressed alternate streams use native preservation with storage-policy reporting",async()=>
        {
            string source=Folder("compressed-stream-input"),target=Folder("compressed-stream-target");var (compressed,_)=StorageFixture(source);File.WriteAllText(compressed+":fixture","named stream");
            var job=await Copy(compressed,target);Complete(job);string output=Path.Combine(target,"compressed.bin");
            Check(Hash(compressed)==Hash(output)&&File.ReadAllText(output+":fixture")=="named stream"&&job.VerificationBytes==2*job.TotalBytes&&job.Notes.Any(note=>note.Contains("Compression")),"Native stream preservation or storage note differs.");
        });
        await Case("A source replaced by a link to its original file is refused before overwriting",async()=>
        {
            string source=Folder("source-link-input"),target=Folder("source-link-target"),file=Write(source,"item.txt","source data"),saved=Path.Combine(source,"original.txt"),output=Write(target,"item.txt","existing data");bool linked=false;
            Hook((phase,_,path,_)=>{if(phase=="before-source-open"&&path==file&&!linked){File.Move(file,saved);File.CreateSymbolicLink(file,saved);linked=true;}});
            var job=await Copy(file,target);
            Check(linked&&job.Phase==Phase.Failed&&job.DoneFiles==0&&File.ReadAllText(saved)=="source data"&&File.ReadAllText(output)=="existing data","A source link was followed or overwrote the old destination.");
        });
        foreach(bool append in new[]{false,true})
        await Case(append?"Target growth during verification is refused":"Target truncation during verification is refused",async()=>
        {
            string source=Folder("verify-size-input-"+append),target=Folder("verify-size-target-"+append),file=Write(source,"item.bin",new string('z',2*1024*1024+31)),output=Path.Combine(target,"item.bin");string original=Hash(file);bool edited=false;
            Hook((phase,_,path,_)=>{if(phase=="verification-chunk"&&path==output&&!edited){using var edit=new FileStream(output,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);edit.SetLength(append?3*1024*1024:1024*1024+1);edited=true;}});
            var job=await Copy(file,target);
            Check(edited&&job.Phase==Phase.Failed&&job.VerifiedFiles==0&&Hash(file)==original&&File.Exists(output),"Changed target length was accepted or changed the source.");
        });
        await Case("Same-volume move renames the exact file identity and preserves empty directories",async()=>
        {
            string source=Folder("rename-input"), target=Folder("rename-target");
            string file=Write(source,"nested/move.txt","same object"); Directory.CreateDirectory(Path.Combine(source,"empty"));
            object id=Identity(file); var job=await Copy(source,target,true); Complete(job);
            string output=Path.Combine(target,"rename-input","nested","move.txt");
            Check(!Directory.Exists(source) && Identity(output).Equals(id) && File.ReadAllText(output)=="same object","Native rename identity/source differs.");
            Check(job.RenamedFiles==1 && job.CopiedFiles==0 && job.VerifiedFiles==0 && Directory.Exists(Path.Combine(target,"rename-input","empty")),"Rename was misreported as copied/hash-verified.");
        });
        await Case("Cross-volume move always verifies and removes only its verified source",async()=>
        {
            string source=Folder("cross-input"), cross=Path.Combine(@"E:\Temp\pathweft-s5-review",Path.GetFileName(root)+"-cross");
            Check(!Directory.Exists(cross),"Preserve existing cross-volume trial."); Directory.CreateDirectory(cross);
            string file=Write(source,"move.txt","cross volume data"); File.WriteAllText(file+":fixture","ADS survives");
            Settings.Verify=false;
            try
            {
                var job=await Copy(file,cross,true); Complete(job);
                Check(job.Verify && job.VerifiedFiles==1 && !File.Exists(file) && File.ReadAllText(Path.Combine(cross,"move.txt"))=="cross volume data" && File.ReadAllText(Path.Combine(cross,"move.txt")+":fixture")=="ADS survives","Mandatory move verification/source removal differs.");
            }
            finally { Settings.Verify=true; }
        });
        await Case("Plain cross-volume moves reuse the copy digest before source removal",async()=>
        {
            string source=Folder("stream-move-input"),target=Path.Combine(@"E:\Temp\pathweft-s5-review",Path.GetFileName(root)+"-stream-move"),file=Write(source,"item.txt","plain verified move");Directory.CreateDirectory(target);
            Settings.Verify=false;
            try {var job=await Copy(file,target,true);Complete(job);Check(job.Verify&&job.VerifiedFiles==1&&job.VerificationBytes==job.TotalBytes&&!File.Exists(file)&&File.ReadAllText(Path.Combine(target,"item.txt"))=="plain verified move","Copied move did not verify its captured digest before removal.");}
            finally{Settings.Verify=true;}
        });
        await Case("Destination links are refused before mutation without changing their targets",async()=>
        {
            string source=Folder("destination-link-input"),target=Folder("destination-link-target"),outside=Folder("destination-link-outside");
            string file=Write(source,"nested/item.txt","source must remain"),sentinel=Write(outside,"sentinel.txt","outside must remain");
            string parent=Directory.CreateDirectory(Path.Combine(target,"destination-link-input")).FullName;
            string directoryLink=Path.Combine(parent,"nested");Directory.CreateSymbolicLink(directoryLink,outside);
            var directoryJob=await Copy(source,target,true);
            Check(directoryJob.Phase==Phase.Failed&&File.ReadAllText(file)=="source must remain"&&File.ReadAllText(sentinel)=="outside must remain"&&!File.Exists(Path.Combine(outside,"item.txt")),"Directory-link destination was followed or source data was consumed.");
            string plain=Write(source,"plain.txt","new input"),external=Write(outside,"existing.txt","existing outside content"),fileLink=Path.Combine(target,"plain.txt");
            File.CreateSymbolicLink(fileLink,external);var fileJob=await Copy(plain,target,true);
            Check(fileJob.Phase==Phase.Failed&&File.ReadAllText(plain)=="new input"&&File.ReadAllText(external)=="existing outside content"&&File.GetAttributes(fileLink).HasFlag(FileAttributes.ReparsePoint),"File-link overwrite followed its target or removed its source.");
        });
        await Case("Merge and overwrite retain unrelated existing members",async()=>
        {
            string source=Folder("merge-input/tree"), target=Folder("merge-target");
            Write(source,"same.txt","incoming"); Write(source,"nested/new.txt","nested");
            Write(target,"tree/same.txt","old"); string keep=Write(target,"tree/keep.txt","keep me");
            var job=await Copy(source,target,true); Complete(job);
            Check(job.Merged && File.ReadAllText(keep)=="keep me" && File.ReadAllText(Path.Combine(target,"tree/same.txt"))=="incoming" && !Directory.Exists(source),"Merge lost existing data or retained completed source.");
        });
        await Case("Skip and Newer preserve planned counts and both original files",async()=>
        {
            string source=Folder("skip-input"), target=Folder("skip-target"); string file=Write(source,"item.txt","incoming"); string old=Write(target,"item.txt","existing");
            var skip=await Copy(file,target,true,Choice.Skip);
            Check(skip.Phase==Phase.Done && !skip.Successful && skip.SkippedFiles==1 && skip.TotalFiles==1 && skip.DoneFiles==0 && skip.Title.Contains("skipped") && File.Exists(file) && File.ReadAllText(old)=="existing","Skip outcome/source differs.");
            File.SetLastWriteTimeUtc(file,new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc)); File.SetLastWriteTimeUtc(old,new DateTime(2021,1,1,0,0,0,DateTimeKind.Utc));
            var newer=await Copy(file,target,true,Choice.Newer);
            Check(newer.SkippedFiles==1 && File.Exists(file) && File.ReadAllText(old)=="existing","Newer decision overwrote or removed a skipped member.");
        });
        await Case("Rename avoids collisions with other planned arrivals",async()=>
        {
            string source=Folder("names-input"), target=Folder("names-target");
            string a=Write(source,"a.txt","first"), b=Write(source,"a (2).txt","second"); Write(target,"a.txt","existing");
            var job=Ferry.Submit(new[]{a,b},target,false); job.Resolve(Choice.Rename); await job.Completion; Complete(job);
            Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="existing" && File.ReadAllText(Path.Combine(target,"a (2).txt"))=="second" && File.ReadAllText(Path.Combine(target,"a (3).txt"))=="first","Planned renamed targets collided.");
        });
        await Case("Hard-link copies are independent and directory links are excluded from moves",async()=>
        {
            string source=Folder("links-input"), target=Folder("links-target"), outside=Folder("links-referent");
            string file=Write(source,"first.txt","hard link payload"), second=Path.Combine(source,"second.txt");
            Check(CreateHardLinkW(second,file,IntPtr.Zero),"Could not create owned hard-link fixture: "+Marshal.GetLastWin32Error());
            var copied=await Copy(source,target); Complete(copied);
            string a=Path.Combine(target,"links-input","first.txt"), b=Path.Combine(target,"links-input","second.txt");
            Check(!Identity(a).Equals(Identity(b)) && Hash(a)==Hash(b) && copied.Notes.Any(n=>n.Contains("hard links")),"Hard-link copy policy was not applied/reported.");
            string referent=Write(outside,"kept.txt","outside selected tree"), link=Path.Combine(source,"directory-link");
            Directory.CreateSymbolicLink(link,outside);
            string moved=Folder("links-move-target"); var job=await Copy(source,moved,true);
            Check(job.Phase==Phase.Done && job.Partial && !job.Successful && job.ExcludedItems==1 && File.ReadAllText(referent)=="outside selected tree" && Directory.Exists(link) && !Directory.Exists(Path.Combine(moved,"links-input","directory-link")),"Move followed a link or hid its exclusion.");
        });
        await Case("Source sharing remains available after copying; changed source data is retained",async()=>
        {
            string source=Folder("pin-input"), target=Folder("pin-target"), file=Write(source,"item.txt","incoming"), old=Write(target,"item.txt","existing");
            bool written=false;
            Hook((phase,_,path,_)=>{if(phase=="copy-written" && path==file) {File.WriteAllText(file,"concurrent edit");written=true;}});
            var copy=await Copy(file,target);
            Check(written&&copy.Phase==Phase.Failed&&File.ReadAllText(file)=="concurrent edit"&&File.ReadAllText(old)=="incoming","The transfer locked its source across verification or removed an edited source.");
        });
        await Case("Overwrite choice writes directly to the selected existing destination",async()=>
        {
            string source=Folder("direct-input"),target=Folder("direct-target"),file=Write(source,"item.txt","incoming"),old=Write(target,"item.txt","existing");bool direct=false;
            Hook((phase,_,path,output)=>{if(phase=="copy-written"&&path==file)direct=output==old&&File.ReadAllText(old)=="incoming";});
            var copy=await Copy(file,target);Complete(copy);Check(direct&&NoStages(target),"The caller still stages overwrites instead of direct copying.");
        });
        await Case("Source changed after scanning is retained and the overwrite target survives",async()=>
        {
            string source=Folder("changed-input"), target=Folder("changed-target"); string file=Write(source,"item.txt","before"); string old=Write(target,"item.txt","existing");
            Hook((phase,_,path,_)=>{if(phase=="before-source-open" && path==file) File.WriteAllText(file,"changed source after planning");});
            var job=await Copy(file,target,true);
            Check(job.Phase==Phase.Failed && File.ReadAllText(file)=="changed source after planning" && File.ReadAllText(old)=="existing" && NoStages(target),"Changed source was removed or published.");
        });
        await Case("Failed real stream verification preserves the source and reports the failed direct output",async()=>
        {
            string source=Folder("corrupt-input"), target=Folder("corrupt-target"); string file=Write(source,"item.txt","nonzero data"); string old=Write(target,"item.txt","existing"); string initial=Hash(file);
            Hook((phase,_,path,stage)=>{if(phase=="copy-written" && path==file) File.WriteAllBytes(stage,new byte[new FileInfo(stage).Length]);});
            var job=await Copy(file,target,false);
            Check(job.Phase==Phase.Failed && job.VerifiedFiles==0 && job.CopiedFiles==1 && Hash(file)==initial && File.ReadAllBytes(old).All(value=>value==0) && NoStages(target),"Failed direct verification was misreported or the source was removed.");
        });
        foreach(bool verify in new[]{false,true})
        await Case("A late source stream cannot escape the final publication check (verify="+verify+")",async()=>
        {
            string name=verify?"late-stream-verified":"late-stream-unchecked",source=Folder(name+"-input"),target=Folder(name+"-target");
            string file=Write(source,"item.txt","source"),old=Write(target,"item.txt","existing");bool added=false,refused=false;
            Hook((phase,_,path,_)=>{if(phase=="copy-written"&&path==file){try{File.WriteAllText(file+":late","new stream");added=true;}catch(IOException){refused=true;}}});
            Settings.Verify=verify;
            try
            {
                var job=await Copy(file,target,false);
                Check(added?job.Phase==Phase.Failed&&File.ReadAllText(file+":late")=="new stream":refused&&job.Successful,"A late stream was silently omitted or its source lost.");
                Check(File.ReadAllText(file)=="source"&&NoStages(target),"Late stream handling lost the source or an unpublished stage.");
            }
            finally{Settings.Verify=true;}
        });
        await Case("Denied destination writing reports failure and retains the source",async()=>
        {
            string source=Folder("denied-input"), target=Folder("denied-target"); string file=Write(source,"item.txt","incoming"); string old=Write(target,"item.txt","existing");
            var directory=new DirectoryInfo(target); var original=FileSystemAclExtensions.GetAccessControl(directory); var denied=FileSystemAclExtensions.GetAccessControl(directory);
            denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.CreateFiles,AccessControlType.Deny));
            var targetFile=new FileInfo(old);var originalFile=FileSystemAclExtensions.GetAccessControl(targetFile);var deniedFile=FileSystemAclExtensions.GetAccessControl(targetFile);
            deniedFile.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.WriteData|FileSystemRights.AppendData,AccessControlType.Deny));
            FileSystemAclExtensions.SetAccessControl(directory,denied);
            FileSystemAclExtensions.SetAccessControl(targetFile,deniedFile);
            try { var job=await Copy(file,target,false); Check(job.Phase==Phase.Failed && File.Exists(file) && File.ReadAllText(old)=="existing" && NoStages(target),"Denied destination did not preserve data/report failure."); }
            finally { FileSystemAclExtensions.SetAccessControl(targetFile,originalFile);FileSystemAclExtensions.SetAccessControl(directory,original); }
        });
        await Case("Disappearing destination reports failure without recreating or overwriting it",async()=>
        {
            string source=Folder("offline-input"), target=Folder("offline-target"), offline=Path.Combine(root,"offline-retained"); string file=Write(source,"item.txt","incoming"); Write(target,"item.txt","existing");
            Check(Path.GetFullPath(target).StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(offline).StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase),"Scoped directory move required.");
            Hook((phase,_,path,_)=>{if(phase=="before-copy" && path==file) Directory.Move(target,offline);});
            var job=await Copy(file,target,false);
            Check(job.Phase==Phase.Failed && File.Exists(file) && !Directory.Exists(target) && File.ReadAllText(Path.Combine(offline,"item.txt"))=="existing","Disappearing destination lost data or was silently recreated.");
        });
        await Case("Cancellation during verification removes the unverified output and retains the source",async()=>
        {
            string source=Folder("verify-cancel-input"), target=Folder("verify-cancel-target"); string file=Write(source,"item.txt",new string('v',2*1024*1024)); string old=Write(target,"item.txt","existing");
            Hook((phase,job,path,_)=>{if(phase=="verification-chunk" && path==old) job.Cancel();});
            var job=await Copy(file,target,false);
            Check(job.Phase==Phase.Cancelled && job.VerificationBytes>0 && File.Exists(file) && !File.Exists(old) && NoStages(target),"Verification cancellation retained its incomplete output or removed source.");
        });
        foreach(bool cancel in new[]{false,true})
        await Case(cancel?"Copy cancellation releases actual paused progress and removes incomplete output":"Copy pauses/resumes through actual progress",async()=>
        {
            string label=cancel?"pause-cancel":"pause-resume", source=Folder(label+"-input"), target=Folder(label+"-target");
            string file=Write(source,"item.txt",new string('p',8*1024*1024)), old=Write(target,"item.txt","existing");
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int entered=0;
            Hook((phase,job,path,_)=>{if(phase=="copy-chunk" && path==file && Interlocked.CompareExchange(ref entered,1,0)==0){job.Pause();ready.TrySetResult();}});
            var job=Ferry.Submit(new[]{file},target,false); job.Resolve(Choice.Overwrite);
            await Task.WhenAny(ready.Task,job.Completion);
            Check(ready.Task.IsCompleted && job.Paused && !job.Completion.IsCompleted && File.Exists(old),"Actual direct copy did not enter a pending paused callback.");
            if(cancel) job.Cancel(); else job.Resume();
            await job.Completion;
            if(cancel) Check(job.Phase==Phase.Cancelled && File.Exists(file) && !File.Exists(old) && NoStages(target),"Paused cancellation retained incomplete output or lost the source.");
            else { Complete(job); Check(Hash(file)==Hash(old) && job.VerifiedFiles==1 && NoStages(target),"Resumed native copy differs."); }
        });
        await Case("Cancellation removes a completed but unverified empty output",async()=>
        {
            string source=Folder("empty-cancel-input"),target=Folder("empty-cancel-target"),file=Write(source,"empty.txt","");
            Hook((phase,job,path,_)=>{if(phase=="copy-written"&&path==file)job.Cancel();});
            var job=await Copy(file,target);Check(job.Phase==Phase.Cancelled&&File.Exists(file)&&!File.Exists(Path.Combine(target,"empty.txt"))&&job.Errors.IsEmpty,"Empty-file cancellation cleanup failed.");
        });
        await Case("Verification cancellation cleans a read-only copied output without changing its source",async()=>
        {
            string source=Folder("readonly-cancel-input"),target=Folder("readonly-cancel-target"),file=Write(source,"item.txt",new string('r',2*1024*1024));File.SetAttributes(file,FileAttributes.ReadOnly);
            string output=Path.Combine(target,"item.txt");
            Hook((phase,job,path,_)=>{if(phase=="copy-written"&&path==file)File.SetAttributes(output,FileAttributes.ReadOnly);if(phase=="verification-chunk"&&path==output)job.Cancel();});
            var job=await Copy(file,target);Check(job.Phase==Phase.Cancelled&&File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)&&!File.Exists(Path.Combine(target,"item.txt"))&&job.Errors.IsEmpty,"Read-only cancellation cleanup failed or changed the source.");
        });
        await Case("Cancelling the current copy retains files already completed",async()=>
        {
            string source=Folder("partial-cancel-input"),target=Folder("partial-cancel-target"),first=Write(source,"completed.txt","completed"),second=Write(source,"unfinished.txt",new string('c',2*1024*1024));
            Hook((phase,job,path,_)=>{if(phase=="copy-chunk"&&path==second)job.Cancel();});Settings.ParallelCopy=false;
            try
            {
                var job=Ferry.Submit(new[]{first,second},target,false);await job.Completion;
                Check(job.Phase==Phase.Cancelled&&job.DoneFiles==1&&job.TotalFiles==2&&File.ReadAllText(Path.Combine(target,"completed.txt"))=="completed"&&!File.Exists(Path.Combine(target,"unfinished.txt"))&&File.Exists(first)&&File.Exists(second),"Cancellation removed a completed output or retained its current incomplete copy.");
            }
            finally{Settings.ParallelCopy=true;}
        });
        await Case("Cancellation never deletes a destination replaced by another owner",async()=>
        {
            string source=Folder("replaced-cancel-input"),target=Folder("replaced-cancel-target"),file=Write(source,"item.txt",new string('o',2*1024*1024)),output=Path.Combine(target,"item.txt"),retained=Path.Combine(target,"retained-copy.txt");bool changed=false;
            Hook((phase,job,path,_)=>{if(phase=="verification-chunk"&&path==output&&!changed){changed=true;File.Move(output,retained);File.WriteAllText(output,"replacement belongs to another owner");job.Cancel();}});
            var job=await Copy(file,target);
            Check(changed&&job.Phase==Phase.Cancelled&&File.ReadAllText(output)=="replacement belongs to another owner"&&File.Exists(retained)&&File.Exists(file)&&!job.Errors.IsEmpty&&job.StateGroup=="Needs attention","Cleanup deleted a replacement or hid its refusal.");
        });
        await Case("Queued and conflict-wait cancellation are event-driven and preserve inputs",async()=>
        {
            string source=Folder("wait-input"), target=Folder("wait-target"); string file=Write(source,"item.txt",new string('q',2*1024*1024));
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int entered=0;
            Hook((phase,job,path,_)=>{if(phase=="copy-chunk" && path==file && Interlocked.CompareExchange(ref entered,1,0)==0){job.Pause();ready.TrySetResult();}});
            var first=Ferry.Submit(new[]{file},target,false); await Task.WhenAny(ready.Task,first.Completion); Check(ready.Task.IsCompleted,"First job did not reach its pause boundary.");
            var queued=Ferry.Submit(new[]{file},target,false); Check(queued.Phase==Phase.Queued && !queued.Completion.IsCompleted,"Destination lane did not queue the second job.");
            queued.Cancel(); first.Resume(); await Task.WhenAll(first.Completion,queued.Completion); Complete(first); Check(queued.Phase==Phase.Cancelled,"Queued cancellation did not settle after its owner.");
            var awaitingConflict=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Hook((phase,_,_,_)=>{if(phase=="conflicts") awaitingConflict.TrySetResult();});
            var conflict=Ferry.Submit(new[]{file},target,false);
            await Task.WhenAny(awaitingConflict.Task,conflict.Completion);
            Check(awaitingConflict.Task.IsCompleted && conflict.HasConflicts && !conflict.Completion.IsCompleted,"The job never reached its actual conflict wait.");
            conflict.Cancel(); await conflict.Completion;
            Check(conflict.Phase==Phase.Cancelled && File.Exists(file),"Cancelled conflict wait consumed its source.");
        });
        await Case("Archive source deletion uses a verified exact handle and refuses ADS loss",async()=>
        {
            string source=Folder("archive-removal-input"); string file=Write(source,"item.txt","verified member");
            var type=typeof(Ferry).Assembly.GetType("FileExplorer.TransferFile",true)!;
            var remove=type.GetMethod("RemoveVerifiedSource",BindingFlags.Static|BindingFlags.NonPublic)!;
            string fingerprint=Hash(file); File.WriteAllText(file+":fixture","not represented in archive");
            bool refused=false; try {remove.Invoke(null,new object[]{file,fingerprint,new Job()});} catch(TargetInvocationException ex) when(ex.InnerException is IOException){refused=true;}
            Check(refused && File.ReadAllText(file+":fixture")=="not represented in archive","Archive cut could discard alternate streams.");
            string plain=Write(source,"plain.txt","verified member"); remove.Invoke(null,new object[]{plain,Hash(plain),new Job()}); Check(!File.Exists(plain),"Verified exact-handle archive source was not removed.");
        });
        File.WriteAllText(Path.Combine(root,"contracts.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return results.All(result=>result.Passed)?0:1;
    }
}
