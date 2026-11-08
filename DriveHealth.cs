using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// Projection, refresh cadence and warning policy adapted from SysOptimizer
// 6ddd104 DiskHealthService / MainWindow.DriveDetails / DriveStatus (MIT).
// The storage-only boundary uses inbox Windows APIs instead of opening the
// donor's all-device LHM inventory. Never match duplicate models by list order.
internal sealed record DriveHealth(uint Disk,string Name,DateTimeOffset Observed,
    double? TempC=null,double? WarnTempC=null,double? LifePercent=null,
    double? PowerOnHours=null,double? WrittenTB=null,string WindowsHealth=null,
    bool? SpareBelowThreshold=null,byte? CriticalWarning=null,string Source=null,
    string Issue=null,bool PermissionRequired=false)
{
    internal (string Text,string Level) Status=>
        WindowsHealth=="Unhealthy"||SpareBelowThreshold==true||LifePercent<=5||CriticalWarning is {} critical&&(critical&0x3d)!=0?("Back up this drive","bad"):
        WindowsHealth=="Warning"||LifePercent<=10?("Wear warning","warn"):
        TempC is {} temp&&WarnTempC is {} limit&&temp>=limit||CriticalWarning is {} warning&&(warning&2)!=0?("Running hot","warn"):
        WindowsHealth=="Healthy"||CriticalWarning==0?("Healthy","ok"):("Health unavailable","none");
    internal string Details=>string.Join(" · ",new[]{
        TempC is {} temp?$"{temp:0.#}°C"+(WarnTempC is {} warn?$" (warning {warn:0.#}°C)":""):null,
        LifePercent is {} life?$"{life:0}% life remaining":null,
        PowerOnHours is {} hours?$"{hours:N0} h powered on":null,
        WrittenTB is {} written?$"{written:0.##} TB written":null,
        WindowsHealth is {} health?$"Windows: {health}":null}.Where(s=>s!=null));
}

