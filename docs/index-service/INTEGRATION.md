# Wiring the index service into Pathweft

Status: the service, client DLL and CLI build and pass their tests (19/19);
the elevated self-test passes all 8 checks on the installed build (d5d9fc7),
which is running on the owner's PC. Measurements against Everything are in
`MEASUREMENTS.md`, the stage record in `PLAN.md`. Nothing in the Pathweft app
has been changed; this note describes the wiring that remains.

## What Pathweft talks to

- Pipe `\\.\pipe\PathweftIndex`, framed JSON (see `PROTOCOL.md`). No DLL is
  required: `NamedPipeClientStream` with `TokenImpersonationLevel.Impersonation`
  is enough. `pathweft_index.dll` (`pwi_request`/`pwi_free`) is an optional C ABI.
- The service runs as LocalSystem (`PathweftIndex`, automatic start) and holds
  the whole index in memory (about 680 MB for 11.9 M NTFS entries). NTFS volumes
  come from the MFT and are kept current from the USN journal. Folder roots are
  opt-in background scans, added with `folder_add` or `pwindex folder add`. On
  the owner's PC, H:, J: (Google Drive), T: (Tresorit, at the owner's request)
  and `\\100.74.127.101\flint-ssd` (M:) are configured. Use the UNC path for
  mapped network drives.
- Queries never wait on indexing. While a store is loading or re-reading, the
  answer carries `"updating": true` and `updating_stores`, and contains what is
  indexed at that moment.

## Mapping from `EverythingSearchClient`

| Pathweft today | Index service |
| --- | --- |
| `SearchAsync(expr, offset, count, sort, desc, switches)` | `{"op":"query","query":expr,"offset":…,"count":…,"sort":"name|path|size|extension|created|modified","descending":…,"match_path":…,"match_case":…,"whole_word":…,"regex":…}` |
| `QueryAsync(expr, instance, …, "Name"/"Size"/"Type"/"Modified", …)` | the same request; `Type` maps to `extension`; `instance` has no equivalent and is ignored |
| `FolderExpression(paths)` (`folder: <path:wfn:"…" \| …>`) | accepted unchanged |
| `IndexedQuery.Compose(query, folder)` output | accepted unchanged (covered by `query::tests::pathweft_compositions`) |
| `FolderSizesAsync(paths, instance)` | `{"op":"folder_sizes","paths":[…]}`; a missing `size` means unknown, so leave the key out of the dictionary as now |
| `IndexedHit(Path, IsFolder, Size, Modified, Created, Attributes)` | `hits[]`: `path`, `visible`, `folder`, `size` (-1 unknown), `modified`/`created` FILETIME UTC (`DateTime.FromFileTimeUtc`, 0 unknown), `attributes` |
| `IndexedPage(Hits, Total, Engine, FolderSizesIndexed)` | `hits`, `total`; Engine `"Pathweft index"`; FolderSizesIndexed = true |
| SDK error / unanswered Everything | `{"ok":false,"error":…}`, or connect timeout when the service is absent |

Unsupported syntax (date functions such as `dm:`, `dupe:`, …) returns an error
instead of a different result, so the existing "Everything rejected the query"
path can show the message as it is.

## Suggested wiring

1. Add an `IndexServiceClient` next to `EverythingSearchClient` with the same
   `SearchAsync` / `QueryAsync` / `FolderSizesAsync` surface, then pick the
   engine per call: the index service when its pipe connects within about 250 ms,
   otherwise Everything, otherwise the existing traversal fallback.
   Reuse one connection per caller; the service allows 16 concurrent connections.
2. Show `updating` as a small "index updating" hint on the result count. Do not
   retry and do not block.
3. Settings > Windows integration: show service status (`{"op":"status"}`,
   or `pwindex status`), the per-store state and the folder roots, with Add/Remove
   for folder roots. Installing and removing the service needs elevation:
   `pathweft-indexd.exe install` / `uninstall` through Pathweft's existing
   elevated-session path.
4. Packaging: ship `pathweft-indexd.exe`, `pwindex.exe` and (optionally)
   `pathweft_index.dll` from `native/pathweft-index` (`cargo build --release`
   through the Rust gate). `install` copies them to
   `%ProgramFiles%\PathweftIndex` and keeps config and logs in
   `%ProgramData%\PathweftIndex`.

## Behaviour Pathweft should know about

- Every match is returned with `visible`: whether the calling user may list its
  parent folder. Pathweft decides whether to hide the others (for example a
  "Show protected items" toggle). To filter on the service side and keep
  `total` and paging consistent, send `"visible_only": true`.
- Folder sizes are always the administrator view: everything indexed below the
  folder, including folders the user cannot open. Disk analysis can therefore
  find problem folders such as large protected caches.
- T: (Tresorit) is never indexed by default; on the owner's PC it was added as
  a folder root at the owner's request, through a plain directory listing (no
  Tresorit databases or interfaces). No file contents are read anywhere: folder
  scans only enumerate directories, and directory security descriptors are read
  with `FILE_FLAG_OPEN_NO_RECALL`.
- Folder roots are rescanned from scratch after a service restart and every
  `rescan_minutes` (default 360). Until a root's first scan finishes it appears
  in `updating_stores` and contributes nothing. Measured first scans: H: 386 k
  entries in 2–15 min, T: 175 k in 1.5–14 min, the network share in 21 min.
- Not-always-attached NTFS volumes (F:, I:) are picked up within 30 s of
  appearing; a volume that disappears after loading is reported `offline` and
  retried every 15 s.
