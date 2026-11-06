# Craft core refresh and additional preview engines

Assessment requested 2026-11-05; implementation authorized 2026-11-06. S16 is
ACTIVE under specification0.73. The source candidates below are downloaded and
their official tag-to-commit identities and archive digests are recorded. All
seven borrowed libraries now pass source and combined candidate qualification
as recorded in verification/2026-11-06-s16-craft.md. Final release and applicable
installed acceptance remain. Installed Craft selections remain separate.
The local maintenance handoff qualifies vendor applications; Pathweft's borrowed
source-built libraries require their own qualification.

## Existing cores

| Core | Previous Pathweft pin | Qualified S16 pin | Exact selected source |
| --- | --- | --- | --- |
| WordCraft | 0.15.1 | 0.15.2 | edfea68101220c9bdc4b2b0ccf4acd9612bedb6e |
| GridCraft | 0.16.0 | 0.16.1 | e581bc95b2a17d2a744a115478a20a1c2ad1ef72 |
| DeckCraft | 0.15.0 | 0.15.1 | 9bac60ff53029039c82083ee574c22d74c1541f1 |

Official comparisons: [Word](https://github.com/storytold/wordcraft/compare/v0.15.1...v0.15.2),
[Grid](https://github.com/storytold/gridcraft/compare/v0.16.0...v0.16.1),
[Deck](https://github.com/storytold/deckcraft/compare/v0.15.0...v0.15.1).
They include importer/model/layout/render/dependency changes, not only version
labels. Word's API comparison returns the 300-file ceiling; inspect the complete
source trees before adopting it. Preserve the existing native bridge boundary,
page/sheet/slide navigation, worker crash recovery and LibreOffice fallback.
Replay real supported-format and fidelity fixtures against old and new cores;
vendor CLI success alone is not an integrated acceptance result.

## Additional engines

| Priority | Candidate | Value over current Pathweft | Proposed borrowed scope |
| --- | --- | --- | --- |
| 1 | PhotoCraft 0.20.2 | Render layered documents rather than generic/embedded-only results | Read-only import and composition for native `.pcraft`, PSD/PSB, then individually qualified layered formats |
| 2 | VectorCraft 0.21.1 | Vector/native-artwork previews beyond text or shell thumbnails | Document/format/SVG/PDF-compatible AI import, artboard enumeration and raster rendering; qualify legacy AI/EPS separately |
| 3 | FigCraft 0.15.1 | Current `.fig` module reads an embedded thumbnail only | Native `.figcraft` document/frame import and rendering; actual `.fig` remains best-effort until verified |
| 4 | PdfCraft 0.18.2 | Potential navigation, text and rendering improvements over Windows.Data.Pdf | Evaluate PDF parse/render/text crates against the current viewer; replace only after an actual fidelity/resource comparison |

Sources: pinned-tag [PhotoCraft README](https://github.com/storytold/photocraft/blob/v0.20.2/README.md)
and [workspace](https://github.com/storytold/photocraft/blob/v0.20.2/Cargo.toml),
[VectorCraft README](https://github.com/storytold/vectorcraft/blob/v0.21.1/README.md)
and [workspace](https://github.com/storytold/vectorcraft/blob/v0.21.1/Cargo.toml),
[FigCraft README](https://github.com/storytold/figcraft/blob/v0.15.1/README.md)
and [workspace](https://github.com/storytold/figcraft/blob/v0.15.1/Cargo.toml),
[PdfCraft README](https://github.com/storytold/pdfcraft/blob/v0.18.2/README.md)
and [workspace](https://github.com/storytold/pdfcraft/blob/v0.18.2/Cargo.toml).
These document capabilities and reusable library crates; they do not establish
Pathweft fidelity, format coverage, DLL size or performance. PdfCraft currently
borrows Hayro for page drawing. Avoid adding a second PDF stack solely because
the standalone application was updated.

Use the agreed minimal source-built DLLs in a Pathweft-owned preview worker,
loaded on demand. Import parsing/rendering dependencies only; exclude each
application's GUI, CLI, MCP, collaboration, editing and service infrastructure.
Reuse existing sidebar/PiP/Windowed ownership, cancellation and archive leases.
Preserve originals and MOTW, and disable scripts/plugins/online resources.
No separately installed Craft application or end-user Coordinator is required.

All four candidate workspaces declare Rust 1.95 and MIT OR Apache-2.0. Qualify
their actual locked dependency graphs, optional codecs/fonts and redistributable
assets. Pin the required build toolchain for these cores without silently
changing the existing RAR toolchain. Corporate brand assets have separate terms;
do not assume a source license authorizes importing every logo or icon.

The subsequent2026-11-05 Coordinator0.20.8 handoff confirms all four selected
applications are now native-qualified: PhotoCraft0.20.2 (two-layer save/reopen,
PSD round trip and inspected PNG export), WordCraft0.15.2, GridCraft0.16.1 and
DeckCraft0.15.1 (document/formula/slide save/reopen and inspected rendering).
PhotoCraft's candidate-admission/lifecycle blocker is resolved. This qualifies
the standalone tools; it does not qualify Pathweft's borrowed DLLs or change its
current pins. No end-user gate or Coordinator dependency is introduced.

## Execution boundary

S16 is the single ACTIVE stage, after completed S23/1.1.25 and before S18.
First refresh the existing Office cores as one complete preview workflow. Then
qualify each additional engine's first real read-only preview before expanding
its format matrix. Establish the authoritative implementation requirements,
fixed source/dependency inventory, comparison fixtures and resource budgets
as that stage proceeds. Keep PDF replacement evidence-driven and preserve
an explicit working renderer/fallback until a replacement passes. Publish one
verified final release for the subsequently authorized implementation scope.

The2026-11-06 request also adds reusable, bounded located-text extraction to the
source cores and owned worker. Return strings with native paragraph/table,
sheet/cell, slide/object or PDF-page locations and continuation tokens; preserve
the rendering ABI. Explicitly report the imported-model coverage and unsupported
or image-only input. S19 owns the index/database and metadata refresh; OCR is not
included in S16. Source/API implementation is not extraction acceptance evidence.

No installed selection, MCP or Coordinator configuration changes in S16.
