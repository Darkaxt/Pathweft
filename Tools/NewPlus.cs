using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FileExplorer;

// New+ templates (TOOLS-07): C# reimplementation following PowerToys v0.103.3151 (2d817b0a3a7e16cce7f66820932d91a86c863cc7, MIT)
// NewPlus/NewShellExtensionContextMenu: helpers_variables.h, Helpers.cpp (GetDatedFileName), template_item.cpp, template_folder.cpp,
// helpers_filesystem.h, new_utilities.h (copy_template, update_last_write_time), settings.cpp/settings.h/constants.h (defaults).
// Differences from the donor are noted inline. Only copies files; never opens or launches template content.
internal sealed record NewTemplate(string Path,string DisplayName,bool IsFolder);

internal static class NewPlus
{
    const string ParentFolderVariable="$PARENT_FOLDER_NAME";

    // Donor default is <PowerToys settings>\NewPlus\Templates; Pathweft keeps its own folder (configurable by the caller).
    internal static string DefaultFolder=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pathweft","Templates");

    // template_folder::rescan_template_folder: folders first, then files, each ordinal-sorted by full path.
    // Hidden/system files are skipped; folders are not (donor only tests files). A missing folder gives an empty list instead of throwing.
    // DisplayName uses the donor defaults: extension hidden, starting digits hidden, variables shown unresolved.
    internal static IReadOnlyList<NewTemplate> Discover(string templateFolder)
    {
        var dirs=new List<string>();var files=new List<string>();
        try
        {
            foreach(var entry in Directory.EnumerateFileSystemEntries(templateFolder))
                if(Directory.Exists(entry))dirs.Add(entry);
                else if(!Excluded(entry))files.Add(entry);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){return Array.Empty<NewTemplate>();}
        dirs.Sort(StringComparer.Ordinal);files.Sort(StringComparer.Ordinal);
        return dirs.Select(d=>Make(d,true)).Concat(files.Select(f=>Make(f,false))).ToList();
        static NewTemplate Make(string p,bool folder)
        {
            var t=new NewTemplate(p,"",folder);
            return t with{DisplayName=MenuTitle(t,false,false,false,DateTime.Now)};
        }
    }

    // template_item::get_menu_title. Variables resolve with $PARENT_FOLDER_NAME left as is, like the donor menu.
    internal static string MenuTitle(NewTemplate t,bool showExtension,bool showStartingDigits,bool resolveVariables,DateTime now)
    {
        string name=System.IO.Path.GetFileName(t.Path);
        string title=showStartingDigits?name:RemoveStartingDigits(name,t.IsFolder);
        if(resolveVariables)title=ResolveVariables(title,ParentFolderVariable,now);
        string ext=StdExtension(name);
        if(showExtension||ext.Length==0||t.IsFolder)return title;
        return title.Length>=ext.Length?title[..^ext.Length]:title;
    }

    // helpers::variables::resolve_variables_in_filename: date/time tokens, then %ENV%, then $PARENT_FOLDER_NAME, then invalid
    // filename characters become spaces. parentFolder may be a path or a bare name; its last component is used.
    internal static string ResolveVariables(string name,string parentFolder,DateTime now)
    {
        string result=ResolveDateTime(name??"",now);
        result=ResolveEnvironment(result);
        string parent=string.IsNullOrEmpty(parentFolder)?"":System.IO.Path.GetFileName(parentFolder.TrimEnd('\\','/'));
        if(parent.Length>0)result=result.Replace(ParentFolderVariable,parent,StringComparison.Ordinal);  // case-sensitive like the donor
        return MakeValidFilename(result);
    }

    // new_utilities.h copy_template name. The donor has no hide-extension option for the created item, only HideStartingDigits (default true).
    // Difference: the donor runs resolution over every component of the destination path; only the new leaf is resolved here.
    internal static string TargetName(NewTemplate t,string parentFolder,DateTime now,bool resolveVariables,bool hideStartingDigits=true)
    {
        string name=System.IO.Path.GetFileName(t.Path);
        if(hideStartingDigits)name=RemoveStartingDigits(name,t.IsFolder);
        return resolveVariables?ResolveVariables(name,parentFolder,now):name;
    }

