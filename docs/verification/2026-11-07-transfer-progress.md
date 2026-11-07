# Transfer progress presentation — S24 / TRANSFER-17

The user requested removal of the unexplained track, aligned action icons and
labels, completed/total counters around the main bar, processed bytes beside
speed and no routine verification counters.

Root cause: the template always rendered a secondary CurrentFraction bar;
concurrent copying intentionally returns zero for that fraction. Fluent glyph
and Segoe text Runs also shared a font baseline rather than separate centers.

The existing template now has one overall bar, opposing counters above it,
active/current file context beside centered actions, and outcome/error details.
The aggregate header displays processed bytes at right; multiple active jobs
retain their separate legend. Internal verification accounting/engine, native
clipboard, transfer resolution and completion/cancel lifecycle are unchanged.

Actual hidden WPF workflow114480 passes dark/light,100/150%,640/460-DIP layouts,
counter positions, one bar, button centers/accessibility, Pause/Resume, multiple
jobs and errors. Inspected dark normal and light scaled/minimum snapshots retain
legible controls/counters and compact content-sized layout. The same workflow
passes real copy/move, queued jobs, hide/reopen, cancellation after actual worker
exit, partial/failure retention and virtualized conflicts. Workflow111652 also
passes real full-set conflict resolutions with differences-first metadata review.
No user desktop input or native clipboard mutation was used. Existing clipboard
preservation logic is unchanged; no new physical clipboard claim is made.

Source receipts and rendered snapshots are retained privately under
`.local/evidence/2026-11-07-transfer-progress`. The initial isolated trial refused
the new scratch root before execution; both Debug-only ownership allowlists now
include this exact task root. Production contains no hidden trial hooks.

Closure assessment: the redundant secondary UI path is removed; existing state
and transfer owners are retained. No new framework, optional refactor, blocker
or tracked deferral is introduced. Final1.1.27 package, independent retrieval and
applicable installed acceptance remain pending; S24 stays ACTIVE.
