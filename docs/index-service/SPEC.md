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
`install` copies the binary to `%ProgramFiles%\PathweftIndex` (administrator-only
write), writes a protected config under `%ProgramData%\PathweftIndex`, registers
and starts the service. `uninstall` stops and removes it.
*Accept:* installed only through the administrator broker; `sc qc PathweftIndex`
shows LocalSystem/auto; the service answers `status` after start.

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
answering. Reparse points are not followed. Default rescan interval 6 h, plus an
explicit `rescan`. Nothing is scanned by default; T: is never indexed unless the
owner adds it.
*Accept:* a folder root added through the CLI reaches `ready` with items, queries
return its entries, and queries stay responsive while it scans.

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
`file:`/`folder:`, `ext:a;b`, and `size:` comparisons. Unsupported functions
return an explicit error, never a silently different result.
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

**IDX-08 Results limited to what the caller can list.** Chosen rule (per result,
not per query): an entry is returned and counted only if the caller's token is
granted `FILE_LIST_DIRECTORY` on the entry's parent directory. The check is
`AccessCheck` of the caller's impersonation token against the parent's security
descriptor, cached per caller SID and NTFS security ID (security descriptors are
shared by ID). Folder sizes count only files whose parent directory the caller
can list, and a folder's own size is returned only if the folder itself is
visible. Scanned roots are visible only to the SID that added them (they were
listed with that user's token). Raw volume handles never leave the service.
*Accept:* unit test of the filtering rule with a fake access oracle; live test
that a directory the user cannot list (e.g. `C:\System Volume Information`)
contributes no results.
*Owner-approved exception (2026-11-18):* the config field `"visibility"`
(`"caller"` by default, or `"all"`, set with `pathweft-indexd install
--visibility all`) can turn the per-result check off for NTFS entries on one
machine; any allowed pipe user then sees every indexed path. The owner chose
`all` for this machine ("I don't find much risk in knowing the file paths of my
files"). The default stays `caller`, so other installations are unchanged;
scanned roots keep their owner rule, and only names and metadata are exposed.

**IDX-09 Health and CLI.** `status` reports per store: kind, state
(loading/ready/reloading/scanning/waiting-for-user/offline/error), items, folders,
journal ID, next USN, load time, re-reads, last error, estimated memory; and the
process working set and private bytes. `pwindex.exe` provides status, query,
sizes, folder add/remove, rescan, bench, churn and Everything comparison.
*Accept:* each command exercised against the running service.

**IDX-10 Compact memory.** Per entry: a fixed 40-byte record plus the UTF-8 name.
Target: process private bytes at most half of Everything's working set for the
same NTFS volumes. *Accept:* measured and reported, with the Everything figure.

**IDX-11 Metadata only.** The service never reads file contents and never opens a
file for data access. Volume reads cover only MFT records. Directory and security
lookups use `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS |
FILE_FLAG_OPEN_NO_RECALL` with no data access, so cloud placeholders are never
hydrated. Scans use directory enumeration only. *Accept:* source inspection.

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

## Constraints carried from the brief

Builds only through the Coordinator Rust gate; administrator actions only through
the broker, each approved by the owner; build trees under `D:\Temp\pathweft-index`,
disposable scratch under `E:\Temp\pathweft-index`, cleaned through
transactional-cleanup; Everything is never restarted, killed or reconfigured; no
Tresorit internals, credentials or content; no GitHub publication; any future UI is
native WPF.
