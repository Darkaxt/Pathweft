using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace FileExplorer;

// Only Setup's persistent application/helper launches use the desktop context.
// Ordinary file Open remains Windows ShellExecute, with the user's associations.
static class DesktopLaunch
{
    const string Prefix="Pathweft-Setup-";
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)]
    delegate int Launch([MarshalAs(UnmanagedType.LPWStr)]string file,[MarshalAs(UnmanagedType.LPWStr)]string arguments,[MarshalAs(UnmanagedType.LPWStr)]string directory,int show);
    internal static void Start(string arguments,int show=1)
    {
        string version=typeof(App).Assembly.GetName().Version.ToString(3);
        var library=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,$"PathweftShell-{version}-x64.dll"));
        try{Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<Launch>(NativeLibrary.GetExport(library,"PathweftLaunchDesktop"))(Environment.ProcessPath,arguments,AppContext.BaseDirectory,show));}
        finally{NativeLibrary.Free(library);}
    }
    internal static int FromSetup(bool resume)
    {
        if(!resume){Start("--front");return 0;}
        // Setup must receive the real resume result, not just COM acceptance.
        string name=Prefix+Guid.NewGuid().ToString("N");
        using var server=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.CurrentUserOnly);
        string arguments="--resume-agent --desktop-result="+name;
#if DEBUG
        if(Environment.GetEnvironmentVariable("PATHWEFT_DESKTOP_TEST_PROFILE") is {Length:>0} profile)
        {
            if(!Path.GetFullPath(profile).StartsWith(@"D:\Temp\pathweft-s8-delivery\appearance-",StringComparison.OrdinalIgnoreCase)||profile.Contains('"'))throw new ArgumentException("Owned desktop fixture required.");
            arguments+=" \"--desktop-test-profile="+profile+"\"";
        }
#endif
        Start(arguments,0);
        server.WaitForConnection();
        using var reader=new StreamReader(server);
        return int.TryParse(reader.ReadLine(),out var result)?result:1;
    }
    internal static void Report(string name,int result)
    {
        if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name[Prefix.Length..],"N",out _))throw new ArgumentException("Invalid Setup result channel.");
        using var pipe=new NamedPipeClientStream(".",name,PipeDirection.Out,PipeOptions.CurrentUserOnly);
        pipe.Connect();using var writer=new StreamWriter(pipe){AutoFlush=true};writer.WriteLine(result);
    }
}
