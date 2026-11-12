# S47 donor tweak inventory and proposed selection (2026-11-13)

Read-only inventory of the 18 projects the owner listed. Each was shallow-cloned and
read; no donor code was run and no setting on this PC changed. Full per-project
reports and row tables (about 6,000 rows) were produced in the task scratch
`E:\Temp\pathweft-s47\reports` and are reproducible from the commits below.
"In Pathweft" figures are name-match heuristics against `MaintenanceGuidance.json`
(77 rules: 40 tweaks, 34 services, 3 apps).

## Projects, licences and reuse route

Pathweft is MIT. MIT/Apache donors may be reused with their notices. Copyleft,
source-available or unlicensed donors are reference-only: values are re-derived
from Microsoft documentation or an MIT donor, never copied.

| Project | Commit | Licence | Route | Rows | Notes |
| --- | --- | --- | --- | --- | --- |
| WinUtil | 2ef5e5dc | MIT | reuse | 139 | Good undo (stores originals); 239 installer entries excluded |
| Win11Debloat | 516fe42 | MIT | reuse | 441 | Undo .reg per tweak, JSON registry backup; 162 apps rated safe/optional/unsafe |
| Sophia Script (Win11 PS5.1) | b9010fc | MIT | reuse | 243 | 121 functions, both states; Defender hardening, DNS over HTTPS |
| SophiApp | a774d3a | MIT | reuse | 111 | Per-setting state readback pattern |
| Bloatynosy | 489a9de3 | MIT | reuse | 109 | App safe/protected classification lists |
| CrapFixer | 58e280ae | MIT | reuse | 115 | 57/61 tweaks duplicate Bloatynosy; remote "extensions" not reusable |
| RemoveWindowsAI | 793e458 (KalebGreek mirror; original deleted) | MIT | reuse | 507 | Recall/Copilot/Click to Do policies; -revert mode |
| UnattendedWinstall | 3414845e | MIT | reuse | 355 | Same author as Winhance; MIT route to most of Winhance's registry set |
| Harden Windows Security | abe25f92 | MIT | reuse | 288 (+extras) | Apply/Verify/Remove per item |
| HardeningKitty | da097607 | MIT | reuse | 418 (main list) | 133 finding lists incl. Microsoft baselines, CIS, STIG; audit mode |
| Bulk Crap Uninstaller | 30da6093 | Apache-2.0 | reuse (keep NOTICE) | capability report | Bundled ObjectListView is GPL: do not copy |
| Winhance | db80ecfc | PolyForm-Shield-1.0.0 | reference | 491 | Non-compete; best Explorer set |
| WinScript | 33d4a6f6 | GPL-3.0 | reference | 124 | No undo; third-party hosts list download |
| privacy.sexy | 7b956f3d | AGPL-3.0 | reference | 920 | 255 rows weaken security ("privacy over security") |
| optimizerDuck | 1ad3285 | GPL-3.0 | reference | 463 | Best undo (saves prior values) |
| RyTuneX | 957885f | AGPL-3.0 | reference | 583 | Policy-leftover detector idea; one mismatched toggle bug |
| AtlasOS | 9e6c206 | GPL-3.0 | reference | 999 | 86 security-weakening rows (Defender removal, SAC, mitigations, VBS) |
| Tiny11 Builder | 38f7ab6b | none (no redistribution) | reference | about 110 | Image-time only; list of removable packages |

## Proposed selection

Every adopted rule keeps the current catalog standard: stable ID, exact target,
Windows build range, primary Microsoft source where one exists plus donor
source, feature loss, current-state readback and restore of the saved original
value. Rules are added per setting, never as a donor preset.

1. **Goal views.** Debloat, Privacy, Performance and Security as the top-level
   organisation over the existing categories (Explorer, Start and taskbar,
   Appearance, AI, Updates). Each rule states its goal and cost; a rule that
   trades security for anything is labelled, not hidden.
2. **AI.** Copilot (policy and taskbar button), Recall (`AllowRecallEnablement`,
   `TurnOffSavingSnapshots`), Click to Do, Settings agent and agent connectors,
   Notepad/Paint/Edge AI policies, AI service autostart (`WSAIFabricSvc`).
   Sources: RemoveWindowsAI, Win11Debloat, Sophia.
3. **Privacy.** Union of telemetry scheduled tasks (Compatibility Appraiser,
   ProgramDataUpdater, Consolidator, UsbCeip, DiskDiagnostic, Autochk proxy,
   MareBackup, Maps); per-capability app-permission denials; inking/typing
   personalisation, online speech, cloud clipboard, app-launch tracking,
   feedback prompts, cloud search; Find My Device as Advanced.
4. **Debloat.** ConsumerFeatures off; Edge policies (Startup Boost, sidebar,
   shopping, first run, default-browser prompt); Widgets; Teams/PushToInstall
   auto-install blocks; Store automatic app archiving.
5. **Explorer** (closest to a file manager): navigation pane items (Home,
   Gallery, OneDrive, duplicate removable drives, This PC folders), full path in
   title bar, checkbox selection, separate Explorer process, hide empty drives,
   folder-type discovery off, context-menu cleanup (Give access to, Include in
   library, Share) and the classic context menu, shortcut arrow/suffix,
   long paths.
6. **Start and taskbar.** Recommended section, Bing/web results in Start,
   Start search WebView, Phone Link pane, clock seconds, multi-monitor taskbar.
7. **Performance** (nothing that weakens security): Delivery Optimization peer
   downloads off, individual visual effects, NTFS last-access updates, Game Mode
   and hardware GPU scheduling, hibernation (Advanced), reserved storage
   (Advanced).
8. **Updates** (Advanced, labelled): exclude drivers from quality updates, no
   automatic restart while signed in, active hours. Never disable updates.
9. **Security, new.** A read-only posture check driven by HardeningKitty's main
   list and Microsoft baseline lists, with Harden Windows Security's Verify
   logic; results grouped by severity with the expected value and source.
   Opt-in hardening toggles with restore: Defender Network Protection, PUA
   detection, Defender sandbox, LSA protection, WPBT, removal of PowerShell 2.0,
   WMIC and VBScript optional features.
10. **Policy leftovers.** Detect policies set by other tweak tools that override
    Windows defaults, with source-attributed explanation and restore (idea from
    RyTuneX, re-implemented).
11. **Windows apps tab with Bulk Crap Uninstaller capabilities.** Today the tab
    removes a fixed list of 24 Store apps for the current user
    (`Maintenance.cs:78-89`). Replace with: full inventory (registry uninstall
    keys incl. HKCU and Wow6432Node, MSI, Store via `PackageManager`, Scoop,
    Chocolatey, Steam, Windows features); safe/optional/never classification
    (Win11Debloat ratings plus Bloatynosy lists); quiet uninstall where the
    installer supports it; bulk uninstall with BCU's ordering and conflict
    rules; leftover scan with confidence scores, registry `.reg` backup and
    Recycle Bin deletion; restore point before bulk work.

## Excluded

Anything that disables or weakens Defender, SmartScreen, Mark-of-the-Web, UAC
or its secure desktop, VBS/HVCI, Smart App Control, exploit mitigations,
BitLocker or Windows Update; IFEO debugger hijacks; forced Edge removal;
downloads of third-party host lists or remote scripts; install-image building
(Tiny11, UnattendedWinstall answer files); app installer catalogs.