internal static class WindowsDriveHealth
{
    const uint QueryProperty=0x002d1400;
    internal static DriveHealth Read(uint number)
    {
        var value=new DriveHealth(number,$"Physical drive {number}",DateTimeOffset.UtcNow);
        // Number is the IOCTL volume-extent identity, not a friendly name or
        // an ordinal in a WMI result. Associations preserve that same identity.
        object locator=null,services=null,disks=null,disk=null,counters=null;
        try
        {
            locator=Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator",true));
            services=((dynamic)locator).ConnectServer(".",@"root\Microsoft\Windows\Storage");
            disks=((dynamic)services).ExecQuery($"SELECT * FROM MSFT_Disk WHERE Number={number.ToString(CultureInfo.InvariantCulture)}","WQL",0);
            if(Convert.ToInt32(((dynamic)disks).Count,CultureInfo.InvariantCulture)>0)
            {
                disk=((dynamic)disks).ItemIndex(0);
                object Property(object row,string name)=>PerformanceProcessMetadataReader.Property(row,name);
                value=value with{Name=(Property(disk,"FriendlyName") as string)?.Trim() is {Length:>0} name?name:value.Name,
                    WindowsHealth=Number(Property(disk,"HealthStatus")) is not {} health?null:health switch{0=>"Healthy",1=>"Warning",2=>"Unhealthy",_=>"Unknown"},Source="Windows Storage"};
                object path=((dynamic)disk).Path_;
                string relative;
                try{relative=(string)((dynamic)path).RelPath;}
                finally{Marshal.FinalReleaseComObject(path);}
                counters=((dynamic)services).ExecQuery("ASSOCIATORS OF {"+relative+"} WHERE AssocClass=MSFT_DiskToStorageReliabilityCounter ResultClass=MSFT_StorageReliabilityCounter","WQL",0);
                if(Convert.ToInt32(((dynamic)counters).Count,CultureInfo.InvariantCulture)>0)
                {
                    object counter=((dynamic)counters).ItemIndex(0);
                    try
                    {
                        double? Read(string name)=>Number(Property(counter,name));
                        var wear=Read("Wear");
                        // TemperatureMax is the maximum observed temperature,
                        // not the device's warning threshold. Only the NVMe
                        // controller's explicit warning limit populates that fact.
                        value=value with{TempC=Read("Temperature"),LifePercent=wear.HasValue?Math.Clamp(100-wear.Value,0,100):null,PowerOnHours=Read("PowerOnHours")};
                    }
                    finally{Marshal.FinalReleaseComObject(counter);}
                }
            }
        }
        catch(Exception error)when(error is UnauthorizedAccessException or COMException or TypeLoadException)
        {value=value with{Issue=error.Message,PermissionRequired=Denied(error)};}
        finally
        {foreach(var item in new[]{counters,disk,disks,services,locator})if(item!=null&&Marshal.IsComObject(item))Marshal.FinalReleaseComObject(item);}

        // Read-only STORAGE_QUERY_PROPERTY: no vendor pass-through, firmware,
        // ATA writes, kernel driver or device monitoring thread is installed.
        using var handle=CreateFile(@"\\.\PhysicalDrive"+number,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid){int code=Marshal.GetLastWin32Error();return value with{PermissionRequired=value.PermissionRequired||code==5,Issue=value.Issue??new Win32Exception(code).Message};}
        var identity=new byte[12];
        if(!DeviceIoControl(handle,0x002d1080,[],0,identity,12,out uint count,IntPtr.Zero)||count<12||BinaryPrimitives.ReadUInt32LittleEndian(identity.AsSpan(4))!=number)
            return value with{Issue="Windows could not confirm the physical-drive identity"};
        var descriptor=new byte[1024];var query=new byte[12]; // STORAGE_PROPERTY_QUERY includes AdditionalParameters[1] and alignment.
        if(!DeviceIoControl(handle,QueryProperty,query,(uint)query.Length,descriptor,(uint)descriptor.Length,out count,IntPtr.Zero))
        {int code=Marshal.GetLastWin32Error();return value with{PermissionRequired=value.PermissionRequired||code==5,Issue=value.Issue??new Win32Exception(code).Message};}
        if(count<32)return value with{Issue="Windows returned an incomplete storage descriptor"};
        if(BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(28))!=17)return value; // BusTypeNvme
        var smart=Protocol(handle,2,2,512,out int errorCode);
        if(smart==null)return value with{PermissionRequired=value.PermissionRequired||errorCode==5,Issue=value.Issue??new Win32Exception(errorCode).Message};
        var identify=Protocol(handle,1,1,4096,out _);
        value=FromNvme(value,smart,identify);
        return value with{Source=value.Source==null?"NVMe SMART via Windows":value.Source+" + NVMe SMART",Issue=null,PermissionRequired=false};
    }
    // WMI represents an unsupported reliability property as DBNull, including
    // when an elevated query exposes a counter absent from the standard view.
    // Preserve that absence instead of converting it or inventing a zero.
    internal static double? Number(object value)=>value is null or DBNull?null:Convert.ToDouble(value,CultureInfo.InvariantCulture);
    internal static DriveHealth FromNvme(DriveHealth value,ReadOnlySpan<byte> smart,byte[] identify=null)
    {
        if(smart.Length<512)throw new InvalidDataException("NVMe health log is truncated");
        int kelvin=BinaryPrimitives.ReadUInt16LittleEndian(smart[1..]);
        int warning=identify is {Length:>=268}?BinaryPrimitives.ReadUInt16LittleEndian(identify.AsSpan(266)):0;
        return value with{TempC=kelvin>0?kelvin-273.15:value.TempC,WarnTempC=warning>0?warning-273.15:value.WarnTempC,
            LifePercent=Math.Max(0,100-smart[5]),SpareBelowThreshold=smart[3]<smart[4],CriticalWarning=smart[0],
            PowerOnHours=(double)BinaryPrimitives.ReadUInt128LittleEndian(smart[128..]),
            WrittenTB=(double)BinaryPrimitives.ReadUInt128LittleEndian(smart[48..])*512000d/1e12};
    }
    static byte[] Protocol(SafeFileHandle handle,uint type,uint request,int length,out int error)
    {
        // Microsoft STORAGE_PROTOCOL_DATA_DESCRIPTOR: 8-byte header, 40-byte
        // protocol data, then the exact NVMe log/identify payload. Validate all
        // device-returned offsets before parsing. Property id50 = DeviceProtocol.
        var buffer=new byte[48+length];
        void Put(int offset,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset),value);
        Put(0,50);Put(8,3);Put(12,type);Put(16,request);Put(24,40);Put(28,(uint)length);
        if(!DeviceIoControl(handle,QueryProperty,buffer,(uint)buffer.Length,buffer,(uint)buffer.Length,out uint count,IntPtr.Zero)){error=Marshal.GetLastWin32Error();return null;}
        error=13;
        if(count<48||BinaryPrimitives.ReadUInt32LittleEndian(buffer)!=48||BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4))!=48)return null;
        uint offset=BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(24)),size=BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28));
        if(offset<40||size<length||8UL+offset+size>count||count>buffer.Length)return null;
        error=0;return buffer.AsSpan(8+(int)offset,length).ToArray();
    }
    static bool Denied(Exception error)=>error is UnauthorizedAccessException||error.HResult==unchecked((int)0x80070005)||error.HResult==unchecked((int)0x80041003);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateFileW",SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputLength,byte[] output,uint outputLength,out uint returned,IntPtr overlapped);
}
