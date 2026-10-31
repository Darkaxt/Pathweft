# S11 native folder destinations: investigation and implementation plan

JUMP-03 research is complete; S11 implementation is NOT STARTED. The user's
screenshot shows a StartAllBack Start-menu Jump List. Windows' taskbar right-click
menu is another surface for the same application destination list. Its precise
layout/expand arrow is owned by the Shell/menu host, not by Pathweft's theme.
The user's clarification confirms that the native presentation itself is suitable;
the requested difference is useful folder destinations instead of just tasks.
This work requires no Windows Search replacement or StartAllBack binary patch.

## What Windows actually supports

Microsoft's [Jump List model](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar-extensions)
separates destinations, which users can pin/remove, from application tasks.
The Opus example's Tasks-only menu therefore does not prove folder integration.
Opus also documents [configurable folder entries](https://www.gpsoft.com.au/help/Opus10/Documents/Prefs/Jumplist.htm);
its visible task-only state is not evidence that the platform cannot do more.

There is a specific trap: simply calling
[SHAddToRecentDocs with a folder](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shaddtorecentdocs)
feeds Explorer's folder Jump List, not another application's. Reusing Explorer's
AppID/list or rewriting its cache would also violate ownership. Pathweft currently
has no explicit AppUserModelID/Jump List publisher in its application bootstrap
or installer shortcut definitions; an installer GUID alone is not that identity.

Use a stable explicit Pathweft AppUserModelID on the process, shortcuts and list.
Represent each destination as an IShellLink launching Pathweft.exe with the
quoted folder path as an argument and a descriptive title/folder icon. A link to
the folder itself would route through its default handler. The argument-bearing
link route lets Pathweft open its own destinations independently of folder opt-in.
Microsoft explicitly permits IShellLink destinations in
[ICustomDestinationList.AppendCategory](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icustomdestinationlist-appendcategory)
and requires arguments for pin/unpin/removal support. This is the promising
supported integration boundary; runtime qualification is still required.

## Bounded S11 implementation

1. Establish one stable process/shortcut/list AppID before creating windows.
   Preserve existing updater/installer identity, taskbar grouping and ownership;
   qualify an already pinned old shortcut rather than silently replacing it.
2. Publish native argument-bearing folder destinations under Recent folders and
   useful existing Quick access entries. Windows owns the Pinned section and
   user pin/unpin actions. Reuse Pathweft's navigation history, deduplicate paths,
   and record genuine visits rather than polluting history during background
   indexing/preview. Do not pretend a custom category named Pinned is equivalent
   to Windows' persistent pinned destinations.
3. BeginList retrieves removed destinations; omit them on regeneration until a
   genuine qualifying visit/user action makes reintroduction appropriate. Respect
   privacy settings/policy and E_ACCESSDENIED; do not change Windows privacy
   switches. Keep tasks available when history is disabled. Never clear global
   Recent items or another application's list. Offer only useful existing actions.
4. Qualify Unicode/spaces/UNC/drive roots, offline destinations, cold launch and
   running-instance handoff. Existing navigation/handoff owns opening; extend
   its argument parser only for required virtual destinations. This PC/libraries/
   Recycle Bin need an explicit capability decision and real acceptance, not fake
   filesystem paths. Persist native pinning across restart/update, remove only
   owned app state on uninstall, and preserve user profile/history.
5. Verify actual Shell destinations and native pin/unpin/open/remove behavior,
   privacy-off conditions, installed shortcut identity and update/uninstall.
   StartAllBack's expansion is a caller-compatibility check using that same native
   list. Use app-owned/headless diagnostics first; any final visible menu check
   must preserve the user's desktop-control preference. Do not reopen Sandbox.

This plan is feasible using documented Windows interfaces, but exact host pinning,
privacy and StartAllBack presentation remain unverified until S11 executes. No
Jump List publisher, new default handler or live Shell setting is installed by
the current rendering/Settings/Help refinement release.
