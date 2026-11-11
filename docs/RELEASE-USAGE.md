# Pathweft 1.1.52

Pathweft 1.1.52 is a reliability release. Scoop cleanup preserves a version
whose program is running and names it; other versions are removed as before.
Expected Windows errors in Maintenance, transfers, updates, Settings, drive
health, archives and previews are reported instead of closing the app.

Pathweft installs under Program Files through a Windows administrator-consent
prompt. The desktop app keeps its profile and opt-in folder routes. Packaged
applications can open registered filesystem folders through the protected
native shell class. Helium's Show in folder opens the Downloads folder and
selects the requested file. The updater reports failure if post-install
integration or desktop restart does not finish.

Maintenance refreshes its cards and Cleanup review automatically. Selecting a
category shows cached rows immediately and updates them in the background on
return. There is no Refresh or generic Analyze button. Cleanup Details are brief;
select a row for exact cache paths or partial-access diagnostics. Recommendation
is shown for the fully reviewed Windows tweaks catalog; Startup, Services and
Windows apps show their actual state and selected details without a mostly empty
advice column. Cleaning or changing Windows state still requires the explicit
action button. The administrator shield is for Explorer browsing and is disabled
in Maintenance; protected Cleanup review and elevated actions use Maintenance's
own administrator session.
Npm Cleanup previews and clears its `_cacache` package store; Npm logs and the
separate `_npx` tool cache remain outside that action. The popup animates while
the native manager command runs and reports its final result after readback.

The brush beside Performance opens five Maintenance summary cards while the
file browser remains usable. The cards select
Cleanup, Startup, Services, Windows tweaks and optional Windows apps. Cleanup
automatically analyzes six categories, including obsolete Scoop versions and
completed old downloads. Temporary files and downloads use exact reviewed-file
removal. Obsolete Scoop versions use Everything size estimates without
recursively enumerating package members. Clean starts removing each reviewed
obsolete version folder directly. It unlinks ReadOnly junction names into
Scoop's persistent data without changing that data. If native deletion stops
at a ReadOnly regular file, it clears that obsolete file's ReadOnly bit and
finishes removal. A locked file or other access failure leaves an incomplete
version and explicit error. Current
installations and ambiguous versions remain protected; downloads and installed
versions are separate categories. Startup covers Run/RunOnce and policy Run entries, Startup-folder
files/shortcuts and scheduled boot/logon tasks, with a Type column identifying the
source. Windows-owned startup approval is read without rewriting its flags.
Services lists installed Windows application services with actual startup modes
and descriptions. All categories use compact single-line summaries with a
complete selectable detail well. Forty individual tweaks include24 definitions
adapted from Win11Debloat, grouped by category. Guidance distinguishes reviewed
Keep enabled, Optional and Advanced advice from unknown identities. Selecting
an entry shows its effects before acting. Explicit actions run directly without
an extra app confirmation; double-click toggles supported Startup entries. Supported registry/service changes save exact
original values for Restore; supported Startup files and tasks retain their exact
original file/definition and enabled state. Independently changed values or files
are not overwritten. Maintenance and Performance are mutually exclusive views;
switching either off returns to files and ordinary statistics.
Read-only analysis keeps selection, sorting, filtering and already loaded tabs
available. Back returns to files and keeps the cards and scan available. Switching
Maintenance off cancels and drains its original scan without blocking mode
navigation. Mutations and duplicate scans remain gated. Scoop Cleanup does not
scan package members or query their consumers before deletion.
The hidden-files eye remains visible without its active square in Maintenance
and Performance modes; its original state returns with ordinary folder statistics.
Optional app removal applies only to the current user and does not remove Windows
provisioning. Reinstall removed apps through Microsoft Store.

Ctrl+Z retains Undo history across restarts in the selected bulk-data folder.
Successful transfers still close their cards automatically. Interrupted or
failed transfers offer Undo for accepted arrivals; later additions, modified
files and occupied original locations are left alone. Replaced destination
contents cannot be restored by Undo. Native deletes restore the exact Windows
Recycle Bin item; permanent deletes remain irreversible.

