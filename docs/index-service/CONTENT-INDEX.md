# Content index and hash cache: design draft

Status: SUPERSEDED for S19 by SPEC IDX-15..19 and PROTOCOL version 2
(2026-11-20, IS10). Owner decision (2026-11-19): "the database with the file
paths and the file contents should be stored together. For speed C: makes more
sense if we can keep, as per our design, the contents index under control."
The service owns the store (SQLite FTS5, terms and locations only, beside the
file index on C:). Pathweft's user-context worker reads and may hydrate files;
the service never does.

Not part of IS10: Tantivy, stored text, content-hash dedup, the hash cache
(CIX-10) and `dupe:` (CIX-11). Each needs its own owner go-ahead.

The rest of this file is the earlier draft, kept unchanged for reference.

Owner direction: the index service should later take over selective file-contents
indexing. Initial idea (owner): Pathweft's plugins read the files and export
their strings; an index holds the strings and the file. Refined in discussion:
key extracted text by content hash so moved or copied files are not parsed
again; reuse Pathweft's hash algorithms (CRC-32, CRC-64, MD5, SHA-1, SHA-256,
SHA-384, SHA-512, SHA3-256, XXH64, XXH3-64, possibly a HashTab-like wider set).

## Roles

```
index service (SYSTEM)                          extractor worker (user)
  change tracking: USN / notifications /          asks for the next batch
  scheduled checks (existing)                     reads each file once:
  content rules -> dirty queue        ------->      hashes (SHA-256 + cached extras)
  files + documents tables            <-------      text + metadata via plugins
  Tantivy content index                           never runs as SYSTEM;
                                                  a worker mode of Pathweft.exe
  query: name/date filters + content:
  hash cache
```

- The service decides **what** must be read (it already sees every change) and
  owns the index. It never parses file contents itself as SYSTEM.
- The extractor decides **how** to read: native plain-text/code extraction, and
  Pathweft's C# plugins for other formats (PDF, Office, media metadata, ...).
  It runs in the user's context at low priority, so it can only read what that
  user can read, and a malicious file attacks an unprivileged process.
- The extractor is a worker mode of `Pathweft.exe` selected before WPF starts
  (like the existing OfficeWorker and MaintenanceWorker), so nothing extra is
  installed or signed. Pathweft starts it (the app, or its sign-in agent once
  `Agent.StartAtSignIn` exists); the service never does. It exits when
  `content_next` returns an empty queue and is started again on the next change;
  the service's status reply says whether the queue is non-empty. No permanently
  resident extra process.

## Requirements (provisional)

**CIX-01 Selective rules.** Content indexing applies only to owner-configured
rules: folders (include/exclude), extensions, maximum file size, maximum text per
file. Default: nothing is content-indexed.

**CIX-02 Two layers.** `files`: file identity (volume + NTFS file ID, or folder
root key) -> version stamp (size, modified time, USN where available) and content
hash. `documents`: content hash -> extracted text and fields, indexed once, with
a reference count. A change is detected by the stamp, without reading the file.

