# Pathweft

**Browse. Find. Preview. Move.**

A Windows file manager forked from [File Labs](https://github.com/limburatorul/file-labs),
combining selected search and preview source with native archive operations and
one shared transfer queue. It supports single and split folder views.

## Current state

Version 1.1.3 combines core search, the taskbar widget, internal previews, archive
editing and transfers with optional native folder routing and an alternate
Native appearance. Installer and portable packages include matching source,
dependency notices and checksums through [Pathweft releases](https://github.com/Darkaxt/Pathweft/releases).
Inherited File Labs releases are separate products.

StartAllBack search replacement and the wider Everything/Outlook-preserving
indexing migration remain unfinished later integrations. Pathweft does not change
the Windows Search service or indexing configuration.

## Core workflows

- Pane search and the compact taskbar popup share the existing default Everything
  instance. The pane's single address/search field tries paths first, then searches
  within the folder; This PC searches the full index. Ctrl+Alt+Space opens the
  optional global live-search popup with four matching switches and a plain results
  list. No second indexer starts automatically. Transfers stay in the main manager.
- Eight internal preview modules serve the side pane, Space preview and widget.
  Standalone QuickLook is unnecessary. The native F3D GLB prototype is opt-in.
- Native ZIP/7z/TAR browsing and editing share rename/copy/cut/paste actions.
  Eligible RAR variants support reading and preservation-checked editing through
  the embedded Rust library. Unsupported variants remain explicitly read-only
  with a reason. No installed 7-Zip or WinRAR is required.
- Transfers use the internal engine developed from observed TeraCopy workflows;
  the TeraCopy executable is not a runtime dependency. The shared queue owns
  conflicts, cancellation, verification and progress. Local performance samples
  do not establish a universal speed advantage.
- Ctrl+P switches single/split panes. Ctrl+H toggles Windows-hidden and dot-name
  files. Read-only entries retain readable names and use gray icons with a lock.
- Settings > Appearance offers Original or Native, with Windows light/dark and
  accent preferences or explicit choices. Settings > Windows integration owns
  independent, reversible opt-in folder routing and Win+E.

Folder routing covers qualified normal Shell verbs and shortcuts. Explicit
Explorer launches, virtual Shell locations and common Open/Save dialogs remain
Windows surfaces. Direct `SHOpenFolderAndSelectItems` is unsupported: it can
open the folder without the requested selection and return `E_ABORT`. Pathweft's
own `/select` command supports selection. StartAllBack's Shell/known-folder/library
links remain outside this core release; their native routing belongs to S6.
See [usage](docs/RELEASE-USAGE.md).

## Development and evidence

Windows, PowerShell 7, Git and the .NET SDK selected by `global.json` are required.
The application targets .NET 10; self-contained packaging includes its desktop
runtime. The optional F4 editor requires WebView2 Evergreen.

```powershell
pwsh -NoProfile -File scripts/Build-Development.ps1 -ScratchRoot D:/Temp/pathweft-development-build
```

Register exact generated scratch before creation where practical and clean it
through the host's transactional-cleanup workflow after its last consumer exits.
Use app-owned hidden diagnostics for routine UI verification without activating
the user's windows or synthesizing desktop input.

- [Authoritative specification](docs/SPECIFICATION.md)
- [Stage plan and reconciliation](docs/IMPLEMENTATION-PLAN.md)
- [Development and packaging](docs/DEVELOPMENT.md)
- [S8 delivery contract](docs/S8-DELIVERY.md)
- [Preview modules](docs/PREVIEW-MODULES.md)
- [Archive capabilities](docs/ARCHIVES.md)
- [Transfer evidence and measured limits](docs/verification/2026-10-29-s5.md)

## Provenance and licensing

File Labs was imported at `a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6`;
selected fixes through `9bfbbce` are included. Donor/dependency
revisions and notices remain under `vendor/`.

Inherited source retains its [MIT notice](LICENSE). Imported QuickLook source
makes the combined application [GPL-3.0-or-later](COPYING-GPL-3.0.txt).
Dependencies retain their terms; see [distribution notices](docs/THIRD-PARTY-NOTICES.md).
Final delivery must include matching application/dependency source, checksums
and accurate signing status alongside the installer and portable package.
