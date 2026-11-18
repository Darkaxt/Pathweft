using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FileExplorer;

// PowerRename's renaming engine, ported from PowerToys v0.103.3151 (2d817b0a3a7e16cce7f66820932d91a86c863cc7, MIT),
// src/modules/powerrename/lib: PowerRenameRegEx.cpp (Replace, term handling), Enumerating.cpp/.h, Randomizer.cpp/.h,
// Helpers.cpp (GetTrimmedFileName, GetTransformedFileName, GetDatedFileName, isFileTimeUsed), Renaming.cpp (DoRename),
// PowerRenameItem.cpp (GetTime flag choice) and PowerRenameInterfaces.h (flags).
// Donor default UseBoostLib=false, so regexes follow std::wregex ECMAScript and std::regex_replace format rules.
// Not ported: EXIF/XMP metadata tokens ($CAMERA_MAKE etc. stay literal), boost engine, MAX_PATH truncation.

// Same names and values as PowerRenameFlags.
[Flags]
internal enum RenameFlags
{
    None=0,CaseSensitive=0x1,MatchAllOccurrences=0x2,UseRegularExpressions=0x4,EnumerateItems=0x8,
    ExcludeFiles=0x10,ExcludeFolders=0x20,ExcludeSubfolders=0x40,NameOnly=0x80,ExtensionOnly=0x100,
    Uppercase=0x200,Lowercase=0x400,Titlecase=0x800,Capitalized=0x1000,RandomizeItems=0x2000,
    CreationTime=0x4000,ModificationTime=0x8000,AccessTime=0x10000,MetadataSourceEXIF=0x20000,MetadataSourceXMP=0x40000,
}

internal enum RenameTime{Creation,Modified,Accessed}

// The time source is chosen by the CreationTime/ModificationTime/AccessTime flags, as in the donor.
internal sealed record RenameRule(string Search,string Replace,RenameFlags Flags);

internal static class PowerRename
{
    const RenameFlags CaseFlags=RenameFlags.Uppercase|RenameFlags.Lowercase|RenameFlags.Titlecase|RenameFlags.Capitalized;
    const string Prefix=@"(([^\$]|^)(\$\$)*)\$"; // a $ token not escaped by $$
    static readonly TimeSpan Timeout=TimeSpan.FromSeconds(2);
    static readonly Regex Token=new(@"\$\{.*?\}"),
        EnumStart=new(@"start=(-?\d+)"),EnumIncrement=new(@"increment=(-?\d+)"),EnumPadding=new(@"padding=(\d+)"),
        RandAlnum=new(@"rstringalnum=(\d+)"),RandAlpha=new(@"rstringalpha=(-?\d+)"),RandDigit=new(@"rstringdigit=(\d+)"),
        ZeroGroup=new(Prefix+"[0]"),OtherGroups=new(Prefix+"([1-9])"),
        TimeUsed=new(Prefix+"[YMDhmsfHTt]");
    static readonly string[] TitleExceptions={"a","an","to","the","at","by","for","in","of","on","up","and","as","but","or","nor"};

    // Longer tokens before their prefixes, exactly the donor's order; the lookaheads keep metadata tokens intact.
    static readonly (Regex re,Func<DateTime,string> value)[] DateTokens=
    {
        (T("YYYY"),t=>t.Year.ToString("D4")),(T("YY"),t=>(t.Year%100).ToString("D2")),(T("Y"),t=>(t.Year%10).ToString()),
        (T("MMMM"),t=>Cap(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(t.Month))),
        (T("MMM"),t=>Cap(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(t.Month))),
        (T("MM"),t=>t.Month.ToString("D2")),(T("M"),t=>t.Month.ToString()),
        (T("DDDD"),t=>Cap(CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(t.DayOfWeek))),
        (T("DDD"),t=>Cap(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(t.DayOfWeek))),
        (T("DD"),t=>t.Day.ToString("D2")),(T("D(?!(ATE_TAKEN_|ESCRIPTION|OCUMENT_ID))"),t=>t.Day.ToString()),
        (T("HH"),t=>Hour12(t).ToString("D2")),(T("H(?!(EIGHT))"),t=>Hour12(t).ToString()),
        (T("TT"),t=>t.Hour<12?"AM":"PM"),(T("tt"),t=>t.Hour<12?"am":"pm"),
        (T("hh"),t=>t.Hour.ToString("D2")),(T("h"),t=>t.Hour.ToString()),
        (T("mm"),t=>t.Minute.ToString("D2")),(T("m"),t=>t.Minute.ToString()),
        (T("ss"),t=>t.Second.ToString("D2")),(T("s"),t=>t.Second.ToString()),
        (T("fff"),t=>t.Millisecond.ToString("D3")),(T("ff"),t=>(t.Millisecond/10).ToString("D2")),(T("f"),t=>(t.Millisecond/100).ToString()),
    };
    static Regex T(string token)=>new(Prefix+token);
    static int Hour12(DateTime t)=>t.Hour%12==0?12:t.Hour%12;
    static string Cap(string s)=>s.Length==0?s:char.ToUpperInvariant(s[0])+s[1..];

