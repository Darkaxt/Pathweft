# S13 specialist preview contract

Source reference: QuickLook `2ec219aa18416c6e3b89d6aad3d82d10610f917f`.
Exact selected reference files/hashes are in vendor/QuickLook/source-manifest.json;
scripts/Prepare-SpecialistPreviewSources.py reproduces the import. Originals are
reference/source-distribution material; QuickLook assemblies are not loaded.
The following is the intended implementation matrix, not a passed-support claim.
All rows require actual integrated qualification under PREVIEW-21..36.

LibreOffice compatibility boundary: its Windows extension-registry initialization
still uses path-limited operations. A deeply nested configured cache can make
fallback startup unavailable even when the Craft core succeeds. Qualification
uses an isolated profile directly below its owned verification root, matching
the installed layout; it does not relocate data outside the chosen storage or
change Windows path policy. The same packaged runtime renders64pages with a
short profile and fails at startup with the deliberately lengthened profile.

PREVIEW-37 also belongs to this stage: Windowed moves the existing preview into
a resizable floating window and restores it to the sidebar when that window
closes. PiP moves it into a compact corner overlay inside Pathweft and frees the
sidebar column. Both pin the item and preserve the live renderer/navigation state;
neither creates a second renderer. Explicit preview close remains a separate action.
The latest user clarification requires the renderer's app icon left of the
filename, with no renderer-name suffix. In Office, that icon changes to LibreOffice
when fallback takes over; its tooltip explains the actual engine/recovery reason.
Use the existing Native/Original palette and Segoe icon language, with32DIP named
mode/close targets in the header. PiP defaults to at most400x320DIP at the bottom
right of PaneArea, above the command bar, with an opaque themed surface. Clamp it
to the available client area at narrow sizes. Windowed starts840x680DIP and can
resize to360x260DIP. Existing content controls reflow/scroll; preserve sidebar
width and text scale, keyboard access and real100%/150% DPI behavior. Inspect
actual Office/media/model/HTML rendering across moves, not just container bounds.

| Module | Declared input/feature scope | Selected implementation/dependency boundary |
| --- | --- | --- |
| CSV tables | CSV/TSV/PSV; quoted separators/newlines; header toggle, paged rows | CsvHelper33.1.0; BOM/encoding detection;16MiB,50,000 rows,256 columns; visible bounds |
| Databases | SQLite .sqlite/.sqlite3/.db/.db3/.sdb and LiteDB .litedb/.lite; schema, collections/tables and paged rows | Microsoft.Data.Sqlite.Core10.0.10, SQLitePCLRaw.provider.winsqlite3 2.1.11 (OS library), LiteDB5.0.21; encrypted/locked errors explicit; no source/sidecar mutation |
| Mail | .eml/.msg; headers, body and attachment names | MimeKit4.15.1, OpenMcdf3.1.4/RtfPipe2.0.7677.4303; existing isolated static HTML renderer; no attachment execution or remote resources |
| Certificates | .cer/.crt/.pem/.p7s/.pkcs7/.p12/.pfx/.mobileprovision/.certSigningRequest; public certificate/signature/provisioning metadata | Windows/.NET X509/PKCS; ephemeral key storage; protected/private-key-only inputs explicit; no trust changes |
| Application packages | Android .apk/.aab; Windows .appx/.appxbundle/.msix/.msixbundle/.msi/.msp; .ipa/.nupkg/.snupkg | Selected donor parsers; QuickLook.ApkReader2.1.0, read-only system MSI API; bounded archive entry reads; other package families not declared |
| Design thumbnails | .cdr/.fig/.kra/.pdn/.pip/.pix/.sketch/.xd/.xmind; real embedded image | Selected donor providers; existing archive and WIC decoding; per-format missing-thumbnail errors, no editor launch |
| Media information | Technical container/video/audio/subtitle/color report for media inputs; explicit selectable alternative to playback | QuickLook.MediaInfo26.5.0 managed C API with official signed MediaInfoLib26.10 native DLL; actual values, no duplicate player |
| Binary | .bin/.hex plus explicit alternative for other file inputs; navigable hex/ASCII offsets | Bounded seekable .NET file reads; no external editor |
| ELF | ELF, Mach-O and UImage; architecture/headers/sections/dependencies available in the file | ELFSharp2.17.3 and selected donor parsing; no executable loading |
| Crash dumps | .dmp/.dump/.mdmp/.hdmp/.minidump; minidump process/exception/thread/module records | Selected donor MinidumpReader; bounded stream/record reads; no automatic symbols/network |
| Prefetch | .pf; version-qualified run counts/times and referenced names, including compressed records | Prefetch2026.5.2; no access to unrelated system data during tests |
| CHM help | .chm; topic list and embedded static page/image navigation | Existing7-Zip entry API plus isolated static HTML renderer; no hh.exe/script/network |
| Shell CLSID | ::{GUID}; known/unknown registration and Shell identity inspection | Read-only registry metadata; no CoCreateInstance of the inspected class |
| Office | .doc/.docx/.docm/.odt/.xls/.xlsx/.xlsm/.xlsb/.ods/.ppt/.pptx/.odp/.vsd/.vsdx | Pinned Craft source-core DLLs hosted by a small Pathweft-owned worker, with required bundled LibreOffice fallback for unsupported formats and failed/crashed rendering. Full real page/sheet/slide navigation. Macros/link updates disabled, original/MOTW preserved, no associated application UI or installed Craft/Office/Coordinator dependency. Qualify actual content and recovery; app-workload coordination is development tooling, not a library requirement |

