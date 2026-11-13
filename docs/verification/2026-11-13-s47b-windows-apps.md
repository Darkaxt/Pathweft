# Windows apps with Bulk Crap Uninstaller capabilities (S47b)

Authority: specification 1.18, MAINT-32..39. Plan: IMPLEMENTATION-PLAN S47 row.

Implementation: MaintenanceApps.cs (inventory from the machine 64/32-bit and
current-user uninstall keys, current-user main Store packages, Scoop and
Chocolatey; folding of nameless, SystemComponent, patch and parent-bound entries;
protection; uninstall by the entry's own command through ShellExecute, msiexec by
product code, Store removal for the current user only; descendant wait; re-read
for Removed / Still installed / Cancelled / Not started; installer mutex guard;
BCU bulk order), MaintenanceLeftovers.cs (folder, shortcut, Software key and
startup scanners with Likely / Possible / Unlikely confidence; Recycle Bin
removal; .reg backup and Restore; administrator items through the worker's
remove-leftovers, which re-scans and removes only requested items it finds
again), MaintenanceAppWindows.cs (confirmation naming every app with the
restore-point option, leftovers window). Store app guidance: 83 rules by exact
package name from Win11Debloat Config/Apps.json and Bloatynosy Data/AppRules.ini
(241 catalog rules, reviewed 2026-11-13); desktop programs are never rated.
Provenance: vendor/BulkCrapUninstaller (Apache-2.0 licence, NOTICE, 19 cited
files with SHA-256 at 30da609), checked by Verify-ReleaseSources; the NOTICE and
manifest ship with the application.

Host scenario (gated debug builds s47b-apps-debug-1 and -2): Maintenance scenario
271 of 271 passed both times. It covers fixture registry parsing and folding,
MSI/Inno quiet switches, protection, bulk order, command splitting, guidance
matching, leftover confidences (scan only), the apps table headers, Extended
selection, protected rows, the confirmation naming apps in bulk order and decline
changing nothing. The dark/light 1.0/1.5 and narrow captures of Windows apps were
inspected (grouped by source; card "17 optional · 452 installed apps"). No
uninstall or leftover removal ran on the host.

Guest qualification (disposable Windows Sandbox, no network, 4 GiB, gated debug
build s47b-apps-debug-5, --verify-maintenance-apps): 13 checks, none failed,
result 0.
- A detached-child uninstaller (Inno/NSIS style) is waited for and confirmed
  Removed after 4.0 s.
- An interactive uninstaller that leaves its entry reports Still installed; the
  declared quiet command removes it.
- Exit code 1602 reports Cancelled.
- A Windows Installer uninstall reports Not started while the installer mutex
  exists.
- The leftover scan finds every fixture item with its expected confidence.
- Likely registry leftovers are removed after a .reg backup and Possible ones
  stay.
- The worker removes the Likely folders and the dead shortcut into the Recycle
  Bin and leaves Possible and unrelated folders.
- Restore imports the backup with exact value kinds.
- A malformed request and the retired remove-app action are refused.
- The Store package Microsoft.MicrosoftEdge.Stable was removed for the current
  user and is no longer listed.

Restore point: not qualified. Sandbox has System Restore disabled ("the service
cannot be started because it is disabled"), so the result is recorded only.

Guest runs before the passing one, kept for the record:
1. The first run stalled until the 1800 s exec limit with no results. The
   fixture's uninstaller used `start` on a .cmd, which keeps that window open
   (cmd /K), so the descendant wait never ended.
2. A rerun with incremental results and 3-minute step bounds confirmed the stall
   at "uninstall plain".
3. With `cmd /c`, the next run passed everything except Recycle Bin removal
   (error 120). The fixture was on the host-redirected C:\Coordinator\Work
   folder, which has no Recycle Bin. The fixture moved to guest-local disk.

These were test fixture faults; the product code did not change between those
runs.

Notes:
- Microsoft.MicrosoftEdge.Stable has no donor rating; Bloatynosy rates only the
  legacy Microsoft.MicrosoftEdge package. It therefore shows Not assessed and can
  be removed for the current user, as Windows allows.
- The elevated remove-leftovers trusts the authenticated client's app record for
  its scan roots. It is bounded by the worker authentication and the exclusion
  rules (Windows, other apps' locations, folders in use by registered programs).
