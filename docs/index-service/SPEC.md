# Pathweft index service: specification

Status: authoritative specification for branch `index-service`. Requested by
the owner on 2026-11-18 to replace Everything (voidtools) as Pathweft's
file-name index, because Everything 1.4 on this PC repeatedly hangs: the window
shows "Not Responding", one thread is CPU-bound, the working set is about 1.4 GB
and its IPC queries block, which freezes Pathweft's search.

Scope is the owner's setup, not a general-purpose indexer (owner, 2026-11-18: "just
something build around my setup requirements"). The setup is:

| Drive | Type | Handling |
| --- | --- | --- |
| C:, D:, E:, F:, G:, I: | local NTFS (F: and I: are not always attached) | MFT read once, then the USN journal |
| H:, J: | FAT32 | opt-in simple scan |
| M: | network share `\\100.74.127.101\flint-ssd` | opt-in simple scan |
| T: | Tresorit drive | excluded; opt-in only, listing only |

Out of scope: ReFS, hard-link nuances (each file is indexed under one name), the
full Everything search language, cases the owner's setup does not have.

## Requirements and acceptance criteria

**IDX-01 Windows service.** One executable `pathweft-indexd.exe` runs as the Windows
service `PathweftIndex` (LocalSystem, automatic start, restart on failure).
Pathweft's setup places the binaries in
`%ProgramFiles%\Pathweft\services\file_index` (owner decision 2026-11-19: Pathweft's
services live under `services\<name>`). `install` registers the service to run
from there, with no copy, and refuses an executable outside `%ProgramFiles%`.
It writes a protected config under `%ProgramData%\Pathweft\services\file_index`,
and registers and starts the service. Over an older installation it re-points the
service and removes `%ProgramFiles%\PathweftIndex`. It moves config and log from
`%ProgramData%\PathweftIndex`. `stop` stops the service and waits for its process
to exit, so setup can replace locked binaries. `uninstall` stops and removes the
service.
*Accept:* installed only through the administrator broker or Pathweft's setup;
`sc qc PathweftIndex` shows LocalSystem/auto and the `services\file_index` path;
the service answers `status` after start; an upgrade from the old layout leaves
no `PathweftIndex` folders behind and keeps the folder roots.

**IDX-02 NTFS from the MFT and USN journal.** For every local NTFS volume with a
drive letter (fixed or removable, filtered by config), the service records the
journal position, reads the MFT once by raw volume reads, and then applies
changes from the USN journal. Changes during the MFT read are replayed from the
recorded USN. If the journal wrapped (`ERROR_JOURNAL_ENTRY_DELETED`), was deleted or
replaced (new journal ID), only that volume is re-read; its old index keeps
answering until the new one is swapped in. Volumes that appear later (F:, I:) are
picked up within 30 s; a volume that disappears is reported offline. NTFS volumes
are never scanned with directory listings.
*Accept:* a self-test on a disposable NTFS volume shows created, renamed, moved,
resized and deleted files reflected in queries within 2 s; a forced re-read
completes and swaps; live service reports per-volume `ready` with a moving USN.