The Performance command beside Analyze space swaps the lower summary region for
CPU, RAM, GPU, current physical-drive and loaded-folder I/O cards. Click a card
for main-pane investigation; Back to files restores the browser. One shared
collector automatically retains the rolling hour and recent app observations
while the browser is in the tray. Saved history uses the configured data folder,
bounded retention and a 256-MiB database cap. No recording switch is required.
Missing sources and collection gaps are not represented as measured zero activity.
App history shows CPU/RAM/GPU charts; boot and system-change readers report actual
Windows events or unavailable access. Selected queries export to CSV/JSON/HTML.

Detailed I/O requires explicit Enable and normal Windows administrator consent.
The bundled app-owned worker traces logical file requests, including cached I/O,
only for the focused folder widget or visible drive details. It is not an
installed service and stops its trace when that scope ends. Physical drive
counters can include other volumes on the same unit. Network, virtual/cloud and
unresolved/spanned device mappings report unavailable physical measurements.
Lost or incomplete file observations are labelled rather than claimed as exact.

Capacity remains in the sidebar and lower statistics card, without a duplicate
strip beneath the address field. Actual foreground folder/search work uses the
field's background as an indeterminate loading band when no useful total exists;
routine background sizing does not trigger it. Read-only archives show a corner
ribbon without changing their actual editing restrictions.

Specialist previews now include bundled Office rendering with page/sheet/slide
navigation and LibreOffice fallback. No Craft application, MCP server or
Coordinator installation is needed by an end user. The complete selected runtime
and its notices ship with Pathweft; matching dependency sources ship separately.
The preview header offers Sidebar, compact in-app PiP and external Windowed
modes. Closing the external window restores its preview to the sidebar.

The taskbar/Start app Jump List uses Windows' Pinned and Frequent filesystem
folders. Windows owns ranking, pinning and removal; history privacy preferences are
respected. Links target Pathweft directly and do not require default-folder
integration. Virtual Shell/archive locations are omitted; offline destinations
remain explicit and report unavailable locations when opened. Portable copies
and explicit profiles use separate application identities. Updates preserve the
installed identity and pinned state; uninstall removes only its owned list.

Analyze space beside Terminal turns the current filesystem folder into an
expandable tree of logical sizes. Toggle it again to restore the original view.
Available Everything file/folder-size indexes provide totals; disabled indexing
or uncovered paths use filesystem metadata. Indexed totals may omit excluded
files and are labelled accordingly. Pathweft does not silently change an existing
Everything configuration. Folder history lives in SQLite under the selected bulk
cache root, with a bounded hot memory cache. Existing text history migrates in
the background; displayed sizes remain visible during a refresh.

Windows 11 x64 is the qualified host. The .NET 10 desktop runtime and native
archive/preview libraries are included. WebView2 Evergreen is required for the
optional F4 editor; the application reports when that runtime is unavailable.

The installer runs for the current user without administrator privileges.
After installation/update, application and helper activation use the existing
desktop's context so they do not inherit Setup's RedirectionGuard restriction.
Setup retains its protection and user file associations remain unchanged.
Ordinary file-open failures appear in the affected row's Message column, with
full details in its tooltip. Successful retry clears the error and hides the
column when empty. Archive working-copy launch failures follow the same rule.
Folder routing is unchecked during installation. Settings > Windows integration
owns folder routing, its restoration action, independent Win+E, and taskbar
search. Navigation within an existing Explorer window stays in Explorer.
Folder routes in that section can be enabled, disabled, added or removed, then
applied. Open JSON edits `folder-routes.json` in the selected settings directory;
Reload reads external edits and Apply updates active handlers. When integration
is off, Apply only saves preferences. Classes are Directory (filesystem folders),
Drive (drive roots), and Folder (explicit Shell folder-class requests); verbs
include open, explore and opennewwindow. The same native handler routes each
enabled command; the editor does not change file associations. Recovery retains
the applied snapshot even if the JSON is later edited or a route is removed.
The native 32/64-bit folder handler includes explicit Folder/explore requests,
such as Chromium/Helium's Open downloads folder action, under the same opt-in.
It preserves a calling Shell browser's site;
external filesystem requests launch Pathweft. Settings refreshes the current
handler ownership when activated. An update migrates only an intact old opt-in.
Explicit Explorer launches, virtual Shell locations and Open/Save
dialogs remain in Windows. StartAllBack's Shell/known-folder/library links are
not covered by this release's filesystem registration; their native integration
belongs to the postponed StartAllBack stage. With folder integration enabled,
SHOpenFolderAndSelectItems uses registered filesystem tabs to preserve the
supplied file selection, including an inactive tab whose listing loads later.
Folder-plus-selection requests sent to Pathweft's /select command are supported.
StartAllBack's grouped Everything results and the wider Windows Search migration
are later unfinished integrations; this release does not change Windows Search.

