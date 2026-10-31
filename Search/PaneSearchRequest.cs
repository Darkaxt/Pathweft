namespace FileExplorer.Search;

// Values cross the widget/pane boundary once; neither surface owns the other's state.
[Flags]
internal enum SearchMatchOptions { None=0,Case=1,WholeWord=2,Path=4,Regex=8 }
internal sealed record PaneSearchRequest(string Folder, string Query, int Scope, string Sort, bool Descending,SearchMatchOptions Match=SearchMatchOptions.None);