**IDX-03 Opt-in scans for non-NTFS and network folders.** Roots added with
`folder add` (FAT32 H:, J:, the M: share, or T: if the owner opts it in) are
listed recursively on a background thread in Windows background-priority mode,
impersonating the user who added them (so the user's drive mappings and
credentials apply). A rescan builds a new tree and swaps it in; the old tree keeps
answering. Reparse points are not followed. A full rescan runs at start-up, on an
explicit `rescan` and after a notification overflow. Nothing is scanned by default; T: is never indexed unless the
owner adds it.
While the service runs, each folder root is followed through Windows change
notifications (`ReadDirectoryChangesW` on the whole tree, opened as the same
user): the folders containing changed paths are re-listed and the difference is
applied, so additions, deletions, renames and size or date changes appear without
a rescan. Notifications start before the scan, so changes made during it are
applied when it finishes. A lost-notification overflow triggers a full rescan; a
root whose notifications cannot start or stop working keeps the scheduled checks.
Notifications miss some changes (a share does not forward changes made on the
server itself), so every folder is also listed again on its own adaptive
schedule: after half the time since it last changed, at least every 15 minutes
and at most every `rescan_minutes` (default 6 h). The folder's own modified time
seeds the schedule when it is believable (after 2000, not in the future); a check
that finds a change resets the folder to 15 minutes; a subfolder whose entry
changed is checked at once (owner, 2026-11-18: "make it learn how often a branch
is updated and refresh that path ... by a minimum of 15 minutes and a maximun of 6
hours").
After a service restart the first scan still runs from scratch (owner, 2026-11-18:
"lets assume that challenge, delayed update for cloud services after a restart.
As long as we can monitor updates while running").
*Accept:* a folder root added through the CLI reaches `ready` with items, queries
return its entries, and queries stay responsive while it scans. While running,
files created, modified, renamed and deleted below a folder root, and a folder
created with content, are reflected in queries within seconds without a rescan.
A change that produces no notification is picked up by the folder's scheduled
check: within 15 minutes for a folder that changed recently, within 6 hours at
most.

**IDX-04 Queries never wait on indexing.** Each volume or folder root is a separate
store with its own lock. Indexing holds a store's write lock only to apply a
bounded batch (one USN read buffer) or to swap in a finished store; MFT reads,
scans, record fetches and name-arena compaction preparation happen without it.
Queries read each store in chunks of 65,536 records under a read lock and release
it between chunks. A store that is loading or rescanning answers with what it has
(nothing, if it has no previous tree) and the response says `updating: true` with
per-store states.
*Accept:* measured query latency while heavy file churn runs on an indexed volume
(p95 and maximum reported); no query fails or stalls because of indexing.

**IDX-05 Search features Pathweft uses.** A query has an expression, offset, count
(max 100,000), sort (name, path, size, extension, created, modified; ascending or
descending) and the switches match-path, match-case, whole-word and regex. It
returns the total number of matches and the requested page with full path,
folder flag, size, modified, created and attributes. Expression language (the
subset Pathweft composes and the MCP tool passes): space = AND, `|` = OR (binds
tighter than AND), `!` = NOT, `< >` grouping, `"..."` quoting (with `""` for a
literal quote), `*`/`?` wildcards (whole-name match), substring otherwise; a term
containing `\` matches the full path; modifiers `path:` `nopath:` `case:` `nocase:`
`ww:` `wfn:` `regex:` `file:` `folder:` applied to a term or a group, standalone
`file:`/`folder:`, `ext:a;b`, `size:` comparisons, dates and `parent:` (Pathweft
request 2026-11-19). `dm:`/`datemodified:` and `dc:`/`datecreated:` take `today`, `yesterday`,
`thisweek`, `lastweek`, `thismonth`, `lastmonth`, `thisyear`, `lastyear`, `YYYY`,
`YYYY-MM` or `YYYY-MM-DD` (`/` also accepted), a range `a..b` (both ends
inclusive) or `>`, `>=`, `<`, `<=`, `=` one of those. Days are local-time days
(daylight saving applied by Windows), weeks start on Monday, and an unknown time
never matches. `parent:<folder>` matches the folder's direct children only
(a drive, a folder-root path or a UNC root also works); a folder that is not
indexed matches nothing.
`content:` is described in IDX-18. Unsupported functions (`dupe:`, ...) return an
explicit error, never a silently different result.
*Accept:* unit tests cover parsing and matching for every listed construct,
including `IndexedQuery.Compose` output and `FolderExpression`; live parity on
sample queries against Everything.

**IDX-06 Folder sizes.** A `folder_sizes` request takes exact folder paths and
returns each indexed folder's recursive size (sum of file sizes in the subtree,
reparse points not followed); missing or unindexed folders are absent, never 0.
*Accept:* unit tests; live comparison with Everything's folder sizes on sample
folders.

**IDX-07 Client IPC.** Local named pipe `\\.\pipe\PathweftIndex`, created as first
instance, rejecting remote clients, with a protected DACL: SYSTEM plus the
configured user SIDs (default: the user who ran `install`), network logons
denied. Protocol: 4-byte little-endian length + UTF-8 JSON, versioned, documented
for direct use from C# (`NamedPipeClientStream`). A Rust client library and a C ABI
DLL (`pwi_request`/`pwi_free`) wrap it. No network listeners.
*Accept:* CLI round trip as the interactive user; documented protocol.

**IDX-08 Per-result visibility flag; the client filters.** Revised by the
owner on 2026-11-18 ("Just return a visibility flag so we can decide in the
client to filter them out or not. But features like the disk analyzer should
always run with admin view, since its purpose is to enumerate problematic
folders"). Every match is returned with `visible`: whether the caller's token
is granted `FILE_LIST_DIRECTORY` on the entry's parent directory (`AccessCheck`
of the caller's impersonation token against the parent's security descriptor,
cached per caller SID and NTFS security ID). Entries of a scanned root added by
another SID are `visible: false`. The request option `visible_only` filters
before counting and paging. Folder sizes are always the administrator view
(everything indexed). Only names and metadata are exposed; raw volume handles
never leave the service and the pipe still admits only the configured users.
*Accept:* unit test of the flag, `visible_only` and full folder sizes with a
fake access oracle; live self-test that an entry of a folder the user cannot
list is returned with `visible: false`, dropped by `visible_only`, and counted
in its ancestors' sizes.

**IDX-09 Health and CLI.** `status` reports per store: kind, state
(loading/ready/reloading/scanning/waiting-for-user/offline/error), items, folders,
journal ID, next USN, load time, re-reads, last error, estimated memory; and the
process working set and private bytes. `pwindex.exe` provides status, query,
sizes, folder add/remove, rescan, bench, churn and Everything comparison.
*Accept:* each command exercised against the running service.

**IDX-10 Compact memory.** Per entry: a fixed 40-byte record plus the UTF-8 name.
Target: process private bytes at most half of Everything's working set for the
same NTFS volumes. *Accept:* measured and reported, with the Everything figure.
Owner revision (2026-11-18): the half target is advisory; any footprint is
accepted while the service improves on Everything ("as long as it is an
improvement over Everything, I accept whatever resources it may be using").

**IDX-11 Metadata only.** The service never reads file contents and never opens a
file for data access. Volume reads cover only MFT records. Directory and security
lookups use `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS |
FILE_FLAG_OPEN_NO_RECALL` with no data access, so cloud placeholders are never
hydrated. Scans use directory enumeration only. Content terms come only from
the user's worker over the pipe (IDX-17). *Accept:* source inspection.

**IDX-12 Measured comparison.** Report for the owner's drives: initial index time,
memory, query latency during heavy churn, and result parity on sample queries,
against Everything (read-only, with timeouts, no root-wide searches, no changes to
Everything or its settings). *Accept:* `docs/index-service/MEASUREMENTS.md`.

**IDX-13 Integration note.** `docs/index-service/INTEGRATION.md` describes what
wiring the service into Pathweft needs. *Accept:* note present; the Pathweft
thread is told the integration point is ready.

**IDX-14 Disposable test before install.** Before installing on real volumes the
`selftest` runs elevated against a disposable VHDX NTFS volume under
`D:\Temp\pathweft-index`, exercising load, churn, USN application, forced
re-read, folder sizes and a pipe round trip. *Accept:* self-test report all checks
passed; VHDX detached and deleted afterwards.

## Content index (IS10, S19 part b)

Owner decisions (2026-11-19):
- "Service owns what runs in the service side, you own what runs in your side"
  (relayed by the Pathweft thread).
- "the database with the file paths and the file contents should be stored
  together. For speed C: makes more sense if we can keep, as per our design, the
  contents index under control."

Split:
- Pathweft's worker (`Pathweft.exe` in worker mode, running as the user) reads
  files, hydrating cloud files when needed (Pathweft CONTENT-04), and pushes
  text chunks with source locations.
- The service tracks which files need work, keeps the terms index and answers
  `content:` inside its ordinary queries.
- The service still never opens a file for data access (IDX-11). Text reaches
  it only over the pipe, and it stores terms, not text.

**IDX-15 Content scopes.**
- `content_scope_add {path}` adds a folder that is indexed by a store (an NTFS
  volume or a folder root). Scopes are global (one owner on this setup).
- A scope inside an existing scope is already covered. A new scope absorbs the
  scopes below it.
- `content_scope_remove {path}` removes an exact scope. It purges the terms of
  every file that no remaining scope covers.
- `content_scopes` lists the scopes.
- *Accept:* unit tests for absorb, covered and purge-with-overlap.

**IDX-16 Change tracking from the name index.**
- About once a minute, and right after a scope is added, each scope is compared
  with the name index: path, size and modified time of every file below it.
- New file: pending.
- Changed stamp:
  - a current file becomes stale, and its previous terms keep answering;
  - a failed or unsupported file becomes pending.
- File gone while its store is ready: if exactly one new file has the same
  extension, size and modified time, it is treated as a move and keeps its
  terms. Otherwise its terms are purged.
- Store not ready (loading, re-reading, first scan, offline, waiting for its
  user): the scope's files become unavailable and keep their data. Unavailable
  is never deleted. They return to their previous state once the store is
  ready and the stamp is unchanged.
- *Accept:* unit tests for every transition; a console-instance test on a
  folder root.

**IDX-17 Worker protocol.**
- `content_next {count, version}` returns files to read, each with
  `{id, path, size, modified}`:
  - pending and stale files;
  - failed and unsupported files whose recorded extractor version is lower than
    `version`.
  A file handed out is not handed out again for 10 minutes unless it is
  answered.
- `content_put {id, version, read, after, chunks:[{location, text}], final}`:
  - chunks are staged until `final`;
  - on `final` the file's previous terms are replaced in one transaction, and the
    stamp the worker read (`read`) is recorded as published;
  - the file is current if `read`, `after` and the index's present stamp are all
    equal, otherwise stale.
  - Limits: 64 KiB per chunk, 64 MiB of text per file.
- `content_fail {id, version, read, error, unsupported}`: a previously
  published file stays stale with the error; otherwise it is failed or
  unsupported.
- *Accept:* unit tests for staging, publish, stale-on-change, fail and retry by
  version; an end-to-end run with the reference worker in `pwindex`.

**IDX-18 Content query and visibility.**
- `content:` in the query language takes a word, a `prefix*` or a "phrase".
  Several `content:` terms combine like other terms.
- Matching uses SQLite FTS5 (unicode61, diacritics removed) over the chunks of
  current and stale files.
- A content match is returned only if the caller may read that file:
  - NTFS: `FILE_READ_DATA` by `AccessCheck` on the file's own descriptor, cached
    per security ID like IDX-08;
  - folder roots: the root's owner only.
  Content results are dropped, not flagged.
- With `locations: N`, each hit carries up to N matching locations, as stored.
  Text and terms are never returned.
- `content_state {folder}` gives badge states for the folder's direct children
  in scopes: pending, current, stale, failed, unsupported or unavailable, with
  indexed time and error. It follows the folder's list access.
- `content_status` reports, per scope, the counts per state, the last reconcile
  and any error; also the store size in bytes and `waiting` (files ready for
  the worker).
- *Accept:* unit tests for term translation (no FTS syntax injection) and
  visibility; a live query through a console instance.

**IDX-19 Store.**
- One SQLite database, `content\content.db`, in the service's data folder
  (`%ProgramData%\Pathweft\services\file_index`, on C: by owner decision).
- The `content` folder has a protected DACL: SYSTEM and Administrators only.
- Tables, from Pathweft's draft: `scopes`, `files` (path, stamp, state, error,
  published stamp, extractor version, indexed time), `chunks` (file, location
  JSON) and a contentless FTS5 table `terms` with `contentless_delete=1`.
- Bounded memory (16 MB page cache).
- The private console instance uses `--content-dir` or has no content index.
- *Accept:* status shows the size; ACL inspected on the installed service.

## Constraints carried from the brief

Builds only through the Coordinator Rust gate; administrator actions only through
the broker, each approved by the owner; build trees under `D:\Temp\pathweft-index`,
disposable scratch under `E:\Temp\pathweft-index`, cleaned through
transactional-cleanup; Everything is never restarted, killed or reconfigured; no
Tresorit internals, credentials or content; no GitHub publication; any future UI is
native WPF.
