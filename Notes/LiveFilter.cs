namespace FileExplorer;

/// BROWSE-10: what the live filter's text means. Words narrow by name (all must appear);
/// tag:x, rating:N (at least N stars) and comment:x read the BROWSE-07 notes. "Quoted words" keep spaces.
internal sealed class LiveFilter
{
    readonly List<(string Field,string Value)> terms=new();
    internal static LiveFilter Parse(string text)
    {
        var filter=new LiveFilter();
        foreach(var token in Tokens(text??""))
        {
            int colon=token.IndexOf(':');string field=colon>0?token[..colon].ToLowerInvariant():"";
            if(field is "tag" or "rating" or "comment"&&colon<token.Length-1)filter.terms.Add((field,token[(colon+1)..].Trim('"')));
            else filter.terms.Add(("",token.Trim('"')));
        }
        filter.terms.RemoveAll(x=>x.Value.Length==0);
        return filter.terms.Count==0?null:filter;
    }
    static IEnumerable<string> Tokens(string text)
    {
        var current=new System.Text.StringBuilder();bool quoted=false;
        foreach(char c in text)
        {
            if(c=='"'){quoted=!quoted;continue;}
            if(char.IsWhiteSpace(c)&&!quoted){if(current.Length>0){yield return current.ToString();current.Clear();}continue;}
            current.Append(c);
        }
        if(current.Length>0)yield return current.ToString();
    }
    internal bool Matches(Entry entry)
    {
        FileNote note=null;bool looked=false;
        FileNote Note(){if(!looked){note=FileNotes.Find(entry.Path);looked=true;}return note;}
        foreach(var (field,value) in terms)
        {
            bool hit=field switch
            {
                "tag"=>Note()?.Tags.Any(tag=>tag.Contains(value,StringComparison.OrdinalIgnoreCase))==true,
                "rating"=>int.TryParse(value.TrimEnd('+'),out int stars)&&(Note()?.Rating??0)>=stars,
                "comment"=>Note()?.Comment?.Contains(value,StringComparison.OrdinalIgnoreCase)==true,
                _=>entry.Name.Contains(value,StringComparison.OrdinalIgnoreCase),
            };
            if(!hit)return false;
        }
        return true;
    }
}
