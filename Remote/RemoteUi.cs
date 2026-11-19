using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

/// BROWSE-09: opening a connection. A missing or refused password is asked for (saved in Windows
/// Credential Manager only when the user ticks it, else kept in memory until exit); a host key seen for
/// the first time is shown and asked about on the UI thread.
internal static class RemoteSessions
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> typed=new();

    static string KnownSecret(RemoteConnection c)
    {
        if(typed.TryGetValue(c.Id,out var secret))return secret;
        try{return RemoteCredentials.Read(c.CredentialTarget);}
        catch(System.ComponentModel.Win32Exception){return null;}
    }
    static bool NeedsSecret(RemoteConnection c)=>c.Kind=="sftp"?c.Auth!="key":c.User.Length>0;
    static bool Refused(Exception ex)=>ex is Renci.SshNet.Common.SshAuthenticationException or Renci.SshNet.Common.SshPassPhraseNullOrEmptyException or UnauthorizedAccessException;

    internal static async Task<IRemoteFileSystem> Open(Window owner,RemoteConnection c,CancellationToken cancel)
    {
        var dispatcher=owner?.Dispatcher??Application.Current.Dispatcher;
        string secret=KnownSecret(c);string refusal=null;
        while(true)
        {
            if(refusal!=null||secret==null&&NeedsSecret(c))
            {
                var answer=await dispatcher.InvokeAsync(()=>RemoteSecretDialog.Ask(owner,c,refusal));
                if(answer==null)throw new OperationCanceledException("Not connected: no password was given.");
                secret=answer.Value.Secret;
                if(answer.Value.Save)RemoteCredentials.Write(c.CredentialTarget,c.User,secret);else typed[c.Id]=secret;
            }
            IRemoteFileSystem fs=null;
            try
            {
                fs=RemoteFileSystems.Create(c,secret,(host,fingerprint)=>dispatcher.InvokeAsync(()=>PromptDialog.Choose(owner,
                    $"Trust the server {host}?","Pathweft has not connected to it before. Its SSH host key fingerprint is\n"+fingerprint+
                    "\n\nCompare it with the one the server's administrator gives you. A key that changes later is refused.","Trust and connect","Cancel")==0).Task);
                await fs.Connect(cancel);
                return fs;
            }
            catch(Exception ex) when(Refused(ex))
            {
                fs?.Dispose();typed.TryRemove(c.Id,out _);
                refusal=ex is Renci.SshNet.Common.SshPassPhraseNullOrEmptyException?"The private key needs its passphrase.":"The server refused the sign-in. Try again.";
            }
            catch{fs?.Dispose();throw;}
        }
    }
    /// What Settings > Privacy and Remove forget: passwords typed this run.
    internal static void Forget(string id)=>typed.TryRemove(id,out _);
    internal static void Remember(string id,string secret)=>typed[id]=secret;

    internal static bool IsConnectionFailure(Exception ex)=>ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException
        or NotSupportedException or ArgumentException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or Renci.SshNet.Common.SshException
        or System.Xml.XmlException or TimeoutException;
}

