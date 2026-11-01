// Selected libmpv client API ABI. Runtime/source pin: vendor/MPV/runtime-manifest.json.
// mpv contributors, GPL-2.0-or-later; see the bundled original notices/sources.
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer.Preview;

internal static class MpvNative
{
    // Exact application-owned location. Do not select a standalone installation
    // or load arbitrary DLLs/configuration from the working directory.
    static readonly IntPtr Library=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"MPV","libmpv-2.dll"),typeof(App).Assembly,
        DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32);
    static T Bind<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library,name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr CreateApi();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int HandleApi(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DestroyApi(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int OptionApi(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CommandApi(IntPtr handle,ulong id,IntPtr arguments);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr EventApi(IntPtr handle,double wait);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ObserveApi(IntPtr handle,ulong id,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr PropertyApi(IntPtr handle,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr ErrorApi(int error);
    internal static readonly CreateApi Create=Bind<CreateApi>("mpv_create");
    internal static readonly HandleApi Initialize=Bind<HandleApi>("mpv_initialize");
    internal static readonly DestroyApi Destroy=Bind<DestroyApi>("mpv_terminate_destroy"),Wake=Bind<DestroyApi>("mpv_wakeup"),Free=Bind<DestroyApi>("mpv_free");
    internal static readonly OptionApi Option=Bind<OptionApi>("mpv_set_option_string");
    internal static readonly CommandApi Command=Bind<CommandApi>("mpv_command_async");
    internal static readonly EventApi Wait=Bind<EventApi>("mpv_wait_event");
    internal static readonly ObserveApi Observe=Bind<ObserveApi>("mpv_observe_property");
    internal static readonly PropertyApi Property=Bind<PropertyApi>("mpv_get_property_string");
    internal static readonly ErrorApi Error=Bind<ErrorApi>("mpv_error_string");
    [StructLayout(LayoutKind.Sequential)] internal struct Event{internal int Id,Error;internal ulong Reply;internal IntPtr Data;}
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyEvent{internal IntPtr Name;internal int Format;internal IntPtr Data;}
    internal static void Check(int result){if(result<0)throw new InvalidOperationException("Media playback: "+Marshal.PtrToStringUTF8(Error(result)));}
    internal static void Send(IntPtr handle,ulong id,params string[] arguments)
    {
        var values=new IntPtr[arguments.Length+1];var array=Marshal.AllocHGlobal(values.Length*IntPtr.Size);
        try
        {
            for(int i=0;i<arguments.Length;i++)values[i]=Marshal.StringToCoTaskMemUTF8(arguments[i]);
            Marshal.Copy(values,0,array,values.Length);Check(Command(handle,id,array));
        }
        finally{foreach(var value in values)if(value!=IntPtr.Zero)Marshal.FreeCoTaskMem(value);Marshal.FreeHGlobal(array);}
    }
}
