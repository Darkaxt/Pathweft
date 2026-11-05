# Native Pinned/Frequent category correction

2026-11-05. S11 source/integration acceptance passes for the corrected category
publisher. Combined package/release/installed qualification belongs to S15 for
the current S11/S14/S15 outcome; this is not a delivered-version claim.

The1.1.18 publisher built custom Quick access/Recent folders groups and
Home/Downloads tasks. A new regression test against the unchanged publisher
fails at that exact category contract (native process133840), reproducing the
user's screenshot discrepancy before changing production behavior.

The correction requests Windows' own Frequent category with no custom groups
or folder tasks. Actual visits still record argument-bearing Pathweft links
under the stable AppID. Windows owns ranking, pinning and removed destinations.
Menu reconstruction no longer replays local history/Quick access or maintains a
second removal file; existing user profile files/pins are left untouched.

## Inspected actual Windows workflows

Hidden native process126032 completed with result0. Its original process exit
was observed by the existing application-owned verification launcher. A fresh
explicit diagnostic profile yields a separate AppID; no installed menu is edited.

- The actual WPF publisher requests Frequent and publishes no custom items.
  Independent `IApplicationDocumentLists::GetList(ADLT_FREQUENT)` readback
  contains the visited Unicode folder link, targeting this executable with its
  argument. This is native data readback, not a renamed custom heading.
- Real native removal of the stored destination clears Recent and Frequent.
  Menu refresh and publisher reconstruction do not recreate it from saved
  history. A later real folder visit records new usage.
- Privacy-disabled mode omits usage categories and records no new visit;
  Windows preferences are unchanged. Installed Recent/Frequent data before/after
  match. No DeleteList/RemoveAllDestinations is used during normal refresh.
- Actual cold/running-instance launches, root/Unicode/UNC argument parsing,
  same-folder refresh suppression, virtual/archive eligibility, unavailable-path
  feedback, exact test-AppID uninstall cleanup and absence of foreground
  activation remain verified.
- The user separately reports pinning a real folder in installed1.1.18 and
  confirming persistence across app restart. This is user-reported native UI
  evidence. Native removal/reconstruction is already covered above; further
  repetitive manual pin/unpin checks are unnecessary. Installed identity and
  preservation through the combined update remain S15 delivery acceptance.

One test diagnostic initially received0x80070490 when asking Shell to remove a
newly synthesized link. It now passes the exact COM destination returned by
Windows, as the API contract requires; no production behavior was changed to
mask that observer failure. The corrected test then passed actual removal.

References: [WPF JumpList](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.jumplist),
[native Frequent readback](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationdocumentlists-getlist),
[native removal](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationdestinations-removedestination).

Private receipts are retained under `.local/evidence/2026-11-04-specialists`;
they include original process identities, results and Recent/Frequent readback.
Current build source remains uncommitted pending the complete authorized round.
No intermediate product release or running-app/registry change occurred.

Closure assessment: removing the custom category replay also removes its
duplicate removal-state owner. No further consolidation is required for S14.
