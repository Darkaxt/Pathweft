#if DEBUG
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FileExplorer;
internal sealed partial class PerformancePanel
{
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    // MONITOR-26: live CPU analysis while a UI Automation client listens to the whole window,
    // as accessibility, text and capture tools do. Samples must not reset the table's view;
    // moving only out-of-order rows keeps the order; the client's added UI-thread cost stays at most half the reset cost.
    internal async Task VerifyAutomationLoad26(string root,Window window,Action<bool,string> check)
    {
        Select(Cpu);LiveForVerification();
        int uiThread=(int)GetCurrentThreadId();var hwnd=new WindowInteropHelper(window).Handle;
        long events=0,delivered=0;int attributing=0;
        // Event sources, read from the cache delivered with each event (as real clients do);
        // their ancestry is resolved after the timed windows.
        var sources=new ConcurrentDictionary<string,int>();var examples=new ConcurrentDictionary<string,AutomationElement>();
        void Attribute(object sender,string kind)
        {
            Interlocked.Increment(ref events);Interlocked.Increment(ref delivered);if(Volatile.Read(ref attributing)==0||sender is not AutomationElement element)return;
            string key;try{key=kind+" | "+element.Cached.ControlType.ProgrammaticName+" "+element.Cached.ClassName+" #"+element.Cached.AutomationId;}catch(Exception ex){key=kind+" | "+ex.GetType().Name;}
            sources.AddOrUpdate(key,1,(_,n)=>n+1);examples.TryAdd(key,element);
        }
        AutomationPropertyChangedEventHandler changed=(sender,e)=>Attribute(sender,e.Property.ProgrammaticName);
        StructureChangedEventHandler structure=(sender,e)=>Attribute(sender,"Structure."+e.StructureChangeType);
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated()=>ready.TrySetResult();monitor.Changed+=Updated;
            try{await ready.Task;}finally{monitor.Changed-=Updated;}
        }
        TimeSpan UiTime(){using var process=Process.GetCurrentProcess();return process.Threads.Cast<ProcessThread>().First(t=>t.Id==uiThread).TotalProcessorTime;}
        int resets=0,moves=0;
        void Counted(object sender,NotifyCollectionChangedEventArgs e){if(e.Action==NotifyCollectionChangedAction.Reset)resets++;else if(e.Action==NotifyCollectionChangedAction.Move)moves++;}
        async Task<(double Percent,int Samples,long Events,int Resets,int Moves)> Measure(Action perSample)
        {
            for(int i=0;i<4;i++)await Next();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            void Extra()=>Dispatcher.BeginInvoke(perSample,DispatcherPriority.Normal);
            if(perSample!=null)monitor.Changed+=Extra;
            resets=moves=0;Interlocked.Exchange(ref events,0);
            var start=UiTime();var clock=Stopwatch.StartNew();int samples=0;
            while(clock.Elapsed<TimeSpan.FromSeconds(20)){await Next();samples++;}
            if(perSample!=null)monitor.Changed-=Extra;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            return((UiTime()-start).TotalMilliseconds/clock.Elapsed.TotalMilliseconds*100,samples,Interlocked.Read(ref events),resets,moves);
        }
        ((INotifyCollectionChanged)table.Items).CollectionChanged+=Counted;
        // The same live table with no UI Automation client.
        var baseline=await Measure(null);
        await Task.Run(()=>
        {
            var element=AutomationElement.FromHandle(hwnd);
            var cache=new CacheRequest();
            foreach(var property in new[]{AutomationElement.ControlTypeProperty,AutomationElement.ClassNameProperty,AutomationElement.AutomationIdProperty})cache.Add(property);
            using(cache.Activate())
            {
                Automation.AddAutomationPropertyChangedEventHandler(element,TreeScope.Subtree,changed,AutomationElement.NameProperty,AutomationElement.IsOffscreenProperty,AutomationElement.BoundingRectangleProperty);
                Automation.AddStructureChangedEventHandler(element,TreeScope.Subtree,structure);
            }
        });
        Interlocked.Exchange(ref attributing,1);
        var current=await Measure(null);
        bool registered=System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.PropertyChanged)&&System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.StructureChanged);
        Interlocked.Exchange(ref attributing,0);
        var rows=table.Items.Cast<PerformanceProcessRow>().ToArray();
        int realized=rows.Count(x=>table.ItemContainerGenerator.ContainerFromItem(x)!=null);
        int Compare(PerformanceProcessRow a,PerformanceProcessRow b)
        {
            int c=b.Pinned.CompareTo(a.Pinned);if(c!=0)return c;
            c=b.SortUsage.CompareTo(a.SortUsage);if(c!=0)return c;
            c=string.Compare(a.Process,b.Process,StringComparison.CurrentCulture);return c!=0?c:a.Pid.CompareTo(b.Pid);
        }
        bool ordered=rows.Length>1&&rows.Zip(rows.Skip(1)).All(x=>Compare(x.First,x.Second)<=0);
        // The same client and table with the view resets of 1.1.89: two panel refreshes per sample,
        // three resets each.
        var legacy=await Measure(()=>{for(int i=0;i<6;i++)SortRows(true);});
        registered&=System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.PropertyChanged)&&System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.StructureChanged);
        ((INotifyCollectionChanged)table.Items).CollectionChanged-=Counted;
        var top=sources.OrderByDescending(x=>x.Value).Take(15).ToArray();
        var ancestry=await Task.Run(()=>top.Select(x=>
        {
            var chain=new List<string>();
            try
            {
                for(var element=examples[x.Key];element!=null&&chain.Count<8;element=TreeWalker.RawViewWalker.GetParent(element))
                    chain.Add(element.Current.ControlType.ProgrammaticName.Replace("ControlType.","")+":"+element.Current.ClassName+(element.Current.AutomationId is {Length:>0} id?"#"+id:""));
            }
            catch(Exception ex){chain.Add(ex.GetType().Name);}
            return new{source=x.Key,count=x.Value,ancestry=string.Join(" < ",chain)};
        }).ToArray());
        await Task.Run(Automation.RemoveAllEventHandlers);
        Select(-1);
        // The client counts as listening when WPF sees its registrations (WPF raises events, and pays their cost,
        // for as long as they exist) and it has received events; an in-process client's delivery can stall.
        bool listening=registered&&delivered>0;
        bool light=(current.Percent-baseline.Percent)*2<=legacy.Percent-baseline.Percent;
        bool pass=current.Resets==0&&ordered&&listening&&light;
        File.WriteAllText(Path.Combine(root,"automation-load.json"),JsonSerializer.Serialize(new{pass,rows=rows.Length,realized,ordered,registered,delivered=Interlocked.Read(ref delivered),
            baseline=new{percentOfOneProcessor=baseline.Percent,baseline.Samples,baseline.Moves},
            current=new{percentOfOneProcessor=current.Percent,current.Samples,current.Events,current.Resets,current.Moves},
            legacy=new{percentOfOneProcessor=legacy.Percent,legacy.Samples,legacy.Events,legacy.Resets,legacy.Moves},
            topSources=ancestry},new JsonSerializerOptions{WriteIndented=true}));
        check(current.Resets==0,"Live CPU analysis applies samples without resetting the table view");
        check(ordered,"Incremental ordering keeps the process table in its sort order across samples");
        check(listening,"An actual UI Automation client listened to the window during the measurement");
        check(light,$"UI thread {current.Percent:0.0}% of one processor with a listening client (no client {baseline.Percent:0.0}%), against {legacy.Percent:0.0}% with the resets of 1.1.89: the client's added cost is at most half of the reset cost");
    }
}
#endif
