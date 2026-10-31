# Pathweft 1.1.6

Windows 11 x64 is the qualified host. The .NET 10 desktop runtime and native
archive/preview libraries are included. WebView2 Evergreen is required for the
optional F4 editor; the application reports when that runtime is unavailable.

The installer runs for the current user without administrator privileges.
Folder routing is unchecked during installation. Settings > Windows integration
owns folder routing, its restoration action, independent Win+E, and taskbar
search. Navigation within an existing Explorer window stays in Explorer.
The native 32/64-bit folder handler preserves a calling Shell browser's site;
external filesystem requests launch Pathweft. Settings refreshes the current
handler ownership when activated. An update migrates only an intact old opt-in.
Explicit Explorer launches, virtual Shell locations and Open/Save
dialogs remain in Windows. StartAllBack's Shell/known-folder/library links are
not covered by this release's filesystem registration; their native integration
belongs to the postponed StartAllBack stage. Direct SHOpenFolderAndSelectItems is unsupported:
with folder routing enabled it can open a Pathweft folder, lose the requested
selection and return E_ABORT to its caller. Some applications' Show in folder
actions use that API; do not enable folder routing if those actions are essential.
Folder-plus-selection requests sent to Pathweft's /select command are supported.
StartAllBack's grouped Everything results and the wider Windows Search migration
are later unfinished integrations; this release does not change Windows Search.

Settings > Appearance selects Original or Native. Native follows Windows
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
PDF and 3D previews use icon actions with tooltips. The animation selector
follows the theme, and models composite directly onto the preview surface.
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

For portable use, extract Pathweft/ to a writable directory and launch
Pathweft.exe. Settings live in Data/. Preserve Data/ when replacing the program
files. Disable folder/Win+E/startup integration before moving or deleting a
portable copy. A portable copy has its own application instance and profile.

Pathweft binaries/installer are unsigned. Verify SHA256SUMS.txt against the
published assets. release-manifest.json identifies the source commit and each
packaged file. The normal release contains matching application/dependency
source archives and original notices. Pathweft's combined derived application
is GPL-3.0-or-later; dependencies retain their individual terms. You may replace
compatible shared libraries; no signature restriction prevents relinking.

Source builds also require Visual Studio C++ x86/x64 tools and Windows SDK
10.0.26100.0 for the small native Shell handler. The installed runtime does not
require Visual Studio or a separate Visual C++ redistributable.
