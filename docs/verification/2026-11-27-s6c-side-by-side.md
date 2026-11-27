# S6c side-by-side check - 2026-11-27

Stage S6c (SAB-01 settings inventory, SAB-07 layout and contents). The owner allowed the comparison on his own
laptop with a rendered screenshot (2026-11-27: "For the side by side comparation, you can just do it in my own
laptop. You just need to take a rendered screenshot and don't need to take much longer"). This replaced the
Windows Sandbox comparison, which was blocked on llm-coordinator#58.

## Method

- StartAllBack 3.9.28's menu, opened by the taskbar's own SC_TASKLIST, rendered with PrintWindow and closed again
  (the owner's settings unchanged).
- Pathweft's menu from the debug build of 42ad45c (`--render-start-menu=`, DEBUG only): the same settings,
  rendered offscreen with RenderTargetBitmap, without a window and before the single-instance check, so the running
  Pathweft was not touched. Builds: tickets 25b2f89c (render hook, 17c68d3) and a7b8fd45 (fixes, 42ad45c), both
  exit 0; renders through the pipeline gate, exit 0.
- Screenshots stay in local scratch (they show the owner's name and programs).

## Settings read (the owner's)

Icon size M (24 px), Folders first, glyph icons on, user picture on, power button default action Hibernate
(Start_PowerButtonAction 0x40), 5 recent programs, the right column's items as StartAllBack shows them.

## First render: differences found and fixed (42ad45c)

| Difference | Fix |
| --- | --- |
| Pathweft showed every shortcut in User Pinned\StartMenu (12) in partly name order; StartAllBack showed 10 in its own order | Pins are those in Windows' pin list (StartPage2 Favorites), in its order; Pin and Unpin go through Windows' Start Menu Pin handler (startpin, startunpin), as StartAllBack does |
| A custom folder missing | StartAllBack's custom folders (StartPage2 CustomFolders) after the known folders |
| Computer, PC settings, Recent items | This PC, Settings, Recent Items; Recent Items grouped with This PC |
| Right-column rows 24 px apart | 35 px, as StartAllBack's |
| The picture took a block at the column's top | It hangs over the menu's top edge, centred on the right column |
| Power button text only, its arrow far right | Power glyph and action, the arrow beside it |
| No search glyph | Magnifier in the search box |
| 685 px tall against 568 | Height follows the content (584 px under the picture's overhang) |

## Second render: matches

Same pinned programs in the same order, same right column (items, names, grouping, glyphs, custom folder with
StartAllBack's wrench glyph), picture placement, "Hibernate" with its glyph, icon sizes and heights within a few pixels.

## Remaining differences (issue #15)

Recent programs (StartAllBack's ranking source not found yet), which programs show a jump-list arrow, and the All
Programs highlight for newly installed programs. Visual style differences (colours, glass, the programs list's
border) belong to S6d.

## Not verified here

A change in StartAllBack's configuration reaching an open Pathweft menu (StartMenuSettingsWatcher): it needs a
setting changed in the owner's StartAllBack, which this check did not do. It is part of the owner's trial (S6e).
