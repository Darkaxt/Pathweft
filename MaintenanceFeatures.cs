using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

// A Windows optional feature or capability named in code by a hardening tweak (MAINT-43). Marker is a file
// that exists while the capability is installed, so the user-level list can show its state without elevation.
internal sealed record WindowsComponent(string Name,bool Capability,string Marker=null);
internal sealed record ComponentState(string Name,bool Capability,bool Installed);

// Changes go through Windows' servicing API (dismapi.dll) in the shared administrator session. Features are
// disabled with their payload kept, so Restore re-enables them offline; a removed capability is re-added from
// Windows Update. Nothing here accepts a name from data: callers pass the components their tweak declares.
internal static class MaintenanceFeatures
{
    const string Online="DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}";
    const int RebootRequired=0xBC2,UnknownFeature=unchecked((int)0x800F080C),UnknownCapability=unchecked((int)0x800F0950);
    static readonly object gate=new();

    // User-level view for the Windows tweaks list: null means the component is not part of this Windows.
    internal static bool? Installed(WindowsComponent component)
    {
        if(component.Capability)return File.Exists(Environment.ExpandEnvironmentVariables(component.Marker));
        var wmi=MaintenanceSecurity.Wmi.Query(@"root\CIMV2","SELECT InstallState FROM Win32_OptionalFeature WHERE Name='"+component.Name+"'");
        var item=wmi.Items.FirstOrDefault();if(item==null)return null;
        return Convert.ToInt32(wmi.Get(item,"InstallState"),System.Globalization.CultureInfo.InvariantCulture)==1;
    }
    internal static bool Present(WindowsComponent component)=>component.Capability?File.Exists(Environment.ExpandEnvironmentVariables(component.Marker)):Installed(component)!=null;

    // Authoritative states for the journal; components Windows does not know are left out.
    internal static ComponentState[] Read(IEnumerable<WindowsComponent> components)=>Session(session=>components.Select(c=>State(session,c)).Where(s=>s!=null).ToArray());
    // Returns true when Windows finishes the change after a restart.
    internal static bool Set(IEnumerable<ComponentState> wanted)=>Session(session=>
    {
        bool restart=false;
        foreach(var item in wanted)
        {
            var current=State(session,new(item.Name,item.Capability))??throw new IOException(item.Name+" is no longer part of this Windows");
            if(current.Installed==item.Installed)continue;
            int result=item.Capability
                ?item.Installed?DismAddCapability(session,item.Name,false,null,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero):DismRemoveCapability(session,item.Name,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)
                :item.Installed?DismEnableFeature(session,item.Name,null,0,false,null,0,false,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero):DismDisableFeature(session,item.Name,null,false,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            if(result<0)throw new IOException((item.Installed?"Windows could not add ":"Windows could not remove ")+item.Name+$" (0x{result:X8})"+(item.Capability&&item.Installed?". Adding it back needs Windows Update and an internet connection.":""));
            restart|=result==RebootRequired;
        }
        return restart;
    });

    static ComponentState State(uint session,WindowsComponent component)
    {
        IntPtr info;
        int result=component.Capability?DismGetCapabilityInfo(session,component.Name,out info):DismGetFeatureInfo(session,component.Name,null,0,out info);
        if(result is UnknownFeature or UnknownCapability)return null;
        if(result<0)throw new IOException($"Windows could not read {component.Name} (0x{result:X8})");
        try
        {
            // Both structures start with a name pointer followed by the state (dismapi.h packs them to 1 byte).
            int state=Marshal.ReadInt32(info,IntPtr.Size);
            return new(component.Name,component.Capability,state is 4 or 5 or 7);
        }
        finally{DismDelete(info);}
    }
    static T Session<T>(Func<uint,T> work)
    {
        lock(gate)
        {
            int result=DismInitialize(0,null,null);if(result<0)throw new IOException($"Windows servicing is unavailable (0x{result:X8})");
            try
            {
                result=DismOpenSession(Online,null,null,out uint session);if(result<0)throw new IOException($"Windows servicing is unavailable (0x{result:X8})");
                try{return work(session);}finally{DismCloseSession(session);}
            }
            finally{DismShutdown();}
        }
    }
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismInitialize(int logLevel,string logFile,string scratch);
    [DllImport("dismapi.dll")]static extern int DismShutdown();
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismOpenSession(string image,string windows,string drive,out uint session);
    [DllImport("dismapi.dll")]static extern int DismCloseSession(uint session);
    [DllImport("dismapi.dll")]static extern int DismDelete(IntPtr structure);
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismGetFeatureInfo(uint session,string name,string identifier,int package,out IntPtr info);
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismGetCapabilityInfo(uint session,string name,out IntPtr info);
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismEnableFeature(uint session,string name,string identifier,int package,bool limitAccess,string[] sources,uint sourceCount,bool enableAll,IntPtr cancel,IntPtr progress,IntPtr user);
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismDisableFeature(uint session,string name,string package,bool removePayload,IntPtr cancel,IntPtr progress,IntPtr user);
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismAddCapability(uint session,string name,bool limitAccess,string[] sources,uint sourceCount,IntPtr cancel,IntPtr progress,IntPtr user);
    [DllImport("dismapi.dll",CharSet=CharSet.Unicode)]static extern int DismRemoveCapability(uint session,string name,IntPtr cancel,IntPtr progress,IntPtr user);
}
