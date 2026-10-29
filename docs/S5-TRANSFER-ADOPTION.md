# S5 transfer adoption contract

Authority: SPECIFICATION.md TRANSFER-01..07. Initial acceptance passed at e478986;
the user's continued comparison/implementation goal reopens S5 as ACTIVE. See
the [verification record](verification/2026-10-29-s5.md), including the subsequent
operation-count reductions and unresolved pipeline findings.
This document selects implementation details within that scope;
it does not replace acceptance or authorize a later stage.

## Current user-directed behavior

The user explicitly chose matching the observed TeraCopy workflow on 2026-10-29
and clarified cancellation cleanup. Direct writes replace staged overwrites;
ordinary native source sharing replaces locks held across verification. Cancel
removes the current incomplete/unverified output, preserves sources and leaves
completed files. It does not restore an overwritten destination's old contents.
Source pinning across verification and staged overwrites were our prototype
additions, not recovered TeraCopy internals, and are not the current design.
Potential future protections are
optional work requiring a demonstrated need, not current required deferrals.

Observed reference source opens use GENERIC_READ with share READ|WRITE|DELETE;
destination writes use CREATE_ALWAYS directly at the final path. Verification
and source-preserving move completion, actual queue/conflict/pause/cancel behavior,
metadata/streams and accurate outcomes remain required. CopyFileEx and a bounded
hash-during-copy path are Pathweft's own implementation choices; the packed donor's
internal algorithm was not recovered. No TeraCopy binary becomes a dependency.

## Identified reference and evidence

