using System.Globalization;
using System.IO;
using System.Text;

namespace FileExplorer;

// UI-free .reg parser for the Registry Preview tool (TOOLS-05). Ported from PowerToys v0.103.3151
// (2d817b0a3a7e16cce7f66820932d91a86c863cc7, MIT): registrypreview/RegistryPreviewUILib/
// RegistryPreviewMainPage.Utilities.cs (ParseRegistryFile, AddTextToTree, StripFirstAndLast, StripEscapedCharacters,
// ScanAndRemoveComments, CheckKeyLineForBrackets, tool-tip helpers), ParseHelper.cs, RegistryKey.xaml.cs, RegistryValue.xaml.cs.
// TreeViewNodes become RegKey/RegValue, message boxes become Problems. Read-only: never touches the registry.
internal sealed class RegKey
{
    public string Name;
    public string FullPath;
    public bool Deleted;  // [-HKEY...] leaf (donor's deleted-folder icon)
    public bool Error;    // missing ']' or unknown root (donor's error icon)
    public List<RegKey> Keys=new();
    public List<RegValue> Values=new();
    public int Line;
}

internal sealed class RegValue
{
    public string Name;
    public string Type;   // REG_SZ, REG_DWORD, REG_QWORD, REG_BINARY, REG_EXPAND_SZ, REG_MULTI_SZ, REG_NONE, ERROR, "" (deleted)
    public string Data;   // donor display text; REG_MULTI_SZ lines are separated by '\r' like the donor's Value
    public bool Deleted;
    public bool Error;
    public int Line;
    public string DataOneLine=>Data.Replace('\r',' ');  // donor ValueOneLine
}

internal sealed record RegParse(IReadOnlyList<RegKey> Roots,IReadOnlyList<(int Line,string Message)> Problems,string Header,bool Truncated);

internal static class RegistryFile
{
    const string Header4="regedit4",Header5="windows registry editor version 5.00";
    // Donor Resources.resw (en-US) strings.
    const string InvalidString="(Invalid string value)",InvalidBinary="(Invalid binary value)",ZeroLength="(zero-length binary value)",
        InvalidDword="(Invalid DWORD (32-bit) value)",InvalidQword="(Invalid QWORD (64-bit) value)",NoNodes="No valid Keys were found",
        InvalidFile="This appears to be an invalid registry file.";
    enum Kind{Key,Deleted,Error}

