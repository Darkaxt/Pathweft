# Explorer icon reference

Recovered2026-11-04 from the installed Windows Explorer assets, without running
or modifying Explorer. The user subsequently requested compact MENU-03 integration
in S13. S9's broader context-menu reorganization is now BLOCKED on native
Properties qualification; its source context commands also consume the recovered
Cut/Paste artwork through the same reader.

The exact SVG source directory is:

`C:/Windows/SystemApps/MicrosoftWindows.Client.FileExp_cw5n1h2txyewy/FileExplorerExtensions/Assets/images`

Local recovered copies, per-file SHA-256 and source paths, rendered contact sheets
and authenticated REA resource evidence are retained in
`.local/evidence/2026-11-04-specialists/explorer-icons`.
`svg-manifest.json` binds each labelled asset and theme to its source. The
extraction and rendering scripts alongside that folder reproduce the result.
Copies are unmodified. No Windows font, system binary or SVG has been added to
the product package. ExplorerIcons.cs reads a finite set of installed system SVGs
as data, caches their frozen WPF vector drawings per theme and uses existing
system font glyphs when those Win11 files are absent. The input is restricted to
the fixed Windows directory, XML DTD/external resolution is disabled and the
reader accepts only the required path-based assets. No extra rendering runtime
or font dependency is packaged. The bottom row keeps16-DIP art in30x24-DIP
buttons; pane View/Sort use14-DIP art in28x22-DIP buttons. Names, shortcut hints
and current view/sort remain in tooltips and accessible properties.

| UI control | Exact SVG filename |
| --- | --- |
| New | windows.newitem.svg |
| Cut / Copy / Paste / Rename | windows.cut.svg / windows.copy.svg / windows.paste.svg / windows.rename.svg |
| Share / Delete | Windows.ModernShare.svg / windows.ribbondelete.svg |
| Sort | SortBy.svg |
| View button in the supplied screenshot | windows.iconsize.details.svg |
| Extra large / Large / Medium icons | windows.iconsize.extralarge.svg / windows.iconsize.large.svg / windows.iconsize.medium.svg |
| Small icons | windows.iconsize.smallicon.svg |
| List / Details / Tiles / Content | windows.iconsize.list.svg / windows.iconsize.details.svg / windows.iconsize.tile.svg / windows.iconsize.content.svg |
| Details pane | windows.previewpane.svg |
| Preview pane | windows.readingpane.svg |
| New folder / Hidden items / Extensions / Terminal | newfolder.svg / windows.showhiddenfiles.svg / windows.showfileextensions.svg / windows.powershell.run.svg |

The user subsequently selected the terminal-window `>_` icon for the actual
Terminal command. It uses Segoe Fluent Icons U+E756 instead of the recovered
PowerShell folder/play asset; the latter remains only in the reference inventory.

Native filenames do not always match the visible role: `View.svg` contains four
squares, whereas the reference View button has horizontal lines. Likewise the
pane filenames are counterintuitive; the mapping above follows inspected artwork
and the user's screenshots, rather than inferring it from filenames.

All selected SVGs have dark, light, contrast-black and contrast-white variants.
Normal variants include the original blue accent paths. Structural controls use
the installed Segoe Fluent Icons font: More U+E712, dropdown U+E70D, submenu/Show
U+E76C, selected dot U+E915. Local font1.54 was inspected and rendered; its file
hash is retained. Microsoft's glyph reference is
https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font.

Verification: every selected SVG parses, each recovered copy hashes identically
to its system source, and all four variant contact sheets render. Dark/light
and high-contrast contact sheets were visually inspected against the supplied
toolbar and View-menu references. This verifies recovered artwork, not product
integration by itself. Subsequent integrated source workflows PID120064 and39948
verify the compact grouped actions, real Copy/Move, supported View/Sort commands,
hover/accessibility/state, narrow layouts and actual96/144DPI transitions. PID39948
also verifies sidebar utilities, About and child-toggle spacing. Final release
delivery remains assigned to S11.