Reference: TeraCopy **4.0.5.0 x64**, Code Sector Pty Ltd., valid Authenticode.
The installed signed executable and the independently extracted official
[4.0.5 package](https://blog.codesector.com/2026/10/08/teracopy-4-0-5/) application
match SHA-256 `1f0cca0be4e6a22e856c3b0de6655a1af8059a06cc66133398307de335098c6f`.
Installer SHA-256: `22f050b51c80a344cbdaf368c7f6a9266efca4d95b5109d606466906a421ff7e`.
The generic official portable ZIP is a different 4.0.3.2 artifact, excluded from
the behavioral/performance reference. No donor binaries become a Pathweft runtime
dependency or public release asset.

Tests use an independent portable profile and disposable data. Actual transfer
settings, confirmed after the application's initial configuration generation:
`[Transfer] Threads=2, Buffers=10, BufferSize=1048576, HashName=xxHash3-64,
TestAlways=1, TestSkippedFiles=0, SyncMethodOnly=1, UseWriteCache=1, UseVSS=0`.
Settings placed in the initial `[Main]` section were ignored for transfer options;
the actual generated configuration and checked Verify control supersede that
preliminary assumption. The user's settings, history and activation were not
copied. Windows blocked copying a separately installed module; that operation
was not retried and protection/activation were not changed. Official bytes omit
that module. The installer was run only with its documented extraction switch;
no MSI installation or shell registration occurred.

REA used the installed Coordinator gate, Ghidra 12.1.4 and the pinned Frida
17.23.2 dependency. The missing Frida N-API binding was repaired through its
existing prebuild installer, without changing REA version or registration.
Complete private bundles are under `.local/research/teracopy-4.0.5/`; exports
were copied exclusively, hash-compared and authenticated by REA import. Native
analysis and initial runtime consumers closed before spent acquisition cleanup;
subsequent benchmark/profile workflows also closed after retaining their evidence.

Static Ghidra observations show uninitialized main code sections in the packed
on-disk image (`ev_df64ce66317bfd3439e0a90f81fb47fe81a3337eaeda65f7ed0156cfbf8a4152`).
Sparse string/procedure searches do **not** establish absence of transfer code
or recover its algorithm. No decompiled text is treated as original source.

Runtime observations below are caller-authored, scoped Win32 API instrumentation
and hidden native control actions through REA. Evidence has null artifact identity;
the explicit spawn path/PID, qualified input hash and observed module bind it to
this reference. Window presentation was suppressed; physical visible UI behavior
and uninstrumented performance are separate verification boundaries. A barrier
deliberately holds fixture I/O for control tests; its durations are not benchmarks.

## Bounded adoption

| Behavior and evidence | Inherited integration gap | Decision and acceptance check |
| --- | --- | --- |
| Two reader threads, 1 MiB requests, separate write activity and target reread; `ev_6fe930b1f48d1ee945c58cef91e97a99f59f005fcf44c054897237f95762da1a`. | Ferry already has optional small-file concurrency and native copying. Donor thread count alone does not prove a better setting. | Keep the native copier and existing queue. Compare inherited and changed paths with identical workloads; tune concurrency/buffering only for measured benefit without correctness loss. No invented small-file packing or speedup claim. |
| Copied and Verified are distinct native counters; `ev_b05ac74bcc3cb5079c96df02cf08b837697a01a18a0b3202a7e226b1d436e909`. | Done currently says Copied/Moved even after member errors, and filesystem skips shrink totals without a skipped count. | Preserve planned totals; report successful, skipped, failed, cancelled and verified outcomes accurately. Never label a failed/unverified member fully successful. Verify cards, aggregate progress, selection and undo/cut completion through real callers. |
| Cross-volume copy, target verification read, then source DeleteFile; `ev_f7b520faecddeb57302e58be9847bb81bb2e181826414c2924ec373c38ca0ab8`, UI `ev_4e8ef17258878218618c2558f65011d0bc1d3d32b908a8b81c8bf60047db8ce0`. | Optional verification can remove an unchecked copied source; changed-source removal must be refused. | Cross-volume moves always verify before source removal. Write directly with normal sharing. Check the verified source at final removal, without a source lock held across copy/hash phases. Test copied moves, changing sources and failed verification; do not restore overwritten old contents. |
| Same-volume MoveFileEx rename, no observed copy; `ev_298b6d3d3d1bf8e3f17f9df5e7aa1c6090e6db4a5c64649b2cbc2c8610d22c88`. | A whole-folder shortcut can move members Scan excluded and falsely count failures as done. | Use native MoveFileEx for planned files. Only ERROR_NOT_SAME_DEVICE selects copied/verified moving, including aliases across volumes. Count actual successes; keep merge/skips/links in the shared plan. A rename preserves file identity and is not a hash-verification pass. |
| OverwriteAll produced verified contents; pause fixture's actual option readback says Overwrite all. | Existing conflict ownership is appropriate; duplicate planned names and file/folder collisions need correct handling. | Keep job-level Skip/Overwrite/Rename/Newer. Write directly with the selected overwrite authority; use native fail-if-exists when overwrite was not selected. Verify choices and cut-source retention. Do not import the donor's automatic Skip All timer. |
| Native Pause toggle stops writes after the explicit fixture barrier is released; second toggle restarts writes (`ev_a424003fc2fa7fdedde9cb2e1665f58d29beb29e9f5193b00841edf957c957b8`, `ev_902da7f0464ec4a5423d4cd7d08f0c25bbb93018ce528480420a33ba15040d11`), then verified completion `ev_29535d629bc753c3d2d2dffe0feb90e21204d2d8eef9c8b953d79b534b96e725`. | Preserve existing event-driven control through copy, verify and source removal. | Live pause/resume; cancellation releases paused/conflict waits. No arbitrary deadline. The hidden donor's button text remained Pause; verify Pathweft's actual Resume and Cancel controls separately. |
| Stop after a partial write closes handles and deletes the incomplete target; sources hash unchanged. `ev_ea8c9b020f480718fd6650f6c6a19e8fc66d123c6f675a63f9c1e88d9e4f124f`. | Cancellation must clean incomplete copied output without removing completed files or source data. | Native PROGRESS_CANCEL cleans active copying; cancellation during verification removes that current unverified output. Track the output's native identity so cleanup cannot delete another owner's replacement. Leave completed outputs. No staging or overwrite rollback. Exercise copy/paused/queued/verify/conflict cancellation and empty/read-only outputs. |
| Denied target open returns Win32 error 5 twice, UI Failed:1; existing target retained (`ev_e8b1e8e00e9d8e35f3b045d1a46c22a29682e587e68c8cf3fc4d4df0f5036c1f`, `ev_82ccbe3d735d12cd41708d0bd06d2773d0b3d9a20869483dbd5217c5cb4fdb5a`). Fresh process succeeds after fixture ACL restoration (`ev_1b390baae99f5a1a056db36319f1030cec397e418158b0eed6fc6d03a8b2b6cb`). | IOException is currently reported as Cancelled; some member failures still finish with Copied. | Distinguish failure from user cancellation. Surface the concrete error, retain data, allow a fresh resubmission after the cause is resolved. No blind timed retry. Test real denied access, destination disappearance/fullness and source changes. |
| ADS is read/written in the copy trace, alongside per-file metadata controls. | File payload hashing alone does not verify ADS; directory times/attributes and unsupported destination capabilities need explicit handling. | Preserve and verify the complete supported stream set plus required timestamps/attributes. Keep reparse points untraversed and report exclusions. Detect/report unsupported preservation instead of silently discarding it. Define filesystem precision/security limits and exercise representative fixtures. |
| Closed-process fixture History contains file results/hash, metadata/Offset and Run records (`ev_6e6a1038324bcfbad646538335810e3789ff253b7d407340593997939e7c7799`, `ev_a55e8cce8812e299d12f344169f6010c332b99c73033a857d892779d8562362b`). | A stored schema does not prove automatic restart/offset recovery; Ferry's queue is in memory. | Live pause/resume and source-preserving failure/cancellation, followed by fresh manual resubmission. A crash can leave incomplete direct output because it cannot execute cancellation cleanup. Verify that source data remains and resubmission overwrites from zero. No durable queue or recovery framework. |

## Native integration boundary

Under TRANSFER-07, ordinary single-stream data will use one normally shared
source reader and a pooled 1 MiB buffer, writing directly while feeding xxHash3.
Verification rereads the destination against that captured hash. The source is
not opened again merely to calculate its hash. Retain CopyFileEx for named streams
and encrypted/compressed/sparse storage requirements, including existing target
streams/storage that ordinary truncation cannot reproduce correctly. Keep native
MoveFileEx for same-volume moves. This is one bounded path inside the existing
Ferry job and queue, not a provider framework or separate transfer service.

Actual copy-time and verification I/O counters must reflect the selected path;
do not retain the old two-read verification accounting or invent source-read
events to satisfy old diagnostics. Compare operation counts, output contents,
metadata/stream preservation, overwritten targets, sharing, pause/cancel,
failures and real callers before accepting the change. Source/target stream-set
changes must still be refused; stream discovery is part of preserving the native
path's behavior, not an optional guard. Native storage cases remain explicit
comparison work until their best supported path has evidence.

The separate metadata-state probe found that the ordinary 100-file tree's 101
attribute writes all requested the already stored value; timestamp writes were
still necessary. Reuse the existing attribute query and skip SetAttributes only
when the complete requested value already matches. Keep batched timestamp
application and the final metadata readback; do not add a query or skip required
attribute changes. The state probe adds diagnostic reads and is excluded from
operation-count and timing comparisons.

No exclusive source/stream handle spans copying or verification. Brief final
removal operations verify identity and address their exact file handle, just at
the point where source removal or cancellation cleanup is required. This does
not create a lock manager, persistence service or alternate transfer engine.
The user-selected direct-write policy permits destructive overwrites; cleanup
does not restore the previous version.

Microsoft documents [native copy preservation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-copyfileexw),
[handle-based file operations](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle),
and [stream sharing/metadata rules](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams).
Those are platform contracts, separate from donor observations. Security owner,
audit/ACL cloning, every cloud placeholder or every remote filesystem semantic
is not established by the donor's controls; support or refusal must be explicit.
Destination permissions follow normal native inheritance. Reparse entries are
reported as excluded; their targets are not recursively copied. Hard-link copies
are independent files, with a note. Folder alternate streams and folder encryption/
compression policies are refused. Named streams require destination support;
FAT32 timestamp rounding is reported. Unsupported storage attributes produce an
explicit failure. No physical power-loss durability guarantee is made.
Native copying and the ordinary direct path refuse existing ReadOnly, Hidden or
System overwrite targets with a failed outcome and retain both files. Attribute
preservation on new files and supported overwrites remains required; no implicit
clearing of these target protections is added.

## Required final S5 evidence

One connected Ferry/action owner; hidden real pane/widget/archive callers;
copy, same/cross-volume move, merge, overwrite and conflict choices; truthful
completed/partial/failed/cancelled/verified UI; source preservation and cancellation cleanup;
metadata/streams/reparse limitations; adverse fixtures; actual pause/cancel and
restart contract; comparable many-small/large workloads for this pinned donor,
the inherited pre-S5 source and the integrated implementation. All assigned S5
criteria pass with the explicit measured limitations in the verification record.
S8 owns final packages and product release.
