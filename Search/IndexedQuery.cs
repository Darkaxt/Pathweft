using System.IO;

namespace FileExplorer.Search;

// Everything's native language is retained. A grouped expression prevents OR
// terms from escaping the separately quoted subtree constraint.
internal static class IndexedQuery
{
    internal static string Compose(string query, string folder,SearchMatchOptions options=SearchMatchOptions.None)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter a search query.");
        string input=query;
        bool regex=options.HasFlag(SearchMatchOptions.Regex);
        string modifiers=(options.HasFlag(SearchMatchOptions.Case)?"case:":"")
            +(options.HasFlag(SearchMatchOptions.WholeWord)&&!regex?"ww:":"")
            +(options.HasFlag(SearchMatchOptions.Path)?"path:":"");
        if(regex)query=modifiers+"regex:\""+query.Replace("\"","\"\"")+"\"";
        else if(modifiers.Length>0)query=modifiers+"<"+query+">";
        if (folder == null) return query;
        if (!Path.IsPathFullyQualified(folder) || folder.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new ArgumentException("Choose an absolute folder path.");
        int depth = 0; bool quoted = false;
        // Validate the user's grouping before adding modifier groups. A modifier
        // prefix's :< is a group, not a numeric less-than comparison.
        for (int i = 0; !regex && i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            // Numeric comparison operators after a function colon aren't groups.
            if (c is '<' or '>' && i > 0 && input[i - 1] is ':' or '=') continue;
            if (c == '<') depth++;
            if (c == '>' && --depth < 0) throw new ArgumentException("Unbalanced Everything search groups.");
        }
        if (quoted || depth != 0) throw new ArgumentException("Close the search quotes and groups.");
        string root = Path.GetFullPath(folder).TrimEnd('\\', '/') + "\\";
        return $"<{query}> path:\"{root}\"";
    }
}
