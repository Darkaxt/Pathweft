using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LiteDB;
using Microsoft.Data.Sqlite;

namespace FileExplorer.Preview;

internal sealed class DatabaseViewer(MainWindow owner):IViewer
{
    internal sealed record Catalog(bool Sqlite,string[] Tables);
    ContextObject context;string path;ComboBox tables;ContentControl body;long revision;
    CancellationTokenSource reading;bool disposed;Task loading=Task.CompletedTask;
    static DatabaseViewer()=>SqliteRuntime.Ensure();
    public bool CanHandle(string value)=>true;
    public void Init(){}
    public void Prepare(string value,ContextObject current){path=value;context=current;current.Title=Path.GetFileName(value)+" · Database";}
    public async Task View(string value,ContextObject current)
    {
        var catalog=await Task.Run(()=>ReadCatalog(path,current.Token),current.Token);current.Token.ThrowIfCancellationRequested();
        var panel=new DockPanel();var controls=new DockPanel{Margin=new Thickness(12,8,12,8)};DockPanel.SetDock(controls,Dock.Top);panel.Children.Add(controls);
        var refresh=PreviewControls.Icon(owner,"Refresh database preview","\uE72C");DockPanel.SetDock(refresh,Dock.Right);controls.Children.Add(refresh);
        tables=new ComboBox{ItemsSource=catalog.Tables,Style=(Style)owner.FindResource("PreviewClipCombo"),MaxDropDownHeight=300};
        System.Windows.Automation.AutomationProperties.SetName(tables,"Database table or collection");controls.Children.Add(tables);
        body=new ContentControl();panel.Children.Add(body);current.ViewerContent=panel;
        if(catalog.Tables.Length==0){body.Content=BuiltinViewer.Message("No user tables or collections.");return;}
        tables.SelectedIndex=0;
        void Load(){loading=LoadTable(catalog.Sqlite);current.Track(loading);}
        tables.SelectionChanged+=(_,_)=>Load();refresh.Click+=(_,_)=>Load();
        loading=LoadTable(catalog.Sqlite);await loading;
    }
    async Task LoadTable(bool sqlite)
    {
        if(disposed||tables.SelectedItem is not string table)return;
        long generation=++revision;reading?.Cancel();reading?.Dispose();reading=CancellationTokenSource.CreateLinkedTokenSource(context.Token);var token=reading.Token;
        body.Content=BuiltinViewer.Message("Reading table…");
        try
        {
            var data=await Task.Run(()=>ReadTable(path,table,sqlite,token),token);
            if(!disposed&&generation==revision&&context.IsCurrent)body.Content=new TabularPreview(owner,data);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or SqliteException or LiteException or InvalidOperationException)
        {if(!disposed&&generation==revision&&context.IsCurrent)body.Content=BuiltinViewer.Message("Database table unavailable\n"+error.Message);}
    }
    internal static Catalog ReadCatalog(string path,CancellationToken token)
    {
        using var snapshot=new Snapshot(path,token);
        if(snapshot.Sqlite)
        {
            using var connection=OpenSqlite(snapshot.File);
            using var query=connection.CreateCommand();query.CommandText="SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 1001";
            using var cancel=token.Register(query.Cancel);using var rows=query.ExecuteReader();var names=new List<string>();
            while(rows.Read()){token.ThrowIfCancellationRequested();names.Add(rows.GetString(0));}
            if(names.Count>1000)throw new IOException("Database exceeds1,000 preview tables.");return new(true,names.ToArray());
        }
        using var database=OpenLite(snapshot.File);var collections=database.GetCollectionNames().Order().Take(1001).ToArray();
        if(collections.Length>1000)throw new IOException("Database exceeds1,000 preview collections.");return new(false,collections);
    }
    internal static PreviewTable ReadTable(string path,string name,bool sqlite,CancellationToken token)
    {
        using var snapshot=new Snapshot(path,token);
        if(sqlite!=snapshot.Sqlite)throw new IOException("The database format changed during preview.");
        var rows=new List<string[]>();string[] columns;long characters=0;bool limited=false;
        bool Add(string[] row)
        {
            token.ThrowIfCancellationRequested();characters+=row.Sum(value=>(long)value.Length);
            if(rows.Count>=50_000||characters>16*1024*1024||(long)(rows.Count+1)*row.Length>1_000_000){limited=true;return false;}
            rows.Add(row);return true;
        }
        if(sqlite)
        {
            using var connection=OpenSqlite(snapshot.File);
            using var query=connection.CreateCommand();query.CommandText="SELECT * FROM \""+name.Replace("\"","\"\"")+"\" LIMIT 50001";
            using var cancel=token.Register(query.Cancel);using var reader=query.ExecuteReader();
            if(reader.FieldCount>256)throw new IOException("Database table exceeds256 preview columns.");
            columns=Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).ToArray();
            while(reader.Read())
            {
                var row=new string[reader.FieldCount];
                for(int i=0;i<row.Length;i++)
                {
                    if(reader.IsDBNull(i)){row[i]="NULL";continue;}
                    if(reader.GetFieldType(i)==typeof(byte[])){var buffer=new byte[64];long total=reader.GetBytes(i,0,null,0,0);int read=(int)reader.GetBytes(i,0,buffer,0,(int)Math.Min(total,64));row[i]=Convert.ToHexString(buffer.AsSpan(0,read))+(total>read?$"… ({total:N0} bytes)":"");}
                    else{string value=Convert.ToString(reader.GetValue(i),System.Globalization.CultureInfo.InvariantCulture);row[i]=value.Length>4096?value[..4096]+"… [cell truncated]":value;}
                }
                if(!Add(row))break;
            }
        }
        else
        {
            using var database=OpenLite(snapshot.File);var collection=database.GetCollection(name);var documents=new List<Dictionary<string,string>>();var fields=new List<string>();long held=0;
            foreach(var document in collection.FindAll())
            {
                token.ThrowIfCancellationRequested();foreach(var field in document.Keys)if(!fields.Contains(field))fields.Add(field);
                if(fields.Count>256)throw new IOException("Collection exceeds256 preview fields.");
                var projected=document.ToDictionary(pair=>pair.Key,pair=>{string value=pair.Value.IsString?pair.Value.AsString:pair.Value.ToString();return value.Length>4096?value[..4096]+"… [cell truncated]":value;});
                held+=projected.Sum(pair=>(long)pair.Value.Length);
                if(held>16*1024*1024||(long)(documents.Count+1)*fields.Count>1_000_000){limited=true;break;}
                documents.Add(projected);if(documents.Count>=50_001){limited=true;break;}
            }
            columns=fields.ToArray();
            foreach(var document in documents)
            {
                var row=columns.Select(field=>document.GetValueOrDefault(field,"NULL")).ToArray();if(!Add(row))break;
            }
        }
        return new(columns,rows,limited?"Preview limited to50,000 rows,1,000,000 cells or16MiB of displayed values. Large cells are truncated.":"Read-only preview; source data is never edited.");
    }
    static SqliteConnection OpenSqlite(string file)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=0}.ToString());
        try
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT sqlite_version()";
            string version=(string)command.ExecuteScalar();
            if(!System.Version.TryParse(version,out var current)||current<new System.Version(3,50,2))throw new IOException("Database preview requires Windows SQLite 3.50.2 or newer; this machine provides "+version+". Update Windows to use the database preview safely.");
            command.CommandText="PRAGMA trusted_schema=OFF; PRAGMA query_only=ON;";command.ExecuteNonQuery();return connection;
        }
        catch{connection.Dispose();throw;}
    }
    static LiteDatabase OpenLite(string file)=>new(new ConnectionString{Filename=file,ReadOnly=true,Connection=ConnectionType.Direct,Upgrade=false});
    sealed class Snapshot:IDisposable
    {
        readonly string folder;internal string File{get;}internal bool Sqlite{get;}
        internal Snapshot(string source,CancellationToken token)
        {
            // Hold read-only handles that exclude writers during the snapshot. A live
            // writer produces an explicit sharing error instead of a torn database.
            using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
            using var wal=System.IO.File.Exists(source+"-wal")?new FileStream(source+"-wal",FileMode.Open,FileAccess.Read,FileShare.Read):null;
            byte[] data=PreviewInput.Read(input,256L*1024*1024,token);
            Sqlite=data.AsSpan().StartsWith("SQLite format 3\0"u8);
            if(!Sqlite&&!Encoding.ASCII.GetString(data.AsSpan(0,Math.Min(data.Length,128))).Contains("LiteDB",StringComparison.Ordinal))throw new InvalidDataException("Not a supported SQLite/LiteDB file, or its header is encrypted.");
            folder=Path.Combine(DataFolder.Cache,"database-preview",Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(folder);File=Path.Combine(folder,"content.db");
            try
            {
                System.IO.File.WriteAllBytes(File,data);
                if(Sqlite&&wal!=null)
                {
                    if((System.IO.File.GetAttributes(source+"-wal")&FileAttributes.ReparsePoint)!=0)throw new IOException("Database preview does not follow a linked WAL sidecar.");
                    System.IO.File.WriteAllBytes(File+"-wal",PreviewInput.Read(wal,256L*1024*1024,token));
                }
                token.ThrowIfCancellationRequested();
            }
            catch{Dispose();throw;}
        }
        public void Dispose(){if(System.IO.Directory.Exists(folder))System.IO.Directory.Delete(folder,true);}
    }
    public void Cleanup(){if(disposed)return;disposed=true;++revision;reading?.Cancel();context?.Track(loading);reading?.Dispose();}
}
