using Microsoft.Win32;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

internal static class CloudVolumes
{
    internal record Quota(long Total,long Used)
    {
        internal long Free=>Math.Max(0,Total-Used);
    }
    // DriveFS's Shell registration binds a root to its account across letter changes.
    internal static Dictionary<string,string> GoogleMounts()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Google\DriveFS\Share");
            return key?.GetValue("SyncTargets") is byte[] data?ReadGoogleMounts(data):new(StringComparer.OrdinalIgnoreCase);
        }
        catch(System.Security.SecurityException){return new(StringComparer.OrdinalIgnoreCase);}
        catch(UnauthorizedAccessException){return new(StringComparer.OrdinalIgnoreCase);}
    }
    internal static HashSet<string> GoogleRoots()=>new(GoogleMounts().Keys,StringComparer.OrdinalIgnoreCase);
    internal static HashSet<string> ReadGoogleRoots(byte[] data)=>new(ReadGoogleMounts(data).Keys,StringComparer.OrdinalIgnoreCase);
    internal static Dictionary<string,string> ReadGoogleMounts(byte[] data)
    {
        var mounts=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var entry in Fields(data).Where(f=>f.Tag==1&&f.Bytes!=null))
            {
                var fields=Fields(entry.Bytes).ToArray();
                string root=Encoding.UTF8.GetString(fields.Single(f=>f.Tag==2&&f.Bytes!=null).Bytes).TrimEnd('\\');
                if(root.Length!=2||!char.IsAsciiLetter(root[0])||root[1]!=':')continue;
                string account="";
                var identity=fields.SingleOrDefault(f=>f.Tag==1&&f.Bytes!=null);
                if(identity.Bytes!=null)
                {
                    try
                    {
                        var id=Fields(identity.Bytes).SingleOrDefault(f=>f.Tag==1&&f.Bytes!=null).Bytes;
                        if(id!=null)account=Encoding.UTF8.GetString(id);
                        if(account.Length==0||!account.All(char.IsAsciiDigit))account="";
                    }
                    catch(Exception ex) when(ex is InvalidDataException or InvalidOperationException){account="";}
                }
                root+="\\";
                if(mounts.TryGetValue(root,out var previous)&&previous!=account)account="";
                mounts[root]=account;
            }
        }
        catch(Exception ex) when(ex is InvalidDataException or InvalidOperationException){mounts.Clear();}
        return mounts;
    }
    internal static Quota GoogleQuota(string account)
    {
        if(string.IsNullOrEmpty(account)||!account.All(char.IsAsciiDigit))return null;
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","DriveFS",account,"metadata_sqlite_db");
        return ReadQuotaDatabase(file);
    }
    // Read only these non-auth properties in one statement/snapshot. No tokens,
    // catalogue, settings, database copies or network calls. No CREATE/write flags.
    internal static Quota ReadQuotaDatabase(string file)
    {
        IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
        try
        {
            if(sqlite3_open_v2(file,out db,1|0x8000,IntPtr.Zero)!=0)return null;
            if(sqlite3_db_readonly(db,"main")!=1)return null;
            const string sql="SELECT property,value FROM properties WHERE property IN ('driveway_account','driveway_account_fields')";
            if(sqlite3_prepare_v2(db,sql,-1,out statement,IntPtr.Zero)!=0)return null;
            byte[] account=null;string mask=null;int step;
            while((step=sqlite3_step(statement))==100)
            {
                string key=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,0));
                int size=sqlite3_column_bytes(statement,1);
                var pointer=sqlite3_column_blob(statement,1);
                if(size<=0||pointer==IntPtr.Zero)return null;
                var value=new byte[size];Marshal.Copy(pointer,value,0,size);
                if(key=="driveway_account")account=value;
                else if(key=="driveway_account_fields")mask=Encoding.UTF8.GetString(value);
            }
            return step==101?ReadGoogleQuota(account,mask):null;
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException){return null;}
        finally
        {
            if(statement!=IntPtr.Zero)sqlite3_finalize(statement);
            if(db!=IntPtr.Zero)sqlite3_close(db);
        }
    }
    // Observed DriveFS account schema v1: wrapper.account.metadata.quota.
    // Require semantic field names and consistent byte counters. Unknown/unlimited
    // schemas stay unavailable; the cache volume is never a cloud-quota fallback.
    internal static Quota ReadGoogleQuota(byte[] account,string mask)
    {
        if(account==null||mask==null||!new[]{"quota_type","bytes_limit","bytes_used_by_all_services","bytes_remaining"}.All(n=>mask.Contains("account.metadata.quota."+n,StringComparison.Ordinal)))return null;
        try
        {
            var wrapper=Fields(account).ToArray();
            if(wrapper.Single(f=>f.Tag==1&&f.Bytes==null).Number!=1)return null;
            byte[] Message(IEnumerable<Field> fields,int tag)=>fields.Single(f=>f.Tag==tag&&f.Bytes!=null).Bytes;
            var quota=Fields(Message(Fields(Message(Fields(Message(wrapper,2)),3)),1)).ToArray();
            ulong Number(int tag)=>quota.Single(f=>f.Tag==tag&&f.Bytes==null).Number;
            ulong total=Number(7),used=Number(10),remaining=Number(8);
            if(Number(6)!=1||total==0||total>long.MaxValue||used>long.MaxValue||remaining>long.MaxValue||remaining!=(used>=total?0:total-used))return null;
            return new((long)total,(long)used);
        }
        catch(Exception ex) when(ex is InvalidDataException or InvalidOperationException){return null;}
    }
    readonly record struct Field(int Tag,ulong Number,byte[] Bytes);
    static IEnumerable<Field> Fields(byte[] data)
    {
        int position=0;
        ulong Number()
        {
            ulong value=0;
            for(int shift=0;shift<70;shift+=7)
            {
                if(position==data.Length)throw new InvalidDataException();
                byte b=data[position++];if(shift==63&&b>1)throw new InvalidDataException();
                value|=(ulong)(b&127)<<shift;if(b<128)return value;
            }
            throw new InvalidDataException();
        }
        while(position<data.Length)
        {
            ulong tag=Number();if(tag<8||tag>>3>0x1fffffff)throw new InvalidDataException();
            int length;
            switch(tag&7)
            {
                case 0:yield return new((int)(tag>>3),Number(),null);continue;
                case 1:length=8;break;
                case 2:ulong size=Number();if(size>int.MaxValue)throw new InvalidDataException();length=(int)size;break;
                case 5:length=4;break;
                default:throw new InvalidDataException();
            }
            if(length>data.Length-position)throw new InvalidDataException();
            if((tag&7)==2)yield return new((int)(tag>>3),0,data[position..(position+length)]);
            position+=length;
        }
    }
    internal static string Provider(string root,string remote,HashSet<string> googleRoots)
    {
        if(googleRoots.Contains(root))return "Google Drive";
        string host=remote.TrimStart('\\').Split('\\')[0];
        return host.EndsWith(".drive.tresorit.com",StringComparison.OrdinalIgnoreCase)?"Tresorit":"";
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_db_readonly(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int size,out IntPtr statement,IntPtr tail);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_blob(IntPtr statement,int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement,int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
}
