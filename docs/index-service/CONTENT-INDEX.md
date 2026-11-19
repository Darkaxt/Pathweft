# Content index and hash cache: design draft

Status: DRAFT for discussion (2026-11-19). Not in development; nothing here is
implemented or scheduled. Requirement IDs (CIX-nn) are provisional.

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
  Tantivy content index                           never runs as SYSTEM
  query: name/date filters + content:
  hash cache
```

- The service decides **what** must be read (it already sees every change) and
  owns the index. It never parses file contents itself as SYSTEM.
- The extractor decides **how** to read: native plain-text/code extraction, and
  Pathweft's C# plugins for other formats (PDF, Office, media metadata, ...).
  It runs in the user's context at low priority, so it can only read what that
  user can read, and a malicious file attacks an unprivileged process.

## Requirements (provisional)

**CIX-01 Selective rules.** Content indexing applies only to owner-configured
rules: folders (include/exclude), extensions, maximum file size, maximum text per
file. Default: nothing is content-indexed.

**CIX-02 Two layers.** `files`: file identity (volume + NTFS file ID, or folder
root key) -> version stamp (size, modified time, USN where available) and content
hash. `documents`: content hash -> extracted text and fields, indexed once, with
a reference count. A change is detected by the stamp, without reading the file.

**CIX-03 Content key.** One fixed internal algorithm, never user-configurable:
SHA-256 (in Pathweft's list; collision-proof; typically faster than the disk).
64-bit hashes are not used as keys (a collision would attach one file's text to
another). Alternative under discussion: XXH3-128 (faster, not yet in Pathweft).

**CIX-04 No needless re-extraction.** Moves within a volume keep the file ID: no
work. Copies, cross-volume moves and re-downloads produce a new identity whose
hash matches an existing document: link, do not parse. Only new content is
parsed. A metadata-only edit (for example MP3 tags) changes the hash and is
re-parsed; acceptable.

**CIX-05 Extraction protocol.** Over the existing pipe: `content_next` (batch of
identity, path, stamp), `content_put` (identity, stamp, hash, text chunks,
fields, extractor/plugin version) and `content_fail` (identity, stamp, reason;
retried only after the stamp changes or the plugin version changes). A stamp
that changed since the batch was handed out is discarded.

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
result (`hash_put`). The extractor may feed several cheap hashers in its single
read; other algorithms are computed on demand only. Entries go stale with the
stamp. Only true whole-file hashes are cached under an algorithm name (ED2K is
chunked and BTIH is a torrent info-hash; they need their own names/definitions).

**CIX-11 `dupe:`.** Duplicate detection over content-indexed files compares the
internal key only, so it is exact and independent of displayed columns.

## Stages (when development starts)

1. Rules, dirty queue, protocol, native text/code extractor, Tantivy,
   `content:`, visibility (end to end on plain text and source code).
2. Pathweft plugin extractor (PDF, Office, media metadata).
3. Hash cache for the Hashes column; `dupe:`.
4. Optional: snippets, metadata fields, n-gram field for substring/regex search
   in code, OCR.

## Open questions

- Extractor lifetime: only while Pathweft runs, or a headless worker started at
  sign-in (keeps the index current; one more background process)?
- Search style: whole words/phrases (documents) vs substrings (code); decides
  whether stage 4's n-gram field moves earlier.
- Which formats first (code/text, Office/PDF, media metadata)?
- SHA-256 or XXH3-128 as the internal key (Pathweft's view on adding XXH3-128).
- Plugin contract shape in Pathweft (stream -> text chunks + fields), plugin
  versioning, and whether plugins can run out of process.
- Should the hash cache also serve hashes computed for files outside the content
  rules (any file Pathweft hashes)? Proposed: yes, it costs only storage.
