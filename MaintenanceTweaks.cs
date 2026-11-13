using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

// Registry tweak catalogue adapted from SysOptimizer 6ddd104 / TweakService.cs (MIT).
// Apply builds a declarative edit list; the native boundary journals exact original
// values before any change. Donor Revert defaults are retained only as provenance
// and are never used for restoration.
internal sealed class MaintenanceTweak
{
    public string Name {get;init;}
    public string Description {get;init;}
    public Func<bool> IsApplied {get;init;}
    public Action<List<RegistryEdit>> Apply {get;init;}
    public Action<List<RegistryEdit>> Revert {get;init;}
    // Exact scheduled task paths this tweak disables (MAINT-27); empty for registry-only tweaks.
    public string[] TaskPaths {get;init;}=[];
    // Windows optional features or capabilities this tweak turns off (MAINT-43); empty for most tweaks.
    public WindowsComponent[] Components {get;init;}=[];
}
internal static class MaintenanceTweaks
{
    internal static List<MaintenanceTweak> All() =>
    [
        .. MaintenanceDonorTweaks.All(),
        .. MaintenanceS47Tweaks.All(),
        new MaintenanceTweak
        {
            Name = "Windows telemetry",
            Description = "Sets diagnostic data to the lowest level Windows allows: off on Enterprise and Education, Required only on Home and Pro",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0,
            Apply = edits => Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
            Revert = edits => Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
        },
        new MaintenanceTweak
        {
            Name = "Cortana and web search in Start",
            Description = "Turns off Cortana and web results in the Start menu",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana") == 0,
            Apply = edits =>
            {
                Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0);
                Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0);
            },
            Revert = edits =>
            {
                Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana");
                Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb");
            },
        },
        new MaintenanceTweak
        {
            Name = "Xbox Game Bar / Game DVR",
            Description = "Stops Game Bar's background recording, which uses CPU/GPU",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") == 0,
            Apply = edits => Dword(edits, Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0),
            Revert = edits => Dword(edits, Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1),
        },
        new MaintenanceTweak
        {
            Name = "Animations and transparency",
            Description = "Disables window animations and transparency for a snappier desktop",
            IsApplied = () => ReadString(Registry.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate") == "0",
            Apply = edits =>
            {
                Text(edits, Registry.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0");
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
            },
            Revert = edits =>
            {
                Text(edits, Registry.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "1");
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1);
            },
        },
        new MaintenanceTweak
        {
            Name = "Ads and suggestions in Start / lock screen",
            Description = "Turns off suggested apps and promoted content from Windows",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled") == 0,
            Apply = edits =>
            {
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0);
            },
            Revert = edits =>
            {
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 1);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 1);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 1);
            },
        },
        new MaintenanceTweak
        {
            Name = "Hibernation",
            Description = "Disables hibernation and removes hiberfil.sys, freeing disk space; Fast Startup and hybrid sleep go with it",
            IsApplied = () => !File.Exists(Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "hiberfil.sys")),
            Apply = edits => throw new InvalidOperationException("Use Windows power settings to change hibernation."),
            Revert = edits => throw new InvalidOperationException("Use Windows power settings to change hibernation."),
        },
        new MaintenanceTweak
        {
            Name = "Activity history",
            Description = "Stops Windows from recording and syncing your activity timeline",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed") == 0,
            Apply = edits =>
            {
                Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0);
                Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
                Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
            },
            Revert = edits =>
            {
                Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed");
                Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities");
                Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities");
            },
        },
        new MaintenanceTweak
        {
            Name = "Location tracking",
            Description = "Denies apps and Windows access to your location",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation") == 1,
            Apply = edits => Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1),
            Revert = edits => Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation"),
        },
        new MaintenanceTweak
        {
            Name = "Storage Sense",
            Description = "Stops Windows from automatically deleting files it thinks are junk",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01") == 0,
            Apply = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 0),
            Revert = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1),
        },
        new MaintenanceTweak
        {
            Name = "Background apps",
            Description = "Blocks apps from running or sending notifications in the background",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground") == 2,
            Apply = edits => Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2),
            Revert = edits => Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground"),
        },
        new MaintenanceTweak
        {
            Name = "Notification toasts",
            Description = "Turns off popup notifications from apps and Windows",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") == 0,
            Apply = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0),
            Revert = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1),
        },
        new MaintenanceTweak
        {
            Name = "Power throttling",
            Description = "Stops Windows from deliberately slowing down background processes to save power",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff") == 1,
            Apply = edits => Dword(edits, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1),
            Revert = edits => Remove(edits, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"),
        },
        new MaintenanceTweak
        {
            Name = "Show hidden files and extensions",
            Description = "Explorer shows hidden files and real file extensions instead of guessing for you",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden") == 1,
            Apply = edits =>
            {
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0);
            },
            Revert = edits =>
            {
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 2);
                Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 1);
            },
        },
        new MaintenanceTweak
        {
            Name = "\"End task\" on taskbar right-click",
            Description = "Adds End Task to a taskbar app's right-click menu, without opening Task Manager",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask") == 1,
            Apply = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1),
            Revert = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 0),
        },
        new MaintenanceTweak
        {
            Name = "Classic right-click menu (Windows 11)",
            Description = "Brings back the full context menu instead of the shortened Windows 11 one",
            IsApplied = () => Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32") != null,
            Apply = edits => Text(edits, Registry.CurrentUser, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""),
            Revert = edits => Remove(edits, Registry.CurrentUser, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", ""),
        },
        new MaintenanceTweak
        {
            Name = "Startup app delay",
            Description = "Removes Explorer's built-in delay before launching startup apps — a leftover from spreading out disk I/O on old HDDs",
            IsApplied = () => ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec") == 0,
            Apply = edits => Dword(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0),
            Revert = edits => Remove(edits, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec"),
        },
        new MaintenanceTweak
        {
            Name = "Windows Recall",
            Description = "Disables Recall's continuous screen snapshotting (Copilot+ PCs)",
            IsApplied = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis") == 1,
            Apply = edits => Dword(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
            Revert = edits => Remove(edits, Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis"),
        },
    ];


    static int? ReadDword(RegistryKey root,string path,string name)
    {using var key=root.OpenSubKey(path);return key?.GetValue(name) is int value?value:null;}
    static string ReadString(RegistryKey root,string path,string name)
    {using var key=root.OpenSubKey(path);return key?.GetValue(name) as string;}
    static void Dword(List<RegistryEdit> edits,RegistryKey root,string path,string name,int value)=>edits.Add(new(root.Name,path,name,RegistryValueKind.DWord,JsonSerializer.SerializeToElement(value),true));
    static void Text(List<RegistryEdit> edits,RegistryKey root,string path,string name,string value)=>edits.Add(new(root.Name,path,name,RegistryValueKind.String,JsonSerializer.SerializeToElement(value),true));
    static void Remove(List<RegistryEdit> edits,RegistryKey root,string path,string name)=>edits.Add(new(root.Name,path,name,RegistryValueKind.String,default,false));
}
