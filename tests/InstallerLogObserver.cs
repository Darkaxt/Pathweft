using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// Observe an actual installer state marker, never a delay or an idle-CPU guess.
public sealed class InstallerLogObserver:IDisposable
{
    readonly Timer monitor;
    readonly object gate=new object();
    bool disposed;
    Exception observationError;
    readonly string path,marker;
    readonly ManualResetEvent observed=new ManualResetEvent(false);
    public InstallerLogObserver(string path,string marker)
    {
        this.path=path;this.marker=marker;
        // NTFS last-write notifications can remain buffered while Inno holds
        // its log open at the native process wait. This is a diagnostic read
        // interval, never a completion deadline: only the actual marker or
        // actual process exit can finish WaitOrExit.
        monitor=new Timer(_=>Inspect(),null,0,250);
    }
    void Inspect()
    {
        lock(gate)
        {
        if(disposed)return;
        try
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(stream))if(reader.ReadToEnd().Contains(marker))observed.Set();
        }
        catch(FileNotFoundException){} // invalidated creation notification; await subsequent actual changes
        catch(Exception error){observationError=error;observed.Set();}
        }
    }
    sealed class ProcessSignal:WaitHandle
    {
        internal ProcessSignal(Process process){SafeWaitHandle=new SafeWaitHandle(process.Handle,false);}
    }
    public void WaitOrExit(Process process)
    {
        using(var finished=new ProcessSignal(process))
            if(WaitHandle.WaitAny(new WaitHandle[]{observed,finished})!=0)throw new InvalidOperationException("Installer exited before the required state marker.");
        if(observationError!=null)throw new IOException("Installer log observation failed.",observationError);
    }
    public static void WaitReadyOrExit(EventWaitHandle ready,Process process)
    {
        using(var finished=new ProcessSignal(process))
            if(WaitHandle.WaitAny(new WaitHandle[]{ready,finished})!=0)throw new InvalidOperationException("Owned test process exited before readiness.");
    }
    public void Dispose(){lock(gate){disposed=true;monitor.Dispose();observed.Dispose();}}
}