    // Which file time the rule wants (PowerRenameItem::GetTime): modified, then accessed, else creation.
    internal static RenameTime TimeOf(RenameFlags flags)=>
        (flags&RenameFlags.ModificationTime)!=0?RenameTime.Modified:(flags&RenameFlags.AccessTime)!=0?RenameTime.Accessed:RenameTime.Creation;

    // True when the replace term has date tokens, so the caller only needs to read file times then.
    internal static bool UsesFileTime(RenameRule rule)=>TimeUsed.IsMatch(Strip(Sanitize(rule.Replace??""),rule.Flags,0).term);

    // Characters Windows refuses in names (DoRename marks such items ItemNameInvalidChar instead of renaming).
    internal static bool HasInvalidChars(string name)=>name.IndexOfAny(new[]{'<','>',':','"','\\','/','|','?','*'})>=0;

    // DoRename for one item. index is the enumeration counter: the donor advances it only for items whose search matched
    // (see Matches), so the caller keeps that counter.
    internal static string NewName(RenameRule rule,string name,bool isFolder,bool isSubfolderItem,int index,DateTime time)
    {
        var flags=rule.Flags;
        if(isFolder&&(flags&(RenameFlags.ExcludeFolders|RenameFlags.ExtensionOnly))!=0||!isFolder&&(flags&RenameFlags.ExcludeFiles)!=0||
           isSubfolderItem&&(flags&RenameFlags.ExcludeSubfolders)!=0)return name;
        string source=isFolder?name:(flags&RenameFlags.NameOnly)!=0?Stem(name):(flags&RenameFlags.ExtensionOnly)!=0?Ext(name).TrimStart1('.'):name;
        string result;
        try{result=Replace(rule,source,index,time,out _);}
        catch(Exception ex)when(ex is ArgumentException or RegexMatchTimeoutException){return name;} // donor: E_FAIL leaves the item alone
        if(result==null&&(flags&CaseFlags)!=0)result=source;
        if(result==null)return name;
        if(!isFolder)
        {
            if((flags&RenameFlags.NameOnly)!=0)result+=Ext(name);
            else if((flags&RenameFlags.ExtensionOnly)!=0)result=Ext(name).Length>0?Stem(name)+"."+result:name;
        }
        result=Trim(result);
        if((flags&CaseFlags)!=0)result=Transform(result,flags,isFolder);
        return IsSameName(name,result)?name:result;
    }

    // Whether the search matched this item, i.e. whether the donor would advance the enumeration counter after it.
    internal static bool Matches(RenameRule rule,string name,bool isFolder)
    {
        var flags=rule.Flags;
        string source=isFolder?name:(flags&RenameFlags.NameOnly)!=0?Stem(name):(flags&RenameFlags.ExtensionOnly)!=0?Ext(name).TrimStart1('.'):name;
        try{Replace(rule,source,0,default,out bool matched);return matched;}
        catch(Exception ex)when(ex is ArgumentException or RegexMatchTimeoutException){return false;}
    }

