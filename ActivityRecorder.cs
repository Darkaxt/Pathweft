using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;

namespace FileExplorer;

// ACTIVITY-01..03: the windowless recorder started by the elevated logon task.
// Performance monitoring only: executable path, process tree, lifetimes and
// resource totals. No hashes, signatures, blocking or network.
// ACTIVITY-07: a second session on the kernel logger's process events supplies each
// start's command line (its rundown covers programs already running); the starting
// directory is read from the new process. Both are stored once per distinct text,
// capped and with passwords and tokens masked.
// Windows' Kernel-Process start/stop events give exact launches and lifetime
// totals; one system-wide process snapshot every 30 seconds attributes the use
// of long-running programs to time. Writes are batched every 30 seconds.
internal sealed class ActivityRecorder
{
    internal static string FilePath=>Path.Combine(DataFolder.Bulk,"Activity","activity.sqlite");
    internal const long BucketMs=5*60*1000;
    internal const long StorageLimit=1024L*1024*1024;
    const string KernelProcess="Microsoft-Windows-Kernel-Process";
    static readonly TimeSpan FlushInterval=TimeSpan.FromSeconds(30);

    sealed class Node
    {
        public int Pid,Parent;public long Start,Created;public string Path,Command,Directory;public Work Work;public bool Root;
        public long Cycles,Read,Write,Peak;           // latest observed lifetime totals
        public long BaseCycles,BaseRead,BaseWrite;    // lifetime totals when first observed (0 for observed starts)
    }
    sealed class Work
    {
        public long Id,Start,End;public string Root;public int Processes,Tiny;
        public long Cycles,Read,Write,Peak;           // exited members, observed portion
        public readonly HashSet<Node> Members=new();public bool Dirty;
    }
    sealed class Usage{public int Starts;public long Cycles,Read,Write,Peak;}
    sealed record Finished(long Workload,int Pid,int Parent,long Start,long Exit,string Path,long Cycles,long Read,long Write,long Peak,int Code,string Command,string Directory);

    readonly object gate=new();
    readonly Dictionary<int,Node> live=new();
    readonly List<Work> changed=new();
    readonly List<Finished> finished=new();
    readonly Dictionary<(long,string),Usage> usage=new();
    readonly Dictionary<string,string> devices=new(StringComparer.OrdinalIgnoreCase);
    // Command lines that arrived before their Kernel-Process start or after the process was finished.
    readonly Dictionary<int,(long Time,string Text)> commands=new();
    long nextWorkload,missedStarts,unmatchedStops,cycleThreshold,droppedBatches;
    string state;long stateStart,lastFlush,lastPrune,reportedLoss;
    SqliteConnection db;TraceEventSession session,kernel;IntPtr snapshot;int snapshotSize=1<<20;string commandSource="unavailable";

