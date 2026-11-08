# S33 Maintenance coverage and table-state verification

Specification 0.90, MAINT-01..05. Source acceptance 2026-11-08; normal release
and independent/installed delivery verification remain required.

- **MAINT-01:** the WPF Aero2 disabled ListBox template supplied an opaque white
  background. Shared application templates now retain the transparent themed
  surface, including the GridView scroll/header path. Actual inline-confirmation
  captures and visual-tree assertions pass dark/light at 100/150 percent, along
  with narrow-layout navigation. Source app 134872 exited normally, result 0.
- **MAINT-02/03:** native disposable Sandbox app 3620, result 0, verifies actual
  HKCU/HKLM32 RunOnce and policy Run raw values, Windows-owned StartupApproved
  disabled-state recognition, exact-handle Startup-file disable/Restore with
  external replacement refusal, and boot/logon task disable/Restore without
  executing or rewriting definitions. Time-only tasks are excluded. Existing
  Run identities/journals remain compatible. Reads preserve independent sources
  when a task/folder is inaccessible; no existing user item was mutated as a test.
- **MAINT-04:** source app 134872 reads the actual installed SCM inventory beyond
  ten entries, with service-name/startup-type columns. Native guest fixture outside
  the donor shortlist is enumerated and its true startup/delayed configuration is
  disabled/restored without starting it. Kernel drivers are outside this inventory.
- **MAINT-05:** actual toolbar clicks cover files → Maintenance → Performance →
  files and selected Performance analysis → Maintenance → files. Visual and
  accessibility active states match the actual view; browser selection/path and
  natural close are preserved. The exact asynchronous transition task is awaited.
- App-host self-test and full Performance scenario60844 pass on the current
  production sources, including actual layouts, range/history and natural close.
  The independent package consumer compiles against the changed application.
  Final package
  checks exercise the delivered Startup/Services columns and exclusive mode routing.

Native workflow ticket `8288d6d1239b44478996869c95db6843` used a 4096-MiB,
network-disabled headless guest, explicitly mapped app/runtime and task-owned
scratch. `sandbox.powershell` returned native ExitCode 0; workflow.end confirmed
released/ok. No host desktop/clipboard or live installed configuration was used.
Private outcomes and rendered evidence are under
`.local/evidence/2026-11-08-s33`.

The earlier native failure exposed an unterminated Win32 absolute-rename buffer:
the OS accepted a destination with appended heap characters. The corrected
NUL-terminated buffer passes exact file-name/content/identity restoration. A
subsequent journal-count failure came from a previous guest's failed fixture
journal in reused host scratch; the verifier now binds a new journal root to
each native invocation. Both failure receipts remain evidence, not waived checks.

Donor investigation requested during S33 is recorded separately in
[MAINTENANCE-DONOR-REVIEW.md](../MAINTENANCE-DONOR-REVIEW.md). It supplies proposed
recommendation metadata and reuse/licensing boundaries, not implemented safety
badges or new tweaks. Seventeen exact pinned clones were inspected; the named
RemoveWindowsAI repository could not be cloned (metadata and Git returned 404).
Spent clones were transactionally removed after retaining pins and findings.

The first unpublished 1.1.35 package failed its app-host self-test because the
in-app changelog still began with 1.1.34 after the version bump. The required
1.1.35 entry was added; the assertion remains unchanged. That package was not
published. Its failure log is retained in the private release evidence; the
corrected committed package passed before publication: final app211320 and
independently downloaded app230548 both returned 0 with no failed assertions.
Normal1.1.35/2f62564 is published; all six assets and the remote source/tag match.

Reconciliation: source and independent delivered-package behavior are verified as
above. S33 remains ACTIVE until installed readback after the user's normal update.
No tracked deferral or required source refactor is identified.