    internal static string Problem(RenameRule rule)
    {
        if((rule.Flags&RenameFlags.UseRegularExpressions)==0||string.IsNullOrEmpty(rule.Search))return null;
        try{Build(Sanitize(rule.Search),rule.Flags);return null;}
        catch(ArgumentException ex){return "Invalid regular expression: "+ex.Message;}
    }

    // CPowerRenameRegEx::Replace. Null when there is nothing to match (empty search or source).
    static string Replace(RenameRule rule,string source,int index,DateTime time,out bool matched)
    {
        matched=false;
        var flags=rule.Flags;
        string search=Sanitize(rule.Search??"");
        if(search.Length==0||string.IsNullOrEmpty(source))return null;
        string src=Sanitize(source);
        var (term,inserts)=Strip(Sanitize(rule.Replace??""),flags,index);
        if(term.Length>0&&TimeUsed.IsMatch(term))foreach(var (re,value) in DateTokens)term=StdReplace(re,term,"$01"+value(time),true);
        // The donor inserts counters at offsets measured before date expansion; kept, clamped where it would throw.
        int offset=0;
        foreach(var (pos,text) in inserts){term=term.Insert(Math.Min(pos+offset,term.Length),text);offset+=text.Length;}
        bool all=(flags&RenameFlags.MatchAllOccurrences)!=0,ignoreCase=(flags&RenameFlags.CaseSensitive)==0;
        if((flags&RenameFlags.UseRegularExpressions)!=0)
        {
            // $0 becomes literal and $1..$9 become $01..$09, so "$12" is group 1 then "2" as in the donor.
            term=StdReplace(ZeroGroup,term,"$1$$$0",true);
            term=StdReplace(OtherGroups,term,"$1$0$4",true);
            var re=Build(search,flags);
            matched=re.IsMatch(src);
            return StdReplace(re,src,term,all);
        }
        string res=src;
        int at=0;
        do
        {
            at=ignoreCase?Lower(src).IndexOf(Lower(search),at,StringComparison.Ordinal):src.IndexOf(search,at,StringComparison.Ordinal);
            if(at>=0){src=res=src[..at]+term+src[(at+search.Length)..];at+=term.Length;matched=true;}
        }while(all&&at>=0);
        return res;
    }

    static Regex Build(string pattern,RenameFlags flags)
    {
        // std::wregex ECMAScript has no lookbehind, named groups, inline options or atomic groups; .NET would accept them.
        bool inClass=false;
        for(int i=0;i<pattern.Length;i++)
        {
            char c=pattern[i];
            if(c=='\\'){i++;continue;}
            if(inClass){if(c==']')inClass=false;continue;}
            if(c=='[')inClass=true;
            else if(c=='('&&i+1<pattern.Length&&pattern[i+1]=='?'&&(i+2>=pattern.Length||pattern[i+2] is not (':' or '=' or '!')))
                throw new ArgumentException("the (? group at position "+i+" is not supported");
        }
        var options=RegexOptions.ECMAScript|((flags&RenameFlags.CaseSensitive)==0?RegexOptions.IgnoreCase:0);
        return new Regex(pattern,options,Timeout);
    }

    // std::regex_replace with format_default: $$ $& $` $' and $n/$nn. $0 stays literal and an absent group expands
    // to nothing (MSVC behaviour as pinned by the donor's VerifyHandleCapturingGroups table).
    static string StdReplace(Regex re,string input,string format,bool all)
    {
        var sb=new StringBuilder();
        int last=0;bool prevNonEmpty=false;
        foreach(Match m in re.Matches(input))
        {
            if(m.Length==0&&m.Index==last&&prevNonEmpty)continue; // MSVC: ".*" on "AAAAAA" gives one "Foo", not two
            sb.Append(input,last,m.Index-last);
            Expand(sb,m,input,format);
            last=m.Index+m.Length;prevNonEmpty=m.Length>0;
            if(!all)break;
        }
        return sb.Append(input,last,input.Length-last).ToString();
    }