    internal static int Run()
    {
        using var identity=WindowsIdentity.GetCurrent();
        if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 2;
        Settings.Load();
        using var single=new Mutex(true,StartupTasks.RecorderMutexName,out bool first);
        if(!first)return 0;
        try
        {
            using var stop=new EventWaitHandle(false,EventResetMode.ManualReset,StartupTasks.StopEventName);
            var recorder=new ActivityRecorder();
            try{recorder.Record(stop);}
            finally{recorder.Close();}
            return 0;
        }
        finally{single.ReleaseMutex();}
    }
    void Record(EventWaitHandle stop)
    {
        mhz=NominalMhz();cycleThreshold=(long)(mhz*1e6*0.05); // 50 ms of CPU
        OpenStore();
        lock(gate)Observe(initial:true);
        // One fixed per-user name: Create replaces a session left behind by a crashed run.
        session=new TraceEventSession("Pathweft-Activity-"+WindowsIdentity.GetCurrent().User.Value+StartupTasks.Instance,TraceEventSessionOptions.Create){StopOnDispose=true,BufferSizeMB=1};
        session.EnableProvider(KernelProcess,TraceEventLevel.Informational,0x10);
        var source=session.Source;var parser=new RegisteredTraceEventParser(source);
        parser.All+=Event;
        var processing=Task.Run(()=>{try{source.Process();}catch(Exception error)when(error is ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception){}});
        var commandLines=StartCommands();
        lastFlush=Now();
        while(!stop.WaitOne(FlushInterval))
        {
            lock(gate)Observe(initial:false);
            Flush();
        }
        session.Dispose();session=null;processing.Wait(5000);
        kernel?.Dispose();kernel=null;commandLines?.Wait(5000);
        lock(gate)Observe(initial:false);
        Flush();
    }
    void Close()
    {
        session?.Dispose();
        kernel?.Dispose();
        db?.Dispose();
        if(snapshot!=IntPtr.Zero){Marshal.FreeHGlobal(snapshot);snapshot=IntPtr.Zero;}
    }
    double mhz;
    // ACTIVITY-07: a private system-logger session (Windows 8+) with only the process keyword.
    Task StartCommands()
    {
        try
        {
            kernel=new TraceEventSession("Pathweft-Activity-Commands-"+WindowsIdentity.GetCurrent().User.Value+StartupTasks.Instance,TraceEventSessionOptions.Create){StopOnDispose=true,BufferSizeMB=1};
            kernel.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
            var source=kernel.Source;
            source.Kernel.ProcessStart+=data=>CommandLine(data,false);
            source.Kernel.ProcessDCStart+=data=>CommandLine(data,true);
            commandSource="kernel";
            return Task.Run(()=>{try{source.Process();}catch(Exception error)when(error is ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception){}});
        }
        catch(Exception error)when(error is COMException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            kernel?.Dispose();kernel=null;commandSource="unavailable: "+error.GetType().Name;return null;
        }
    }
    void CommandLine(ProcessTraceData data,bool rundown)
    {
        string text=data.CommandLine;if(string.IsNullOrWhiteSpace(text))return;
        int pid=data.ProcessID;long time=new DateTimeOffset(data.TimeStamp.ToUniversalTime()).ToUnixTimeMilliseconds();
        lock(gate)
        {
            // A rundown names programs running before recording; a start matches by its time.
            if(live.TryGetValue(pid,out var node)&&node.Command==null&&(rundown||Math.Abs(node.Start-time)<5000))node.Command=text;
            else if(!rundown)commands[pid]=(time,text);
        }
    }
    static readonly Regex[] Secrets=
    {
        new(@"(?i)((?:--?|/)(?:password|passwd|pwd|pass|token|secret|api[-_]?key|access[-_]?key|client[-_]?secret|auth(?:orization)?)(?:[=:]|\s+))(""[^""]*""|'[^']*'|\S+)",RegexOptions.Compiled),
        new(@"(?i)\b((?:password|passwd|pwd|token|secret|api[-_]?key|apikey|access[-_]?token|client[-_]?secret)[=:])(""[^""]*""|'[^']*'|[^\s&;""']+)",RegexOptions.Compiled),
        new(@"(?i)(authorization:\s*(?:bearer|basic|token)\s+|\bbearer\s+)([A-Za-z0-9._~+/=-]{8,})",RegexOptions.Compiled),
        new(@"(://)([^/\s:@]+:[^/\s@]+)(@)",RegexOptions.Compiled),
        new(@"()\b(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|xox[abprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{30,})",RegexOptions.Compiled),
    };
    internal const int TextLimit=2048;
    // Passwords and tokens masked, then capped; the length of a cut text is kept.
    internal static string Clean(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        text=text.Trim();
        foreach(var secret in Secrets)text=secret.Replace(text,m=>m.Groups[1].Value+"***"+(m.Groups.Count>3?m.Groups[3].Value:""));
        return text.Length<=TextLimit?text:text[..TextLimit]+"… ("+text.Length.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" characters)";
    }
    // The starting directory from the new process's parameters (64-bit layout; null when unreadable).
    static string StartDirectory(int pid)
    {
        IntPtr process=OpenProcess(0x1000|0x0010,false,pid);if(process==IntPtr.Zero)return null;
        try
        {
            var basic=new long[6];
            if(NtQueryInformationProcess(process,0,basic,48,out _)!=0||basic[1]==0)return null;
            var pointer=new byte[8];
            if(!ReadProcessMemory(process,(IntPtr)(basic[1]+0x20),pointer,8,out _))return null;
            long parameters=BitConverter.ToInt64(pointer,0);if(parameters==0)return null;
            var current=new byte[16];
            if(!ReadProcessMemory(process,(IntPtr)(parameters+0x38),current,16,out _))return null;
            int length=BitConverter.ToUInt16(current,0);long buffer=BitConverter.ToInt64(current,8);
            if(length==0||length>8192||buffer==0)return null;
            var text=new byte[length];
            if(!ReadProcessMemory(process,(IntPtr)buffer,text,length,out _))return null;
            string directory=Encoding.Unicode.GetString(text);
            return directory.Length>3?directory.TrimEnd('\\'):directory;
        }
        finally{CloseHandle(process);}
    }
    static long Now()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static double NominalMhz()
    {
        using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("~MHz") is int value&&value>0?value:1000;
    }