/// The password or key passphrase for one connection.
internal sealed class RemoteSecretDialog : AuxDialog
{
    readonly PasswordBox secret=new(){FontSize=13,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    readonly CheckBox save;
    RemoteSecretDialog(Window owner,RemoteConnection c,string refusal) : base(owner,"Connect to "+(c.Name.Length>0?c.Name:c.Host),"Connect")
    {
        bool key=c.Kind=="sftp"&&c.Auth=="key";
        Body.Children.Add(Label((key?"Passphrase for "+Path.GetFileName(c.KeyFile):"Password for "+(c.User.Length>0?c.User+"@":"")+c.Host),0));
        secret.SetResourceReference(ForegroundProperty,"Palette.E8EEF6");secret.SetResourceReference(PasswordBox.CaretBrushProperty,"Palette.E8EEF6");
        System.Windows.Automation.AutomationProperties.SetName(secret,key?"Passphrase":"Password");
        var frame=new Border{Child=secret};frame.SetResourceReference(StyleProperty,"AuxiliaryField");Body.Children.Add(frame);
        save=Check("Save it in Windows Credential Manager",false);Body.Children.Add(save);
        Body.Children.Add(Note(refusal??"Not saved, it is kept only until Pathweft exits."));
        Loaded+=(_,_)=>secret.Focus();
    }
    internal static (string Secret,bool Save)? Ask(Window owner,RemoteConnection c,string refusal)
    {
        var dialog=new RemoteSecretDialog(owner,c,refusal);
        return dialog.Present()?(dialog.secret.Password,dialog.save.IsChecked==true):null;
    }
}

/// Add connection… / Edit…: where, who and how to sign in. The password goes to Credential Manager, never to connections.json.
internal sealed class RemoteConnectionDialog : AuxDialog
{
    static readonly string[] Kinds={"SFTP (SSH)","WebDAV over https","WebDAV over http (this PC only)"};
    readonly TextBox name,host,port,user,folder,keyFile;
    readonly ComboBox kind,auth;
    readonly PasswordBox password=new(){FontSize=13,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    readonly CheckBox savePassword;
    readonly StackPanel keyRow=new(),passwordRow=new(),authRow=new();
    readonly TextBlock problem;
    readonly RemoteConnection start;
    RemoteConnectionDialog(Window owner,RemoteConnection start,string title) : base(owner,title,"Save",500)
    {
        this.start=start;
        Body.Children.Add(Label("Name",0));
        name=Field(start.Name,"Connection name",out var nameFrame);Body.Children.Add(nameFrame);
        Body.Children.Add(Label("Protocol"));
        kind=Choice("Protocol",Kinds);kind.SelectedIndex=start.Kind switch{"webdavs"=>1,"webdav"=>2,_=>0};Body.Children.Add(kind);
        var hostGrid=new Grid();hostGrid.ColumnDefinitions.Add(new ColumnDefinition());hostGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(110)});
        var hostColumn=new StackPanel{Margin=new Thickness(0,0,8,0)};hostColumn.Children.Add(Label("Server"));host=Field(start.Host,"Server",out var hostFrame);hostColumn.Children.Add(hostFrame);
        var portColumn=new StackPanel();portColumn.Children.Add(Label("Port (optional)"));port=Field(start.Port>0?start.Port.ToString(System.Globalization.CultureInfo.InvariantCulture):"","Port",out var portFrame);portColumn.Children.Add(portFrame);
        Grid.SetColumn(portColumn,1);hostGrid.Children.Add(hostColumn);hostGrid.Children.Add(portColumn);Body.Children.Add(hostGrid);
        Body.Children.Add(Label("User name"));user=Field(start.User,"User name",out var userFrame);Body.Children.Add(userFrame);
        Body.Children.Add(Label("Folder to open"));folder=Field(start.Root,"Folder to open",out var folderFrame);Body.Children.Add(folderFrame);
        authRow.Children.Add(Label("Sign in with"));auth=Choice("Sign in with","Password","Private key file");auth.SelectedIndex=start.Auth=="key"?1:0;authRow.Children.Add(auth);Body.Children.Add(authRow);
        keyRow.Children.Add(Label("Private key file"));
        var keyGrid=new DockPanel();var browse=Action("Browse…",()=>{var pick=new Microsoft.Win32.OpenFileDialog{Title="Private key file",CheckFileExists=true};if(pick.ShowDialog(this)==true)keyFile.Text=pick.FileName;});
        browse.Margin=new Thickness(6,0,0,0);DockPanel.SetDock(browse,Dock.Right);keyGrid.Children.Add(browse);
        keyFile=Field(start.KeyFile,"Private key file",out var keyFrame);keyGrid.Children.Add(keyFrame);keyRow.Children.Add(keyGrid);Body.Children.Add(keyRow);
        passwordRow.Children.Add(Label("Password or passphrase (optional, asked when needed)"));
        password.SetResourceReference(ForegroundProperty,"Palette.E8EEF6");password.SetResourceReference(PasswordBox.CaretBrushProperty,"Palette.E8EEF6");
        System.Windows.Automation.AutomationProperties.SetName(password,"Password");
        var passwordFrame=new Border{Child=password};passwordFrame.SetResourceReference(StyleProperty,"AuxiliaryField");passwordRow.Children.Add(passwordFrame);
        savePassword=Check("Save it in Windows Credential Manager",true);passwordRow.Children.Add(savePassword);Body.Children.Add(passwordRow);
        Body.Children.Add(Note("SSH host keys are checked: the first connection shows the server's fingerprint, a changed key is refused. https uses Windows certificate validation. Remote changes cannot be undone."));
        problem=Note("");Body.Children.Add(problem);
        kind.SelectionChanged+=(_,_)=>Arrange();auth.SelectionChanged+=(_,_)=>Arrange();Arrange();
        Loaded+=(_,_)=>{(start.Host.Length==0?host:name).Focus();};
    }
    string Kind=>kind.SelectedIndex switch{1=>"webdavs",2=>"webdav",_=>"sftp"};
    void Arrange()
    {
        bool sftp=Kind=="sftp";
        authRow.Visibility=sftp?Visibility.Visible:Visibility.Collapsed;
        keyRow.Visibility=sftp&&auth.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
    }
    protected override bool Validate()
    {
        string server=host.Text.Trim();
        if(server.Length==0||server.Contains('/')||server.Contains(' ')){problem.Text="Type the server's name or address, without a protocol or path.";return false;}
        if(port.Text.Trim().Length>0&&!(int.TryParse(port.Text.Trim(),out int p)&&p is >0 and <65536)){problem.Text="The port is a number from 1 to 65535, or empty for the protocol's usual one.";return false;}
        if(Kind=="sftp"&&user.Text.Trim().Length==0){problem.Text="SFTP needs a user name.";return false;}
        if(Kind=="sftp"&&auth.SelectedIndex==1&&!File.Exists(keyFile.Text.Trim())){problem.Text="Choose the private key file.";return false;}
        if(Kind=="webdav"&&!(server is "localhost" or "127.0.0.1" or "::1"||server=="[::1]")){problem.Text="Plain http is only for a server on this PC. Use WebDAV over https.";return false;}
        return true;
    }
    RemoteConnection Result()=>new()
    {
        Id=start.Id,Name=name.Text.Trim().Length>0?name.Text.Trim():host.Text.Trim(),Kind=Kind,Host=host.Text.Trim(),
        Port=int.TryParse(port.Text.Trim(),out int p)?p:0,User=user.Text.Trim(),Root=RemotePath.Normalize(folder.Text.Trim().Length>0?folder.Text.Trim():"/"),
        Auth=Kind=="sftp"&&auth.SelectedIndex==1?"key":"password",KeyFile=Kind=="sftp"&&auth.SelectedIndex==1?keyFile.Text.Trim():"",
    };
    internal static (RemoteConnection Connection,string Password,bool Save)? Ask(Window owner,RemoteConnection start,string title)
    {
        var dialog=new RemoteConnectionDialog(owner,start,title);
        return dialog.Present()?(dialog.Result(),dialog.password.Password,dialog.savePassword.IsChecked==true):null;
    }
}

