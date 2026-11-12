using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

// S47a settings adapted per setting from MIT donors (Win11Debloat, Sophia Script, SophiApp,
// WinUtil, Bloatynosy, RemoveWindowsAI, UnattendedWinstall). Exact cited files, pins and
// licences: vendor/<donor>/source-manifest.json. Generated from the reviewed rule list;
// goal, risk and explanations live in MaintenanceGuidance.json. Restore always uses the
// exact journaled original values and task states, never donor undo defaults.
internal static class MaintenanceS47Tweaks
{
    internal static MaintenanceTweak[] All()=>
    [
        // win11debloat 516fe42d Regfiles/Disable_Copilot.reg:8-13
        Setting("Windows Copilot (policy)","Turn off Windows Copilot by policy",[],D("HKEY_CURRENT_USER","Software\\Policies\\Microsoft\\Windows\\WindowsCopilot","TurnOffWindowsCopilot",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsCopilot","TurnOffWindowsCopilot",1)),
        // win11debloat 516fe42d Regfiles/Disable_Copilot.reg:4-5
        Setting("Copilot button on the taskbar","Hide the Copilot taskbar button",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","ShowCopilotButton",0)),
        // win11debloat 516fe42d Regfiles/Disable_AI_Recall.reg:9-10
        Setting("Recall availability","Do not allow Recall to be enabled",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI","AllowRecallEnablement",0)),
        // win11debloat 516fe42d Regfiles/Disable_Click_to_Do.reg:3-7
        Setting("Click to Do","Turn off Click to Do",[],D("HKEY_CURRENT_USER","Software\\Policies\\Microsoft\\Windows\\WindowsAI","DisableClickToDo",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI","DisableClickToDo",1)),
        // removewindowsai 793e458f RemoveWindowsAi.ps1:885-894
        Setting("AI agent in Settings","Turn off the Settings agent",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI","DisableSettingsAgent",1)),
        // removewindowsai 793e458f RemoveWindowsAi.ps1:886-894
        Setting("AI agent connectors","Turn off local and remote agent connectors",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI","DisableAgentConnectors",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI","DisableRemoteAgentConnectors",1)),
        // removewindowsai 793e458f RemoveWindowsAi.ps1:887-894
        Setting("AI agent workspaces","Turn off agent workspaces",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI","DisableAgentWorkspaces",1)),
        // removewindowsai 793e458f RemoveWindowsAi.ps1:1077-1078
        Setting("App access to Windows AI models","Deny apps access to generative AI and system AI models",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy","LetAppsAccessGenerativeAI",2),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy","LetAppsAccessSystemAIModels",2)),
        // win11debloat 516fe42d Regfiles/Disable_Notepad_AI_Features.reg:3-4
        Setting("AI features in Notepad","Turn off Notepad AI features",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\WindowsNotepad","DisableAIFeatures",1)),
        // win11debloat 516fe42d Regfiles/Disable_Paint_AI_Features.reg:3-8
        Setting("AI features in Paint","Turn off Paint AI features",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint","DisableCocreator",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint","DisableGenerativeFill",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint","DisableImageCreator",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint","DisableGenerativeErase",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Paint","DisableRemoveBackground",1)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-8
        Setting("Copilot reading pages in Edge","Stop Copilot in Edge reading page content",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","CopilotCDPPageContext",0),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","CopilotPageContext",0),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","EdgeEntraCopilotPageContext",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-7
        Setting("Microsoft 365 Copilot icon in Edge","Hide the Microsoft 365 Copilot Chat icon in the Edge toolbar",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","Microsoft365CopilotChatIconEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-10
        Setting("Copilot writing help in Edge","Turn off inline Compose in Edge",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","ComposeInlineEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-9
        Setting("AI search of Edge history","Turn off AI-powered history search in Edge",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","EdgeHistoryAISearchEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-11
        Setting("On-device AI model in Edge","Do not download the Edge on-device AI model",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","GenAILocalFoundationalModelSettings",1)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-12
        Setting("Copilot on the Edge new tab page","Hide Copilot on the Edge new tab page",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","NewTabPageBingChatEnabled",0)),
        // sophiapp a774d3ad src/Customizations/Mutators.cs:175-183
        Setting("Customer Experience Improvement Program tasks","Disable the CEIP data collection scheduled tasks",["\\Microsoft\\Windows\\Autochk\\Proxy","\\Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator","\\Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip","\\Microsoft\\Windows\\DiskDiagnostic\\Microsoft-Windows-DiskDiagnosticDataCollector"]),
        // sophiapp a774d3ad src/Customizations/Mutators.cs:167-173
        Setting("Compatibility Appraiser tasks","Disable the Compatibility Appraiser scheduled tasks",["\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser","\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser Exp","\\Microsoft\\Windows\\Application Experience\\ProgramDataUpdater"]),
        // sophiapp a774d3ad src/Customizations/Mutators.cs:165-165
        Setting("App backup data task (MareBackup)","Disable the MareBackup scheduled task",["\\Microsoft\\Windows\\Application Experience\\MareBackup"]),
        // sophiapp a774d3ad src/Customizations/Mutators.cs:171-171
        Setting("Startup apps notification task","Disable the StartupAppTask scheduled task",["\\Microsoft\\Windows\\Application Experience\\StartupAppTask"]),
        // sophiapp a774d3ad src/Customizations/Mutators.cs:185-187
        Setting("Maps scheduled tasks","Disable the Maps toast and update tasks",["\\Microsoft\\Windows\\Maps\\MapsToastTask","\\Microsoft\\Windows\\Maps\\MapsUpdateTask"]),
        // unattendedwinstall 3414845e autounattend.xml:2445-2446
        Setting("Feedback data tasks","Disable the Feedback (SIUF) scheduled tasks",["\\Microsoft\\Windows\\Feedback\\Siuf\\DmClient","\\Microsoft\\Windows\\Feedback\\Siuf\\DmClientOnScenarioDownload"]),
        // win11debloat 516fe42d Regfiles/Disable_Telemetry.reg:12-13
        Setting("Online speech recognition","Turn off online speech recognition",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy","HasAccepted",0)),
        // win11debloat 516fe42d Regfiles/Disable_Telemetry.reg:20-28
        Setting("Inking and typing personalisation","Turn off the personal inking and typing dictionary",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\InputPersonalization","RestrictImplicitInkCollection",1),D("HKEY_CURRENT_USER","Software\\Microsoft\\InputPersonalization","RestrictImplicitTextCollection",1),D("HKEY_CURRENT_USER","Software\\Microsoft\\InputPersonalization\\TrainedDataStore","HarvestContacts",0),D("HKEY_CURRENT_USER","Software\\Microsoft\\Personalization\\Settings","AcceptedPrivacyPolicy",0)),
        // win11debloat 516fe42d Regfiles/Disable_Telemetry.reg:16-17
        Setting("Inking and typing diagnostics","Stop sending inking and typing data to Microsoft",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Input\\TIPC","Enabled",0)),
        // bloatynosy 489a9de3 Plugins/Packs/Privacy.ini:45-51
        Setting("Handwriting error reports","Turn off handwriting data sharing",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\TabletPC","PreventHandwritingDataSharing",1),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\TabletPC","PreventHandwritingDataSharing",1)),
        // win11debloat 516fe42d Regfiles/Disable_Telemetry.reg:35-36
        Setting("App launch tracking","Stop Windows tracking app launches",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","Start_TrackProgs",0)),
        // unattendedwinstall 3414845e autounattend.xml:3033-3033
        Setting("Recent files in Start and jump lists","Stop showing recently opened items in Start and jump lists",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","Start_TrackDocs",0)),
        // bloatynosy 489a9de3 Plugins/Packs/Privacy.ini:59-66
        Setting("Feedback requests","Never ask for feedback",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Siuf\\Rules","NumberOfSIUFInPeriod",0),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection","DoNotShowFeedbackNotifications",1)),
        // bloatynosy 489a9de3 Plugins/Packs/Privacy.ini:18-19
        Setting("Clipboard sync across devices","Turn off cloud clipboard sync",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\System","AllowCrossDeviceClipboard",0)),
        // unattendedwinstall 3414845e autounattend.xml:2904-2905
        Setting("Cloud content in Windows search","Turn off cloud content search for Microsoft and work accounts",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\SearchSettings","IsMSACloudSearchEnabled",0),D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\SearchSettings","IsAADCloudSearchEnabled",0)),
        // unattendedwinstall 3414845e autounattend.xml:2902-2902
        Setting("Search history on this device","Turn off search history on this device",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\SearchSettings","IsDeviceSearchHistoryEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Bing_Cortana_In_Search.reg:4-5
        Setting("Web suggestions in the search box","Turn off web suggestions in search",[],D("HKEY_CURRENT_USER","Software\\Policies\\Microsoft\\Windows\\Explorer","DisableSearchBoxSuggestions",1)),
        // unattendedwinstall 3414845e autounattend.xml:2512-2512
        Setting("App access to diagnostic info","Deny apps access to diagnostic information about other apps",[],S("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\appDiagnostics","Value","Deny")),
        // unattendedwinstall 3414845e autounattend.xml:2511-2511
        Setting("App access to account info","Deny apps access to your account info",[],S("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\userAccountInformation","Value","Deny")),
        // bloatynosy 489a9de3 Plugins/Packs/Privacy.ini:8-9
        Setting("Advertising ID for all accounts","Turn off the advertising ID by policy",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo","DisabledByGroupPolicy",1)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:1812-1812
        Setting("Recent files in Quick access","Hide recently used files in File Explorer",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer","ShowRecent",0)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:1863-1863
        Setting("Frequent folders in Quick access","Hide frequently used folders in File Explorer",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer","ShowFrequent",0)),
        // unattendedwinstall 3414845e autounattend.xml:2946-2946
        Setting("Cloud files in File Explorer Home","Hide files from Office.com in File Explorer",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer","ShowCloudFilesInQuickAccess",0)),
        // win11debloat 516fe42d Regfiles/Disable_Find_My_Device.reg:4-5
        Setting("Find My Device","Turn off Find My Device by policy",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\FindMyDevice","AllowFindMyDevice",0)),
        // win11debloat 516fe42d Regfiles/Disable_Telemetry.reg:49-51
        Setting("Edge diagnostic data","Send no optional diagnostic data from Edge",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","PersonalizationReportingEnabled",0),D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","DiagnosticData",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-10
        Setting("Edge feedback","Turn off the Edge feedback feature",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","UserFeedbackAllowed",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-9
        Setting("Edge suggestions for unreachable pages","Turn off similar-page suggestions when a page is not found",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","AlternateErrorPagesEnabled",0)),
        // winutil 2ef5e5dc config/tweaks.json:451-452
        Setting("Microsoft consumer experiences","Turn off Microsoft consumer experiences",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","DisableWindowsConsumerFeatures",1)),
        // winutil 2ef5e5dc functions/private/Invoke-WinUtilISOScript.ps1:569-569
        Setting("Cloud optimised content","Turn off cloud optimised content",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","DisableCloudOptimizedContent",1)),
        // win11debloat 516fe42d Regfiles/Disable_Settings_365_Ads.reg:4-5
        Setting("Microsoft account notices in Settings and Start","Turn off account-state content",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","DisableConsumerAccountStateContent",1)),
        // unattendedwinstall 3414845e autounattend.xml:2610-2610
        Setting("Widgets","Turn off Widgets by policy",[],D("HKEY_LOCAL_MACHINE","Software\\Policies\\Microsoft\\Dsh","AllowNewsAndInterests",0)),
        // unattendedwinstall 3414845e autounattend.xml:2611-2611
        Setting("News and interests (Windows 10)","Turn off news and interests on the taskbar",[],D("HKEY_LOCAL_MACHINE","Software\\Policies\\Microsoft\\Windows\\Windows Feeds","EnableFeeds",0)),
        // win11debloat 516fe42d Regfiles/Disable_Chat_Taskbar.reg:4-5
        Setting("Chat (Teams) on the taskbar","Hide the Chat button",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","TaskbarMn",0)),
        // win11debloat 516fe42d Regfiles/Disable_Chat_Taskbar.reg:8-9
        Setting("Meet Now (Windows 10)","Hide the Meet Now icon",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","HideSCAMeetNow",1)),
        // winutil 2ef5e5dc functions/private/Invoke-WinUtilISOScript.ps1:578-578
        Setting("Remote app installs from the Store website","Turn off Push to Install",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\PushToInstall","DisablePushToInstall",1)),
        // win11debloat 516fe42d Regfiles/Disable_Device_Auto_App_Download.reg:4-5
        Setting("Device apps and icons from the internet","Stop downloading device metadata",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\Device Metadata","PreventDeviceMetadataFromNetwork",1)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_AI_Features.reg:3-6
        Setting("Edge sidebar","Turn off the Edge sidebar",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","HubsSidebarEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-7
        Setting("Shopping in Edge","Turn off Edge shopping features",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","EdgeShoppingAssistantEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-13
        Setting("Edge first-run experience","Skip the Edge first-run pages",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","HideFirstRunExperience",1)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-15
        Setting("Edge default browser prompts","Stop Edge asking to be the default browser",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","DefaultBrowserSettingEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-11
        Setting("Edge recommendations and notifications","Turn off Edge feature recommendations",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","ShowRecommendationsEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-14
        Setting("Microsoft Rewards in Edge","Hide Microsoft Rewards in Edge",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","ShowMicrosoftRewards",0)),
        // win11debloat 516fe42d Regfiles/Disable_Edge_Ads_And_Suggestions.reg:4-5
        Setting("News on the Edge new tab page","Turn off Microsoft content on the Edge new tab page",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","NewTabPageContentEnabled",0)),
        // win11debloat 516fe42d Regfiles/Hide_Home_from_Explorer.reg:4-6
        Setting("Home in the navigation pane","Hide Home in the navigation pane",[],D("HKEY_CURRENT_USER","Software\\Classes\\CLSID\\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}","System.IsPinnedToNameSpaceTree",0)),
        // win11debloat 516fe42d Regfiles/Hide_Gallery_from_Explorer.reg:4-5
        Setting("Gallery in the navigation pane","Hide Gallery in the navigation pane",[],D("HKEY_CURRENT_USER","Software\\Classes\\CLSID\\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}","System.IsPinnedToNameSpaceTree",0)),
        // win11debloat 516fe42d Regfiles/Hide_Onedrive_Folder.reg:4-5
        Setting("OneDrive in the navigation pane","Hide OneDrive in the navigation pane",[],D("HKEY_CURRENT_USER","Software\\Classes\\CLSID\\{018D5C66-4533-4307-9B53-224DE2ED1FE6}","System.IsPinnedToNameSpaceTree",0)),
        // unattendedwinstall 3414845e autounattend.xml:2952-2952
        Setting("Full path in the title bar","Show the full folder path in the title bar",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CabinetState","FullPath",1)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:1280-1280
        Setting("Item check boxes","Use check boxes to select items",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","AutoCheckSelect",1)),
        // unattendedwinstall 3414845e autounattend.xml:3007-3007
        Setting("Folder windows in a separate process","Launch folder windows in a separate process",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","SeparateProcess",1)),
        // winutil 2ef5e5dc config/tweaks.json:1326-1334
        Setting("Automatic folder type discovery","Treat all folders as general items",[],S("HKEY_CURRENT_USER","Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags\\AllFolders\\Shell","FolderType","NotSpecified")),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:2937-2937
        Setting("\"- Shortcut\" suffix on new shortcuts","Name new shortcuts without the \"- Shortcut\" suffix",[],S("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\NamingTemplates","ShortcutNameTemplate","%s.lnk")),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:4357-4357
        Setting("Long file paths","Enable Win32 long path support",[],D("HKEY_LOCAL_MACHINE","SYSTEM\\CurrentControlSet\\Control\\FileSystem","LongPathsEnabled",1)),
        // win11debloat 516fe42d Regfiles/Disable_Start_Recommended.reg:3-4
        Setting("Recommended section in Start","Hide the Recommended section in Start",[],D("HKEY_CURRENT_USER","Software\\Policies\\Microsoft\\Windows\\Explorer","HideRecommendedSection",1)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:3509-3509
        Setting("Recently added apps in Start","Hide recently added apps in Start",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Start","ShowRecentList",0)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:3713-3713
        Setting("Most used apps in Start","Hide most used apps in Start",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Start","ShowFrequentList",0)),
        // unattendedwinstall 3414845e autounattend.xml:3027-3027
        Setting("More pins in Start","Use the More pins Start layout",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","Start_Layout",1)),
        // win11debloat 516fe42d Regfiles/Disable_Phone_Link_In_Start.reg:4-5
        Setting("Phone Link pane in Start","Hide the mobile device pane in Start",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Start\\Companions\\Microsoft.YourPhone_8wekyb3d8bbwe","IsEnabled",0)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:2346-2346
        Setting("Seconds in the taskbar clock","Show seconds in the taskbar clock",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","ShowSecondsInSystemClock",1)),
        // win11debloat 516fe42d Regfiles/MMTaskbarMode_Active.reg:3-4
        Setting("Taskbar buttons on multiple displays","Show taskbar buttons only on the display where the window is open",[],D("HKEY_CURRENT_USER","Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","MMTaskbarMode",2)),
        // winutil 2ef5e5dc config/tweaks.json:615-616
        Setting("Update sharing with other PCs","Download updates over HTTP only, without peers",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization","DODownloadMode",0)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:8296-8296
        Setting("Hardware-accelerated GPU scheduling","Turn on hardware-accelerated GPU scheduling",[],D("HKEY_LOCAL_MACHINE","SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers","HwSchMode",2)),
        // bloatynosy 489a9de3 Data/WinRules.ini:432-433
        Setting("Edge Startup Boost","Turn off Edge Startup Boost",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Edge","StartupBoostEnabled",0)),
        // win11debloat 516fe42d Regfiles/Disable_Driver_Updates.reg:3-4
        Setting("Drivers in Windows Update","Exclude drivers from quality updates",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate","ExcludeWUDriversInQualityUpdate",1)),
        // win11debloat 516fe42d Regfiles/Prevent_Auto_Reboot.reg:4-5
        Setting("Automatic restart while signed in","Do not restart automatically while someone is signed in",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","NoAutoRebootWithLoggedOnUsers",1)),
        // sophia b9010fce src/Sophia_Script_for_Windows_11/Module/Sophia.psm1:5258-5258
        Setting("Automatic active hours","Adjust active hours automatically",[],D("HKEY_LOCAL_MACHINE","SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings","SmartActiveHoursState",1))
    ];
    static RegistryEdit D(string hive,string path,string name,int value)=>new(hive,path,name,RegistryValueKind.DWord,JsonSerializer.SerializeToElement(value),true);
    static RegistryEdit S(string hive,string path,string name,string value)=>new(hive,path,name,RegistryValueKind.String,JsonSerializer.SerializeToElement(value),true);
    static MaintenanceTweak Setting(string name,string effect,string[] tasks,params RegistryEdit[] targets)=>new()
    {
        Name=name,Description=effect,TaskPaths=tasks,IsApplied=()=>targets.All(t=>t.Same(t.Read())),
        Apply=edits=>edits.AddRange(targets),
        Revert=_=>throw new InvalidOperationException("Restore uses the exact saved original settings.")
    };
}
