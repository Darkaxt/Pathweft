using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// Read-only Windows device topology and hub queries. No device control writes,
// benchmarks, resets or administrator helper is involved.
// Windows SDK usbioctl.h/usbspec.h (26100):
// https://learn.microsoft.com/windows-hardware/drivers/ddi/usbioctl/ni-usbioctl-ioctl_usb_get_node_connection_superspeedplus_information
// Rx/Tx lane counts are zero-based. Capability flags never imply an exact rate.
internal static class DriveConnections
{
    internal sealed record Link(string Text, string Device, string Hub, uint Port, double ReceiveBits, double TransmitBits, int Error);
    static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    static readonly Guid HubInterface = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");
    [StructLayout(LayoutKind.Sequential)] struct DeviceInfo { internal uint Size; internal Guid Class; internal uint Instance; internal IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct InterfaceInfo { internal uint Size; internal Guid Class; internal uint Flags; internal IntPtr Reserved; }
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid kind,string enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr device,ref Guid kind,uint index,ref InterfaceInfo info);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,ref InterfaceInfo info,IntPtr detail,uint bytes,out uint required,ref DeviceInfo device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent,uint child,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_IDW(uint device,StringBuilder id,uint length,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_DevNode_Registry_PropertyW(uint device,uint property,out uint type,out uint value,ref uint bytes,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_Interface_List_SizeW(out uint length,ref Guid kind,string id,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_Interface_ListW(ref Guid kind,string id,[Out] char[] buffer,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string path,uint access,uint sharing,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle file,uint code,byte[] input,int inputBytes,[Out] byte[] output,int outputBytes,out int returned,IntPtr overlapped);
    static SafeFileHandle Open(string path) => CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
    static uint? DiskNumber(string path)
    {
        using var file=Open(path);if(file.IsInvalid)return null;
        var bytes=new byte[12];
        return DeviceIoControl(file,0x2d1080,null,0,bytes,bytes.Length,out int returned,IntPtr.Zero)&&returned>=12&&BitConverter.ToUInt32(bytes)==7?BitConverter.ToUInt32(bytes,4):null;
    }
    static string Id(uint instance){var text=new StringBuilder(512);return CM_Get_Device_IDW(instance,text,(uint)text.Capacity,0)==0?text.ToString():"";}
    static string HubPath(string id)
    {
        var kind=HubInterface;
        if(CM_Get_Device_Interface_List_SizeW(out uint size,ref kind,id,0)!=0||size<=1)return null;
        var paths=new char[size];
        return CM_Get_Device_Interface_ListW(ref kind,id,paths,size,0)==0?new string(paths).Split('\0',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault():null;
    }
    internal static double LaneBits(uint speed,uint zeroBasedLanes) => (speed>>16)*Math.Pow(1000,(speed>>4)&3)*(zeroBasedLanes+1);
    internal static string Rate(double bits) => bits>=1e9?$"{bits/1e9:0.##} Gbps":$"{bits/1e6:0.##} Mbps";
    static Link Probe(uint instance)
    {
        // Walk the actual storage device's parents, not a guessed drive model or
        // port maximum. A composite USB function may have more than one parent.
        while(CM_Get_Parent(out uint parent,instance,0)==0)
        {
            string hub=HubPath(Id(parent));
            if(hub!=null)
            {
                uint bytes=4;
                if(CM_Get_DevNode_Registry_PropertyW(instance,0x1d,out _,out uint port,ref bytes,0)!=0||port==0)return new("USB · speed unavailable",Id(instance),hub,0,0,0,0);
                using var file=Open(hub);
                if(file.IsInvalid)return new("USB · speed unavailable",Id(instance),hub,port,0,0,Marshal.GetLastWin32Error());
                var info=new byte[24];BitConverter.GetBytes(port).CopyTo(info,0);BitConverter.GetBytes(24u).CopyTo(info,4);
                if(DeviceIoControl(file,(0x22u<<16)|(289u<<2),info,info.Length,info,info.Length,out int read,IntPtr.Zero)&&read>=24)
                {
                    double rx=LaneBits(BitConverter.ToUInt32(info,8),BitConverter.ToUInt32(info,12));
                    double tx=LaneBits(BitConverter.ToUInt32(info,16),BitConverter.ToUInt32(info,20));
                    if(rx>0&&tx>0)return new(rx==tx?"USB · "+Rate(rx):"USB · ↓"+Rate(rx)+" ↑"+Rate(tx),Id(instance),hub,port,rx,tx,0);
                }
                // Older hub stacks expose only an operating-speed category. Do
                // not confuse their capability flags with negotiated speed.
                var v2=new byte[16];BitConverter.GetBytes(port).CopyTo(v2,0);BitConverter.GetBytes(16u).CopyTo(v2,4);BitConverter.GetBytes(4u).CopyTo(v2,8);
                if(DeviceIoControl(file,(0x22u<<16)|(279u<<2),v2,16,v2,16,out read,IntPtr.Zero)&&read>=16)
                {
                    uint flags=BitConverter.ToUInt32(v2,12);
                    if((flags&4)!=0)return new("USB · ≥10 Gbps",Id(instance),hub,port,0,0,0);
                    if((flags&1)!=0)return new("USB · 5 Gbps",Id(instance),hub,port,5e9,5e9,0);
                }
                var legacy=new byte[36];BitConverter.GetBytes(port).CopyTo(legacy,0);
                if(DeviceIoControl(file,(0x22u<<16)|(274u<<2),legacy,36,legacy,36,out read,IntPtr.Zero)&&read>=24)
                {
                    double rate=legacy[23] switch{0=>1.5e6,1=>12e6,2=>480e6,3=>5e9,_=>0};
                    if(rate>0)return new("USB · "+Rate(rate),Id(instance),hub,port,rate,rate,0);
                }
                return new("USB · speed unavailable",Id(instance),hub,port,0,0,Marshal.GetLastWin32Error());
            }
            instance=parent;
        }
        return null;
    }
    internal static Dictionary<string,Link> Read(IEnumerable<string> drives)
    {
        var roots=new Dictionary<uint,List<string>>();
        foreach(string root in drives)
            if(DiskNumber(@"\\.\"+root.TrimEnd('\\')) is uint number)
            {if(!roots.TryGetValue(number,out var list))roots[number]=list=new();list.Add(root);}
        var result=new Dictionary<string,Link>(StringComparer.OrdinalIgnoreCase);
        var kind=DiskInterface;IntPtr set=SetupDiGetClassDevsW(ref kind,null,IntPtr.Zero,0x12);
        if(set==new IntPtr(-1))return result;
        try
        {
            for(uint index=0;;index++)
            {
                var face=new InterfaceInfo{Size=(uint)Marshal.SizeOf<InterfaceInfo>()};
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref kind,index,ref face))break;
                var device=new DeviceInfo{Size=(uint)Marshal.SizeOf<DeviceInfo>()};
                SetupDiGetDeviceInterfaceDetailW(set,ref face,IntPtr.Zero,0,out uint needed,ref device);
                if(needed==0)continue;
                IntPtr detail=Marshal.AllocHGlobal(checked((int)needed));
                try
                {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetailW(set,ref face,detail,needed,out _,ref device))continue;
                    string path=Marshal.PtrToStringUni(detail+4);
                    if(DiskNumber(path) is not uint number||!roots.TryGetValue(number,out var matching))continue;
                    if(Probe(device.Instance) is { } link)foreach(string root in matching)result[root]=link;
                }
                finally{Marshal.FreeHGlobal(detail);}
            }
        }
        finally{SetupDiDestroyDeviceInfoList(set);}
        return result;
    }
}