public partial class MainWindow
{
    /// The sidebar's Connections section: each saved connection opens its folder in the active pane.
    void BuildConnections()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(BuildConnections);return;}
        RemoteList.Children.Clear();
        foreach(var connection in RemoteConnections.Load())
        {
            var c=connection;string address=c.Address(c.Root);
            var button=SideButton(c.Kind=="sftp"?"":"",c.Name.Length>0?c.Name:c.Host,"",click:()=>{active.Navigate(address);active.FocusList();});
            button.ToolTip=address;
            button.PreviewMouseDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Middle){active.NewTab();active.Navigate(address);e.Handled=true;}};
            var menu=new ContextMenu();
            menu.Items.Add(Item("Open","","",()=>{active.Navigate(address);active.FocusList();}));
            menu.Items.Add(Item("Open in new tab","","Middle click",()=>{active.NewTab();active.Navigate(address);}));
            menu.Items.Add(Item("Open in other pane","","",()=>Other.Navigate(address),!Settings.SinglePane));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Edit…","","",()=>EditConnection(c)));
            if(c.Kind=="sftp")menu.Items.Add(Item("Forget host key","","",()=>{KnownHosts.Remove(c.Host,c.EffectivePort);Status.Text=$"The next connection to {c.Host} shows its host key again";}));
            menu.Items.Add(Item("Remove","","",()=>
            {
                if(PromptDialog.Choose(this,$"Remove the connection “{c.Name}”?","Its saved password goes too. Nothing on the server changes.","Remove","Cancel")!=0)return;
                RemoteSessions.Forget(c.Id);RemoteConnections.Remove(c.Id);Status.Text=$"Removed the connection “{c.Name}”";
            }));
            button.ContextMenu=menu;
            RemoteList.Children.Add(button);
        }
        var add=SideButton("","Add connection…","",click:()=>EditConnection(null));
        add.ToolTip="An SFTP or WebDAV server, browsed like a folder";
        RemoteList.Children.Add(add);
    }

    void EditConnection(RemoteConnection existing)
    {
        if(RemoteConnectionDialog.Ask(this,existing??new RemoteConnection(),existing==null?"Add connection":"Edit connection") is not { } answer)return;
        var c=answer.Connection;
        RemoteSessions.Forget(c.Id);
        if(answer.Password.Length>0)
            try{if(answer.Save)RemoteCredentials.Write(c.CredentialTarget,c.User,answer.Password);else RemoteSessions.Remember(c.Id,answer.Password);}
            catch(System.ComponentModel.Win32Exception ex){Status.Text="The password could not be saved: "+ex.Message;RemoteSessions.Remember(c.Id,answer.Password);}
        RemoteConnections.Upsert(c);
        if(existing==null){active.Navigate(c.Address(c.Root));active.FocusList();}
    }
}
