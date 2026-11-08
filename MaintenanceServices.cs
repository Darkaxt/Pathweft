using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

internal sealed record MaintenanceServiceState(uint State,uint Start,bool Delayed,uint Type=0,string Description="",string Dependencies="",string Account="",string Binary="")
{
    internal string Label=>State switch{1=>"Stopped",2=>"Starting",3=>"Stopping",4=>"Running",_=>"Changing"};
    internal string StartLabel=>Start switch{2=>Delayed?"Automatic (delayed)":"Automatic",3=>"Manual",4=>"Disabled",_=>"System"};
}
// Service selection/descriptions from SysOptimizer ServiceManager. Native SCM
// preserves exit codes and actual startup state instead of parsing localized sc text.
internal static class MaintenanceServices
{
    internal static MaintenanceRow[] List(Dictionary<string,MaintenanceUndo> undo,CancellationToken cancel)
    {
        IntPtr manager=OpenSCManager(null,null,4);if(manager==IntPtr.Zero)throw new Win32Exception();
        const int capacity=256*1024;IntPtr buffer=Marshal.AllocHGlobal(capacity);uint resume=0;var rows=new List<MaintenanceRow>();
        try
        {
            bool more;
            do
            {
                cancel.ThrowIfCancellationRequested();bool ok=EnumServicesStatusEx(manager,0,0x30,3,buffer,capacity,out _,out uint count,ref resume,null);
                int error=ok?0:Marshal.GetLastWin32Error();if(!ok&&error!=234)throw new Win32Exception(error);more=!ok;
                int stride=Marshal.SizeOf<ServiceEntry>();
                for(int i=0;i<count;i++)
                {
                    cancel.ThrowIfCancellationRequested();var entry=Marshal.PtrToStructure<ServiceEntry>(buffer+i*stride);string name=Marshal.PtrToStringUni(entry.Name),display=Marshal.PtrToStringUni(entry.Display);
                    try
                    {
                        var state=Read(name);rows.Add(new("service:"+name,display,"This computer",state.Label,
                            state.Description+"\nRequires: "+(string.IsNullOrEmpty(state.Dependencies)?"No declared service dependencies":state.Dependencies)+
                            "\nAccount: "+state.Account+"\nExecutable: "+state.Binary+
                            (undo.ContainsKey("service:"+name)?"\nOriginal startup configuration saved; Restore uses that state, not a Windows default.":""),Type:name,StartupMode:state.StartLabel));
                    }
                    catch(Win32Exception problem){rows.Add(new("service:"+name,display,"This computer",problem.NativeErrorCode==5?"Access denied":"Unavailable",problem.Message,Type:name,CanChange:false));}
                }
            }while(more);
            return rows.OrderBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally{Marshal.FreeHGlobal(buffer);CloseServiceHandle(manager);}
    }
    internal static MaintenanceServiceState Read(string name)
    {
        IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception();
        IntPtr service=IntPtr.Zero,config=IntPtr.Zero;
        try
        {
            service=OpenService(manager,name,1|4);if(service==IntPtr.Zero)throw new Win32Exception();
            if(!QueryServiceStatus(service,out var status))throw new Win32Exception();
            QueryServiceConfig(service,IntPtr.Zero,0,out uint size);config=Marshal.AllocHGlobal((int)size);
            if(!QueryServiceConfig(service,config,size,out _))throw new Win32Exception();
            var configuration=Marshal.PtrToStructure<Configuration>(config);uint start=configuration.Start;bool delayed=false;
            IntPtr delay=Marshal.AllocHGlobal(4);try{if(QueryServiceConfig2(service,3,delay,4,out _))delayed=Marshal.ReadInt32(delay)!=0;}finally{Marshal.FreeHGlobal(delay);}
            string description="";QueryServiceConfig2(service,1,IntPtr.Zero,0,out uint descriptionSize);
            if(descriptionSize>0)
            {
                IntPtr text=Marshal.AllocHGlobal((int)descriptionSize);
                try{if(QueryServiceConfig2(service,1,text,descriptionSize,out _))description=Marshal.PtrToStringUni(Marshal.ReadIntPtr(text))??"";}
                finally{Marshal.FreeHGlobal(text);}
            }
            var dependencies=new List<string>();IntPtr item=configuration.Dependencies;
            if(item!=IntPtr.Zero)for(string value=Marshal.PtrToStringUni(item);!string.IsNullOrEmpty(value);value=Marshal.PtrToStringUni(item))
            {dependencies.Add(value);item+=2*(value.Length+1);}
            return new(status.State,start,delayed,status.Type,description,string.Join(", ",dependencies),Marshal.PtrToStringUni(configuration.Account)??"",Marshal.PtrToStringUni(configuration.Binary)??"");
        }
        finally{if(config!=IntPtr.Zero)Marshal.FreeHGlobal(config);if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    internal static string Change(string name,bool restore,string journal=null)
    {
        using var lease=new Maintenance.JournalLease(journal);
        var records=Maintenance.Undo(journal);string id="service:"+name;var state=Read(name);
        if((state.Type&0x30)==0||state.Start is <2 or >4)throw new ArgumentException("Only installed Windows application services can be changed");
        if(restore)
        {
            var saved=records.GetValueOrDefault(id)??throw new InvalidOperationException("No original service configuration is saved");
            if(saved.Service!=name||saved.ServiceStart is <2 or >4)throw new InvalidOperationException("Invalid saved service configuration");
            if(state.Start==saved.ServiceStart&&state.Delayed==saved.ServiceDelayed)
            {records.Remove(id);Maintenance.Save(records,journal);return "Original startup configuration is already present. Saved restore entry cleared.";}
            if(state.Start!=4)throw new InvalidOperationException("Service configuration changed outside Pathweft; restore stopped");
            Configure(name,saved.ServiceStart,saved.ServiceDelayed,false);var actual=Read(name);
            if(actual.Start!=saved.ServiceStart||actual.Delayed!=saved.ServiceDelayed)throw new IOException("Service configuration was not restored");
            records.Remove(id);Maintenance.Save(records,journal);return "Original startup configuration restored. The current running state is unchanged.";
        }
        if(records.ContainsKey(id))throw new InvalidOperationException("Original configuration is already saved");
        records[id]=new(id,[],[],name,state.Start,state.Delayed);Maintenance.Save(records,journal);
        Configure(name,4,false,true);var changed=Read(name);if(changed.Start!=4)throw new IOException("Service startup configuration was not disabled");
        records[id]=records[id] with{Phase="applied"};Maintenance.Save(records,journal);
        return changed.State==1?"Service stopped and startup disabled.":"Startup disabled; stop requested. Current state: "+changed.Label+".";
    }
    static void Configure(string name,uint start,bool delayed,bool stop)
    {
        IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception();IntPtr service=IntPtr.Zero;
        try
        {
            service=OpenService(manager,name,2|4|(stop?32u:0));if(service==IntPtr.Zero)throw new Win32Exception();
            if(!ChangeServiceConfig(service,uint.MaxValue,start,uint.MaxValue,null,null,IntPtr.Zero,null,null,null,null))throw new Win32Exception();
            // Delayed auto-start is meaningful only for automatic services.
            if(start==2&&!ChangeServiceConfig2(service,3,ref delayed))throw new Win32Exception();
            if(stop&&!ControlService(service,1,out _)&&Marshal.GetLastWin32Error()!=1062)throw new Win32Exception();
        }
        finally{if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    [StructLayout(LayoutKind.Sequential)]struct Status{public uint Type,State,Controls,Exit,Specific,Checkpoint,Hint;}
    [StructLayout(LayoutKind.Sequential)]struct Configuration{public uint Type,Start,Error;public IntPtr Binary,Group;public uint Tag;public IntPtr Dependencies,Account,Display;}
    [StructLayout(LayoutKind.Sequential)]struct ServiceEntry{public IntPtr Name,Display;public Status Status;public uint ProcessId,Flags;}
    [DllImport("advapi32.dll",EntryPoint="EnumServicesStatusExW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool EnumServicesStatusEx(IntPtr manager,uint info,uint type,uint state,IntPtr buffer,uint size,out uint needed,out uint returned,ref uint resume,string group);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenSCManager(string machine,string database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatus(IntPtr service,out Status status);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryServiceConfig(IntPtr service,IntPtr config,uint size,out uint needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryServiceConfig2(IntPtr service,uint level,IntPtr config,uint size,out uint needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ChangeServiceConfig(IntPtr service,uint type,uint start,uint error,string binary,string group,IntPtr tag,string dependencies,string account,string password,string display);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ChangeServiceConfig2(IntPtr service,uint level,[MarshalAs(UnmanagedType.Bool)]ref bool delayed);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool ControlService(IntPtr service,uint control,out Status status);
}