    static void Expand(StringBuilder sb,Match m,string input,string f)
    {
        for(int i=0;i<f.Length;i++)
        {
            char c=f[i];
            if(c!='$'||i+1==f.Length){sb.Append(c);continue;}
            char n=f[i+1];
            if(n=='$'){sb.Append('$');i++;}
            else if(n=='&'){sb.Append(m.Value);i++;}
            else if(n=='`'){sb.Append(input,0,m.Index);i++;}
            else if(n=='\''){int end=m.Index+m.Length;sb.Append(input,end,input.Length-end);i++;}
            else if(char.IsAsciiDigit(n))
            {
                int j=i+2,g=n-'0';
                if(j<f.Length&&char.IsAsciiDigit(f[j])){g=g*10+f[j]-'0';j++;}
                if(g==0)sb.Append(f,i,j-i);else if(g<m.Groups.Count)sb.Append(m.Groups[g].Value);
                i=j-1;
            }
            else sb.Append(c);
        }
    }

    // _OnEnumerateOrRandomizeItemsChanged + the insertion half of Replace: ${...} tokens are cut out of the term and their
    // values recorded with the position to insert them. A randomizer wins over an enumerator at the same token.
    static (string term,List<(int pos,string text)> inserts) Strip(string raw,RenameFlags flags,int index)
    {
        var inserts=new List<(int,string)>();
        bool enumerate=(flags&RenameFlags.EnumerateItems)!=0,randomize=(flags&RenameFlags.RandomizeItems)!=0;
        if(!enumerate&&!randomize)return (raw,inserts);
        var sb=new StringBuilder();int last=0;
        foreach(Match m in Token.Matches(raw))
        {
            string value=randomize?Randomize(m.Value):null;
            if(value==null&&enumerate)value=Enumerate(m.Value,index);
            if(value==null)continue;
            sb.Append(raw,last,m.Index-last);
            inserts.Add((sb.Length,value));
            last=m.Index+m.Length;
        }
        return (sb.Append(raw,last,raw.Length-last).ToString(),inserts);
    }

    // Enumerator::printTo: start + index*increment printed as %0<padding>d (sign counts toward the width).
    static string Enumerate(string token,int index)
    {
        int start=Int(EnumStart,token,0),increment=Int(EnumIncrement,token,1);
        var p=EnumPadding.Match(token);
        uint padding=p.Success?(uint.TryParse(p.Groups[1].Value,out uint pv)?pv:0)%260:0;
        int value=unchecked(start+index*increment);
        string digits=Math.Abs((long)value).ToString(CultureInfo.InvariantCulture),sign=value<0?"-":"";
        if(Math.Max(padding,(uint)(sign.Length+digits.Length))+1>=260)padding=0; // donor falls back to no padding when it can't fit
        return sign+digits.PadLeft(Math.Max(0,(int)padding-sign.Length),'0');
    }
    static int Int(Regex re,string token,int fallback)
    {
        var m=re.Match(token);
        return !m.Success?fallback:int.TryParse(m.Groups[1].Value,out int v)?v:0; // from_chars overflow leaves the emplaced 0
    }

    // Randomizer::randomize; null when the token has no randomizer option.
    static string Randomize(string token)
    {
        if(token.Contains("ruuidv4",StringComparison.Ordinal))return Guid.NewGuid().ToString("D").ToUpperInvariant(); // StringFromCLSID without braces
        string chars="";int? length=null;
        void Option(Regex re,string set){var m=re.Match(token);if(!m.Success)return;chars+=set;length=int.TryParse(m.Groups[1].Value,out int v)?v:260;}
        const string Letters="abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ",Digits="0123456789";
        Option(RandAlnum,Letters+Digits);Option(RandAlpha,Letters);Option(RandDigit,Digits);
        if(chars.Length==0)return null;
        int count=Math.Min(length??10,260); // donor has no cap; the name is limited to MAX_PATH anyway
        var sb=new StringBuilder();
        for(int i=0;i<count;i++)sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }

    // GetTrimmedFileName: leading whitespace, trailing whitespace and dots.
    static string Trim(string s)
    {
        int first=0,last=s.Length-1;
        while(first<=last&&char.IsWhiteSpace(s[first]))first++;
        while(first<=last&&(char.IsWhiteSpace(s[last])||s[last]=='.'))last--;
        return s[first..(last+1)];
    }

