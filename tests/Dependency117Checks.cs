using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using System.Reflection;

// Included by both Debug source acceptance and the independent Release consumer.
// Operates through the served editor and normal preview button, without page hooks.
internal static class Dependency117Checks
{
    internal static string Markdown => "# Dependency check\n\n**bold** שלום العربية 😀\n\n|a|b|\n|-|-|\n|1|2|\n\n```text\ncode\n```\n\n[broken](<unfinished\n\n[]("+new string('\u00a0',50000);
    internal static async Task Editor(CoreWebView2 core,string output,Action<bool,string> check)
    {
        check(core.Source=="https://pathweft.editor/editor.html","Markdown uses the actual served editor URL");
        var result=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Received(object sender,CoreWebView2WebMessageReceivedEventArgs e)
        {
            using var json=JsonDocument.Parse(e.WebMessageAsJson);
            if(json.RootElement.TryGetProperty("dependency117",out var value))result.TrySetResult(value.GetString());
        }
        core.WebMessageReceived+=Received;
        try
        {
            await core.ExecuteScriptAsync("""
                (()=>{const frame=document.getElementById('preview');const started=performance.now();
                const observer=new MutationObserver(()=>{if(!frame.srcdoc)return;observer.disconnect();
                  chrome.webview.postMessage({type:'dependency117',dependency117:JSON.stringify({html:frame.srcdoc,elapsed:performance.now()-started,sandbox:frame.getAttribute('sandbox')})});});
                observer.observe(frame,{attributes:true,attributeFilter:['srcdoc']});
                document.getElementById('md-toggle').click();})();
                """);
            string receipt=await result.Task;
            File.WriteAllText(output,receipt);
            using var document=JsonDocument.Parse(receipt);
            string html=document.RootElement.GetProperty("html").GetString();
            check(html.Contains("<h1>Dependency check</h1>")&&html.Contains("<strong>bold</strong>")&&html.Contains("<table>")&&html.Contains("<code class=\"language-text\">"),"Real editor renders headings, emphasis, tables and fenced code");
            check(html.Contains("שלום العربية 😀")&&html.Contains(new string('\u00a0',50000)),"Real editor completes malformed Unicode link input with the upstream regression length");
            check(document.RootElement.GetProperty("elapsed").GetDouble()<2000,"Real editor's complete 50,000-character Unicode preview stays responsive including its existing debounce");
            check(document.RootElement.GetProperty("sandbox").GetString()=="","Markdown keeps its sandboxed preview iframe");
        }
        finally{core.WebMessageReceived-=Received;}
    }
    internal static async Task Subtitles(object player,string root,Action<bool,string> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var type=player.GetType();
        Task Command(params string[] arguments)=>(Task)type.GetMethod("CommandAsync",flags).Invoke(player,new object[]{arguments});
        string Property(string name)=>(string)type.GetMethod("Property",flags).Invoke(player,new object[]{name});
        string subtitles=Path.Combine(root,"rtl117.srt");
        string text="שלום עולם — مرحبا بالعالم (123)\n"+string.Concat(Enumerable.Repeat("\u2067(אבג)\u2069 \u2066[123]\u2069 ",512));
        File.WriteAllText(subtitles,"1\n00:00:00,000 --> 00:00:07,500\n"+text+"\n");
        await Command("sub-add",subtitles,"select");
        await Command("set","sub-font-size","24");
        await Command("set","sub-visibility","yes");
        async Task SeekFrame(string at)
        {
            var frame=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<string,object> changed=(name,value)=>{if(name=="frame")frame.TrySetResult();};
            var evt=type.GetEvent("Changed",flags);evt.GetAddMethod(true).Invoke(player,new object[]{changed});
            try{await Command("seek",at,"absolute+exact");await frame.Task;}
            finally{evt.GetRemoveMethod(true).Invoke(player,new object[]{changed});}
        }
        await SeekFrame("1");
        check(Property("sub-text")?.Contains("שלום עולם")==true,"Native libass/FriBidi decodes the actual RTL and isolate-heavy subtitle track");
        string visible=Path.Combine(root,"rtl117-visible.png"),hidden=Path.Combine(root,"rtl117-hidden.png");
        await Command("screenshot-to-file",visible,"window");
        await Command("set","sub-visibility","no");await SeekFrame("1");
        await Command("screenshot-to-file",hidden,"window");
        check(!File.ReadAllBytes(visible).SequenceEqual(File.ReadAllBytes(hidden)),"Native GPU output renders the RTL subtitle overlay rather than only loading track metadata");
        await Command("sub-remove");
    }
    internal static async Task DolbySeeks(object player,string name,Action<bool,string> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var type=player.GetType();
        Task Command(params string[] arguments)=>(Task)type.GetMethod("CommandAsync",flags).Invoke(player,new object[]{arguments});
        string Property(string property)=>(string)type.GetMethod("Property",flags).Invoke(player,new object[]{property});
        foreach(bool paused in new[]{true,false})
        {
            await Command("set","pause",paused?"yes":"no");
            double goal=paused?1:2;
            var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<string,object> changed=(property,value)=>{if(property=="time-pos"&&value is double position&&Math.Abs(position-goal)<.3)reached.TrySetResult();};
            var evt=type.GetEvent("Changed",flags);evt.GetAddMethod(true).Invoke(player,new object[]{changed});
            try
            {
                await Command("seek",goal.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute+exact");await reached.Task;
                check(Property("pause")== (paused?"yes":"no"),"Native Dolby Vision seek reaches its requested time and preserves "+(paused?"paused":"playing")+" state: "+name);
            }
            finally{evt.GetRemoveMethod(true).Invoke(player,new object[]{changed});}
        }
        await Command("set","pause","yes");
    }
}
