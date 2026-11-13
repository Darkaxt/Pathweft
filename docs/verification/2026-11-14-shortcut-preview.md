# Shortcut details preview (PREVIEW-39) and catalog-derived package checks

Authority: specification 1.21, PREVIEW-39. Owner request 2026-11-14: ".lnk shows as an hexdump"; the
owner chose a preview over a Target column.

Implementation: Preview/ShortcutInspection.cs (IShellLink and IShellLinkDataList reads, [InternetShortcut]
parsing, target state without contacting network targets, ShortcutViewer with Open and Show target in its
folder), Preview/PreviewModules.cs ("shortcut" module, .lnk and .url, above text and hex),
Preview/PreviewSession.cs.

Gated development stage shortcut-preview-dev-1.1.64 (ticket d58f7f78...):
- windows-debug and packaged-checks (compiled against that debug Pathweft.dll) built with exit 0.
- shortcuts scenario: 18 of 18 passed. A real .lnk with arguments, start folder, maximized run state,
  Ctrl+Alt+F, icon and comment reads back every field and "File found"; a folder shortcut reads "Folder
  found" and omits empty fields; a shortcut to an absent file reads "Not found at this location" and offers
  neither button; a web .url shows its address and icon and only Open; a file: .url reads as that file; the
  preview session selects the Shortcuts module, not Hex and text; Show target in its folder lists the folder
  with the target selected; previewing leaves the shortcut bytes unchanged and releases the file.
- maintenance scenario: 312 of 312 passed, including the new agreement of the Windows tweaks table with the
  shipped catalogs (129 rows, 24 donor, 89 S47) and of the Security check rows with the evaluated rules of
  the shipped list (335). The package checks now use these same catalog lookups instead of fixed counts;
  the stale 418 count is what failed the unpublished 1.1.63 release stage (s47c-release-1.1.63b).

Not covered: a shortcut to a Windows item without a file path (Control Panel) and a network target; the
package-check run itself happens in the 1.1.64 release on the packaged app.
