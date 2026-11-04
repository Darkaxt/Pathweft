# Context-menu contract (S9)

Authority: MENU-01/02 in SPECIFICATION.md. MENU-03 compact command presentation
was completed in S13. S9 preserves existing action implementations and native
Shell extensions; it reorganizes presentation and adds the requested search-result
containing-folder and Properties access. It does not build a dynamic plugin host.

| Context | Common actions / eligibility | Grouped secondary actions |
| --- | --- | --- |
| Single native file | Open, native Open with, Edit, Preview; Cut/Copy/Paste, Rename/Delete; Properties of that exact file | Transfer (other pane/destinations/group/Drop bin), Copy details (path/name), Archive (ZIP/RAR/extract/installed archive extensions), Tools (run as admin/compare/editor/checksum/mount/shortcut/duplicates), More actions (optional apps/cloud/navigation) |
| Single native folder or folder shortcut | Preserve Open/new-tab/other-pane and paste-into semantics; shortcuts retain their own Properties; no file-only editor/administrator commands | Same transfer/archive/details groups; folder duplicate/pin/app actions remain available |
| Multiple/mixed native entries | Preserve Open on the current result, Cut/Copy/Paste, batch Rename/Delete and multi-file Properties; Preview requires one item; file-only tools keep existing predicates | Each group retains eligible original actions; other-pane actions disabled when one-pane mode has no visible destination |
| Empty folder background | Paste/Undo, creation; native View/Sort; Refresh/Hidden | Select (pattern/invert), Tools (compare/duplicates/terminal), More actions (pin/Explorer/optional apps), Windows menu and folder Properties |
| Writable archive entry/background | Open/Preview for one entry; Copy/Cut/Paste/Rename/Delete according to current native archive capabilities | Extract, Archive tools (password/working copies), show physical archive; native Shell operations do not target synthetic member paths |
| Read-only archive | Read/open/preview/copy/extract remain; mutation actions disabled with the actual Archive.ChangeReason on hover | Same applicable extraction/tools; no false enabled write path |
| This PC / drives | Preserve actual Windows menu and native map/disconnect actions | No replacement or reorganization of third-party/native drive commands |
| Search widget result | Open, Preview, Open containing folder (Ctrl+Shift+Enter), Copy path (Ctrl+Shift+C), Properties (Alt+Enter) for selected physical object | Optional native Windows menu; multiple selection disables single-item preview/reveal, while copy paths/native combined Properties retain their original object identities |

Common file actions stay at the root. Secondary groups have text labels and
familiar14-DIP icons; use the existing Native/Original ContextMenu/MenuItem styles,
palette, keyboard focus and shortcut hints. Retain native Open with/7-Zip/WinRAR/
Drive command nodes and invocation/disposal ownership; the Windows-menu choice
and Settings' native-menu preference remain authoritative. Suppress empty groups,
leading/trailing/doubled separators and duplicate presentation of the same action.
Keep the actual menu within the available work area through WPF/native placement
and scrollable height at normal/narrow screens and100%/150% scaling.

Search-widget actions operate on its own selection, never MainWindow.active's
unrelated selection. Reveal opens the parent in Pathweft and highlights the result,
including folders and the shortcut file itself; keep Ctrl+Enter query handoff.
Properties uses the existing Windows property-sheet API with the main application
window, retaining the selected path rather than resolving a shortcut target.
Windows supplies its own StubWindow32 owner for the native sheet. Successful
dispatch dismisses search, like opening a result; the sheet must survive popup
hide and close normally. Failed dispatch keeps search visible with a row error.
Direct native GW_OWNER equality with the widget is not a product requirement.
Empty selection never dispatches a file operation.
Native combined context menus require one containing folder because their child
PIDLs belong to one IShellFolder; cross-parent results use the custom menu with
that native choice disabled and explained. Combined Properties requires a shared
drive/share. Drive roots have no containing folder to reveal. Keyboard dispatch
uses the same eligibility and normalizes WPF Alt system-key events.

Verification must exercise real menu events and filesystem/clipboard/archive
action boundaries where applicable, native-menu selection/extension inventory,
keyboard access, selection eligibility, theme/placement and lifetime. Diagnostic
dispatch seams alone cannot establish a native dialog was visually inspected.
Final independent-package and installed acceptance belong to S11.