    // copy_template: name, make unique with the donor's "name (1).ext" counter starting at 1, copy, resolve nested names
    // (folders only, hidden/system skipped), then touch everything to now. Difference: plain file copies instead of
    // SHFileOperation (no shell undo/progress); reparse-point folders inside a template are not followed; a failed or
    // cancelled copy removes the partial new item, which did not exist before.
    internal static string Create(NewTemplate t,string destinationFolder,DateTime now,bool resolveVariables,CancellationToken ct,bool hideStartingDigits=true)
    {
        ct.ThrowIfCancellationRequested();
        string target=MakeUniquePath(System.IO.Path.Combine(destinationFolder,TargetName(t,destinationFolder,now,resolveVariables,hideStartingDigits)));
        try
        {
            if(t.IsFolder)CopyFolder(t.Path,target,ct);
            else System.IO.File.Copy(t.Path,target,false);
            if(resolveVariables&&t.IsFolder)RenameNested(target,now,ct);
            Touch(target,t.IsFolder);
        }
        catch
        {
            try{if(t.IsFolder){if(Directory.Exists(target))Directory.Delete(target,true);}else if(System.IO.File.Exists(target))System.IO.File.Delete(target);}catch{}
            throw;
        }
        return target;
    }

    static void CopyFolder(string from,string to,CancellationToken ct)
    {
        Directory.CreateDirectory(to);
        foreach(var file in Directory.EnumerateFiles(from)){ct.ThrowIfCancellationRequested();System.IO.File.Copy(file,System.IO.Path.Combine(to,System.IO.Path.GetFileName(file)),false);}
        foreach(var dir in Directory.EnumerateDirectories(from))
            if((System.IO.File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0)CopyFolder(dir,System.IO.Path.Combine(to,System.IO.Path.GetFileName(dir)),ct);
    }

    // resolve_variables_in_filename_and_rename_files: depth first, so a child sees its parent's still-unresolved name.
    static void RenameNested(string folder,DateTime now,CancellationToken ct)
    {
        foreach(var dir in Directory.GetDirectories(folder))RenameNested(dir,now,ct);
        string parent=System.IO.Path.GetFileName(folder);
        foreach(var entry in Directory.GetFileSystemEntries(folder))
        {
            ct.ThrowIfCancellationRequested();
            if(Excluded(entry))continue;
            string leaf=System.IO.Path.GetFileName(entry),resolved=ResolveVariables(leaf,parent,now);
            if(string.Equals(leaf,resolved,StringComparison.OrdinalIgnoreCase))continue;  // StrCmpIW
            string renamed=MakeUniquePath(System.IO.Path.Combine(folder,resolved));
            if(Directory.Exists(entry))Directory.Move(entry,renamed);else System.IO.File.Move(entry,renamed);
        }
    }

    static void Touch(string path,bool folder)
    {
        var utc=DateTime.UtcNow;
        if(!folder){System.IO.File.SetLastWriteTimeUtc(path,utc);return;}
        Directory.SetLastWriteTimeUtc(path,utc);
        foreach(var entry in Directory.EnumerateFileSystemEntries(path,"*",SearchOption.AllDirectories))
            if(Directory.Exists(entry))Directory.SetLastWriteTimeUtc(entry,utc);else System.IO.File.SetLastWriteTimeUtc(entry,utc);
    }

    // helpers::variables::exclude_item: hidden or system; unreadable attributes do not exclude.
    static bool Excluded(string path)
    {
        try{return (System.IO.File.GetAttributes(path)&(FileAttributes.Hidden|FileAttributes.System))!=0;}
        catch{return false;}
    }

    // std::filesystem::path::extension: a name starting with '.' and no other dot (".gitignore") has none.
    static string StdExtension(string name)
    {
        int dot=name.LastIndexOf('.');
        return dot<=0||name=="."||name==".."?"":name[dot..];
    }
    static string StdStem(string name)=>name[..(name.Length-StdExtension(name).Length)];

    // helpers::filesystem::make_unique_path_name
    static string MakeUniquePath(string path)
    {
        string dir=System.IO.Path.GetDirectoryName(path)??"",name=System.IO.Path.GetFileName(path),stem=StdStem(name),ext=StdExtension(name),candidate=path;
        for(int counter=1;System.IO.File.Exists(candidate)||Directory.Exists(candidate);counter++)candidate=System.IO.Path.Combine(dir,$"{stem} ({counter}){ext}");
        return candidate;
    }

    // template_item::remove_starting_digits_from_filename: "01. Name", "01 .Name", "01.Name", "01 Name", "01Name" -> "Name";
    // a file named only digits ("001231.txt") keeps them, a folder only when it has no "extension" ("001231" yes, "0123.Name" no).
    internal static string RemoveStartingDigits(string filename,bool isFolder)
    {
        string stem=StdStem(filename);
        if(stem.Length>0&&stem.All(c=>c>='0'&&c<='9')&&(!isFolder||StdExtension(filename).Length==0))return filename;
        int end=0;
        while(end<filename.Length&&filename[end]>='0'&&filename[end]<='9')end++;
        if(end==0)return filename;
        if(end<filename.Length)
        {
            if(filename[end]=='.'){end++;if(end<filename.Length&&filename[end]==' ')end++;}
            else if(filename[end]==' '){end++;if(end<filename.Length&&filename[end]=='.')end++;}
        }
        return filename[end..];
    }

    // helpers::filesystem::make_valid_filename
    static string MakeValidFilename(string s)
    {
        var chars=s.ToCharArray();
        for(int i=0;i<chars.Length;i++)if(chars[i] is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')chars[i]=' ';
        return new string(chars);
    }

    // helpers_variables.h resolve_environment_variables: unknown or empty variables stay literal; inserted text is not rescanned.
    static readonly Regex EnvVar=new("%([^%]+)%",RegexOptions.CultureInvariant);
    static string ResolveEnvironment(string s)
    {
        int start=0;
        for(Match m;start<=s.Length&&(m=EnvVar.Match(s,start)).Success;)
        {
            string value=null;
            try{value=Environment.GetEnvironmentVariable(m.Groups[1].Value);}catch(ArgumentException){}
            if(!string.IsNullOrEmpty(value)){s=s.Remove(m.Index,m.Length).Insert(m.Index,value);start=m.Index+value.Length;}
            else start=m.Index+m.Length;
        }
        return s;
    }

    // Helpers.cpp GetDatedFileName: tokens in this exact order, case-sensitive, each skipped when preceded by an odd run of '$'.
    // "$$" is never collapsed to "$" (the donor leaves it in the name). Note the donor's $HH/$H are 12-hour and $hh/$h 24-hour.
    // Month/day names use the current culture (donor: user default locale) with the first letter capitalised.
    static readonly (string Token,Func<DateTime,CultureInfo,string> Value)[] DateTokens=
    {
        ("YYYY",(d,c)=>d.Year.ToString("0000",CultureInfo.InvariantCulture)),
        ("YY",(d,c)=>(d.Year%100).ToString("00",CultureInfo.InvariantCulture)),
        ("Y",(d,c)=>(d.Year%10).ToString(CultureInfo.InvariantCulture)),
        ("MMMM",(d,c)=>Capital(d.ToString("MMMM",c),c)),
        ("MMM",(d,c)=>Capital(d.ToString("MMM",c),c)),
        ("MM",(d,c)=>d.Month.ToString("00",CultureInfo.InvariantCulture)),
        ("M",(d,c)=>d.Month.ToString(CultureInfo.InvariantCulture)),
        ("DDDD",(d,c)=>Capital(d.ToString("dddd",c),c)),
        ("DDD",(d,c)=>Capital(d.ToString("ddd",c),c)),
        ("DD",(d,c)=>d.Day.ToString("00",CultureInfo.InvariantCulture)),
        ("D",(d,c)=>d.Day.ToString(CultureInfo.InvariantCulture)),
        ("HH",(d,c)=>Hour12(d).ToString("00",CultureInfo.InvariantCulture)),
        ("H",(d,c)=>Hour12(d).ToString(CultureInfo.InvariantCulture)),
        ("TT",(d,c)=>d.Hour<12?"AM":"PM"),
        ("tt",(d,c)=>d.Hour<12?"am":"pm"),
        ("hh",(d,c)=>d.Hour.ToString("00",CultureInfo.InvariantCulture)),
        ("h",(d,c)=>d.Hour.ToString(CultureInfo.InvariantCulture)),
        ("mm",(d,c)=>d.Minute.ToString("00",CultureInfo.InvariantCulture)),
        ("m",(d,c)=>d.Minute.ToString(CultureInfo.InvariantCulture)),
        ("ss",(d,c)=>d.Second.ToString("00",CultureInfo.InvariantCulture)),
        ("s",(d,c)=>d.Second.ToString(CultureInfo.InvariantCulture)),
        ("fff",(d,c)=>d.Millisecond.ToString("000",CultureInfo.InvariantCulture)),
        ("ff",(d,c)=>(d.Millisecond/10).ToString("00",CultureInfo.InvariantCulture)),
        ("f",(d,c)=>(d.Millisecond/100).ToString(CultureInfo.InvariantCulture)),
    };
    static readonly Regex[] DateRegexes=DateTokens.Select(t=>new Regex(@"(([^\$]|^)(\$\$)*)\$"+t.Token,RegexOptions.CultureInvariant)).ToArray();
    static int Hour12(DateTime d)=>d.Hour%12==0?12:d.Hour%12;
    static string Capital(string s,CultureInfo c)=>s.Length==0?s:char.ToUpper(s[0],c)+s[1..];

    static string ResolveDateTime(string s,DateTime now)
    {
        if(s.Length==0)return s;  // donor returns E_INVALIDARG and an empty buffer
        var culture=CultureInfo.CurrentCulture;
        for(int i=0;i<DateTokens.Length;i++)
        {
            string value=DateTokens[i].Value(now,culture).Replace("$","$$");
            s=DateRegexes[i].Replace(s,"${1}"+value);
        }
        return s;
    }

#if DEBUG
    internal static void Checks(string scratch,Action<bool,string> check)
    {
        string root=System.IO.Path.Combine(scratch,"newplus-"+Guid.NewGuid().ToString("N")),templates=System.IO.Path.Combine(root,"Templates"),dest=System.IO.Path.Combine(root,"Work");
        try
        {
            Directory.CreateDirectory(templates);Directory.CreateDirectory(dest);
            void Write(string relative)=>System.IO.File.WriteAllText(System.IO.Path.Combine(templates,relative),"x");
            Write("01. First entry.txt");Write("02 Second.txt");Write("001231.txt");Write("b.txt");Write(".gitignore");Write("hidden.txt");
            System.IO.File.SetAttributes(System.IO.Path.Combine(templates,"hidden.txt"),FileAttributes.Hidden);
            Directory.CreateDirectory(System.IO.Path.Combine(templates,"0123.Name"));
            string proj=System.IO.Path.Combine(templates,"03. Proj $YYYY");
            Directory.CreateDirectory(System.IO.Path.Combine(proj,"sub $YY"));
            Write(@"03. Proj $YYYY\$PARENT_FOLDER_NAME.md");Write(@"03. Proj $YYYY\sub $YY\$PARENT_FOLDER_NAME.txt");Write(@"03. Proj $YYYY\h$YYYY.txt");
            System.IO.File.SetAttributes(System.IO.Path.Combine(proj,"h$YYYY.txt"),FileAttributes.Hidden);

            var list=Discover(templates);
            check(string.Join("|",list.Select(t=>t.DisplayName))=="Name|Proj $YYYY|.gitignore|001231|First entry|Second|b","New+ lists folders then files in ordinal order, skips hidden files and strips ordering digits and extensions from display names");
            check(list[0].IsFolder&&list[1].IsFolder&&!list[2].IsFolder&&Discover(System.IO.Path.Combine(root,"missing")).Count==0,"New+ discovery marks folders and tolerates a missing template folder");
            check(RemoveStartingDigits("01 .Name",false)=="Name"&&RemoveStartingDigits("05.Fifth entry.txt",false)=="Fifth entry.txt"&&RemoveStartingDigits("001231",true)=="001231"&&RemoveStartingDigits("13. 0123456789012345.txt",false)=="0123456789012345.txt","New+ starting-digit removal matches the donor's documented cases");

            var now=new DateTime(2026,3,5,14,7,9,45);
            check(ResolveVariables("$YYYY-$MM-$DD $hh$mm$ss.$fff",null,now)=="2026-03-05 140709.045"&&ResolveVariables("$HH $H $tt $TT $Y $M $D $f $ff",null,now)=="02 2 pm PM 6 3 5 0 04","New+ date/time tokens match the donor, including 12-hour $HH and 24-hour $hh");
            check(ResolveVariables("$$YYYY a$$$YYYY",null,now)=="$$YYYY a$$2026","New+ '$$' escapes a token and is left as '$$' like the donor");
            check(ResolveVariables("$MMMM $DDDD",null,now)==Capital(now.ToString("MMMM",CultureInfo.CurrentCulture),CultureInfo.CurrentCulture)+" "+Capital(now.ToString("dddd",CultureInfo.CurrentCulture),CultureInfo.CurrentCulture),"New+ month and day names use the current culture, capitalised");
            Environment.SetEnvironmentVariable("PATHWEFT_NEWPLUS_CHECK","v:1");
            check(ResolveVariables("%pathweft_newplus_check%-%PATHWEFT_NO_SUCH_VAR%",null,now)=="v 1-%PATHWEFT_NO_SUCH_VAR%","New+ expands %ENV% case-insensitively, keeps unknown variables and replaces invalid filename characters with spaces");
            Environment.SetEnvironmentVariable("PATHWEFT_NEWPLUS_CHECK",null);
            check(ResolveVariables("$PARENT_FOLDER_NAME x",@"C:\Work\Proj\",now)=="Proj x"&&ResolveVariables("$parent_folder_name",@"C:\Work",now)=="$parent_folder_name","New+ $PARENT_FOLDER_NAME is case-sensitive and uses the parent's last component");

            var first=list.First(t=>t.DisplayName=="First entry");
            check(TargetName(first,dest,now,true)=="First entry.txt"&&TargetName(first,dest,now,true,false)=="01. First entry.txt","New+ target name drops starting digits but keeps the extension");
            string a=Create(first,dest,now,false,CancellationToken.None),b=Create(first,dest,now,false,CancellationToken.None);
            check(System.IO.Path.GetFileName(a)=="First entry.txt"&&System.IO.Path.GetFileName(b)=="First entry (1).txt"&&System.IO.File.ReadAllText(b)=="x","New+ copies a file template and numbers conflicts like the donor");

            var folder=list.First(t=>t.DisplayName=="Proj $YYYY");
            string made=Create(folder,dest,now,true,CancellationToken.None);
            check(System.IO.Path.GetFileName(made)=="Proj 2026"&&System.IO.File.Exists(System.IO.Path.Combine(made,"Proj 2026.md"))&&System.IO.File.Exists(System.IO.Path.Combine(made,"sub 26","sub $YY.txt"))&&System.IO.File.Exists(System.IO.Path.Combine(made,"h$YYYY.txt")),"New+ folder templates resolve nested names depth first and skip hidden items");
            string plain=Create(folder,dest,now,false,CancellationToken.None);
            check(System.IO.Path.GetFileName(plain)=="Proj $YYYY"&&System.IO.File.Exists(System.IO.Path.Combine(plain,"$PARENT_FOLDER_NAME.md")),"New+ without variable resolution copies names verbatim");

            bool cancelled=false;using var cts=new CancellationTokenSource();cts.Cancel();
            try{Create(folder,dest,now,true,cts.Token);}catch(OperationCanceledException){cancelled=true;}
            check(cancelled&&Directory.GetFileSystemEntries(dest).Length==4,"New+ cancellation throws and leaves no partial item");
        }
        finally{try{Directory.Delete(root,true);}catch{}}
    }
#endif
}