    internal static RegParse Parse(string text,CancellationToken ct,int maxLines=200000)
    {
        ct.ThrowIfCancellationRequested();
        var roots=new List<RegKey>();var problems=new List<(int,string)>();
        var map=new Dictionary<string,RegKey>(StringComparer.InvariantCultureIgnoreCase);
        text??="";
        if(text.Length>0&&text[0]=='\uFEFF')text=text[1..];
        // Donor splits on '\r' only (its editor hands it CR-LF); bare LF files are accepted here too.
        string[] lines=text.Replace("\r\n","\r").Replace('\n','\r').Split('\r');
        bool truncated=false;
        if(maxLines>0&&lines.Length>maxLines){Array.Resize(ref lines,maxLines);truncated=true;problems.Add((maxLines,$"Only the first {maxLines:N0} lines were read."));}
        string header=lines[0];
        RegParse Done()=>new(roots,problems,header,truncated);
        if(lines.Length<=1)return Done();  // donor: nothing to parse, no message
        string line=lines[0].ToLowerInvariant();
        if(line!=Header4&&line!=Header5){problems.Add((1,InvalidFile));return Done();}

        RegKey current=null;string lastKeyPath=null;
        int index=1;line=lines[index];
        while(index<lines.Length)
        {
            if((index&1023)==0)ct.ThrowIfCancellationRequested();
            int lineNo=index+1;
            if(line.StartsWith("@=-",StringComparison.InvariantCulture))line=line.Replace("@=-","\"(Default)\"=\"\"");  // regedit clears @ instead
            else if(line.StartsWith("@=",StringComparison.InvariantCulture))line=line.Replace("@=","\"(Default)\"=");

            if(line.StartsWith("[-",StringComparison.InvariantCulture))
            {
                line=line.Remove(1,1);
                var kind=CheckKeyLineForBrackets(ref line,Kind.Deleted);
                line=StripFirstAndLast(line);
                AddKey(line,kind,lineNo,map,roots);  // donor keeps attaching later values to the previous key
                if(kind==Kind.Error)problems.Add((lineNo,$"Key [{line.TrimEnd()}] is not valid."));
            }
            else if(line.StartsWith('['))
            {
                var kind=CheckKeyLineForBrackets(ref line,Kind.Key);
                line=StripFirstAndLast(line);
                current=AddKey(line,kind,lineNo,map,roots);lastKeyPath=line;
                if(kind==Kind.Error)problems.Add((lineNo,$"Key [{line.TrimEnd()}] is not valid."));
            }
            else if(line.StartsWith('"')&&line.EndsWith("=-",StringComparison.InvariantCulture))
            {
                line=StripFirstAndLast(line.Replace("=-",""));
                current?.Values.Add(new RegValue{Name=line,Type="",Data="",Deleted=true,Line=lineNo});
            }
            else if(line.StartsWith('"'))
            {
                int equal=line.IndexOf('=');
                if(equal<0){problems.Add((lineNo,"Parsing stopped: value line has no '='."));break;}  // donor breaks out of the loop
                string name=StripEscapedCharacters(StripFirstAndLast(line[..equal].Trim()));
                string value=line[(equal+1)..].Trim();
                string type="REG_SZ";
                if(value.StartsWith('"')){int last=value.LastIndexOf('"');if(last>=0)value=value[..(last+1)];}  // drops trailing comments
                if(value.StartsWith('"')&&value.EndsWith('"'))value=StripFirstAndLast(value);
                else type="ERROR";  // no quotes on the right side, unless a typed prefix follows
                // Donor tests the prefix case-insensitively but Replace is case-sensitive ("DWORD:1" ends up invalid).
                if(value.StartsWith("dword:",StringComparison.InvariantCultureIgnoreCase)){type="REG_DWORD";value=value.Replace("dword:","");}
                else if(value.StartsWith("hex(b):",StringComparison.InvariantCultureIgnoreCase)){type="REG_QWORD";value=value.Replace("hex(b):","");}
                else if(value.StartsWith("hex:",StringComparison.InvariantCultureIgnoreCase)){type="REG_BINARY";value=value.Replace("hex:","");}
                else if(value.StartsWith("hex(2):",StringComparison.InvariantCultureIgnoreCase)){type="REG_EXPAND_SZ";value=value.Replace("hex(2):","");}
                else if(value.StartsWith("hex(7):",StringComparison.InvariantCultureIgnoreCase)){type="REG_MULTI_SZ";value=value.Replace("hex(7):","");}
                else if(value.StartsWith("hex(0):",StringComparison.InvariantCultureIgnoreCase)){type="REG_NONE";value=value.Replace("hex(0):","");}
                if(type!="REG_SZ"&&type!="ERROR"){if(value==@"\")value=@",\";value=ScanAndRemoveComments(value);}
                if((type=="REG_QWORD"||type=="REG_BINARY"||type=="REG_EXPAND_SZ"||type=="REG_MULTI_SZ")&&value==@"\")value=@",\";

                bool cutOff=false;
                while(value.EndsWith(@",\",StringComparison.InvariantCulture))
                {
                    value=value.TrimEnd('\\');
                    if(value==",")value="";
                    index++;
                    if(index>=lines.Length){cutOff=true;break;}
                    value+=ScanAndRemoveComments(lines[index]).TrimStart();
                }
                if(cutOff){problems.Add((lineNo,$"\"{name}\": the file ends inside a continued value."));return Done();}  // donor returns, value not stored

                switch(type)
                {
                    case "ERROR":break;
                    case "REG_SZ":
                        if(value=="\""){type="ERROR";value=InvalidString;}  // most likely a missing end quote
                        else
                        {
                            for(int i=1;i<value.Length;i++)
                            {
                                if(value[i-1]=='\\')
                                {
                                    if(value[i]!='"'&&value[i]!='\\'){type="ERROR";value=InvalidString;break;}  // only \" and \\ allowed
                                    i++;
                                }
                                if(i<value.Length&&value[i-1]!='\\'&&value[i]=='"'){type="ERROR";value=InvalidString;break;}  // unescaped quote
                            }
                            if(type!="ERROR")value=StripEscapedCharacters(value);
                        }
                        break;
                    case "REG_BINARY":case "REG_NONE":
                        if(value.Length<=0)value=ZeroLength;
                        else if(TryHex(value,out var bin))value=string.Join(' ',bin.Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));
                        else{type="ERROR";value=InvalidBinary;}
                        break;
                    case "REG_DWORD":
                        if(value.Length>0&&uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint dword))value=$"0x{dword:x8} ({dword})";
                        else{type="ERROR";value=InvalidDword;}
                        break;
                    case "REG_QWORD":
                        if(value.Length>0&&TryHex(value,out var q)&&q.Length>=8){ulong qword=BitConverter.ToUInt64(q);value=$"0x{qword:x8} ({qword})";}  // donor: ToUInt64 throws under 8 bytes
                        else{type="ERROR";value=InvalidQword;}
                        break;
                    case "REG_EXPAND_SZ":case "REG_MULTI_SZ":
                        if(TryHex(value,out var s))value=Encoding.Unicode.GetString(s).Replace('\0','\r').TrimEnd('\r');  // PowerToys #36629
                        else{type="ERROR";value=InvalidString;}
                        break;
                }
                bool error=type=="ERROR";
                if(error)problems.Add((lineNo,$"\"{name}\": {value}"));
                current?.Values.Add(new RegValue{Name=name,Type=type,Data=value,Error=error,Line=lineNo});
            }
            // anything else (comments, blank lines, junk) is skipped like the donor

            index++;
            if(index>=lines.Length){if(roots.Count==0)problems.Add((lines.Length,NoNodes));return Done();}
            line=lines[index];
        }
        if(roots.Count==0)problems.Add((index+1,InvalidFile));
        return Done();
    }

    // Donor bytes: comma-separated, every item exactly two hex digits.
    static bool TryHex(string value,out byte[] bytes)
    {
        var parts=value.Split(',');bytes=new byte[parts.Length];
        for(int i=0;i<parts.Length;i++)
            if(parts[i].Length!=2||!byte.TryParse(parts[i],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out bytes[i]))return false;
        return true;
    }

    // AddTextToTree: walk the path leaf-first, create missing nodes, hook the chain to the first existing ancestor or the roots.
    static RegKey AddKey(string keys,Kind kind,int line,Dictionary<string,RegKey> map,List<RegKey> roots)
    {
        string[] parts=keys.Split('\\');string fullPath=keys;RegKey made=null,previous=null;
        for(int i=parts.Length-1;i>=0;i--)
        {
            if(map.TryGetValue(fullPath,out var found)){if(made==null)return found;found.Keys.Add(previous);return made;}
            // only the leaf of a deleted key is marked, and never a hive root
            bool deleted=kind==Kind.Deleted&&i==parts.Length-1&&parts[i] is not ("HKEY_CLASSES_ROOT" or "HKEY_CURRENT_USER" or "HKEY_LOCAL_MACHINE" or "HKEY_USERS" or "HKEY_CURRENT_CONFIG");
            var key=new RegKey{Name=parts[i],FullPath=fullPath,Deleted=deleted,Error=kind==Kind.Error,Line=line};
            map.Add(fullPath,key);
            if(previous==null)made=key;else key.Keys.Add(previous);
            previous=key;
            int at=fullPath.LastIndexOf("\\"+parts[i],StringComparison.InvariantCulture);
            if(at>-1)fullPath=fullPath[..at];
            if(i==0)roots.Add(key);
        }
        return made;
    }

    // ParseHelper.CheckKeyLineForBrackets: last ']' ends the key (regedit behaviour); none, or an unknown hive, is an error.
    static Kind CheckKeyLineForBrackets(ref string line,Kind kind)
    {
        int last=line.LastIndexOf(']');
        if(last==-1){line+=" ";return Kind.Error;}
        if(last<line.Length-1)line=line[..(last+1)];
        return KnownRoot(line)?kind:Kind.Error;
    }

    static readonly string[] Roots={"HKEY_CLASSES_ROOT","HKEY_CURRENT_USER","HKEY_USERS","HKEY_LOCAL_MACHINE","HKEY_CURRENT_CONFIG","HKCR","HKCU","HKU","HKLM","HKCC"};
    static bool KnownRoot(string key)=>Roots.Any(r=>key.StartsWith("["+r+"]",StringComparison.InvariantCultureIgnoreCase)||key.StartsWith("["+r+"\\",StringComparison.InvariantCultureIgnoreCase));

    static string StripFirstAndLast(string line)=>line.Length>1?line[1..^1]:line;
    static string StripEscapedCharacters(string value)=>value.Replace("\\\\","\\").Replace("\\\"","\"");
    // Binary values only: a ';' starts a comment to the end of the line.
    static string ScanAndRemoveComments(string value){int at=value.IndexOf(';');if(at>-1)value=value[..at];return value.TrimEnd();}

    // Donor reads with a default StreamReader (BOM detection, else UTF-8) and refuses files over 10 MB.
    // Difference: BOM-less text that is not valid UTF-8 (typical REGEDIT4 export) is decoded with the ANSI code page instead of U+FFFD.
    internal static string ReadText(string path,long maxBytes)
    {
        byte[] b;
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        {
            if(stream.Length>maxBytes)throw new IOException($"{System.IO.Path.GetFileName(path)} is larger than {maxBytes/1048576.0:0.#} MB which is too large for this application.");
            using var buffer=new MemoryStream();stream.CopyTo(buffer);
            if(buffer.Length>maxBytes)throw new IOException($"{System.IO.Path.GetFileName(path)} is too large.");
            b=buffer.ToArray();
        }
        if(b.Length>=2&&b[0]==0xFF&&b[1]==0xFE)return Encoding.Unicode.GetString(b,2,b.Length-2);
        if(b.Length>=2&&b[0]==0xFE&&b[1]==0xFF)return Encoding.BigEndianUnicode.GetString(b,2,b.Length-2);
        if(b.Length>=3&&b[0]==0xEF&&b[1]==0xBB&&b[2]==0xBF)return Encoding.UTF8.GetString(b,3,b.Length-3);
        try{return new UTF8Encoding(false,true).GetString(b);}
        catch(DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            int cp=CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return Encoding.GetEncoding(cp>0?cp:1252).GetString(b);
        }
    }

#if DEBUG
    internal static void Checks(Action<bool,string> check)
    {
        var none=CancellationToken.None;
        string reg=string.Join("\r\n",
            "Windows Registry Editor Version 5.00","",
            "; comment line",
            @"[HKEY_CURRENT_USER\Software\Pathweft\Test]",
            "@=\"def\"",
            "\"Str\"=\"a \\\"q\\\" \\\\ b\"",
            "\"na\\\"me\"=\"x\" ; trailing",
            "\"Dw\"=dword:0000002a",
            "\"Qw\"=hex(b):01,00,00,00,00,00,00,00",
            @"""Bin""=hex:de,ad,\",
            "  be,ef ; tail comment",
            @"""Lead""=hex:\",
            "  01,02",
            "\"Zero\"=hex:",
            "\"Exp\"=hex(2):25,00,41,00,25,00,00,00",
            "\"Multi\"=hex(7):61,00,00,00,62,00,00,00,00,00",
            "\"Gone\"=-",
            "\"Bad\"=dword:zz",
            "\"BadSz\"=\"a\\nb\"",
            @"[-HKEY_CURRENT_USER\Software\Pathweft\Old]",
            @"[HKEY_CURRENT_USER\Software\Pathweft\Broken",
            @"[HKEY_BOGUS\X]","");
        var p=Parse(reg,none);
        check(p.Header=="Windows Registry Editor Version 5.00"&&p.Roots.Count==2&&p.Roots[0].Name=="HKEY_CURRENT_USER","Registry file with a version 5.00 header parses into hive roots");
        var pw=p.Roots[0].Keys.SingleOrDefault()?.Keys.SingleOrDefault();
        var test=pw?.Keys.FirstOrDefault(k=>k.Name=="Test");
        check(pw!=null&&pw.FullPath==@"HKEY_CURRENT_USER\Software\Pathweft"&&test!=null&&test.Line==4,"Nested key paths build one shared parent chain");
        RegValue V(string n)=>test?.Values.FirstOrDefault(v=>v.Name==n);
        check(V("(Default)")?.Data=="def"&&V("(Default)").Type=="REG_SZ","@ becomes the (Default) REG_SZ value");
        check(V("Str")?.Data=="a \"q\" \\ b"&&V("na\"me")?.Data=="x","Escaped quotes and backslashes are unescaped in names and data; text after the closing quote is ignored");
        check(V("Dw")?.Data=="0x0000002a (42)"&&V("Qw")?.Data=="0x00000001 (1)"&&V("Qw").Type=="REG_QWORD","dword and hex(b) values display as the donor's 0x... (decimal)");
        check(V("Bin")?.Data=="de ad be ef"&&V("Lead")?.Data=="01 02"&&V("Zero")?.Data==ZeroLength,"hex values join '\\' continuation lines, drop ';' comments and show zero-length binary");
        check(V("Exp")?.Data=="%A%"&&V("Exp").Type=="REG_EXPAND_SZ"&&V("Multi")?.Data=="a\rb"&&V("Multi").Type=="REG_MULTI_SZ","hex(2) and hex(7) decode as UTF-16 with '\\r' line separators");
        check(V("Gone") is {Deleted:true,Type:""},"\"x\"=- is a deleted value");
        check(V("Bad") is {Error:true,Type:"ERROR"}&&V("Bad").Data==InvalidDword&&V("BadSz")?.Data==InvalidString,"Invalid dword and invalid escape sequences become donor ERROR values");
        check(pw?.Keys.FirstOrDefault(k=>k.Name=="Old") is {Deleted:true}&&!p.Roots[0].Deleted,"[-HKEY...] marks only the deleted leaf");
        check(pw?.Keys.FirstOrDefault(k=>k.Name=="Broken") is {Error:true}&&p.Roots[1] is {Name:"HKEY_BOGUS",Error:true}&&p.Problems.Any(x=>x.Line==21),"A key line without ']' or with an unknown hive is an error key with a problem");
        var r4=Parse("REGEDIT4\r\n\r\n[HKEY_LOCAL_MACHINE\\A]\r\n\"v\"=\"1\"\r\n",none);
        check(r4.Roots.Count==1&&r4.Roots[0].Keys[0].Values[0].Data=="1"&&r4.Problems.Count==0,"REGEDIT4 header is accepted");
        var lf=Parse("Windows Registry Editor Version 5.00\n[HKCU\\A]\n\"v\"=dword:1\n",none);
        check(lf.Roots.Count==1&&lf.Roots[0].Keys[0].Values[0].Data=="0x00000001 (1)","LF-only files parse like CR-LF files");
        var bad=Parse("Some text\r\n[HKEY_CURRENT_USER\\A]\r\n",none);
        check(bad.Roots.Count==0&&bad.Problems.Count==1&&bad.Problems[0].Line==1,"A missing header yields no keys and one problem");
        var empty=Parse("Windows Registry Editor Version 5.00\r\n\r\n; nothing\r\n",none);
        check(empty.Roots.Count==0&&empty.Problems.Any(x=>x.Message==NoNodes),"A file with no keys reports that no valid keys were found");
        var cut=Parse("Windows Registry Editor Version 5.00\r\n[HKCU\\A]\r\n\"b\"=hex:01,\\",none);
        check(cut.Roots.Count==1&&cut.Roots[0].Keys[0].Values.Count==0&&cut.Problems.Count==1,"A continuation at end of file stops parsing without throwing");
        var big=Parse("Windows Registry Editor Version 5.00\r\n[HKCU\\A]\r\n\"a\"=\"1\"\r\n\"b\"=\"2\"\r\n",none,3);
        check(big.Truncated&&big.Roots[0].Keys[0].Values.Count==1,"maxLines truncates and reports it");
        bool cancelled=false;
        try{using var cts=new CancellationTokenSource();cts.Cancel();Parse(reg,cts.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled,"A cancelled token stops parsing with OperationCanceledException");
        bool safe=true;
        foreach(var junk in new[]{"","\r\n","Windows Registry Editor Version 5.00\r\n\"x","Windows Registry Editor Version 5.00\r\n[\r\n[-\r\n\"\"=\r\n@=-\r\n\"q\"=hex(b):\r\n\"r\"=hex(7):zz"})
            try{Parse(junk,none);}catch{safe=false;}
        check(safe,"Malformed input never throws");
    }
#endif
}
