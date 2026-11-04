# S11 native folder destinations: investigation and implementation plan

JUMP-03 research is complete; S11 is ACTIVE after verified S13/S9 integration. The user's
screenshot shows a StartAllBack Start-menu Jump List. Windows' taskbar right-click
menu is another surface for the same application destination list. Its precise
layout/expand arrow is owned by the Shell/menu host, not by Pathweft's theme.
The user's clarification confirms that the native presentation itself is suitable;
the requested difference is useful folder destinations instead of just tasks.
This work requires no Windows Search replacement or StartAllBack binary patch.

Implementation contract2026-11-04: use WPF's supported JumpList/JumpTask API and
one stable `Darkaxt.Pathweft` process/installer-shortcut identity. Portable copies
and explicit diagnostic profiles use a deterministic path-scoped identity so
they cannot overwrite the installed list. Publish argument-bearing executable
links under Quick access and Recent folders, using the existing selected Quick
access list and navigation history, plus Home/Downloads tasks. These are actual
custom destinations, not a fake Pinned category. The Shell owns pinning/removal.
Coalesce list refreshes at dispatcher idle instead of polling. Listen only to
the main panes' navigation for visits; previews/indexing do not record visits.
Use quoted folder arguments compatible with the existing cold/warm launch path.

Persist only Shell-reported removal argument identities in the current profile;
omit them on reconstruction/restart. A genuine visit may reintroduce that folder
via SHAddToRecentDocs(SHARD_APPIDINFOLINK) with its exact explicit AppID, without
clearing unrelated removals. Actual native readback, rather than the void call's
return, qualifies that boundary. A private desktop without Explorer accepted
custom categories but did not record recent usage; the unchanged implementation
passes native readback on the normal Shell desktop with hidden, nonactivating
app windows and an isolated profile/AppID. Windows
recent-documents privacy/policy disables destination publication/recording while
tasks remain. Keep physical local/UNC/drive paths (including offline paths),
without network existence probes during list construction. Omit This PC/Shell
namespace/archive virtual folders explicitly. Existing pane navigation owns any
offline error after a destination is opened. Uninstall removes only this copy's
AppID destinations; updates retain its identity/list/pins. Never rewrite an old
user-pinned shortcut or another application's destinations.

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

Source/native execution now passes PID126624: Windows accepts real Quick access
and Recent folders, independent IApplicationDocumentLists reads the actual visit,
and real cold/running-instance processes preserve Unicode, UNC and root arguments.
Saved removal and privacy branches are explicitly diagnostic inputs, not claimed
native user pin/remove actions. Uninstall cleanup is scoped to the test AppID;
installed application recent state remains unchanged. The final package, actual
installed shortcut/update ownership, user pin/unpin/open/remove and StartAllBack
presentation remain required S11 acceptance. No live policy or handler setting
was changed by this test.
