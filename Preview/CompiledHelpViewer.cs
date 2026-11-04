using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer.Preview;

internal sealed class CompiledHelpViewer(MainWindow owner,Entry entry):IViewer
{
    ContextObject context;ContentControl body;ComboBox topics;string path;
    HtmlViewer viewer;CancellationTokenSource topicCancellation;Task loading=Task.CompletedTask;long revision;bool disposed;
#if DEBUG
    internal HtmlViewer HtmlForVerification=>viewer;
    internal ComboBox TopicsForVerification=>topics;
    internal Task TopicCompletion=>loading;
#endif
    public bool CanHandle(string value)=>true;
    public void Init(){}
    public void Prepare(string value,ContextObject current){path=value;context=current;current.Title=entry.Name+" · Compiled help";}
    public async Task View(string value,ContextObject current)
    {
        var entries=await Task.Run(()=>Archive.Read(path),current.Token);current.Token.ThrowIfCancellationRequested();
        var pages=entries.Where(e=>!e.IsDir&&Path.GetExtension(e.Path).ToLowerInvariant() is ".htm" or ".html").Select(e=>e.Path).Take(10_001).ToArray();
        if(pages.Length==0)throw new IOException("No supported HTML topics were found in this CHM file.");
        if(pages.Length>10_000)throw new IOException("Help file exceeds10,000 preview topics.");
        var panel=new DockPanel();topics=new ComboBox{ItemsSource=pages,SelectedIndex=0,Style=(Style)owner.FindResource("PreviewClipCombo"),Margin=new Thickness(12,8,12,8),MaxDropDownHeight=300};
        System.Windows.Automation.AutomationProperties.SetName(topics,"Help topic");DockPanel.SetDock(topics,Dock.Top);panel.Children.Add(topics);
        body=new ContentControl();panel.Children.Add(body);current.ViewerContent=panel;
        topics.SelectionChanged+=(_,_)=>{loading=ShowTopic();context.Track(loading);};
        loading=ShowTopic();await loading;
    }
    async Task ShowTopic()
    {
        if(disposed||topics.SelectedItem is not string topic)return;
        long generation=++revision;topicCancellation?.Cancel();topicCancellation?.Dispose();topicCancellation=CancellationTokenSource.CreateLinkedTokenSource(context.Token);
        var token=topicCancellation.Token;var old=viewer;old?.Cleanup();viewer=null;body.Content=BuiltinViewer.Message("Loading topic…");
        try
        {
            if(old!=null)await old.Closed;
            token.ThrowIfCancellationRequested();if(disposed||generation!=revision)return;
            var resource=HtmlResources.CompiledHelp(path,topic,token);
            viewer=new HtmlViewer(entry,resource,"Compiled help");
            var child=new ContextObject(token,()=>context.IsCurrent&&!disposed&&generation==revision,content=>body.Content=content,context.Track,false);
            viewer.Prepare(path,child);
            viewer.InternalLinkRequested+=uri=>
            {
                if(disposed||generation!=revision)return;
                string target=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if(topics.Items.Cast<string>().Contains(target,StringComparer.OrdinalIgnoreCase))topics.SelectedItem=topics.Items.Cast<string>().First(item=>item.Equals(target,StringComparison.OrdinalIgnoreCase));
            };
            await viewer.View(path,child);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {if(context.IsCurrent&&!disposed&&generation==revision)body.Content=BuiltinViewer.Message("Help topic unavailable\n"+error.Message);}
    }
    public void Cleanup(){if(disposed)return;disposed=true;++revision;topicCancellation?.Cancel();viewer?.Cleanup();context?.Track(loading);topicCancellation?.Dispose();}
}
