# Pathweft icon

`pathweft-icon.png` is the retained transparent source artwork. Generated with
the built-in image-generation tool on 2026-10-31 from the existing app icon at
the user's request: remove its pink tile, use a Windows-yellow/gold folder with
contrasting brown two-pane list marks, enlarge the folder to native-looking
occupancy, and remove the bottom-right horizontal mark. Three left marks, two
right marks and the central divider remain. No other product assets are copied.

`installer/make-icon.ps1` uses ImageMagick to trim transparent margins, center the
visible folder at 236px in a 256px square and produce native Windows sizes
16/20/24/32/40/48/64/96/128/256 in `app.ico`. The common icon is consumed by the
WPF resource and application executable, native tray extraction, Windows
shortcuts, installer and uninstall entry. The PNG is source artwork, not a runtime
dependency or a local-machine path binding.
