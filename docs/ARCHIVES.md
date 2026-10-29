# Native archive integration

Pathweft reads archives and writes ZIP/7z/TAR with the official 7-Zip 26.05 x64 `7z.dll` in process through
SharpSevenZip 2.0.130, source commit
`9b2379be603a9d4ebff06c56944f87f0c623f8db`, built as
`2.0.130-pathweft.1`. It requires no installed 7-Zip/WinRAR application, shell
extension or archive utility window. Pins, original interface references,
licenses and the reviewed interop patch are under `vendor/SevenZip` and
`vendor/SharpSevenZip`.

RAR writes use the Rust `rars` library, source commit
`e2415a2eb12e6b62deaa0cca919864eabe4671b1` (actual crate 0.12.3), behind
`native/pathweft-rars` and one synchronous C ABI. The source-built
`Rars/pathweft_rars.dll` is loaded from the app directory. No rars/WinRAR
executable is used for application operations. Source/lock/license/DLL provenance
is under `vendor/rars`; Cargo.lock pins the transitive dependency graph.

## Capabilities

| Container | Pathweft behavior |
| --- | --- |
| ZIP; ZIP-based .jar/.nupkg/.apk/.vsix/.whl | Browse, entry preview/open, copy, cut, rename, delete and paste. Package edits do not preserve or recreate package signatures. |
| 7z, TAR | Browse and the same verified edit/transfer operations. |
| RAR/RAR5/RAR7 | Native reading, previews and extraction; eligible single-file archives also support rename/delete/copy/cut/paste through the same queue. Compress to RAR creates RAR5. See the preservation limits below. |
| CAB, ISO, WIM, ARJ, CPIO, DEB, RPM, XAR, DMG | Native metadata/entry reading; Copy, extraction and eligible previews. Editing is disabled. CAB has real fixture verification; other formats delegate to the pinned native readers and are not all separately qualified. |
| GZIP, BZIP2, XZ, LZMA, Zstandard | Read-only compressed member browsing; native codec recognition still determines availability. GZIP has real fixture verification. |
| TGZ/.tar.gz | Existing streamed TAR/GZIP browsing retained; read-only. No whole-container extraction to browse. |
| .001 split containers | Recognized, with an explicit unsupported virtual-browsing message. No claim of multipart support. |
| Encrypted containers | Session password entry and eligible entry previews/copy. Qualified single-file RAR4/RAR5 can be edited after unlock; other encrypted formats remain read-only. Header-encrypted 7z reading is verified. Passwords stay out of settings/logs. |

The writable inventory is ZIP/7z/TAR plus RAR archives accepted by preservation
preflight. A recognized extension
does not prove that a damaged file, every codec variant or every encryption /
volume arrangement is supported. Native read errors produce an unavailable
pane, never a fabricated empty success. Traversal, Windows-ambiguous names,
case-ambiguous paths, duplicate entries, archive links and special TAR entries
are rejected. Filesystem transfer/extraction refuses unsafe reparse paths.
The Windows read-only file attribute also disables archive editing while
preserving browsing and Copy.

## RAR preservation limits

The writer preserves supported existing member data, names, directory markers,
attributes, timestamps, archive/member comments, solid state and encryption.
Every rewrite checks upstream preservation issues, then verifies retained metadata
through rars and all expected names/content through independent 7-Zip decoding
before atomic replacement. Existing legacy compression is reconstructed within the
source family; byte-identical compressed output is not promised.

Read-only layouts include multipart rewrites, SFX prefixes, recovery records,
links/redirections, unknown/incomplete metadata or codecs, ambiguous paths,
unqualified legacy encodings and embedded old-style legacy member comments.
The latter can trigger UnRAR comment errors even in an untouched source; Pathweft
does not accept an error exit as successful compatibility. Unsupported actions
show the preservation reason in the pane/menu tooltip. Reader support remains
independent when the optional writer DLL is missing or incompatible.

Solid RAR4/5/7 and header-encrypted RAR4/5 are qualified using source-built
fixtures and independent 7-Zip/official UnRAR decoding; this is a finite tested
matrix, not universal RAR parity. Rewrites can stage plaintext payloads even for
encrypted input; they are private operation-owned working files, not secure-erasure
storage. They survive only through the actual write/verification operation.

## Actions and publication

Open a container as a virtual folder in the normal pane. Tabs, breadcrumbs,
selection, sorting and shared context/keyboard actions keep their existing
owners. The archive preview module also lists physical containers directly.

Rename and transfers, including Compress to ZIP and whole-container Extract,
use the existing Ferry destination lane and transfer cards. There is no second
archive queue. Conflicts use the existing Skip, Keep both, Replace and Newer
decisions. Moves reserve their source and destination volumes in that same queue,
so a cross-volume archive cut retains its source owner through verified removal.
Archive cut metadata preserves virtual source identity; ordinary
Copy additionally exports selected real files for external CF_HDROP consumers.
The file list, explicit UTF-8 archive selection and DWORD drop effect are
published as native HGLOBAL data under the clipboard owner lock. Prepared older
copies and completed cuts cannot overwrite a newer clipboard sequence.
Cut from an archive is offered for Pathweft destinations; read-only sources
offer Copy instead. Skipped members remain at their source and appear in the
job's outcome.

Edits write to an adjacent stage, re-read the native result and compare every
entry's SHA-256 content and name before replacement. Inputs and original
container identity/content are checked before publication; the final archive
read handle denies concurrent writes during publication. Cancellation never
publishes a partially verified stage. A cut removes only verified unchanged
source members. A later failure can leave a verified destination alongside a
retained source; the card reports the failure rather than claiming a complete
move. Native calls are allowed to return and release their resources; no timer
or forced thread termination is used as cancellation completion.

## Open and preview ownership

Only selected entry content is extracted when a viewer or external application
needs a file. Internal previews release their copies after actual viewer work
finishes. External opens, drag exports and clipboard exports retain an ownership
record under the current Pathweft profile's `archive-content` directory.

Settings → Files → Archive working copies lists the source, entry names and
purpose. After closing the consuming application or finishing the clipboard /
drag operation, choose **Mark closed and release**. The action refuses an active
internal preview, open editor tab, locked file or substituted link. Unknown
files remain with their ownership record. Launch-process exit and elapsed time
are not evidence that an external consumer finished. Edits to an extracted open
copy are not automatically written back into its archive.

## Reproduction and delivery

Use the S4 dependency/source preparation scripts, `Build-Development.ps1`,
`Test-ArchiveContracts.ps1`, `Prepare-S4Fixtures.ps1` and
`Invoke-S4Verification.ps1` with an explicitly registered native/build scratch
root. The finite WPF/clipboard/consumer hooks are Debug-only. Their hidden menu
actions invoke actual listeners but do not establish physical popup placement.
ArchiveContracts also creates its small cross-volume fixture beneath the exact
`E:\Temp\pathweft-s4-cleanup-review` root; register and clean that root as well.

The wrapper patch fixes upstream empty-directory callbacks and ownership of
streams opened by `CompressFileDictionary`. Matching complete native/wrapper
source, the patch, build instructions, LGPL/BSD/unRAR terms and delivered DLL
hash verification belong to S8's final package. S4 does not publish a product
release.
