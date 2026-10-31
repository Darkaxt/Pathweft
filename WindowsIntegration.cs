using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

// Per-user Shell verbs route before an Explorer window is created. There is no
// window watcher, process-creation interception or managed code in caller hosts.
internal static class WindowsIntegration
{
    const string Classes=@"Software\Classes\";
    const string Verb="pathweft";
    const string RouterClsid="{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}";
    static readonly string[] Types={"Directory","Drive"};
#if DEBUG
    internal static RegistryKey VerificationRoot;
    static RegistryKey CurrentUser=>VerificationRoot??Registry.CurrentUser;
#else
    static RegistryKey CurrentUser=>Registry.CurrentUser;
#endif
    internal static string RecoveryPath=>Path.Combine(Settings.Dir,"shell-integration.json");
    internal sealed record ValueState(string Key,string Name,bool Existed,string Value,RegistryValueKind Kind,string Written);
    internal sealed record Registration(int Version,List<ValueState> Values,List<string> CreatedKeys);

    internal static bool Enabled
    {
        get
        {
            foreach(var item in Values())
            {
                using var key=CurrentUser.OpenSubKey(item.Key);
                if(key?.GetValue(item.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string!=item.Value)return false;
            }
            return true;
        }
    }

    static string Command=>$"\"{Environment.ProcessPath}\" \"%1\"";
    static IEnumerable<(string Key,string Name,string Value)> Values()
    {
        foreach(var type in Types)
        {
            var shell=Classes+type+@"\shell";
            // Keep the canonical verb, but preserve browser navigation through
            // its native site. A plain replacement command loses that context.
            yield return(shell,"","open");
            yield return(shell+@"\pathweft","","Open in Pathweft");
            yield return(shell+@"\pathweft","Icon",Environment.ProcessPath);
            yield return(shell+@"\pathweft\command","",Command);
            foreach(var verb in new[]{"open","explore"})
            {
                yield return(shell+"\\"+verb+@"\command","",Command);
                yield return(shell+"\\"+verb+@"\command","DelegateExecute",RouterClsid);
            }
        }
        foreach(var arch in new[]{"x64","x86"})
        {
            var key=Classes+(arch=="x86"?@"WOW6432Node\CLSID\":@"CLSID\")+RouterClsid+@"\InprocServer32";
            yield return(key,"",RouterPath(arch));
            yield return(key,"ThreadingModel","Apartment");
        }
    }

    static string RouterPath(string arch)=>Path.Combine(AppContext.BaseDirectory,$"PathweftShell-{typeof(WindowsIntegration).Assembly.GetName().Version.ToString(3)}-{arch}.dll");

    internal static void SetEnabled(bool enabled)
    {
        if(Settings.Profile!=null
#if DEBUG
           && VerificationRoot==null
#endif
          )throw new InvalidOperationException("An isolated test profile cannot change Windows integration.");
        if(!enabled){Restore();return;}
#if DEBUG
        if(VerificationRoot==null)
#endif
        foreach(var arch in new[]{"x64","x86"})
            if(!File.Exists(RouterPath(arch)))
                throw new FileNotFoundException("The folder integration component is missing. Repair or reinstall Pathweft before enabling it.");
        if(File.Exists(RecoveryPath))
        {
            if(Enabled)return;
            Restore(); // finish prior registration/recovery before capturing a new owner
        }
        var values=new List<ValueState>();var created=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in Values())
        {
            using var key=CurrentUser.OpenSubKey(item.Key);
            bool exists=key?.GetValueNames().Contains(item.Name,StringComparer.OrdinalIgnoreCase)==true;
            var kind=exists?key.GetValueKind(item.Name):RegistryValueKind.String;
            if(exists && kind is not(RegistryValueKind.String or RegistryValueKind.ExpandString))
                throw new InvalidOperationException("A folder handler has an unsupported value type. Its settings have been left unchanged.");
            values.Add(new(item.Key,item.Name,exists,exists?(string)key.GetValue(item.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames):null,kind,item.Value));
            for(var path=item.Key;path.Length>Classes.Length;path=path[..path.LastIndexOf('\\')])
            {
                using var parent=CurrentUser.OpenSubKey(path);
                if(parent==null)created.Add(path);
            }
        }
        Directory.CreateDirectory(Settings.Dir);
        var registration=new Registration(2,values,created.OrderByDescending(x=>x.Length).ToList());
        // Required recovery record is durable before the first registry change.
        Settings.WriteAllLines(RecoveryPath,new[]{JsonSerializer.Serialize(registration)});
        try
        {
            foreach(var item in values)
            {
                using var key=CurrentUser.CreateSubKey(item.Key);
                key.SetValue(item.Name,item.Written,RegistryValueKind.String);
            }
            Notify();
            if(!Enabled)throw new IOException("Folder integration did not retain its settings.");
        }
        catch
        {
            Restore();throw;
        }
    }

    internal static void UpgradeOwnedRegistration()
    {
        // A previous opt-in registration remains opt-in after updating. Never
        // enable routing from a missing record or take over another manager.
        if(Settings.Profile!=null
#if DEBUG
           && VerificationRoot==null
#endif
          )return;
        if(!File.Exists(RecoveryPath)||Enabled)return;
        Registration prior;
        try{prior=JsonSerializer.Deserialize<Registration>(File.ReadAllText(RecoveryPath));}
        catch(JsonException){return;} // normal recovery UI retains the invalid record
        if(prior?.Version is not(1 or 2)||prior.Values==null)return;
        var current=Values().Where(v=>prior.Version==2||!v.Key.Contains(@"\CLSID\",StringComparison.Ordinal)).ToArray();
        if(prior.Values.Count!=current.Length||!current.All(item=>prior.Values.Any(v=>
            v.Key==item.Key&&v.Name==item.Name&&PriorWrittenValue(prior.Version,v,item.Value))))return;
        foreach(var value in prior.Values)
        {
            using var key=CurrentUser.OpenSubKey(value.Key);
            if(key?.GetValue(value.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string!=value.Written)return;
        }
        SetEnabled(true); // validated restore, then recapture the same original owner
    }

    static bool PriorWrittenValue(int version,ValueState prior,string current)
    {
        if(version==1)
        {
            if(prior.Name=="DelegateExecute")return prior.Written=="";
            if(prior.Name==""&&Types.Any(type=>prior.Key==Classes+type+@"\shell"))return prior.Written is "open" or Verb;
        }
        if(version==2&&prior.Name==""&&prior.Key.EndsWith(@"\InprocServer32",StringComparison.Ordinal))
        {
            var parts=Path.GetFileName(prior.Written)?.Split('-');
            return parts?.Length==3&&parts[0]=="PathweftShell"&&Version.TryParse(parts[1],out var parsed)&&parsed.Build>=0&&parsed.Revision<0&&
                parts[2]==(prior.Key.Contains("WOW6432Node",StringComparison.Ordinal)?"x86.dll":"x64.dll")&&
                string.Equals(Path.GetDirectoryName(prior.Written),Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase);
        }
        return prior.Written==current;
    }

    static void Restore()
    {
        if(!File.Exists(RecoveryPath))
        {
            if(Enabled)throw new InvalidOperationException("Folder integration has no recovery record. Existing handlers were left unchanged.");
            return;
        }
        Registration registration;
        try { registration=JsonSerializer.Deserialize<Registration>(File.ReadAllText(RecoveryPath)); }
        catch(JsonException error) { throw new InvalidDataException("The folder integration recovery record could not be read. No handlers were changed.",error); }
        var allowed=Values().Select(v=>(v.Key,v.Name)).ToHashSet();
        var allowedParents=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in allowed)
            for(var path=item.Key;path.Length>Classes.Length;path=path[..path.LastIndexOf('\\')])allowedParents.Add(path);
        if(registration?.Version is not(1 or 2) || registration.Values==null || registration.CreatedKeys==null ||
           registration.Values.Any(v=>!allowed.Contains((v.Key,v.Name)) || v.Kind is not(RegistryValueKind.String or RegistryValueKind.ExpandString) || v.Existed && v.Value==null) ||
           registration.CreatedKeys.Any(k=>!allowedParents.Contains(k)))
            throw new InvalidDataException("The folder integration recovery record is invalid. No handlers were changed.");
        foreach(var item in registration.Values.AsEnumerable().Reverse())
        {
            using var key=CurrentUser.OpenSubKey(item.Key,writable:true);
            // Another default manager owns its changes. Only restore values that
            // are still exactly what this registration wrote.
            if(key?.GetValue(item.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string!=item.Written)continue;
            if(item.Existed)key.SetValue(item.Name,item.Value,item.Kind);
            else key.DeleteValue(item.Name,false);
        }
        foreach(var path in registration.CreatedKeys.OrderByDescending(x=>x.Length))
        {
            using var key=CurrentUser.OpenSubKey(path);
            if(key!=null && key.ValueCount==0 && key.SubKeyCount==0)CurrentUser.DeleteSubKey(path,false);
        }
        File.Delete(RecoveryPath);
        Notify();
    }

    static void Notify()
    {
#if DEBUG
        if(VerificationRoot!=null)return;
#endif
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    [DllImport("shell32.dll")]static extern void SHChangeNotify(uint change,uint flags,IntPtr first,IntPtr second);
}
