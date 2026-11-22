#if DEBUG
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
    // live shaping keeps the order; the UI thread stays well below the reset-per-sample cost.
    internal async Task VerifyAutomationLoad26(string root,Window window,Action<bool,string> check)
    {
        Select(Cpu);LiveForVerification();
        int uiThread=(int)GetCurrentThreadId();var hwnd=new WindowInteropHelper(window).Handle;
        long events=0;
        AutomationPropertyChangedEventHandler changed=(_,_)=>Interlocked.Increment(ref events);
        StructureChangedEventHandler structure=(_,_)=>Interlocked.Increment(ref events);
        await Task.Run(()=>
        {
            var element=AutomationElement.FromHandle(hwnd);
            Automation.AddAutomationPropertyChangedEventHandler(element,TreeScope.Subtree,changed,AutomationElement.NameProperty,AutomationElement.IsOffscreenProperty,AutomationElement.BoundingRectangleProperty);
            Automation.AddStructureChangedEventHandler(element,TreeScope.Subtree,structure);
        });
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated()=>ready.TrySetResult();monitor.Changed+=Updated;
            try{await ready.Task;}finally{monitor.Changed-=Updated;}
        }
        TimeSpan UiTime(){using var process=Process.GetCurrentProcess();return process.Threads.Cast<ProcessThread>().First(t=>t.Id==uiThread).TotalProcessorTime;}
        int resets=0;
        void Counted(object sender,NotifyCollectionChangedEventArgs e){if(e.Action==NotifyCollectionChangedAction.Reset)resets++;}
        async Task<(double Percent,int Samples,long Events)> Measure(Action perSample)
        {
            for(int i=0;i<4;i++)await Next();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            void Extra()=>Dispatcher.BeginInvoke(perSample,DispatcherPriority.Normal);
            if(perSample!=null)monitor.Changed+=Extra;
            resets=0;Interlocked.Exchange(ref events,0);
            var start=UiTime();var clock=Stopwatch.StartNew();int samples=0;
            while(clock.Elapsed<TimeSpan.FromSeconds(20)){await Next();samples++;}
            if(perSample!=null)monitor.Changed-=Extra;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            return((UiTime()-start).TotalMilliseconds/clock.Elapsed.TotalMilliseconds*100,samples,Interlocked.Read(ref events));
        }
        ((INotifyCollectionChanged)table.Items).CollectionChanged+=Counted;
        var current=await Measure(null);int currentResets=resets;
        var rows=table.Items.Cast<PerformanceProcessRow>().ToArray();
        int Compare(PerformanceProcessRow a,PerformanceProcessRow b)
        {
            int c=b.Pinned.CompareTo(a.Pinned);if(c!=0)return c;
            c=b.SortUsage.CompareTo(a.SortUsage);if(c!=0)return c;
            c=string.Compare(a.Process,b.Process,StringComparison.CurrentCulture);return c!=0?c:a.Pid.CompareTo(b.Pid);
        }
        bool ordered=rows.Length>1&&rows.Zip(rows.Skip(1)).All(x=>Compare(x.First,x.Second)<=0);
        // The same client and table with three view resets per sample, as before MONITOR-26.
        var legacy=await Measure(()=>{for(int i=0;i<3;i++)SortRows(true);});
        ((INotifyCollectionChanged)table.Items).CollectionChanged-=Counted;
        await Task.Run(Automation.RemoveAllEventHandlers);
        Select(-1);
        bool pass=currentResets==0&&ordered&&current.Events>0&&current.Percent<=10&&current.Percent*2<=legacy.Percent;
        File.WriteAllText(Path.Combine(root,"automation-load.json"),JsonSerializer.Serialize(new{pass,rows=rows.Length,currentResets,ordered,
            currentPercentOfOneProcessor=current.Percent,currentSamples=current.Samples,currentEvents=current.Events,
            legacyPercentOfOneProcessor=legacy.Percent,legacySamples=legacy.Samples,legacyEvents=legacy.Events},new JsonSerializerOptions{WriteIndented=true}));
        check(currentResets==0,"Live CPU analysis applies samples without resetting the table view");
        check(ordered,"Live shaping keeps the process table in its sort order across samples");
        check(current.Events>0,"An actual UI Automation client listened to the window during the measurement");
        check(current.Percent<=10&&current.Percent*2<=legacy.Percent,$"UI thread {current.Percent:0.0}% of one processor with a listening client, against {legacy.Percent:0.0}% with a reset per sample");
    }
}
#endif
