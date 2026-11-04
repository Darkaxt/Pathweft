# Bundled Office preview cores and fallback

Pinned WordCraft0.15.1, GridCraft0.16.0 and DeckCraft0.15.0 source-built DLLs run
inside Pathweft's private preview worker. No Craft GUI, CLI executable, installed
Office product, MCP or Coordinator is required. `runtime-manifest.json` records
source commits, exact native members/imports, ABI1 and font inputs. Its older
CLI/release hashes remain historical comparison provenance only. Candidate
status records source/integration qualification; the combined delivered-package
and installed-update checks remain assigned to S11.

The minimal bridges live separately under native/office; existing donor code and
lock pins remain unchanged. Word/Deck return native premultiplied RGBA. Grid's
native print painter produces an in-memory PDF display list rasterised with the
existing Windows PDF API inside the worker, with no converter process/PDF file.
The user selected this worker for real native crash recovery; one job/request
lifetime owns models, source snapshot, pipes and actual process exit.

`*-core-dependencies.json` describes the actual feature/Windows-target-selected
normal/build graphs:142/93/100 packages respectively. These include build-time
packages, not an assertion that every package's code is linked. The older
`*-source-dependencies.json` contains full upstream workspace/CLI records only.
The isolated compiler is Rust1.95.0 x86_64-pc-windows-msvc, one compiler job,
with Windows SDK10.0.26100.0; the default host Rust toolchain is unchanged.
Use Prepare-CraftCoreSources.py, Prepare-CraftFontSources.py and Build-CraftCore.ps1.
Verify-CraftCoreLock.py rejects changed upstream lock records.

The Word/Deck release-pinned craft-fonts source revisions are recorded under each
core's `fonts` entry. Selected fonts are compiled into those DLLs; original
OFL/font/MIT/Apache notices are retained. `license-manifest.json` hashes all450
notices. Unchanged app-local VCRUNTIME140.dll comes from the official LibreOffice
MSI with the supplied Microsoft terms; other native imports are listed explicitly.

## Bundled LibreOffice recovery

The official LibreOffice26.2.7 Windows x64 MSI (source26.2.7.3,
commit08dfbf04dcac895fc3a8d30dbf5a00de1b9dda5e) is hash-checked and extracted as
data, never installed. `libreoffice-runtime-manifest.json` selects9124 members/
637027853 bytes: all format filters, native dependencies, private Python/UNO,
fonts, hyphenation and original LICENSE.html/license.txt/NOTICE are retained.
Interactive help/authoring assets, extra UI translations/icon themes and
spelling/thesaurus data are omitted with exact reasons/totals in the manifest.

Unsupported primary formats (including legacy PPT/ODP and Visio), failed imports,
failed renders and abnormal worker exit use this fallback once, after the old
owned process tree exits. Cancellation/input-policy rejection cannot start it.
The private headless pipe/profile avoids the system Office instance. Every load
is read-only, uses MacroExecutionMode=NEVER_EXECUTE, UpdateDocMode=NO_UPDATE and
abort-only interaction. Pathweft owns diagnostics/recovery; early crash dumps
cannot write into the installed program tree. Both-engine failures stay explicit.

`libreoffice-dependencies.json` records233 distributed PE import boundaries and
the official149-entry external-source download table plus build/patch recipe
hashes. That source table includes other platforms/build-only/omitted features;
it is not a falsely precise linked SBOM. The shipped original LICENSE.html
includes static third-party code terms. Complete official core/dictionaries
archives contain the matching recipes/download hashes; `make fetch` uses those
declared external inputs. Help/translation source packs are omitted because
those resource packs do not ship.

Stage-OfficeSources.ps1 verifies/stages complete donor/font archives,156 selected
Rust/Git dependency archives,450 notices, selected graph/runtime/source manifests
and both complete fallback source packs. The matching Pathweft source contains
the bridges, worker and preparation/build/verification scripts. Do not substitute
selected source snippets or a release URL for the complete corresponding source.

## Selected dependency advisory review

A2026-11-04 OSV review queried176 distinct selected-core/NuGet identities. Only
zip3.0.0 matched advisories (seven IDs including duplicate RustSec/GHSA records).
The used-path triage is specific to these read-only previews:

- All three locks contain zip3.0.0. The new ZIP extraction advisories concern
  filesystem extraction, symlinks/Unix permissions or AES entry decryption.
  The selected document readers read named parts into memory and have no
  ZipArchive::extract, ZipStreamReader or by_*_decrypt calls in their pinned
  source. This preview does not accept an ambient document password. The
  dual-header advisory concerns disagreement between central-directory and
  streaming extraction; this path neither performs streaming extraction nor
  uses archive entry names as output filesystem paths. These observations do
  not certify unrelated Craft commands or general ZIP extraction as safe.
  Primary records: [ZIP extraction allocation](https://rustsec.org/advisories/RUSTSEC-2026-0429.html),
  [AES authentication](https://github.com/zip-rs/zip2/security/advisories/GHSA-8hxf-qr8p-62x4),
  [dual-header parsing](https://github.com/zip-rs/zip2/security/advisories/GHSA-m665-v7pm-7grj).

The old CLI-only tokio/rustybuzz findings do not describe the selected core
graphs. No dependencies were upgraded or silently patched to make qualification
pass. Future dependency adoption still requires actual used-path, compatibility
and packaged-workflow verification.

The official [26.2.7 announcement](https://blog.documentfoundation.org/blog/2026/11/03/libreoffice-26-2-7/)
confirms Windows10+ and Python3.12.16. Its26.2 branch reaches end of life on
2026-11-30; qualify a supported26.8 successor before that maintenance boundary.
The [official advisories](https://www.libreoffice.org/security/) reviewed on
2026-11-04 list the applicable recent link/import fixes as addressed by26.2.5
or earlier; the selected26.2.7 contains those fixes. This is an advisory/pin
review, not a guarantee about all unknown parser faults or optional features.
