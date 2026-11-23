#if DEBUG
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FileExplorer;
internal sealed partial class PerformancePanel
{
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    // An out-of-process UI Automation client, as accessibility, text and capture tools are: it
    // listens to the whole window for Name, IsOffscreen and BoundingRectangle changes and structure
    // changes, reading the cache delivered with each event. A client inside the provider's own
    // process is unreliable (its calls back into the UI thread can block and its events can be
    // lost), so the measurement does not use one. Control file: quiet, attribute or stop.
    const string AutomationClientScript="""
param([long]$Hwnd,[string]$Dir)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type -ReferencedAssemblies UIAutomationClient,UIAutomationTypes,WindowsBase -TypeDefinition @'
using System;using System.Collections.Concurrent;using System.Collections.Generic;using System.IO;using System.Threading;using System.Windows.Automation;
public static class PathweftAutomationClient
{
    static long events;static int attributing;static readonly ConcurrentDictionary<string,int> sources=new ConcurrentDictionary<string,int>();
    static void Hit(object sender,string kind)
    {
        Interlocked.Increment(ref events);if(Volatile.Read(ref attributing)==0)return;
        string key;try{var e=(AutomationElement)sender;key=kind+" | "+e.Cached.ControlType.ProgrammaticName+" "+e.Cached.ClassName+" #"+e.Cached.AutomationId;}catch(Exception ex){key=kind+" | "+ex.GetType().Name;}
        sources.AddOrUpdate(key,1,(k,n)=>n+1);
    }
    static string State(string path){try{return File.ReadAllText(path).Trim();}catch(IOException){return "";}}
    public static void Run(long hwnd,string dir)
    {
        var element=AutomationElement.FromHandle(new IntPtr(hwnd));var cache=new CacheRequest();
        cache.Add(AutomationElement.ControlTypeProperty);cache.Add(AutomationElement.ClassNameProperty);cache.Add(AutomationElement.AutomationIdProperty);
        using(cache.Activate())
        {
            Automation.AddAutomationPropertyChangedEventHandler(element,TreeScope.Subtree,new AutomationPropertyChangedEventHandler((s,e)=>Hit(s,e.Property.ProgrammaticName)),AutomationElement.NameProperty,AutomationElement.IsOffscreenProperty,AutomationElement.BoundingRectangleProperty);
            Automation.AddStructureChangedEventHandler(element,TreeScope.Subtree,new StructureChangedEventHandler((s,e)=>Hit(s,"Structure."+e.StructureChangeType)));
        }
        string control=Path.Combine(dir,"control"),counter=Path.Combine(dir,"events"),ready=Path.Combine(dir,"ready");
        File.WriteAllText(ready,"1");
        while(true)
        {
            string state=State(control);Volatile.Write(ref attributing,state=="attribute"?1:0);
            try{File.WriteAllText(counter+".tmp",Interlocked.Read(ref events).ToString());File.Copy(counter+".tmp",counter,true);}catch(IOException){}
            if(state=="stop")break;Thread.Sleep(100);
        }
        Automation.RemoveAllEventHandlers();
        var top=new List<KeyValuePair<string,int>>(sources);top.Sort((a,b)=>b.Value.CompareTo(a.Value));
        var lines=new List<string>();for(int i=0;i<top.Count&&i<15;i++)lines.Add(top[i].Value+"\t"+top[i].Key);
        File.WriteAllLines(Path.Combine(dir,"sources.tsv"),lines);
        File.WriteAllText(Path.Combine(dir,"done"),Interlocked.Read(ref events).ToString());
    }
}
'@
[PathweftAutomationClient]::Run($Hwnd,$Dir)
""";
    // MONITOR-26: live CPU analysis while that client listens. Samples must not reset the table's
    // view; moving only out-of-order rows keeps the order; the client's added UI-thread cost stays
    // at most half of its cost with the view resets of 1.1.89.
    internal async Task VerifyAutomationLoad26(string root,Window window,Action<bool,string> check)
    {
        Select(Cpu);LiveForVerification();
        int uiThread=(int)GetCurrentThreadId();var hwnd=new WindowInteropHelper(window).Handle;
        string dir=Path.Combine(root,"automation-client"),control=Path.Combine(dir,"control");Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"client.ps1"),AutomationClientScript);File.WriteAllText(control,"quiet");
        long ClientEvents()
        {
            for(int i=0;i<20;i++){try{if(long.TryParse(File.ReadAllText(Path.Combine(dir,"events")),out long n))return n;}catch(IOException){}Thread.Sleep(10);}
            return -1;
        }
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated()=>ready.TrySetResult();monitor.Changed+=Updated;
            try{await ready.Task;}finally{monitor.Changed-=Updated;}
        }
        TimeSpan UiTime(){using var process=Process.GetCurrentProcess();return process.Threads.Cast<ProcessThread>().First(t=>t.Id==uiThread).TotalProcessorTime;}
        int resets=0,moves=0;
        void Counted(object sender,NotifyCollectionChangedEventArgs e){if(e.Action==NotifyCollectionChangedAction.Reset)resets++;else if(e.Action==NotifyCollectionChangedAction.Move)moves++;}
        async Task<(double Percent,int Samples,long Events,int Resets,int Moves)> Measure(Action perSample,bool listened)
        {
            for(int i=0;i<4;i++)await Next();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            void Extra()=>Dispatcher.BeginInvoke(perSample,DispatcherPriority.Normal);
            if(perSample!=null)monitor.Changed+=Extra;
            resets=moves=0;long before=listened?ClientEvents():0;
            var start=UiTime();var clock=Stopwatch.StartNew();int samples=0;
            while(clock.Elapsed<TimeSpan.FromSeconds(20)){await Next();samples++;}
            if(perSample!=null)monitor.Changed-=Extra;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            double percent=(UiTime()-start).TotalMilliseconds/clock.Elapsed.TotalMilliseconds*100;
            await Task.Delay(300);
            return(percent,samples,listened?ClientEvents()-before:0,resets,moves);
        }
        ((INotifyCollectionChanged)table.Items).CollectionChanged+=Counted;
        // The same live table with no UI Automation client.
        var baseline=await Measure(null,false);
        var shell=Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
        var launch=new ProcessStartInfo(shell){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
        foreach(var argument in new[]{"-NoProfile","-NonInteractive","-MTA","-ExecutionPolicy","Bypass","-File",Path.Combine(dir,"client.ps1"),"-Hwnd",hwnd.ToInt64().ToString(),"-Dir",dir})launch.ArgumentList.Add(argument);
        using var client=Process.Start(launch);var errors=client.StandardError.ReadToEndAsync();
        // Lifecycle condition with a deadline: the client has compiled and registered its handlers.
        var deadline=DateTime.UtcNow.AddSeconds(120);
        while(!File.Exists(Path.Combine(dir,"ready"))&&!client.HasExited&&DateTime.UtcNow<deadline)await Task.Delay(250);
        bool started=File.Exists(Path.Combine(dir,"ready"));
        (double Percent,int Samples,long Events,int Resets,int Moves) current=default,legacy=default;bool registered=false,ordered=false;PerformanceProcessRow[] rows=[];int realized=0;
        try
        {
            if(started)
            {
                File.WriteAllText(control,"attribute");
                current=await Measure(null,true);
                File.WriteAllText(control,"quiet");
                registered=AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)&&AutomationPeer.ListenerExists(AutomationEvents.StructureChanged);
                rows=table.Items.Cast<PerformanceProcessRow>().ToArray();
                realized=rows.Count(x=>table.ItemContainerGenerator.ContainerFromItem(x)!=null);
                int Compare(PerformanceProcessRow a,PerformanceProcessRow b)
                {
                    int c=b.Pinned.CompareTo(a.Pinned);if(c!=0)return c;
                    c=b.SortUsage.CompareTo(a.SortUsage);if(c!=0)return c;
                    c=string.Compare(a.Process,b.Process,StringComparison.CurrentCulture);return c!=0?c:a.Pid.CompareTo(b.Pid);
                }
                ordered=rows.Length>1&&rows.Zip(rows.Skip(1)).All(x=>Compare(x.First,x.Second)<=0);
                // The same client and table with the view resets of 1.1.89: two panel refreshes
                // per sample, three resets each.
                legacy=await Measure(()=>{for(int i=0;i<6;i++)SortRows(true);},true);
                registered&=AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)&&AutomationPeer.ListenerExists(AutomationEvents.StructureChanged);
            }
        }
        finally
        {
            ((INotifyCollectionChanged)table.Items).CollectionChanged-=Counted;
            File.WriteAllText(control,"stop");
            using(var wait=new CancellationTokenSource(TimeSpan.FromSeconds(30)))try{await client.WaitForExitAsync(wait.Token);}catch(OperationCanceledException){}
            if(!client.HasExited)client.Kill();
        }
        var sources=File.Exists(Path.Combine(dir,"sources.tsv"))?File.ReadAllLines(Path.Combine(dir,"sources.tsv")).Select(x=>x.Split('\t',2)).Where(x=>x.Length==2).Select(x=>new{source=x[1],count=int.Parse(x[0])}).ToArray():[];
        string clientErrors=(await errors).Trim();
        Select(-1);
        bool listening=started&&registered&&current.Events>0;
        bool light=listening&&(current.Percent-baseline.Percent)*2<=legacy.Percent-baseline.Percent;
        bool pass=current.Resets==0&&ordered&&listening&&light;
        File.WriteAllText(Path.Combine(root,"automation-load.json"),JsonSerializer.Serialize(new{pass,rows=rows.Length,realized,ordered,started,registered,
            baseline=new{percentOfOneProcessor=baseline.Percent,baseline.Samples,baseline.Moves},
            current=new{percentOfOneProcessor=current.Percent,current.Samples,current.Events,current.Resets,current.Moves},
            legacy=new{percentOfOneProcessor=legacy.Percent,legacy.Samples,legacy.Events,legacy.Resets,legacy.Moves},
            topSources=sources,clientErrors},new JsonSerializerOptions{WriteIndented=true}));
        check(started,"An out-of-process UI Automation client registers on the window"+(started?"":": "+clientErrors));
        check(current.Resets==0,"Live CPU analysis applies samples without resetting the table view");
        check(ordered,"Incremental ordering keeps the process table in its sort order across samples");
        check(listening,"An actual UI Automation client listened to the window during the measurement");
        check(light,$"UI thread {current.Percent:0.0}% of one processor with a listening client (no client {baseline.Percent:0.0}%), against {legacy.Percent:0.0}% with the resets of 1.1.89: the client's added cost is at most half of the reset cost");
    }
}
#endif