**CIX-03 Content key.** One fixed internal algorithm, never user-configurable:
SHA-256 (in Pathweft's list; collision-proof; the read is disk-bound anyway).
64-bit hashes are not used as keys (a collision would attach one file's text to
another). XXH3-128 was considered and rejected (Pathweft review): no 128-bit
non-cryptographic hash is needed anywhere.

**CIX-04 No needless re-extraction.** Moves within a volume keep the file ID: no
work. Copies, cross-volume moves and re-downloads produce a new identity whose
hash matches an existing document: link, do not parse. Only new content is
parsed. A metadata-only edit (for example MP3 tags) changes the hash and is
re-parsed; acceptable.

**CIX-05 Extraction protocol.** Over the existing pipe: `content_next` (batch of
identity, path, stamp), `content_put` (identity, stamp, hash, text chunks,
fields, extractor/plugin version) and `content_fail` (identity, stamp, reason;
retried only after the stamp changes or the plugin version changes). A stamp
that changed since the batch was handed out is discarded. Plugin contract
(Pathweft side): `extract(path, stamp) -> {extractor_id, extractor_version,
fields{}, bounded streamed text chunks}`; plugins are versioned individually and
a version bump re-queues only that plugin's failures and documents. The worker
process is the isolation boundary: per-file timeout, memory cap, recycling after
N files or a crash; a crash or timeout becomes `content_fail(reason)`. Office and
PDF reuse Pathweft's OfficeWorker bounded `TextRecord` output (location/offset).

**CIX-06 Query.** `content:<words or "phrase">` in the existing language. Cheap
name/path/date/size terms filter first; the content index narrows within them.
A document hit expands to every live file with that hash; paths are resolved by
the name index, so renames are reflected immediately. Optional fields later
(`title:`, `author:`), snippets for result previews.

**CIX-07 Visibility.** A content hit is returned only if the caller can read the
file itself (NTFS security IDs on records, cached access checks), not just list
its parent: content reveals more than a name.

**CIX-08 Cloud placeholders.** Files that are not locally present are never
read or hashed (reading downloads them). Content indexing on H:, J: and T: is a
per-drive owner opt-in and still covers locally present files only.

**CIX-09 Resources.** Extraction at background priority, paused under load or
on battery; the index on D: (expected 10–30 % of extracted text), not ProgramData
on C:. Owner policy: footprint is acceptable if it beats the alternatives.

**CIX-10 Hash cache.** The service stores hashes keyed by (file identity, stamp,
algorithm name) -> bytes, for any algorithm Pathweft reports. Pathweft's Hashes
column asks first (`hash_get`); on a miss it computes as today and stores the
result (`hash_put`). The cache serves every whole-file hash Pathweft computes,
not only files under the content rules. `hash_get` answers only if the caller can
read the file (a hash confirms content; same rule as CIX-07). Pathweft keeps its
local `hashes.bin` as the fallback when the service is absent and never blocks a
column on the pipe (short timeout, then compute locally). Pathweft's existing key
(volume, 128-bit file ID, size, last write) matches identity + stamp, so entries
move between both caches directly. The extractor's single read also fills
SHA-256 and cheap extras (CRC, XXH); other algorithms are computed on demand only. Entries go stale with the
stamp. Only true whole-file hashes are cached under an algorithm name (ED2K is
chunked and BTIH is a torrent info-hash; they need their own names/definitions).

**CIX-12 Settings.** Rule editing and status (queued, failed, per-plugin
counts) live in Pathweft's Settings > Pathweft index, beside the folder roots; no
separate UI.

**CIX-11 `dupe:`.** Duplicate detection over content-indexed files compares the
internal key only, so it is exact and independent of displayed columns.

## Stages (when development starts)

1. Rules, dirty queue, protocol, native text/code extractor, Tantivy,
   `content:` (words and phrases), visibility (end to end on text and code).
2. Pathweft plugin extractor: PDF and Office (docx/xlsx/pptx via OfficeWorker).
3. Hash cache for the Hashes column; `dupe:`.
4. Optional: media metadata (last: names and tags are mostly reachable already
   and tag edits churn hashes), snippets, metadata fields, n-gram field for
   substring/regex search in code (earlier only if the owner asks), OCR.

## Decided in the Pathweft review (2026-11-19)

Extractor lifetime (on-demand worker mode of Pathweft.exe), plugin contract and
isolation, SHA-256 as the key, format order (text/code, then PDF/Office, media
last), words and phrases first, hash cache for every whole-file hash with
read-access check and local fallback, settings placement. All post-1.1.80.

## Open questions (owner)

- Substring/regex search inside code: needed early, or stage 4 as planned?
- Which folders and extensions should the first rules cover?
- Per-drive opt-in for H:, J: and T: (locally present files only), or leave
  cloud drives out of content indexing entirely?
