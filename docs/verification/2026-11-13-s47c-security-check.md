# Security check and opt-in hardening (S47c), memory growth (MONITOR-22)

Authority: specification 1.20, MAINT-40..45 and MONITOR-22. Plan: IMPLEMENTATION-PLAN S47 row.

Implementation: MaintenanceSecurity.cs (read-only evaluation of the pinned
HardeningKitty main machine list: registry values and lists, service start type,
optional feature state, scheduled task state, Microsoft Defender status and
preferences including attack surface reduction rules, BitLocker volume status;
operators and list defaults as in HardeningKitty; grouping by severity),
MaintenanceFeatures.cs (optional features through the DISM API, capabilities and
state reads), MaintenanceS47Tweaks.cs (eight Security-goal tweaks with typed
components), MaintenancePanel.cs (Security check category, card, details with
"Open in Windows tweaks"), MaintenanceWorker.cs (security-check never requests the
administrator session). Provenance: vendor/HardeningKitty (MIT, pinned list and
manifest) and vendor/HardenWindowsSecurity (MIT, cited sources), both checked by
Verify-ReleaseSources and shipped with their notices. Memory growth:
PerformanceInvestigation.cs (Growth, spike attribution), PerformanceHistory.cs
(spike peak time), PerformanceTimeline.cs (caution markers, investigate),
PerformanceAnalysis.cs (selecting an alert row).

Host scenarios (gated debug builds):
- s47c-debug-5, Maintenance scenario: 311 of 311 passed. List hash, 418 rows with
  335 evaluated and 83 not checked (auditpol 26, firewall rule 23, process
  mitigation 14, accesschk 7, CIM instance 7, account policy 3, secedit 2,
  bcdedit 1), Compare/Evaluate operator and default fixtures, grouping order, no
  writes from the check, columns, card text, PUA link to its tweak, no Apply or
  Restore in the category, six cards, group-aware sort; captures of the category in
  dark and light at 1.0 and 1.5 and narrow width.
- s47c-debug-6, performance-analysis scenario: 130 of 130 passed, including
  MONITOR-22: a steady 1 GB/min grower with a 2 GB working-set trim is one event
  from where its level period ends to its latest high; a 3-minute start-up burst
  is not flagged; growth below 15% of installed memory is not flagged; the
  unsaved recent snapshots are used; a RAM spike sits at its peak and names
  "largest leaky 28.8 GB · leaky +2 GB"; growth events appear only for RAM.
- Detector calibration against the owner's real 24-hour history (read-only): one
  event, qemu-system-x86_64 PID 207496 from 17:49 to 18:12, 6.9 GB to 30.1 GB at
  1.01 GB/min; emulator boots, Java and Rust builds and a game were not flagged.

Guest qualification (disposable Windows Sandbox, no network, 4 GiB, Windows build
26100, gated debug build s47c-debug-7, --verify-maintenance-hardening): result 0,
no failed check.
- Network protection, PUA blocking, Defender sandbox, LSA protection (RunAsPPL=2)
  and WPBT: native worker Apply, every target at the requested kind and value,
  exact saved originals, the list reads Applied, Restore, registry values back
  exactly. These five rules are marked qualified for build 26100.
- PowerShell 2.0, WMIC and VBScript are absent from this guest: each reads "Not
  present on this Windows" or "Not installed" and cannot be selected. Their Apply
  and Restore were not exercised and they are not marked qualified; on a PC that
  has them, Restore of a removed capability needs Windows Update.
- Typed feature action: SearchEngine-Client-Package disabled keeping its payload
  and re-enabled through the servicing API, no restart requested; an unknown
  feature name is left out.
- Superseded run (s47c-debug-6 guest): the verifier journaled to a private file
  the list does not read, so the three policy-only tweaks read "Set outside
  Pathweft" (MAINT-29) instead of Applied. The verifier now uses the guest
  profile's own journal, refusing to run unless it is under the guest work folder;
  the product was unchanged between the two runs.

Not covered: removal of PowerShell 2.0, WMIC and VBScript on a Windows that has
them; capability Restore with network.
