# Pathweft 1.1.1

Windows 11 x64 is the qualified host. The .NET 10 desktop runtime and native
archive/preview libraries are included. WebView2 Evergreen is required for the
optional F4 editor; the application reports when that runtime is unavailable.

The installer runs for the current user without administrator privileges.
Folder routing is unchecked during installation. Settings > Windows integration
owns folder routing, its restoration action, independent Win+E, and taskbar
search. Explicit Explorer launches, virtual Shell locations and Open/Save
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

Search shares the existing default Everything instance; Pathweft does not start
a second index, change its service or alter its settings. The unchanged pinned
Everything 1.4.1.1032 portable engine is included in Everything/. If there is no
existing instance, configure/start that engine explicitly using its own UI and
license; indexing privileges and indexed folders remain an explicit choice.
The single address/search field resolves existing paths first on Enter, then
searches Everything in the current folder. Search from This PC for all indexed
files. Four inline switches control case, whole words, path matching and Regex.
There is no separate filesystem-scan mode or second search row.

Internal preview modules are selected in Settings > Files. The F3D GLB prototype
is opt-in; no standalone QuickLook/F3D application is needed. Archive edit
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