    // ---- Kernel-Process events (event 1 start, event 2 stop) ----
    void Event(TraceEvent data)
    {
        try
        {
            int id=(int)data.ID;if(id is not (1 or 2))return;
            int pid=Convert.ToInt32(data.PayloadByName("ProcessID"));long time=new DateTimeOffset(data.TimeStamp.ToUniversalTime()).ToUnixTimeMilliseconds();
            lock(gate)
            {
                if(id==1)Started(pid,Convert.ToInt32(data.PayloadByName("ParentProcessID")),time,Created(data.PayloadByName("CreateTime"),data.TimeStamp),Win32Path(StartImage(data))??FullPath(pid));
                else Stopped(pid,time,Convert.ToInt64(data.PayloadByName("CPUCycleCount")),Convert.ToInt64(data.PayloadByName("ReadTransferKiloBytes"))*1024,
                    Convert.ToInt64(data.PayloadByName("WriteTransferKiloBytes"))*1024,Convert.ToInt64(data.PayloadByName("CommitPeak")),Convert.ToInt32(data.PayloadByName("ExitCode")));
            }
        }
        catch(Exception error)when(error is InvalidCastException or FormatException or OverflowException or NullReferenceException){lock(gate)unmatchedStops++;}
    }
    // Version 4 of the start event adds a mandatory-label SID before ImageName, which the
    // registered template cannot decode; the NT image path is found by its \Device\ prefix.
    static readonly byte[] DevicePrefix=Encoding.Unicode.GetBytes(@"\Device\");
    static string StartImage(TraceEvent data)
    {
        if(data.PayloadByName("ImageName") is string named&&named.Length>0)return named;
        ReadOnlySpan<byte> raw=data.EventData();
        for(int from=20;from<raw.Length;)
        {
            int found=raw[from..].IndexOf(DevicePrefix);if(found<0)return null;
            int start=from+found;if((start&1)!=0){from=start+1;continue;}
            int end=start;while(end+1<raw.Length&&(raw[end]|raw[end+1])!=0)end+=2;
            return Encoding.Unicode.GetString(raw[start..end]);
        }
        return null;
    }
    static long Created(object value,DateTime fallback)=>value is DateTime date?date.ToUniversalTime().ToFileTimeUtc():fallback.ToUniversalTime().ToFileTimeUtc();
    void Started(int pid,int parentPid,long time,long created,string path)
    {
        if(live.Remove(pid,out var stale))Leave(stale,time,null);
        live.TryGetValue(parentPid,out var parent);
        if(parent!=null&&parent.Created>created)parent=null;
        var node=new Node{Pid=pid,Parent=parentPid,Start=time,Created=created,Path=path??"(unknown)",Directory=StartDirectory(pid)};
        if(commands.Remove(pid,out var command)&&Math.Abs(command.Time-time)<5000)node.Command=command.Text;
        Join(node,parent,time);
        Use(node.Path,time).Starts++;
    }
    // A launching workload: a child joins its parent's workload unless the parent
    // is a long-lived root (a shell, Explorer, an agent) launching a new program.
    void Join(Node node,Node parent,long time)
    {
        bool inherit=parent!=null&&(!parent.Root||time-parent.Start<60000||string.Equals(parent.Path,node.Path,StringComparison.OrdinalIgnoreCase));
        if(inherit)node.Work=parent.Work;
        else{node.Work=new Work{Id=nextWorkload++,Start=time,Root=node.Path};node.Root=true;}
        node.Work.Members.Add(node);node.Work.Processes++;Changed(node.Work);
        live[node.Pid]=node;
    }
    void Changed(Work work){if(!work.Dirty){work.Dirty=true;changed.Add(work);}}
    void Stopped(int pid,long time,long cycles,long read,long write,long peak,int code)
    {
        if(!live.Remove(pid,out var node)){unmatchedStops++;return;}
        Leave(node,time,(cycles,read,write,peak,code));
    }
    void Leave(Node node,long time,(long Cycles,long Read,long Write,long Peak,int Code)? exit)
    {
        if(exit is {} final)
        {
            var use=Use(node.Path,time);
            use.Cycles+=Math.Max(0,final.Cycles-node.Cycles);use.Read+=Math.Max(0,final.Read-node.Read);use.Write+=Math.Max(0,final.Write-node.Write);
            node.Cycles=Math.Max(node.Cycles,final.Cycles);node.Read=Math.Max(node.Read,final.Read);node.Write=Math.Max(node.Write,final.Write);node.Peak=Math.Max(node.Peak,final.Peak);
            use.Peak=Math.Max(use.Peak,final.Peak);
        }
        var work=node.Work;work.Members.Remove(node);
        work.Cycles+=node.Cycles-node.BaseCycles;work.Read+=node.Read-node.BaseRead;work.Write+=node.Write-node.BaseWrite;work.Peak=Math.Max(work.Peak,node.Peak);
        if(work.Members.Count==0)work.End=time;
        Changed(work);
        // Short, light processes count in their workload; others are kept individually.
        bool significant=node.Root||time-node.Start>=1000||node.Cycles>=cycleThreshold||node.Read+node.Write>=1<<20||node.Peak>=64L<<20;
        if(significant)finished.Add(new(work.Id,node.Pid,node.Parent,node.Start,time,node.Path,node.Cycles,node.Read,node.Write,node.Peak,exit?.Code??int.MinValue,node.Command,node.Directory));
        else work.Tiny++;
    }
    Usage Use(string path,long time)
    {
        var key=(time-time%BucketMs,path);
        if(!usage.TryGetValue(key,out var value))usage[key]=value=new();
        return value;
    }

    // ---- 30-second process snapshot ----
    void Observe(bool initial)
    {
        long now=Now(),nowFile=DateTime.UtcNow.ToFileTimeUtc();
        int length=Query();if(length==0)return;
        var seen=initial?new List<(int Pid,int Parent,long Created,long Cycles,long Read,long Write,long Private,long Peak,string Image)>():null;
        for(int offset=0;;)
        {
            IntPtr entry=snapshot+offset;
            int pid=(int)Marshal.ReadInt64(entry,80),parent=(int)Marshal.ReadInt64(entry,88);
            long created=Marshal.ReadInt64(entry,32),cycles=Marshal.ReadInt64(entry,24),privateBytes=Marshal.ReadInt64(entry,184),peak=Marshal.ReadInt64(entry,192),
                read=Marshal.ReadInt64(entry,232),write=Marshal.ReadInt64(entry,240);
            if(pid!=0)
            {
                if(live.TryGetValue(pid,out var node)&&Math.Abs(node.Created-created)<10_000_000)
                {
                    long dc=Math.Max(0,cycles-node.Cycles),dr=Math.Max(0,read-node.Read),dw=Math.Max(0,write-node.Write);
                    node.Cycles=Math.Max(node.Cycles,cycles);node.Read=Math.Max(node.Read,read);node.Write=Math.Max(node.Write,write);node.Peak=Math.Max(node.Peak,peak);
                    if(dc+dr+dw>0)
                    {
                        var use=Use(node.Path,now);use.Cycles+=dc;use.Read+=dr;use.Write+=dw;use.Peak=Math.Max(use.Peak,privateBytes);Changed(node.Work);
                    }
                }
                else if(node==null&&(initial||nowFile-created>5*10_000_000L))
                {
                    // Present before recording, or its start event was lost.
                    var item=(pid,parent,created,cycles,read,write,privateBytes,peak,Image(entry));
                    if(initial)seen.Add(item);else{missedStarts++;Adopt(item,now);}
                }
            }
            int next=Marshal.ReadInt32(entry);if(next==0||offset+next>=length)break;offset+=next;
        }
        if(initial)foreach(var item in seen.OrderBy(x=>x.Created))Adopt(item,now);
    }
    void Adopt((int Pid,int Parent,long Created,long Cycles,long Read,long Write,long Private,long Peak,string Image) item,long now)
    {
        live.TryGetValue(item.Parent,out var parent);if(parent!=null&&parent.Created>item.Created)parent=null;
        long started=item.Created>0?DateTimeOffset.FromFileTime(item.Created).ToUnixTimeMilliseconds():now;
        var node=new Node{Pid=item.Pid,Parent=item.Parent,Start=started,Created=item.Created,Path=FullPath(item.Pid)??item.Image,
            Cycles=item.Cycles,Read=item.Read,Write=item.Write,Peak=item.Peak,BaseCycles=item.Cycles,BaseRead=item.Read,BaseWrite=item.Write,Directory=StartDirectory(item.Pid)};
        Join(node,parent,started);
    }
    int Query()
    {
        while(true)
        {
            if(snapshot==IntPtr.Zero)snapshot=Marshal.AllocHGlobal(snapshotSize);
            int status=NtQuerySystemInformation(5,snapshot,snapshotSize,out int needed);
            if(status==0)return needed;
            if(status!=unchecked((int)0xC0000004)||snapshotSize>=64<<20)return 0;
            Marshal.FreeHGlobal(snapshot);snapshot=IntPtr.Zero;snapshotSize=Math.Max(snapshotSize*2,needed+(256<<10));
        }
    }
    static string Image(IntPtr entry)
    {
        int bytes=(ushort)Marshal.ReadInt16(entry,56);IntPtr text=Marshal.ReadIntPtr(entry,64);
        return bytes>0&&text!=IntPtr.Zero?Marshal.PtrToStringUni(text,bytes/2):"System";
    }
    static string FullPath(int pid)
    {
        IntPtr process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return null;
        try{var text=new StringBuilder(1024);int size=text.Capacity;return QueryFullProcessImageName(process,0,text,ref size)?text.ToString(0,size):null;}
        finally{CloseHandle(process);}
    }
    string Win32Path(string native)
    {
        if(string.IsNullOrEmpty(native)||!native.StartsWith(@"\Device\",StringComparison.OrdinalIgnoreCase))return native;
        for(int attempt=0;attempt<2;attempt++)
        {
            int slash=native.IndexOf('\\',8);string device=slash<0?native:native[..slash];
            if(devices.TryGetValue(device,out var drive))return drive+(slash<0?"":native[slash..]);
            if(attempt==0)LoadDevices();
        }
        return native;
    }
    void LoadDevices()
    {
        devices.Clear();var target=new StringBuilder(1024);
        foreach(char letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            if(QueryDosDevice(letter+":",target,target.Capacity)>0)devices[target.ToString()]=letter+":";
    }

    // ---- Store (ACTIVITY-03) ----
    internal static SqliteConnection Open(string path,bool readOnly)
    {
        SqliteRuntime.Ensure();
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
        connection.Open();
        using var setup=connection.CreateCommand();setup.CommandText=readOnly?"PRAGMA query_only=ON; PRAGMA busy_timeout=2000;":"PRAGMA busy_timeout=2000;";setup.ExecuteNonQuery();
        return connection;
    }
    void OpenStore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));db=Open(FilePath,false);
        Execute("""
            PRAGMA auto_vacuum=INCREMENTAL; PRAGMA journal_mode=WAL; PRAGMA journal_size_limit=1048576;
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value);
            CREATE TABLE IF NOT EXISTS workloads(id INTEGER PRIMARY KEY,root TEXT NOT NULL,start INTEGER NOT NULL,end INTEGER,processes INTEGER NOT NULL,tiny INTEGER NOT NULL,cycles INTEGER NOT NULL,read INTEGER NOT NULL,write INTEGER NOT NULL,peak INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS workloads_start ON workloads(start);
            CREATE TABLE IF NOT EXISTS processes(workload INTEGER NOT NULL,pid INTEGER NOT NULL,parent INTEGER NOT NULL,start INTEGER NOT NULL,exit INTEGER NOT NULL,path TEXT NOT NULL,cycles INTEGER NOT NULL,read INTEGER NOT NULL,write INTEGER NOT NULL,peak INTEGER NOT NULL,code INTEGER);
            CREATE INDEX IF NOT EXISTS processes_exit ON processes(exit);
            CREATE INDEX IF NOT EXISTS processes_workload ON processes(workload);
            CREATE TABLE IF NOT EXISTS app_usage(t INTEGER NOT NULL,path TEXT NOT NULL,starts INTEGER NOT NULL,cycles INTEGER NOT NULL,read INTEGER NOT NULL,write INTEGER NOT NULL,peak INTEGER NOT NULL,PRIMARY KEY(t,path));
            CREATE TABLE IF NOT EXISTS intervals(start INTEGER PRIMARY KEY,end INTEGER NOT NULL,state TEXT NOT NULL,lost INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS texts(id INTEGER PRIMARY KEY,text TEXT NOT NULL UNIQUE);
            """);
        // ACTIVITY-07: stores from 1.1.87 and earlier gain the command and directory references.
        if(Scalar("SELECT COUNT(*) FROM pragma_table_info('processes') WHERE name='cmd'")==0)
            Execute("ALTER TABLE processes ADD COLUMN cmd INTEGER; ALTER TABLE processes ADD COLUMN cwd INTEGER;");
        long pageSize=Scalar("PRAGMA page_size");
        Execute($"PRAGMA max_page_count={StorageLimit/pageSize}; PRAGMA wal_autocheckpoint=256; PRAGMA cache_size=-256;");
        nextWorkload=Scalar("SELECT IFNULL(MAX(id),0)+1 FROM workloads");
        // A previous run ended without observing these exits (sign-out or stop).
        Execute("UPDATE workloads SET end=IFNULL((SELECT value FROM meta WHERE key='heartbeat'),start) WHERE end IS NULL");
        Execute("INSERT OR REPLACE INTO meta VALUES('recorder_started',$0),('tsc_mhz',$1),('version',$2)",Now(),mhz,typeof(ActivityRecorder).Assembly.GetName().Version?.ToString());
    }
    void Execute(string sql,params object[] values)
    {
        using var command=db.CreateCommand();command.CommandText=sql;
        for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue("$"+i,values[i]??DBNull.Value);
        command.ExecuteNonQuery();
    }
    long Scalar(string sql){using var command=db.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
    void Flush()
    {
        long now=Now();
        string current=Session();
        long lost=session?.EventsLost??0;long newLoss=Math.Max(0,lost-reportedLoss);
        List<Finished> rows;List<(Work Work,long Cycles,long Read,long Write,long Peak,bool Open)> works;List<KeyValuePair<(long,string),Usage>> uses;long missed,unmatched;
        lock(gate)
        {
            rows=[..finished];finished.Clear();
            for(int i=0;i<rows.Count;i++)
                if(rows[i].Command==null&&commands.Remove(rows[i].Pid,out var late)&&Math.Abs(late.Time-rows[i].Start)<5000)rows[i]=rows[i] with{Command=late.Text};
            foreach(int pid in commands.Where(c=>now-c.Value.Time>120_000).Select(c=>c.Key).ToList())commands.Remove(pid);
            uses=[..usage];usage.Clear();
            works=changed.Select(w=>(w,w.Cycles+w.Members.Sum(m=>m.Cycles-m.BaseCycles),w.Read+w.Members.Sum(m=>m.Read-m.BaseRead),w.Write+w.Members.Sum(m=>m.Write-m.BaseWrite),Math.Max(w.Peak,w.Members.Select(m=>m.Peak).DefaultIfEmpty().Max()),w.Members.Count>0)).ToList();
            foreach(var w in changed)w.Dirty=false;changed.Clear();
            missed=missedStarts;unmatched=unmatchedStops;
        }
        var cleaned=rows.Select(p=>(Command:Clean(p.Command),Directory:Clean(p.Directory))).ToList();
        for(int attempt=0;;attempt++)
        try
        {
            using var transaction=db.BeginTransaction();
            var ids=new Dictionary<string,long>(StringComparer.Ordinal);
            object Text(string text)
            {
                if(text==null)return null;
                if(ids.TryGetValue(text,out long known))return known;
                using var command=db.CreateCommand();command.Transaction=transaction;
                command.CommandText="INSERT INTO texts(text) VALUES($0) ON CONFLICT(text) DO UPDATE SET text=excluded.text RETURNING id";
                command.Parameters.AddWithValue("$0",text);
                return ids[text]=Convert.ToInt64(command.ExecuteScalar());
            }
            foreach(var w in works)
                Insert(transaction,"INSERT OR REPLACE INTO workloads VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9)",w.Work.Id,w.Work.Root,w.Work.Start,w.Open?null:w.Work.End,w.Work.Processes,w.Work.Tiny,w.Cycles,w.Read,w.Write,w.Peak);
            for(int i=0;i<rows.Count;i++)
            {
                var p=rows[i];
                Insert(transaction,"INSERT INTO processes(workload,pid,parent,start,exit,path,cycles,read,write,peak,code,cmd,cwd) VALUES($0,$1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)",
                    p.Workload,p.Pid,p.Parent,p.Start,p.Exit,p.Path,p.Cycles,p.Read,p.Write,p.Peak,p.Code==int.MinValue?null:p.Code,Text(cleaned[i].Command),Text(cleaned[i].Directory));
            }
            foreach(var (key,u) in uses)
                Insert(transaction,"INSERT INTO app_usage VALUES($0,$1,$2,$3,$4,$5,$6) ON CONFLICT(t,path) DO UPDATE SET starts=starts+excluded.starts,cycles=cycles+excluded.cycles,read=read+excluded.read,write=write+excluded.write,peak=MAX(peak,excluded.peak)",key.Item1,key.Item2,u.Starts,u.Cycles,u.Read,u.Write,u.Peak);
            // Recording intervals: contiguous while heartbeats are at most 90 s apart; gaps are "off".
            if(state==current&&now-lastFlush<=90_000)Insert(transaction,"UPDATE intervals SET end=$1,lost=lost+$2 WHERE start=$0",stateStart,now,newLoss);
            else{stateStart=state!=null&&now-lastFlush<=90_000?lastFlush:now;state=current;Insert(transaction,"INSERT OR REPLACE INTO intervals VALUES($0,$1,$2,$3)",stateStart,now,state,newLoss);}
            Insert(transaction,"INSERT OR REPLACE INTO meta VALUES('heartbeat',$0),('missed_starts',$1),('unmatched_stops',$2),('lost_events',$3),('dropped_batches',$4),('command_lines',$5)",now,missed,unmatched,lost,droppedBatches,commandSource);
            transaction.Commit();
            reportedLoss=lost;lastFlush=now;
            break;
        }
        catch(SqliteException error)
        {
            // At the storage limit the oldest hour goes and the same batch is written once more;
            // any other failure, or a second one, drops the batch and reports it.
            if(error.SqliteErrorCode==13)Trim(now);
            if(error.SqliteErrorCode==13&&attempt==0)continue;
            droppedBatches++;break;
        }
        if(now-lastPrune>=3600_000)Prune(now);
        // Stay small between batches: return SQLite's page cache and compact the managed heap.
        try{Execute("PRAGMA shrink_memory");}catch(SqliteException){}
        GC.Collect(2,GCCollectionMode.Aggressive,true,true);
    }
    void Insert(SqliteTransaction transaction,string sql,params object[] values)
    {
        using var command=db.CreateCommand();command.CommandText=sql;command.Transaction=transaction;
        for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue("$"+i,values[i]??DBNull.Value);
        command.ExecuteNonQuery();
    }
    void Prune(long now)
    {
        Settings.Load();long cutoff=now-Math.Clamp(Settings.PerformanceRetention,1,30)*86_400_000L;
        Execute("DELETE FROM processes WHERE exit<$0; DELETE FROM workloads WHERE end<$0; DELETE FROM app_usage WHERE t<$0; DELETE FROM intervals WHERE end<$0;"+UnusedTexts,cutoff);
        Execute("PRAGMA incremental_vacuum(1024); PRAGMA wal_checkpoint(PASSIVE);");
        lastPrune=now;
    }
    const string UnusedTexts=" DELETE FROM texts WHERE id NOT IN (SELECT cmd FROM processes WHERE cmd IS NOT NULL UNION SELECT cwd FROM processes WHERE cwd IS NOT NULL);";
    void Trim(long now)
    {
        long oldest=Scalar("SELECT IFNULL(MIN(t),0) FROM app_usage");
        Execute("DELETE FROM processes WHERE exit<$0; DELETE FROM workloads WHERE end<$0; DELETE FROM app_usage WHERE t<$0;"+UnusedTexts,Math.Min(now,oldest+3600_000));
        Execute("PRAGMA incremental_vacuum(1024);");
    }
    static string Session()
    {
        IntPtr desktop=OpenInputDesktop(0,false,0x0001);
        if(desktop==IntPtr.Zero)return "locked";
        try
        {
            var name=new StringBuilder(64);
            if(GetUserObjectInformation(desktop,2,name,name.Capacity*2,out _)&&!name.ToString().Equals("Default",StringComparison.OrdinalIgnoreCase))return "locked";
        }
        finally{CloseDesktop(desktop);}
        var input=new LastInput{Size=8};
        return GetLastInputInfo(ref input)&&unchecked((uint)Environment.TickCount-input.Time)>=5*60*1000?"idle":"active";
    }
    [StructLayout(LayoutKind.Sequential)]struct LastInput{public uint Size,Time;}
    [DllImport("ntdll.dll")]static extern int NtQuerySystemInformation(int kind,IntPtr buffer,int length,out int needed);
    [DllImport("ntdll.dll")]static extern int NtQueryInformationProcess(IntPtr process,int kind,long[] info,int length,out int needed);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,nint size,out nint read);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(int access,bool inherit,int pid);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder name,ref int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern int QueryDosDevice(string device,StringBuilder target,int length);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr OpenInputDesktop(int flags,bool inherit,int access);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder info,int length,out int needed);
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput info);
}
