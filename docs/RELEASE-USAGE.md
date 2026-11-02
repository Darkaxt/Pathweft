# Pathweft 1.1.17

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
drive list and sidebar use your accent below80% used, amber at80% and red at90%.
Unavailable drives and empty card-reader slots stay hidden until media is ready.
Cloud mounts identified through provider registration show Cloud and their
provider; their raw reported filesystem is available in the tooltip.
Google Drive account quotas come from the desktop client's read-only local
metadata, bound through its account-to-mount registration. They use GiB/TiB to
match the client's binary byte counts without labelling them as decimal GB/TB.
This PC, sidebar, folder header and capacity card use those cloud figures.
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
