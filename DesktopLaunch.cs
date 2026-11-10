using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;

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
    internal static int FromSetup(bool resume,bool setDefault=false)
    {
        if(!resume&&!setDefault){Start("--front");return 0;}
        // Setup must receive the real resume result, not just COM acceptance.
        string name=Prefix+Guid.NewGuid().ToString("N");
        // The desktop child runs at medium integrity. It owns the pipe so the
        // elevated Setup client can read its result without a high-integrity
        // pipe rejecting the child's write.
        // CurrentUserOnly compares the token owner, which becomes Administrators
        // after UAC even when the desktop child belongs to the same signed-in user.
        using var client=new NamedPipeClientStream(".",name,PipeDirection.In);
        string arguments=(setDefault?"--set-default":"--resume-agent")+" --desktop-result="+name;
#if DEBUG
        if(resume&&Environment.GetEnvironmentVariable("PATHWEFT_DESKTOP_TEST_PROFILE") is {Length:>0} profile)
        {
            if(!Path.GetFullPath(profile).StartsWith(@"D:\Temp\pathweft-s8-delivery\appearance-",StringComparison.OrdinalIgnoreCase)||profile.Contains('"'))throw new ArgumentException("Owned desktop fixture required.");
            arguments+=" \"--desktop-test-profile="+profile+"\"";
        }
#endif
        Start(arguments,0);
        client.Connect();
        using(var identity=WindowsIdentity.GetCurrent())
        {
            var owner=client.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if(!identity.User.Equals(owner))throw new UnauthorizedAccessException("Desktop result channel belongs to another user.");
        }
        using var reader=new StreamReader(client);
        return int.TryParse(reader.ReadLine(),out var result)?result:1;
    }
    internal static int RunAndReport(string name,Func<int> run)
    {
        if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name[Prefix.Length..],"N",out _))throw new ArgumentException("Invalid Setup result channel.");
        using var server=new NamedPipeServerStream(name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.CurrentUserOnly);
        server.WaitForConnection();
        int result=1;
        try{result=run();return result;}
        finally
        {
            try{using var writer=new StreamWriter(server){AutoFlush=true};writer.WriteLine(result);}
            catch(IOException){/* Setup closed its result channel; it already reports failure. */}
        }
    }
}
