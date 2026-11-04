using Microsoft.Win32;

namespace FileExplorer.Preview;

internal static class ShellClassInspection
{
    internal static bool IsInput(string path)=>path?.StartsWith("::{",StringComparison.Ordinal)==true;
    internal static List<(string Name,string Value)> Read(string path,CancellationToken token)
    {
        if(path.Length!=40||!Guid.TryParse(path[2..],out var id))throw new ArgumentException("Enter a Shell class as ::{GUID}.");
        token.ThrowIfCancellationRequested();string keyName="CLSID\\"+id.ToString("B");
        var rows=new List<(string,string)>{("Class identifier",id.ToString("B")),("Inspection","Read-only registration; the class is not activated")};
        using var key=Registry.ClassesRoot.OpenSubKey(keyName,false);
        if(key==null){rows.Add(("Registration","Not registered for this Windows user/process architecture"));return rows;}
        foreach(var name in key.GetValueNames().Take(100))
            rows.Add((name.Length==0?"Name":name,Convert.ToString(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames))));
        foreach(string child in new[]{"InprocServer32","LocalServer32","ProgID","TreatAs","ShellFolder","DefaultIcon"})
        {
            token.ThrowIfCancellationRequested();using var sub=key.OpenSubKey(child,false);if(sub==null)continue;
            foreach(var name in sub.GetValueNames().Take(100))rows.Add((child+(name.Length>0?" / "+name:""),Convert.ToString(sub.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames))));
        }
        return rows;
    }
}
