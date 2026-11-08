using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

internal sealed record MaintenanceServiceState(uint State,uint Start,bool Delayed)
{
    internal string Label=>State switch{1=>"Stopped",2=>"Starting",3=>"Stopping",4=>"Running",_=>"Changing"};
    internal string StartLabel=>Start switch{2=>Delayed?"Automatic (delayed)":"Automatic",3=>"Manual",4=>"Disabled",_=>"System"};
}
// Service selection/descriptions from SysOptimizer ServiceManager. Native SCM
// preserves exit codes and actual startup state instead of parsing localized sc text.
internal static class MaintenanceServices
{
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
            uint start=(uint)Marshal.ReadInt32(config,4);bool delayed=false;
            IntPtr delay=Marshal.AllocHGlobal(4);try{if(QueryServiceConfig2(service,3,delay,4,out _))delayed=Marshal.ReadInt32(delay)!=0;}finally{Marshal.FreeHGlobal(delay);}
            return new(status.State,start,delayed);
        }
        finally{if(config!=IntPtr.Zero)Marshal.FreeHGlobal(config);if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    internal static string Change(string name,bool restore,string journal=null)
    {
        if(!Maintenance.Services.Any(s=>s.Key==name))throw new ArgumentException("Unknown service");
        using var lease=new Maintenance.JournalLease(journal);
        var records=Maintenance.Undo(journal);string id="service:"+name;var state=Read(name);
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
