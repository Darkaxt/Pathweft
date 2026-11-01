using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FileExplorer;

// Folder verbs only; executable commands and registry paths are owned by the
// existing native integration, never supplied by this preferences file.
static class FolderRoutes
{
    internal sealed record Route(string Class,string Verb,bool Enabled);
    internal static readonly string[] Classes={"Directory","Drive","Folder"};
    internal static string FilePath=>Path.Combine(Settings.Dir,"folder-routes.json");
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    internal static List<Route> Defaults()=>new(){new("Directory","open",true),new("Directory","explore",true),new("Drive","open",true),new("Drive","explore",true),new("Folder","explore",true)};
    internal static List<Route> Validate(IEnumerable<Route> routes)
    {
        if(routes==null)throw new InvalidDataException("Folder routes must be a JSON array.");
        var valid=new List<Route>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var route in routes)
        {
            string kind=Classes.FirstOrDefault(c=>c.Equals(route?.Class?.Trim(),StringComparison.OrdinalIgnoreCase));
            string verb=route?.Verb?.Trim().ToLowerInvariant();
            if(kind==null)throw new InvalidDataException("Choose Directory, Drive or Folder for each route.");
            if(string.IsNullOrEmpty(verb)||verb.Length>255||!Regex.IsMatch(verb,@"^[a-z][a-z0-9_.-]*$")||verb=="pathweft")throw new InvalidDataException("Enter a Windows verb such as open, explore or opennewwindow. The name pathweft is reserved.");
            if(!seen.Add(kind+"/"+verb))throw new InvalidDataException($"Remove the duplicate {kind}/{verb} route.");
            valid.Add(new(kind,verb,route.Enabled));
        }
        return valid;
    }
    internal static List<Route> Read()
    {
        if(!File.Exists(FilePath))return Defaults();
        try{return Validate(JsonSerializer.Deserialize<List<Route>>(File.ReadAllText(FilePath),Json));}
        catch(JsonException error){throw new InvalidDataException("The folder routes JSON is invalid. Open the JSON file to correct it, then reload.",error);}
    }
    internal static void Write(IEnumerable<Route> routes)=>Settings.WriteAllLines(FilePath,new[]{JsonSerializer.Serialize(Validate(routes),Json)});
}