    // GetTransformedFileName.
    static string Transform(string s,RenameFlags flags,bool isFolder)
    {
        bool nameOnly=(flags&RenameFlags.NameOnly)!=0,extOnly=(flags&RenameFlags.ExtensionOnly)!=0;
        if((flags&(RenameFlags.Uppercase|RenameFlags.Lowercase))!=0)
        {
            Func<string,string> map=(flags&RenameFlags.Uppercase)!=0?Upper:Lower;
            if(isFolder)return map(s);
            if(nameOnly)return map(Stem(s))+Ext(s);
            if(extOnly)return Ext(s).Length>0?Stem(s)+map(Ext(s)):map(s);
            return map(s);
        }
        if(extOnly)return s;
        string stem=isFolder?s:Stem(s),ext=isFolder?"":Ext(s);
        return Words(stem,(flags&RenameFlags.Titlecase)!=0)+ext;
    }

    // Titlecase (small words stay lower except first and last) and Capitalized, with the donor's contraction rule:
    // a letter after an apostrophe starts a word only when the apostrophe itself follows a space or punctuation.
    static string Words(string stem,bool title)
    {
        var s=stem.ToCharArray();
        int length=s.Length;
        while(length>0&&(Space(s[length-1])||Punct(s[length-1])))length--;
        bool firstWord=true;
        for(int i=0;i<length;i++)
        {
            bool starts=i==0||Space(s[i-1])||Punct(s[i-1])&&(s[i-1]!='\''||i==1||Punct(s[i-2])||Space(s[i-2]));
            if(!starts){s[i]=char.ToLowerInvariant(s[i]);continue;}
            if(Space(s[i])||Punct(s[i]))continue;
            if(!title){s[i]=char.ToUpperInvariant(s[i]);continue;}
            int w=0;
            while(i+w<length&&!Space(s[i+w])&&!Punct(s[i+w]))w++;
            if(firstWord||i+w==length||Array.IndexOf(TitleExceptions,Lower(new string(s,i,w)))<0){s[i]=char.ToUpperInvariant(s[i]);firstWord=false;}
            else s[i]=char.ToLowerInvariant(s[i]);
        }
        return new string(s);
    }
    static bool Space(char c)=>char.IsWhiteSpace(c);
    static bool Punct(char c)=>char.IsPunctuation(c)||char.IsSymbol(c); // iswpunct also covers symbols such as $+<=>^`|~

    // towupper/towlower per UTF-16 unit, so lengths never change.
    static string Upper(string s)=>string.Create(s.Length,s,(d,src)=>{for(int i=0;i<d.Length;i++)d[i]=char.ToUpperInvariant(src[i]);});
    static string Lower(string s)=>string.Create(s.Length,s,(d,src)=>{for(int i=0;i<d.Length;i++)d[i]=char.ToLowerInvariant(src[i]);});

    // std::filesystem::path stem()/extension() for a bare name: ".", ".." and leading-dot names have no extension.
    static int Dot(string n)
    {
        if(n=="."||n=="..")return -1;
        int i=n.LastIndexOf('.');
        return i>0?i:-1;
    }
    static string Stem(string n){int i=Dot(n);return i>=0?n[..i]:n;}
    static string Ext(string n){int i=Dot(n);return i>=0?n[i..]:"";}
    static string TrimStart1(this string s,char c)=>s.Length>0&&s[0]==c?s[1..]:s;

    // SanitizeAndNormalize: NBSP to space, then NFC; unnormalizable input is used as is.
    static string Sanitize(string s)
    {
        if(s.Length==0)return s;
        s=s.Replace('\u00A0',' ');
        try{return s.Normalize(NormalizationForm.FormC);}catch(ArgumentException){return s;}
    }

