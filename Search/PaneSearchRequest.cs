namespace FileExplorer.Search;

// Values cross the widget/pane boundary once; neither surface owns the other's state.
internal sealed record PaneSearchRequest(string Folder, string Query, int Scope, string Sort, bool Descending);
