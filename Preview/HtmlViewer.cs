using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FileExplorer.Preview;

internal sealed class HtmlViewer(Entry entry):IViewer
{
    WebView2 web;
    CoreWebView2Environment environment;
    ContextObject context;
    HtmlResources resources;
    Task initialization=Task.CompletedTask;
    readonly TaskCompletionSource loaded=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource attached=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource exited=new(TaskCreationOptions.RunContinuationsAsynchronously);
    string profile;
    bool cleaned,browserStarted,navigated;
    readonly List<Stream> responses=new();
    internal Task Closed {get;private set;}=Task.CompletedTask;
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject value){context=value;value.Title=entry.Name+" · HTML";}
    public async Task View(string path,ContextObject value)
    {
        resources=new(entry.Path,path,value.Token);
        await resources.Read(resources.Document); // Document errors are explicit, never a blank successful preview.
        value.Token.ThrowIfCancellationRequested();
        web=new WebView2 {DefaultBackgroundColor=System.Drawing.Color.FromArgb(Appearance.Background.R,Appearance.Background.G,Appearance.Background.B)};
        System.Windows.Automation.AutomationProperties.SetName(web,"Static HTML preview. Scripts and online content are disabled.");
        value.ViewerContent=web;
        initialization=Initialize();await initialization;
        value.Token.ThrowIfCancellationRequested();if(cleaned)return;
        using var cancelled=value.Token.Register(()=>loaded.TrySetCanceled(value.Token));
        web.CoreWebView2.Navigate(resources.Document.AbsoluteUri);
        await loaded.Task;
    }
    async Task Initialize()
    {
        if(!web.IsLoaded)
        {
            void Attached(object sender,System.Windows.RoutedEventArgs args)=>attached.TrySetResult();
            web.Loaded+=Attached;
            using var cancelled=context.Token.Register(()=>attached.TrySetCanceled(context.Token));
            try{await attached.Task;}finally{web.Loaded-=Attached;}
        }
        context.Token.ThrowIfCancellationRequested();if(cleaned)return;
        profile=Path.Combine(DataFolder.Cache,"html-preview",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(profile);
        try
        {
            environment=await CoreWebView2Environment.CreateAsync(null,profile,new(){AllowSingleSignOnUsingOSPrimaryAccount=false});
            environment.BrowserProcessExited+=(_,_)=>exited.TrySetResult();
            if(cleaned)return;
            await web.EnsureCoreWebView2Async(environment);browserStarted=true;
            if(cleaned)return;
        }
        catch(WebView2RuntimeNotFoundException error){throw new IOException("HTML preview requires the Microsoft Edge WebView2 Runtime. Disable the HTML module to view source text.",error);}
        var core=web.CoreWebView2;var settings=core.Settings;
        settings.IsScriptEnabled=false;settings.IsWebMessageEnabled=false;settings.AreHostObjectsAllowed=false;
        settings.AreDefaultScriptDialogsEnabled=false;settings.AreDevToolsEnabled=false;settings.IsStatusBarEnabled=false;
        settings.AreDefaultContextMenusEnabled=false;settings.AreBrowserAcceleratorKeysEnabled=false;
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,args)=>context.Track(Respond(args));
        core.NavigationStarting+=(_,args)=>
        {
            bool same=Uri.TryCreate(args.Uri,UriKind.Absolute,out var uri)&&resources.SameDocument(uri);
            bool first=!navigated&&same&&uri.Fragment=="";
            args.Cancel=!first&&!(same&&uri.Fragment!=""&&args.IsUserInitiated);
            if(first)navigated=true;
        };
        core.FrameNavigationStarting+=(_,args)=>args.Cancel=true;
        core.NewWindowRequested+=(_,args)=>args.Handled=true;
        core.LaunchingExternalUriScheme+=(_,args)=>args.Cancel=true;
        core.DownloadStarting+=(_,args)=>args.Cancel=true;
        core.PermissionRequested+=(_,args)=>args.State=CoreWebView2PermissionState.Deny;
        core.NavigationCompleted+=(_,args)=>
        {
            if(!resources.SameDocument(new Uri(core.Source)))return;
            if(args.IsSuccess)loaded.TrySetResult();else loaded.TrySetException(new IOException("The HTML document could not be rendered."));
        };
    }
    async Task Respond(CoreWebView2WebResourceRequestedEventArgs args)
    {
        var deferred=args.GetDeferral();
        try
        {
            if(args.Request.Method!="GET"||!context.IsCurrent||cleaned)throw new IOException("HTML request is disabled.");
            var item=await resources.Read(new Uri(args.Request.Uri));
            context.Token.ThrowIfCancellationRequested();
            if(cleaned||!context.IsCurrent)throw new OperationCanceledException();
            var content=new MemoryStream(item.Bytes,false);responses.Add(content);
            string headers="Content-Type: "+item.Mime+"\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nAccess-Control-Allow-Origin: *\r\n";
            headers+="Content-Security-Policy: sandbox; default-src 'none'; script-src 'none'; style-src 'unsafe-inline' https://"+resources.Host+"; img-src https://"+resources.Host+" data:; font-src https://"+resources.Host+" data:; base-uri 'none'; form-action 'none'; frame-src 'none'; object-src 'none'; connect-src 'none'; worker-src 'none'\r\n";
            args.Response=environment.CreateWebResourceResponse(content,200,"OK",headers);
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            try{args.Response=environment.CreateWebResourceResponse(null,403,"Blocked","Cache-Control: no-store\r\n");}
            catch(COMException)when(cleaned){}
        }
        finally{deferred.Complete();}
    }
    public void Cleanup()
    {
        if(cleaned)return;cleaned=true;attached.TrySetCanceled();loaded.TrySetCanceled();
        Closed=Close();context?.Track(Closed);
    }
    async Task Close()
    {
        web?.Dispose();
        try{await initialization;}catch(Exception error)when(error is IOException or COMException or InvalidOperationException or OperationCanceledException){}
        if(browserStarted)await exited.Task;
        foreach(var response in responses)response.Dispose();responses.Clear();
        if(profile!=null&&Directory.Exists(profile))Directory.Delete(profile,true);
    }
#if DEBUG
    internal CoreWebView2 CoreForVerification=>web?.CoreWebView2;
    internal string ProfileForVerification=>profile;
#endif
}