Settings > Appearance selects Original or Native.
Settings categories are selected from the left column; only that category is
shown and long categories scroll independently. Help's category navigation marks
the section currently being read in bold as you scroll or filter the guide.
Native follows Windows
light/dark and accent preferences, or a selected light/dark/custom accent.
Ctrl+P switches one/two panes. Ctrl+H toggles Windows-hidden and dot-name files.
This PC shows drive capacities, free space, usage bars, filesystems and mapped
network targets. External USB connection rates come from the actual Windows
hub/lane query, not a write benchmark or a device model's maximum. Unknown rates
remain unavailable; older Windows stacks can expose only a speed category.
Detail columns without data are hidden and restored as asynchronous metadata
or results arrive, retaining widths. This PC uses populated drive columns.

Search shares the existing default Everything instance; Pathweft does not start
a second index, change its service or alter its settings. The unchanged pinned
Everything 1.4.1.1032 portable engine is included in Everything/. If there is no
existing instance, configure/start that engine explicitly using its own UI and
license; indexing privileges and indexed folders remain an explicit choice.
For indexed sizing, enable file-size and folder-size indexing in Everything's
index options (`index_size=1` and `index_folder_size=1`) and include the desired
paths in its index. Merely having the executable installed is insufficient.
Pathweft detects the capability through the SDK and labels indexed totals; it
uses metadata sizing for disabled or uncovered paths, preserving basic browsing
without requiring index configuration. This release does not add automatic
Everything installation/configuration or silently expand an existing index.
The single address/search field resolves existing paths first on Enter, then
searches Everything in the current folder. Search from This PC for all indexed
files. Four inline switches control case, whole words, path matching and Regex.
There is no separate filesystem-scan mode or second search row.
The optional Ctrl+Alt+Space popup searches all indexed files while typing. It
contains only the query, four match switches and results. Enter opens a result;
folders and archives open in the main manager. Escape dismisses, Down focuses
results, Ctrl+Enter sends the query to a pane, and Space previews a selection.
Clicking outside dismisses the popup and its preview; moving focus into its
preview keeps the interaction open. Downloads in Favorites follows the actual
Windows known-folder location, including redirection to another drive.

This PC's Free space values include the percentage available. Usage bars in the
drive list and sidebar use blue below80% used, amber at80% and red at90%.
Unavailable drives and empty card-reader slots stay hidden until media is ready.
Cloud mounts identified through provider registration show Cloud and their
provider; their raw reported filesystem is available in the tooltip.
Google Drive account quotas come from the desktop client's read-only local
metadata, bound through its account-to-mount registration. They use GiB/TiB to
match the client's binary byte counts without labelling them as decimal GB/TB.
This PC, sidebar and capacity card use those cloud figures.
Mount-reported capacity can be cache constrained and appears separately in the
tooltip. Missing/unknown/unlimited quota records show an unavailable message;
the ready cloud drive remains accessible. Figures refresh when Google updates
its cached account metadata; this does not make a separate authenticated request.
No credentials are read and Google Drive's cache/streaming settings are unchanged.
Empty statistics cards and their layout space collapse automatically, preserving
the Full/Compact/Off preference. This PC counts locations without fake file totals.
PDF and 3D previews use icon actions with tooltips. The animation selector
follows the theme, and models composite directly onto the preview surface.
Mouse orbit uses F3D's default camera behavior: the model stays upright while
you inspect its sides and underside. Right/middle drag or Shift-left drag pans;
the wheel zooms. Native pole limits prevent accidental camera flips.
Camera math processes all ordered input samples before the batch's frame capture;
it does not trigger an extra render for each mouse event.
The Executable details module reads available version resources, architecture,
size and dates without running the selected application.
Folders and virtual archive folders share an expandable tree; native branches
load on expansion. Clearing selection collapses the preview and splitter without
disabling preview. Selecting another item restores its saved width.

Dragging archive entries between Pathweft panes submits their identities directly
to the shared transfer queue. It does not unpack the selection before starting
the drag. External Windows consumers can request extracted CF_HDROP content on
demand through the synchronous OLE data interface.