    // IsSameName: equal after NFC, otherwise ordinal (ß and ss differ).
    static bool IsSameName(string a,string b)
    {
        static string N(string s){try{return s.Normalize(NormalizationForm.FormC);}catch(ArgumentException){return s;}}
        return string.Equals(N(a),N(b),StringComparison.Ordinal);
    }

#if DEBUG
    internal static void Checks(Action<bool,string> check)
    {
        string Do(string search,string replace,RenameFlags flags,string name,bool folder=false,bool sub=false,int index=0,DateTime time=default)=>
            NewName(new RenameRule(search,replace,flags),name,folder,sub,index,time);
        const RenameFlags Re=RenameFlags.UseRegularExpressions,All=RenameFlags.MatchAllOccurrences;

        check(Do("A","x",RenameFlags.None,"aAa")=="xAa","PowerRename literal case-insensitive search replaces only the first occurrence by default");
        check(Do("A","x",All,"aAa")=="xxx","PowerRename literal case-insensitive search replaces all occurrences with MatchAllOccurrences");
        check(Do("A","x",All|RenameFlags.CaseSensitive,"aAa")=="axa","PowerRename case-sensitive literal search matches exact case only");

        var groups=new[]{
            ("$1_$002_$223_$001021_$00001","foo_$002_bar23_$001021_$00001"),("_$1$2_$123$040","_foobar_foo23$040"),
            ("$$$1","$foo"),("$$1","$1"),("$12","foo2"),("$10","foo0"),("$01","$01"),("$$$11","$foo1"),("$$$$113a","$$113a")};
        bool groupsOk=true;
        foreach(var (replace,expected) in groups)groupsOk&=Do("(foo)(bar)",replace,Re|All|RenameFlags.CaseSensitive,"foobar")==expected;
        check(groupsOk,"PowerRename regex replacement with capture groups matches the donor's VerifyHandleCapturingGroups table");
        check(Do(".*","Foo",Re|All,"AAAAAA")=="Foo","PowerRename regex .* with all occurrences yields a single replacement like the donor");
        check(Do("(o+)","[$&]",Re,"foo.txt")=="f[oo].txt","PowerRename regex $& inserts the whole match");
        check(Problem(new RenameRule("(?<=E12).*","Foo",Re))!=null&&Do("(?<=E12).*","Foo",Re,"AAAAAA")=="AAAAAA",
            "PowerRename rejects lookbehind like std::wregex and leaves the name unchanged");
        check(Problem(new RenameRule("(","x",Re))!=null&&Problem(new RenameRule("(a)\\d+","x",Re))==null&&Problem(new RenameRule("(","x",RenameFlags.None))==null,
            "PowerRename reports invalid regex syntax only in regex mode");

        check(Do("foo","bar_${start=10,increment=5,padding=3}",RenameFlags.EnumerateItems,"foo.txt",index:2)=="bar_020.txt",
            "PowerRename enumeration honours start, increment and padding");
        check(Do("foo","${}",RenameFlags.EnumerateItems,"foo.txt",index:3)=="3.txt"&&Do("foo","${start=-5,padding=3}",RenameFlags.EnumerateItems,"foo")=="-05",
            "PowerRename default enumerator counts from 0 and pads negative numbers like printf");
        string random=Do("foo","${rstringalnum=8}",RenameFlags.RandomizeItems,"foo.txt");
        check(random.Length==12&&random.EndsWith(".txt")&&random[..8].All(char.IsAsciiLetterOrDigit),"PowerRename ${rstringalnum=8} produces eight alphanumerics");
        check(Do("foo","${rstringdigit=4}",RenameFlags.RandomizeItems,"foo")[..4].All(char.IsAsciiDigit)&&Do("foo","${x}",RenameFlags.RandomizeItems,"foo")=="${x}",
            "PowerRename randomizer handles digit sets and leaves non-randomizer tokens literal");
        check(Do("foo","${ruuidv4}",RenameFlags.RandomizeItems,"foo") is string g&&g.Length==36&&Guid.TryParse(g,out _),"PowerRename ${ruuidv4} produces a GUID");

        check(Do("foo","bar",RenameFlags.Uppercase|RenameFlags.NameOnly,"foo.foo")=="BAR.foo"&&Do("foo","bar",RenameFlags.Uppercase,"foo.txt")=="BAR.TXT",
            "PowerRename uppercase respects name-only");
        check(Do("FOO","Bar",RenameFlags.Lowercase,"FOO.TXT")=="bar.txt","PowerRename lowercase converts the whole name");
        check(Do("foo","bar",RenameFlags.Titlecase,"foo And The To")=="Bar and the To","PowerRename titlecase keeps small words lower except the last");
        check(Do("foo","bar",RenameFlags.Titlecase,"the foo i'll and i've you're dogs' the i'd it's i'm don't to y'all")==
            "The Bar I'll and I've You're Dogs' the I'd It's I'm Don't to Y'all","PowerRename titlecase handles contractions like the donor");
        check(Do("foo","bar",RenameFlags.Capitalized,"foo and the to.txt")=="Bar And The To.txt","PowerRename capitalized uppercases every word and keeps the extension");

        check(Do("txt","md",RenameFlags.ExtensionOnly,"txt.txt")=="txt.md"&&Do("txt","md",RenameFlags.NameOnly,"txt.txt")=="md.txt",
            "PowerRename name-only and extension-only limit the replaced part");
        check(Do("txt","md",RenameFlags.ExtensionOnly,"txt",folder:true)=="txt"&&Do("txt","md",RenameFlags.ExtensionOnly,".txt")==".txt",
            "PowerRename extension-only skips folders and names without an extension");
        check(Do("a","b",RenameFlags.ExcludeFolders,"a",folder:true)=="a"&&Do("a","b",RenameFlags.ExcludeFiles,"a")=="a"&&
              Do("a","b",RenameFlags.ExcludeSubfolders,"a",sub:true)=="a"&&Do("a","b",RenameFlags.ExcludeFiles,"a",folder:true)=="b",
            "PowerRename exclude flags skip files, folders and subfolder items");

        var t=new DateTime(2020,7,22,15,6,42,453);
        check(Do("foo","bar$YYYY-$MM-$DD-$hh-$mm-$ss-$fff",Re|All,"foo",time:t)=="bar2020-07-22-15-06-42-453","PowerRename padded date tokens use the file time");
        check(Do("foo","bar$YY-$M-$D-$h-$m-$s-$f",Re|All,"foo",time:t)=="bar20-7-22-15-6-42-4","PowerRename unpadded date tokens use the file time");
        check(Do("test","[$hh:$mm] [$H:$mm $tt]",Re|All,"test",time:new DateTime(2025,4,10,0,0,0))=="[00:00] [12:00 am]"&&
              Do("test","[$HH:$mm $TT]",RenameFlags.None,"test",time:new DateTime(2025,4,10,23,45,0))=="[11:45 PM]",
            "PowerRename 12- and 24-hour tokens match the donor");
        check(Do("foo","$$YYYY",RenameFlags.None,"foo",time:t)=="$$YYYY"&&UsesFileTime(new RenameRule("x","$YYYY",0))&&!UsesFileTime(new RenameRule("x","$$Y",0)),
            "PowerRename $$ escapes date tokens");
        check(TimeOf(RenameFlags.AccessTime)==RenameTime.Accessed&&TimeOf(RenameFlags.None)==RenameTime.Creation,"PowerRename time flags pick the file time");

        check(Do("名前","Name",RenameFlags.None,"Ünïcødé 名前.txt")=="Ünïcødé Name.txt"&&Do("ünï","Uni",RenameFlags.None,"Ünïcødé 名前.txt")=="Unicødé 名前.txt",
            "PowerRename handles Unicode names and case-folds non-ASCII letters");
        check(Do("","x",RenameFlags.Uppercase,"Ünïcødé 名前.txt")=="ÜNÏCØDÉ 名前.TXT"&&Do("","x",RenameFlags.None,"abc")=="abc",
            "PowerRename empty search only applies case transforms");
        check(Do("foo","bar. ",RenameFlags.None,"foo")=="bar"&&Do("e\u0301","x",RenameFlags.None,"\u00e9")=="x",
            "PowerRename trims trailing dots and spaces and matches NFC-normalised terms");
        check(Matches(new RenameRule("a","b",0),"cat",false)&&!Matches(new RenameRule("z","b",0),"cat",false),
            "PowerRename reports whether an item advances the enumeration counter");
    }
#endif
}
