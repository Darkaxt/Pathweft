namespace FileExplorer;

// Database previews and fallback size persistence reuse Windows SQLite.
internal static class SqliteRuntime
{
    static readonly Lazy<bool> initialized=new(()=>{SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());return true;});
    internal static void Ensure()=>_ = initialized.Value;
}