Internal preview modules are selected in Settings > Files. The interactive F3D
viewer is opt-in; no standalone QuickLook/F3D application is needed. Drag rotates,
right/middle-drag pans and the wheel zooms. Arrows rotate, +/- zoom, Home resets,
and F fits the model. Animated scenes expose clip/playback/timeline controls.
The bundled readers select model formats; there is no application model-size
cap, and relative external resources stay beside their model. Reader-specific
limits still apply. Archive previews use an expandable metadata-only file tree.
About > Open settings folder opens the profile in Pathweft. Archive edit
capabilities depend on the actual container/variant. Unsupported/read-only
entries keep their filename readable and show a gray icon with a lock and reason.
See the source's docs/ARCHIVES.md and docs/PREVIEW-MODULES.md for exact limits.

To update an installed copy, close active windows/transfers/editor work first
and run the newer installer over the same directory. Automatic update waits for
the actual requesting process to exit. Nothing is force-killed by image name.
Uninstall restores registrations still owned by this installation and preserves
settings/history and other applications' registrations. If restoration fails,
resolve it in Settings > Windows integration before removing the executable.

The updater reports downloaded bytes, checksum verification and installation
phases in matching themed popups. Installation progress remains visible after
the app exits; closing that popup hides progress without cancelling replacement.
File percentages measure installer file work, not an estimated overall duration.
Video and animated 3D timeline clicks seek immediately during playback and while
paused; dragging and keyboard controls remain available.

For portable use, extract Pathweft/ to a writable directory and launch
Pathweft.exe. Settings live in Data/. Preserve Data/ when replacing the program
files. Disable folder/Win+E/startup integration before moving or deleting a
portable copy. A portable copy has its own application instance and profile.

Settings > Storage > Change location moves settings and runtime data to a chosen
empty non-system folder after the app exits, verifies it, updates the small
Roaming startup pointer and reopens Settings to the result. Finish transfers,
save/close editor tabs and release external archive working copies first.

Initially installed bulk data uses D:\AppData\Pathweft\<user SID>, separate from small
Roaming settings. Archive working copies, folder-size/preview caches and WebView2
runtime data belong there. A data-root.txt file in the settings folder can name
another absolute non-system data directory; unavailable storage is reported
without a system-drive fallback. Close consumers and verify content before
migrating existing data. Legacy external working copies retain their original
paths until released from Archive working copies. Cancelled/failed drag exports
are discarded automatically; accepted external handoffs retain their lifetime.

Pathweft binaries/installer are unsigned. Verify SHA256SUMS.txt against the
published assets. release-manifest.json identifies the source commit and each
packaged file. The normal release contains matching application/dependency
source archives and original notices. Pathweft's combined derived application
is GPL-3.0-or-later; dependencies retain their individual terms. You may replace
compatible shared libraries; no signature restriction prevents relinking.

Video and audio previews use bundled libmpv, FFmpeg and libplacebo from the
source-pinned VLC fork, with the isolated FriBidi 1.0.18 subtitle refresh. GPU playback uses D3D11 and gpu-next; media plays directly
in the preview without an installed player or Windows codec dependency. Side
previews begin paused; Space previews autoplay. Icon controls provide play/pause,
mute and seeking. Shader caches use the selected runtime data location.
Dolby Vision Profiles 5 and 8 are qualified for BT.709/sRGB SDR conversion using
the native metadata and the Nuvio fork's BT.2390 curve. Native Dolby Vision display
signaling and full Profile 7 FEL are not claimed. The corresponding-source asset
includes pinned media sources and build recipes; MPV/ contains original notices.

HTML previews display static HTML/CSS and local images/fonts, including resources
inside archives. JavaScript, online content and downloads are disabled. Switch
off HTML under Settings > Files > Preview modules to see source text instead.
The existing Microsoft Edge WebView2 Runtime is required.

3D GLB/glTF timelines automatically trim held endings when stored curves can be
inspected. Right-click the timeline and uncheck **Trim idle tail** for its complete
original range. The model file is never rewritten.

Source builds also require Visual Studio C++ x86/x64 tools and Windows SDK
10.0.26100.0 for the small native Shell handler. The installed runtime does not
require Visual Studio or a separate Visual C++ redistributable.
