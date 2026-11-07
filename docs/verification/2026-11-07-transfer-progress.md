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

Initial hidden WPF workflow114480 passes dark/light,100/150%,640/460-DIP layouts,
counter positions, one bar, button text-box centers/accessibility, Pause/Resume, multiple
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
or tracked deferral is introduced. Subsequent user refinements remove the
redundant "processed"/"files" labels and identify optical misalignment that the
first text-box check did not establish. The transfer action label now centers
native text drawing by actual glyph/label outlines. Final source workflow45728
repeats the complete rendered/lifecycle qualification and checks actual raster
ink centers within one physical pixel for Pause/Cancel at every layout, rather
than comparing text-box centers. Updated dark normal and light minimum/scaled
renders are inspected; counters read "480 completed" / "1,635 total" and the
byte amount is unlabelled. The production consumer then exposes WPF's unbound
first-measure state: Rect.Empty has negative-infinite width, unlike the populated
label used in the shown-window source trial. Absent glyphs now occupy zero width
and no gap; an explicit initial-measure regression supplements the actual
compiled-template consumer. Corrected production qualification107156 passes;
its explicitly unreleased source candidate is diagnostic evidence, not release
acceptance. Superseded unpublished packages are cleaned with their original
receipts preserved; no old assets were published.

Normal1.1.27 is published from6b294cf8263ad04b1f41a4928d0ed3b3dd8d0903.
Final packaged consumer99016 and independently downloaded consumer85284 pass
the actual application checks, including unbound first measurement, rasterized
Pause/Cancel alignment, shorter labels and real Pause behavior. Corresponding
source/dependency/license checks pass; all six independently retrieved assets
match the published sizes and SHA-256 digests. The existing unsigned release
policy is unchanged. Actual dark/light,100/150%,normal/minimum-width source
renders and full transfer/conflict workflows45728/113412 pass. Evidence applies
to the delivered implementation; no dependency versions or transfer engine were
changed. Build, source staging, both verification copies, independently retrieved
assets and task scratch/pointers are transactionally cleaned after evidence
retention; all final cleanup receipts report applied with no errors. Required
release assets and diagnostic/acceptance evidence remain available.

Applicable installed acceptance remains pending the user's built-in update;
S24 stays ACTIVE and S18 stays NOT STARTED. This is the only unmet delivery
criterion; no external blocker or tracked deferral is introduced.