New NuGet dependency versions above follow the inspected donor baseline; qualify
their actual target-framework/native assets, terms and advisories before shipping.
Do not copy donor dependencies used solely for export/edit functions, obsolete
Framework shims, the external HexEditor UI, Zone.Identifier removal or duplicate
HTML/media/image hosts. Every imported package's transitive graph and notices
must be recorded from the actual restore and included in release provenance.

Visual contract: preserve Native/Original palette and existing Segoe UI/text
scaling. Specialist content fills the preview, with12-16DIP insets and one compact
32DIP action row where needed. Use existing themed icon buttons/tooltips and
accessible names. Tables virtualize rows/columns, scroll horizontally rather
than obscure values, expose current row/page and explicit limits, and remain
usable at320DIP side width and100%/150% scaling. Read-only metadata values are
selectable/copyable; errors appear in the preview. No debug paths in headings.

The stage plan records current integrated acceptance and its remaining inventory/
delivery requirements. This matrix does not claim every upstream variant or
separate add-on parity, nor a published S13 release.

Office checkpoint2026-11-04: the earlier pinned CLI app-chain baseline passed
through Coordinator0.20, including64 Word pages, separate workbook sheets and
five distinct slides. That historical comparison is not acceptance of the new
architecture. The user selected borrowed source cores, worker crash isolation
and LibreOffice fallback, and clarified workload-gate scope. No new gate identity
is a requirement for a Pathweft library. All three bridges compile with upstream
lock entries unchanged; actual page/sheet/slide and remaining declared-format
workflows pass with source/MOTW preserved, as recorded in the plan.
Word/Deck use direct donor raster APIs; Grid retains its native workbook and
uses its existing in-memory print painter with Windows PDF rasterisation inside
the worker. No CLI converter participates in the new primary path. Preserve
Grid's import warnings, including skipped embedded objects. The official
LibreOffice26.2.7 MSI matches its published hash and is extracted as data without
installation. Actual legacy PPT/ODP/Visio and forced-primary-exit recovery pass;
combined error, cancellation/policy exclusion and process/workspace cleanup are
verified. See vendor/Office for source/license/lock provenance. Final source/
inventory reconciliation and combined delivery remain; do not publish candidates.

Qualification decisions2026-11-04: do not adopt SQLitePCLRaw.lib.e_sqlite3 2.1.11
(GHSA-2m69-gcr7-jv3q/CVE-2025-6965); use the Windows SQLite boundary already used
by Pathweft, with actual native-version/runtime checks. Do not accept WiX7's OSMF
EULA on the user's behalf; MSI metadata needs only MsiOpenDatabase read-only.
Neither rejected dependency is a required architecture or deployed runtime.
MsgReader6.1.0's selected Windows asset adds MAUI/Win2D/WinUI and changes WebView2
to its WinRT projection, removing the existing WPF control reference. Use targeted
MIME/compound-file parsing for the required read-only mail content rather than
importing that unrelated presentation/runtime graph. No global WebView2 override
or WinUI application bootstrap is introduced.

Actual package qualification also found that QuickLook.ApkReader2.1.0's
netstandard assets omit the required public.xml embedded resource. Select the
same pinned package's resource-complete net462 assembly explicitly; it uses no
Framework host or extra runtime package. Its actual APK and AAB workflows must
pass on Pathweft's .NET10 host. QuickLook.MediaInfo's high-level constructor
registers an assembly resolver on every instance, which fails on the second
selection. Initialize Libs once lazily and own one low-level MediaInfoHandle per
request, preserving actual Close/Delete on disposal and no shared file handle.
