using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace FileExplorer;

/// BROWSE-04: the owner's own context actions, kept in actions.json in the data folder
/// (docs/CUSTOM-ACTIONS.md). They appear in Pathweft's menu under Actions and run only when clicked.
internal sealed class CustomAction
{
    public string Name{get;set;}="";
    public string Glyph{get;set;}="";            // a Segoe Fluent Icons character, or empty
    public string Submenu{get;set;}="";          // groups actions under Actions > Submenu
    public string Executable{get;set;}="";
    public string Arguments{get;set;}="";        // %f %F %n %N %d, %% for a literal %
    public string WorkingFolder{get;set;}="";    // empty: the current folder; may use %d
    public string Patterns{get;set;}="*";        // "*.jpg;*.png"; folders ignore it
    public string AppliesTo{get;set;}="both";    // files, folders, both
    public string Selection{get;set;}="any";     // one, several, any

    internal bool Applies(IReadOnlyList<Entry> selection)
    {
        if(selection.Count==0||Selection=="one"&&selection.Count!=1||Selection=="several"&&selection.Count<2)return false;
        var patterns=Patterns.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        foreach(var entry in selection)
        {
            if(entry.IsDir?AppliesTo=="files":AppliesTo=="folders")return false;
            if(!entry.IsDir&&patterns.Length>0&&!patterns.Any(p=>System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p,entry.Name)))return false;
        }
        return true;
    }

    /// One argument, quoted by the rules CommandLineToArgvW and the C runtime read back.
    internal static string Quote(string value)
    {
        if(value.Length>0&&value.IndexOfAny(new[]{' ','\t','"'})<0)return value;
        var text=new StringBuilder("\"");int slashes=0;
        foreach(char c in value)
        {
            if(c=='\\'){slashes++;continue;}
            if(c=='"'){text.Append('\\',slashes*2+1).Append('"');slashes=0;continue;}
            text.Append('\\',slashes).Append(c);slashes=0;
        }
        return text.Append('\\',slashes*2).Append('"').ToString();
    }
    internal static string Expand(string template,IReadOnlyList<string> paths,string folder)
    {
        var text=new StringBuilder();
        for(int i=0;i<template.Length;i++)
        {
            if(template[i]!='%'||i==template.Length-1){text.Append(template[i]);continue;}
            char code=template[++i];
            string Names(IEnumerable<string> values)=>string.Join(" ",values.Select(Quote));
            switch(code)
            {
                case 'f':text.Append(paths.Count>0?Quote(paths[0]):"");break;
                case 'F':text.Append(Names(paths));break;
                case 'n':text.Append(paths.Count>0?Quote(Path.GetFileName(paths[0].TrimEnd('\\'))):"");break;
                case 'N':text.Append(Names(paths.Select(p=>Path.GetFileName(p.TrimEnd('\\')))));break;
                case 'd':text.Append(Quote(folder??""));break;
                case '%':text.Append('%');break;
                default:text.Append('%').Append(code);break;
            }
        }
        return text.ToString();
    }
    /// Starts the program; the process is not owned past its start (as Open with). Returns an error or null.
    internal string Run(IReadOnlyList<string> paths,string folder)
    {
        try
        {
            string work=WorkingFolder.Length==0?folder:Expand(WorkingFolder,paths,folder).Trim('"');
            var start=new ProcessStartInfo(Environment.ExpandEnvironmentVariables(Executable))
            {
                Arguments=Expand(Arguments,paths,folder),UseShellExecute=true,
                WorkingDirectory=Directory.Exists(work)?work:folder??"",
            };
            Process.Start(start)?.Dispose();return null;
        }
        catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {return $"{Name}: could not start {Executable}: {ex.Message}";}
    }
}

internal static class CustomActions
{
    internal static string FilePath=>Path.Combine(Settings.Dir,"actions.json");
    static readonly JsonSerializerOptions json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,DefaultIgnoreCondition=JsonIgnoreCondition.Never};
    static List<CustomAction> actions;static DateTime loadedStamp;
    internal static string LoadError{get;private set;}
    /// Read again when the file changed (it may be edited by hand, as documented).
    internal static List<CustomAction> All
    {
        get
        {
            var stamp=File.Exists(FilePath)?File.GetLastWriteTimeUtc(FilePath):default;
            if(actions!=null&&stamp==loadedStamp)return actions;
            loadedStamp=stamp;LoadError=null;
            try{actions=stamp==default?new():JsonSerializer.Deserialize<List<CustomAction>>(File.ReadAllText(FilePath),json)??new();}
            catch(Exception ex)when(ex is JsonException or IOException or UnauthorizedAccessException){actions=new();LoadError="actions.json could not be read: "+ex.Message;}
            actions.RemoveAll(a=>string.IsNullOrWhiteSpace(a.Name)||string.IsNullOrWhiteSpace(a.Executable));
            return actions;
        }
    }
    internal static void Save(List<CustomAction> list)
    {
        Directory.CreateDirectory(Settings.Dir);var temp=FilePath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(list,json));File.Move(temp,FilePath,true);
        actions=list;loadedStamp=File.GetLastWriteTimeUtc(FilePath);
    }
    /// The Actions submenu for this selection, or null when no action applies.
    internal static MenuItem Menu(IReadOnlyList<Entry> selection,string folder,Action<string> report)
    {
        var applicable=All.Where(a=>a.Applies(selection)).ToList();
        if(applicable.Count==0)return null;
        var root=MainWindow.Item("Actions","","",()=>{});root.Tag="custom-actions";
        var paths=selection.Select(e=>e.Path).ToList();
        foreach(var group in applicable.GroupBy(a=>a.Submenu.Trim(),StringComparer.OrdinalIgnoreCase))
        {
            ItemsControl target=root;
            if(group.Key.Length>0){var sub=MainWindow.Item(group.Key,"","",()=>{});root.Items.Add(sub);target=sub;}
            foreach(var action in group){var a=action;target.Items.Add(MainWindow.Item(a.Name,a.Glyph,"",()=>{if(a.Run(paths,folder) is {} error)report(error);}));}
        }
        return root;
    }
}
