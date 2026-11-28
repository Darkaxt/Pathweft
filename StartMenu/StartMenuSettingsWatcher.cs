using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer.StartMenu;

/// SAB-07 (S6c): a change to StartAllBack's Start-menu settings (HKCU\Software\StartIsBack) or to the Windows 7 menu
/// settings in Explorer\Advanced (or the power button's policy, or Windows' dark mode for S6d's visual styles) reaches the menu at once, as it reaches StartAllBack's: both keys are watched
/// (RegNotifyChangeKeyValue, values only, never their subkeys, so licensing is never touched) and each burst of
/// changes is reported once, on the thread that made the watcher.
internal sealed class StartMenuSettingsWatcher : IDisposable
{
    readonly ManualResetEvent stop = new(false);
    readonly Thread thread;
    readonly System.Windows.Threading.DispatcherTimer settle;

    internal StartMenuSettingsWatcher(Action changed)
    {
        settle = new() { Interval = TimeSpan.FromMilliseconds(250) };
        settle.Tick += (_, _) => { settle.Stop(); changed(); };
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        thread = new Thread(() => Watch(() => dispatcher.BeginInvoke(() => { settle.Stop(); settle.Start(); }))) { IsBackground = true, Name = "Start menu settings" };
        thread.Start();
    }

    void Watch(Action changed)
    {
        string[] names = [StartMenuOptions.Key, StartMenuOptions.AdvancedKey, StartMenuOptions.PolicyKey, StartMenuOptions.PersonalizeKey];
        var keys = new RegistryKey[names.Length];
        var events = new AutoResetEvent[names.Length];
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                // a missing key (StartAllBack never configured) is made by StartAllBack later: look again now and then
                keys[i] = Registry.CurrentUser.OpenSubKey(names[i]);
                events[i] = new AutoResetEvent(false);
                Arm(keys[i], events[i]);
            }
            var handles = events.Cast<WaitHandle>().Append(stop).ToArray();
            while (true)
            {
                int which = WaitHandle.WaitAny(handles, TimeSpan.FromMinutes(1));
                if (which == handles.Length - 1) return;
                if (which == WaitHandle.WaitTimeout)
                {
                    for (int i = 0; i < keys.Length; i++)
                        if (keys[i] == null && (keys[i] = Registry.CurrentUser.OpenSubKey(names[i])) != null) { Arm(keys[i], events[i]); changed(); }
                    continue;
                }
                Arm(keys[which], events[which]);
                changed();
            }
        }
        finally
        {
            foreach (var k in keys) k?.Dispose();
            foreach (var e in events) e?.Dispose();
        }
    }

    // one notification per call: armed again after each
    static void Arm(RegistryKey key, AutoResetEvent signal)
    {
        if (key != null) RegNotifyChangeKeyValue(key.Handle, false, 0x1 | 0x4 /* NAME | LAST_SET */, signal.SafeWaitHandle, true);
    }

    public void Dispose()
    {
        settle.Stop();
        stop.Set();
    }

    [DllImport("advapi32.dll")] static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool subtree, uint filter, SafeWaitHandle signal, bool asynchronous);
}
