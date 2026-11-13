using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CsvHelper;
using Microsoft.Win32;

namespace FileExplorer;

internal sealed record SecurityRule(string Id,string Category,string Name,string Method,string Argument,string Path,string Item,string DcpPath,string DcpItem,
    string Default,string Recommended,string Operator,string Severity,int Line);
internal sealed record SecurityFinding(string Id,string Category,string Name,string Severity,string Target,string Operator,string Recommended,string Current,
    bool WindowsDefault,string Result,int Line);

// Security check (MAINT-40): HardeningKitty's main machine list (scip ag, MIT, vendor/HardeningKitty) is
// evaluated read-only. Nothing here writes; values the caller cannot read are reported, never guessed.
internal static class MaintenanceSecurity
{
    internal const string Met="Met",NotMet="Not met",NeedsAdmin="Needs administrator access",Unavailable="Not available";
    internal static readonly string[] Evaluated=["Registry","RegistryList","service","WindowsOptionalFeature","ScheduledTask","MpComputerStatus","MpPreference","MpPreferenceAsr","MpPreferenceExclusion","BitLockerVolume"];
    internal const string ResourceName="FileExplorer.SecurityChecks.csv";

    internal static SecurityRule[] Rules(Stream source=null)
    {
        using var stream=source??Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)??throw new FileNotFoundException("The security check list is missing from this build");
        using var reader=new StreamReader(stream);using var csv=new CsvReader(reader,CultureInfo.InvariantCulture);
        csv.Read();csv.ReadHeader();var rules=new List<SecurityRule>();
        while(csv.Read())
        {
            string F(string name)=>csv.GetField(name)??"";
            rules.Add(new(F("ID"),F("Category"),F("Name"),F("Method"),F("MethodArgument"),F("RegistryPath"),F("RegistryItem"),F("RegistryPathDCP"),F("RegistryItemIntune"),
                F("DefaultValue"),F("RecommendedValue"),F("Operator"),F("Severity"),csv.Parser.RawRow));
        }
        return rules.ToArray();
    }
    // Methods that need tools Pathweft does not run (audit/security policy export, user rights, firewall, mitigations, CIM, bcdedit).
    internal static Dictionary<string,int> NotEvaluated(SecurityRule[] rules)=>rules.Where(r=>!Evaluated.Contains(r.Method)).GroupBy(r=>r.Method).ToDictionary(g=>g.Key,g=>g.Count());

    internal static SecurityFinding[] Check(CancellationToken cancel=default)
    {
        var rules=Rules();var defender=new Lazy<Wmi>(()=>Wmi.Query(@"root\Microsoft\Windows\Defender","SELECT * FROM MSFT_MpPreference"));
        var status=new Lazy<Wmi>(()=>Wmi.Query(@"root\Microsoft\Windows\Defender","SELECT * FROM MSFT_MpComputerStatus"));
        var bitlocker=new Lazy<Wmi>(()=>Wmi.Query(@"root\CIMV2\Security\MicrosoftVolumeEncryption","SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter='"+(Environment.GetEnvironmentVariable("SystemDrive")??"C:")+"'"));
        var features=new Lazy<Wmi>(()=>Wmi.Query(@"root\CIMV2","SELECT Name,InstallState FROM Win32_OptionalFeature"));
        var findings=new List<SecurityFinding>();
        foreach(var rule in rules.Where(r=>Evaluated.Contains(r.Method)))
        {
            cancel.ThrowIfCancellationRequested();
            Reading value;
            try
            {
                value=rule.Method switch
                {
                    "Registry"=>Registry(rule),"RegistryList"=>RegistryList(rule),"service"=>Service(rule.Argument),
                    "WindowsOptionalFeature"=>Feature(features.Value,rule.Argument),"ScheduledTask"=>ScheduledTask(rule.Argument),
                    "MpComputerStatus"=>Property(status.Value,rule.Argument),"MpPreference"=>Property(defender.Value,rule.Argument),
                    "MpPreferenceAsr"=>Asr(defender.Value,rule.Argument),"MpPreferenceExclusion"=>Exclusion(defender.Value,rule.Argument),
                    "BitLockerVolume"=>BitLocker(bitlocker.Value,rule.Argument),_=>throw new InvalidOperationException()
                };
            }
            catch(Exception error)when(error is UnauthorizedAccessException or System.Security.SecurityException||error is COMException com&&(uint)com.HResult==0x80041003)
            {value=new(null,false,NeedsAdmin);}
            catch(Exception error)when(error is COMException or IOException or InvalidCastException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {value=new(null,false,Unavailable);}
            findings.Add(Evaluate(rule,value));
        }
        return findings.ToArray();
    }
    // Current value, whether it came from the list's Windows default, or a reason it could not be read.
    internal sealed record Reading(string Value,bool WindowsDefault=false,string Problem=null);
    internal static SecurityFinding Evaluate(SecurityRule rule,Reading reading)
    {
        string target=Target(rule);
        if(reading.Problem!=null)return new(rule.Id,rule.Category,rule.Name,rule.Severity,target,rule.Operator,rule.Recommended,reading.Problem,false,reading.Problem,rule.Line);
        string current=reading.Value??(rule.Default.Length>0?rule.Default:null);bool windowsDefault=reading.Value==null&&current!=null;
        if(current==null)return new(rule.Id,rule.Category,rule.Name,rule.Severity,target,rule.Operator,rule.Recommended,"Not configured",false,NotMet,rule.Line);
        return new(rule.Id,rule.Category,rule.Name,rule.Severity,target,rule.Operator,rule.Recommended,current,windowsDefault,Compare(current,rule.Operator,rule.Recommended)?Met:NotMet,rule.Line);
    }
    internal static bool Compare(string current,string op,string recommended)
    {
        // Something absent cannot run: a missing service or task meets a "Disabled" recommendation.
        if(op=="="&&recommended.Equals("Disabled",StringComparison.OrdinalIgnoreCase)&&current is "Not installed" or "Not present")return true;
        bool numbers=double.TryParse(current,NumberStyles.Float,CultureInfo.InvariantCulture,out double a)&double.TryParse(recommended,NumberStyles.Float,CultureInfo.InvariantCulture,out double b);
        bool equal=numbers?a==b:string.Equals(current.Trim(),recommended.Trim(),StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "="=>equal,"!="=>!equal,
            ">="=>numbers&&a>=b,"<="=>numbers&&a<=b,
            "contains"=>current.Contains(recommended,StringComparison.OrdinalIgnoreCase),
            "=|0"=>equal||current.Trim()=="0",
            _=>false
        };
    }
    static string Target(SecurityRule rule)=>rule.Method switch
    {
        "Registry"=>rule.Path+"\\"+rule.Item,"RegistryList"=>rule.Path+" (list contains "+rule.Item+")",
        "service"=>"Service "+rule.Argument+" startup type","WindowsOptionalFeature"=>"Windows optional feature "+rule.Argument,
        "ScheduledTask"=>"Scheduled task "+rule.Argument,"MpPreferenceAsr"=>"Microsoft Defender attack surface reduction rule "+rule.Argument,
        "BitLockerVolume"=>"BitLocker system drive "+rule.Argument,_=>"Microsoft Defender "+rule.Argument
    };

    static RegistryKey Open(string path,out string sub)
    {
        int colon=path.IndexOf(":\\",StringComparison.Ordinal);if(colon<0)throw new ArgumentException("Unsupported registry path");
        sub=path[(colon+2)..];
        return RegistryKey.OpenBaseKey(path[..colon].ToUpperInvariant() switch{"HKLM"=>RegistryHive.LocalMachine,"HKCU"=>RegistryHive.CurrentUser,_=>throw new ArgumentException("Unsupported registry hive")},RegistryView.Registry64);
    }
    static string Text(object value)=>value switch{null=>null,string[] list=>string.Join(";",list),byte[] bytes=>Convert.ToHexString(bytes),int number=>((uint)number).ToString(CultureInfo.InvariantCulture),_=>Convert.ToString(value,CultureInfo.InvariantCulture)};
    static string Value(string path,string item)
    {
        using var root=Open(path,out string sub);using var key=root.OpenSubKey(sub);
        return Text(key?.GetValue(item,null,RegistryValueOptions.DoNotExpandEnvironmentNames));
    }
    // A policy delivered by device management lands in PolicyManager\current\device; HardeningKitty reads it there too.
    static Reading Registry(SecurityRule rule)=>new(Value(rule.Path,rule.Item)??(rule.DcpPath.Length>0&&rule.DcpItem.Length>0?Value(rule.DcpPath,rule.DcpItem):null));
    static Reading RegistryList(SecurityRule rule)
    {
        using var root=Open(rule.Path,out string sub);using var key=root.OpenSubKey(sub);
        if(key==null)return new(null);
        return new(key.GetValueNames().Select(n=>Text(key.GetValue(n))).Any(v=>string.Equals(v,rule.Item,StringComparison.OrdinalIgnoreCase))?rule.Item:"Not in list");
    }
    static Reading Service(string name)
    {
        using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name);
        if(key==null)return new("Not installed");
        return new((key.GetValue("Start") as int?) switch{0=>"Boot",1=>"System",2=>"Automatic",3=>"Manual",4=>"Disabled",_=>"Unknown"});
    }
    static Reading Feature(Wmi wmi,string name)
    {
        var item=wmi.Items.FirstOrDefault(o=>string.Equals(wmi.Get(o,"Name") as string,name,StringComparison.OrdinalIgnoreCase));
        if(item==null)return new("Not present");
        return new(Convert.ToInt32(wmi.Get(item,"InstallState"),CultureInfo.InvariantCulture) switch{1=>"Enabled",2=>"Disabled",3=>"DisabledWithPayloadRemoved",_=>"Unknown"});
    }
    static Reading ScheduledTask(string name)
    {
        string state=null;
        using var session=new MaintenanceStartup.TaskSession();
        session.Each((task,_)=>{if(state==null&&string.Equals((string)task.Name,name,StringComparison.OrdinalIgnoreCase))state=(bool)task.Enabled?"Ready":"Disabled";},CancellationToken.None,(_,_)=>{});
        return new(state??"Not present");
    }
    static Reading Property(Wmi wmi,string name)
    {
        var item=wmi.Items.FirstOrDefault()??throw new IOException("Microsoft Defender is not available");
        object value=wmi.Get(item,name);
        return value is string text&&text.StartsWith("N/A",StringComparison.Ordinal)?new(null,false,NeedsAdmin):new(Text(value));
    }
    static Reading Asr(Wmi wmi,string id)
    {
        var item=wmi.Items.FirstOrDefault()??throw new IOException("Microsoft Defender is not available");
        var ids=wmi.Get(item,"AttackSurfaceReductionRules_Ids") as object[];var actions=wmi.Get(item,"AttackSurfaceReductionRules_Actions") as object[];
        if(ids==null||actions==null)return new(null);
        int index=Array.FindIndex(ids,x=>string.Equals(Convert.ToString(x,CultureInfo.InvariantCulture),id,StringComparison.OrdinalIgnoreCase));
        return new(index<0||index>=actions.Length?null:Convert.ToString(actions[index],CultureInfo.InvariantCulture));
    }
    // An exclusion list passes when it is empty; Defender hides exclusions from non-administrators.
    static Reading Exclusion(Wmi wmi,string name)
    {
        var item=wmi.Items.FirstOrDefault()??throw new IOException("Microsoft Defender is not available");
        var values=(wmi.Get(item,name) as object[])?.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)).ToArray()??[];
        if(values.Any(v=>v!=null&&v.StartsWith("N/A",StringComparison.Ordinal)))return new(null,false,NeedsAdmin);
        return new(values.Length==0?"":string.Join(";",values));
    }
    static Reading BitLocker(Wmi wmi,string name)
    {
        var item=wmi.Items.FirstOrDefault()??throw new IOException("BitLocker is not available");
        object value=wmi.Get(item,name=="VolumeStatus"?"ConversionStatus":name);
        int number=Convert.ToInt32(value,CultureInfo.InvariantCulture);
        return new(name=="VolumeStatus"
            ?number switch{0=>"FullyDecrypted",1=>"FullyEncrypted",2=>"EncryptionInProgress",3=>"DecryptionInProgress",4=>"EncryptionPaused",5=>"DecryptionPaused",_=>"Unknown"}
            :number switch{0=>"None",1=>"Aes128Diffuser",2=>"Aes256Diffuser",3=>"Aes128",4=>"Aes256",5=>"Hardware",6=>"XtsAes128",7=>"XtsAes256",_=>"Unknown"});
    }

    // Late-bound WMI scripting objects, like the Task Scheduler session; no WMI package is shipped.
    internal sealed class Wmi
    {
        internal object[] Items {get;private init;}=[];
        internal static Wmi Query(string ns,string query)
        {
            dynamic locator=Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator",true));
            try
            {
                dynamic services=locator.ConnectServer(".",ns);dynamic set=services.ExecQuery(query);
                int count=set.Count;var items=new object[count];for(int i=0;i<count;i++)items[i]=set.ItemIndex(i);
                return new Wmi{Items=items};
            }
            finally{Marshal.ReleaseComObject(locator);}
        }
        internal object Get(object item,string name){dynamic value=((dynamic)item).Properties_.Item(name);return value.Value;}
    }
}
