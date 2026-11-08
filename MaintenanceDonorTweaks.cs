using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

// Individual registry settings adapted from Raphire/Win11Debloat, MIT.
// Exact selected files/pin/notices: vendor/Win11Debloat/source-manifest.json.
// The shared typed worker journals original kinds/values; donor Undo defaults
// are intentionally unused. Settings may need sign-out/restart to take effect.
internal static class MaintenanceDonorTweaks
{
    const string Advanced=@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string Content=@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    internal static MaintenanceTweak[] All()=>
    [
        Setting("Advertising ID","Apps stop using this Windows advertising ID for personalized ads.",D(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo","Enabled",0)),
        Setting("Tailored experiences","Windows stops personalizing tips and offers using diagnostic data; diagnostic collection itself is unchanged.",D(@"Software\Microsoft\Windows\CurrentVersion\Privacy","TailoredExperiencesWithDiagnosticDataEnabled",0)),
        Setting("Windows welcome suggestions","Hide the Windows welcome experience after updates and occasional sign-ins.",D(Content,"SubscribedContent-310093Enabled",0)),
        Setting("Windows tips and tricks","Hide Windows usage tips and soft-landing suggestions.",D(Content,"SubscribedContent-338389Enabled",0),D(Content,"SoftLandingEnabled",0)),
        Setting("Lock-screen tips","Hide lock-screen tips and promotional suggestions; the lock screen remains enabled.",D(Content,"SubscribedContent-338387Enabled",0)),
        Setting("Suggested content in Settings","Hide suggested content in Windows Settings.",D(Content,"SubscribedContent-338393Enabled",0),D(Content,"SubscribedContent-353694Enabled",0),D(Content,"SubscribedContent-353696Enabled",0),D(Content,"SubscribedContent-353698Enabled",0)),
        Setting("Finish-setting-up reminders","Hide suggestions to finish setting up this Windows device.",D(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement","ScoobeSystemSettingEnabled",0)),
        Setting("Settings account notifications","Hide account-related notifications in Windows Settings; sign-in and accounts are unchanged.",D(@"Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications","EnableAccountNotifications",0)),
        Setting("Explorer provider suggestions","Hide sync-provider promotional notifications in Explorer; synchronization is unchanged.",D(Advanced,"ShowSyncProviderNotifications",0)),
        Setting("Start tips and recommendations","Hide supported Start recommendations for tips, shortcuts and new apps; pinned applications remain.",D(Advanced,"Start_IrisRecommendations",0)),
        Setting("Start account notifications","Hide account-related notifications in the Start menu; accounts are unchanged.",D(Advanced,"Start_AccountNotifications",0)),
        Setting("Left-aligned taskbar","Align Windows taskbar buttons to the left.",D(Advanced,"TaskbarAl",0)),
        Setting("Hide taskbar search","Hide the Windows taskbar search box; keyboard search remains available.",D(@"Software\Microsoft\Windows\CurrentVersion\Search","SearchboxTaskbarMode",0)),
        Setting("Hide Task View button","Hide the Task View taskbar button; Win+Tab remains available.",D(Advanced,"ShowTaskViewButton",0)),
        Setting("Last-active taskbar window","Click an application taskbar button to activate its last-used window; hover still shows thumbnails.",D(Advanced,"LastActiveClick",1)),
        Setting("Never combine taskbar buttons","Show separate Windows taskbar buttons with labels; uses more taskbar space.",D(Advanced,"TaskbarGlomLevel",2)),
        Setting("Explorer opens This PC","Open new Windows Explorer windows at This PC instead of Home; Pathweft navigation is unchanged.",D(Advanced,"LaunchTo",1)),
        Setting("Drive letters before names","Show drive letters before drive names in Windows Explorer.",D(@"Software\Microsoft\Windows\CurrentVersion\Explorer","ShowDriveLettersFirst",4)),
        Setting("Dark Windows appearance","Use dark mode for supported Windows applications and system surfaces.",D(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",0),D(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)),
        Setting("Hide browser tabs in Alt+Tab","Show application windows rather than supported browser tabs in Alt+Tab.",D(Advanced,"MultiTaskingAltTabFilter",3)),
        Setting("Disable Snap suggestions","Stop suggesting other windows after snapping; window snapping itself remains available.",D(Advanced,"SnapAssist",0)),
        Setting("Disable mouse acceleration","Use unaccelerated Windows pointer movement; pointer feel changes and sign-out may be needed.",S(@"Control Panel\Mouse","MouseSpeed","0"),S(@"Control Panel\Mouse","MouseThreshold1","0"),S(@"Control Panel\Mouse","MouseThreshold2","0")),
        Setting("Disable Sticky Keys shortcut","Disable the five-Shift Sticky Keys shortcut using the donor flag value; accessibility behavior changes.",S(@"Control Panel\Accessibility\StickyKeys","Flags","506")),
        Setting("Disable Fast Startup","Use a full Windows startup after shutdown; startup can take longer. Hibernate remains available.",D(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power","HiberbootEnabled",0,"HKEY_LOCAL_MACHINE"))
    ];
    static RegistryEdit D(string path,string name,int value,string hive="HKEY_CURRENT_USER")=>new(hive,path,name,RegistryValueKind.DWord,JsonSerializer.SerializeToElement(value),true);
    static RegistryEdit S(string path,string name,string value)=>new("HKEY_CURRENT_USER",path,name,RegistryValueKind.String,JsonSerializer.SerializeToElement(value),true);
    static MaintenanceTweak Setting(string name,string effect,params RegistryEdit[] targets)=>new()
    {
        Name=name,Description=effect,IsApplied=()=>targets.All(t=>t.Same(t.Read())),
        Apply=edits=>edits.AddRange(targets),
        Revert=_=>throw new InvalidOperationException("Restore uses the exact saved original settings.")
    };
}
