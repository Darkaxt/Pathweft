# Pathweft

**Browse. Find. Preview. Move.**

A Windows file-manager project forked from [File Labs](https://github.com/limburatorul/file-labs), bringing selected search, taskbar, preview and transfer capabilities into one maintained product.

## Current state

**S1-S4 are complete: isolated pane search, the connected taskbar workflow, internal selectable previews and native archive browsing/editing are verified in the development branch.**

Pathweft has independent executable, profile, IPC, startup, shell, installer and update identities. Pane search reuses the existing default Everything instance, with explicit folder/all-index scopes and a separate live-filesystem mode. Current verified stages start no duplicate indexer and leave Windows Search unchanged. Installer/update execution remains an S8 acceptance boundary; do not treat this development branch as an installable release.

Starting snapshot: `a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6` (upstream development version 1.0.62; latest published release observed at setup: v1.0.61). See [S1 evidence](docs/verification/2026-10-28-s1.md) and [S2 evidence](docs/verification/2026-10-29-s2.md). Inherited upstream releases are not Pathweft releases.

Enable taskbar search in Settings → Windows integration. Ctrl+Alt+Space opens a separate File Labs-themed search popup while Pathweft runs. It hands query/scope/sort into a pane and shares reveal, open, copy/move and the transfer queue. Settings reports hotkey mode when the taskbar cannot expose safe space for a search box; hotkey mode was verified across a real Explorer restart and mixed-DPI monitors.

## Direction

- Reuse selected EverythingToolbar search clients, controls and taskbar integration.
- Share one deliberately selected Everything index across panes and widget. The engine remains an external binary/API dependency.
- Port selected QuickLook viewer and add-on source into internal modules, controlled by persisted enable/disable settings. Standalone QuickLook is not required.
- Share file actions and one transfer queue across all surfaces.
- Use **REA analysis of TeraCopy** and measured workflows to guide transfer development. Upstream changes are baseline context, not the transfer design authority.
- Preserve responsive browsing and an uncluttered interface.

S3 integrates eight selectable preview modules across the side pane, Space and widget, including an opt-in native F3D GLB prototype with camera controls. See the [module inventory](docs/PREVIEW-MODULES.md) and [S3 evidence](docs/verification/2026-10-29-s3.md). S4 adds [native archive browsing/editing](docs/ARCHIVES.md): verified ZIP/7z/TAR edits, read-only RAR5/CAB reading, entry previews and one transfer owner. See [S4 evidence](docs/verification/2026-10-29-s4.md). Next: S5 REA-driven transfers, S6 native StartAllBack search, S7 all-Everything search migration/Windows Search retirement, and S8 final delivery. S6/S7 feasibility is unresolved: native host hooks and content/property/mail consumers require actual evidence before Windows Search can be disabled.

## Documents

- [Authoritative specification](docs/SPECIFICATION.md)
- [Stage plan and reconciliation](docs/IMPLEMENTATION-PLAN.md)
- [Development environment](docs/DEVELOPMENT.md)
- [Setup verification](docs/verification/2026-10-28-setup.md)

## Development build

Windows, PowerShell 7, Git and the .NET SDK selected by `global.json` are required.

```powershell
pwsh -NoProfile -File scripts/Build-Development.ps1 -ScratchRoot D:/Temp/pathweft-development-build
```

Register exact generated scratch before creation where practical; clean it through the host's transactional-cleanup workflow after its last consumer exits. See the development notes for storage and runtime boundaries.

## Upstream and licensing

The inherited File Labs source retains its original [MIT notice](LICENSE). Selected EverythingToolbar client code and the official native Everything SDK retain their [MIT notices and pins](vendor/EverythingSDK/NOTICES.txt). Imported QuickLook-derived source retains its [GPL-3.0-or-later materials](COPYING-GPL-3.0.txt); the combined application must not be described as MIT-only. Native archive integration preserves the separate [7-Zip](vendor/SevenZip/NOTICES.txt) and [SharpSevenZip](vendor/SharpSevenZip/NOTICES.txt) terms and matching-source delivery requirements.

**No Pathweft application release exists yet.**
