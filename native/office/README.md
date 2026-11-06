# Borrowed Office cores

Each bridge is staged as `apps/pathweft-office-bridge` in its exact hash-checked
donor source workspace. Existing upstream files are preserved. Its workspace
dependency pins, patches and original lock govern compilation; only the bridge
package and its required crates are built. GUI, CLI and MCP applications are not
runtime dependencies. `common/ffi.rs` defines the current C ABI; imported models
stay alive until the preview worker closes them.

WordCraft and DeckCraft return the donor renderer's premultiplied RGBA pixels.
GridCraft's non-UI print painter returns its in-memory PDF display list; the
worker retains that list for the selected sheet and rasterises requested pages
with the Windows PDF API already used by Pathweft. There is no converter process,
PDF file, donor GUI, or separate PDF application in this path. Changing pages
does not reimport the workbook or regenerate its display list.

The worker is a private mode of Pathweft's packaged executable, selected before
WPF starts. The browser owns its redirected request pipe and kill-on-close
Windows job, assigns the process before sending the first document request, and
observes its actual exit before fallback. Navigation requests are serialised;
cancelled navigation responses are drained so binary framing stays intact.
Preview lifetime cancellation closes the owned process tree and cannot start
another renderer. Native process faults are recovered by the browser, not by
claiming that a managed exception handler can catch every native fault.

The agent's managed workload gates coordinate registered tool execution and
resource use. They are not linked into these libraries, required by this worker,
or installed as an end-user dependency.

## Located text extension

The additive `pw_office_extract(document, unit, offset, limit)` export preserves
ABI v1 rendering. It returns a UTF-8 JSON C string, owned by the document until
the next extraction/close. Render calls do not invalidate that text; extraction
does not invalidate a rendered frame. A null pointer uses `pw_office_error`.
The worker invokes it serially and copies the reply before continuing.

`limit` is a serialized-byte bound (1024..49152), not a document truncation.
Start at `(0,0)` and use the returned `next` token until null. Each record has
`location`, `offset` (UTF-8 bytes within that native text unit) and `text`.
`coverage` describes exactly what the imported model exposes. Word returns final
paragraph text excluding tracked deletions, with zero-based native block paths
through tables and story/part identity. Grid returns stored cell values with
zero-based sheet/row/column plus an A1 address; it executes neither formulas nor
Python. Deck returns model-order shape/group/table paragraphs and speaker notes
with zero-based slide/object paths. Unsupported imports/objects are not inferred
from rendered pixels. No OCR or retained full-document text copy is introduced.

The worker's `extract` command carries the JSON as a separately length-framed
binary payload. Cancellation drains that entire payload before another request.
`OfficeDocument.ExtractText` exposes one bounded batch to later consumers;
rendering recovery through LibreOffice reports extraction as unsupported rather
than silently claiming Craft coverage or starting an external extractor.

## PhotoCraft

The same worker/ABI hosts the source-built PhotoCraft library explicitly, with
no LibreOffice recovery for image inputs. Native ZIP `.pcraft` documents use
the donor bounded loader; PSD/PSB use its real layer importer. The donor CPU
compositor renders the layer tree, converts its output profile to sRGB and
returns premultiplied RGBA. It is not an embedded-thumbnail-only path. Input
remains64 MiB; the photo-specific decoded allowance is256 MiB and the canvas
limit64 megapixels. Requested output fits a4096-pixel longest side. Directory
bundles and other PhotoCraft importers are not registered by this module.

Editable type is returned in bottom-to-top layer-tree order with zero-based
`layerPath`, `layerId`, layer name and effective `visible` state. Hidden type is
included with `visible=false`; smart-object contents and flattened pixels are
outside this extraction coverage. A document with no imported type returns
`image-only`, not an inferred OCR result. Returned import notes remain visible
in the normal preview, and the renderer's identification icon uses its pinned,
permissively licensed app artwork.

## VectorCraft

The source-built vector core imports native `.vectorcraft`/legacy `.drawcraft`,
SVG/SVGZ and PDF-compatible AI into the donor document model, and renders one
selected artboard through its CPU renderer. The artboard selector uses that
model's names and bounds. Output fits a4096-pixel longest side. Existing PDF
viewing remains a separate module. Legacy PostScript AI/EPS are not exposed by
this bridge: the donor has a separate interpreter whose format coverage is not
qualified here. PDF-compatible AI reports that private editing data is outside
its coverage. SVG external-file callbacks and native placed-file readers are
not installed; only embedded resources or their saved previews are available.

Editable text returns layer-tree order, zero-based `objectPath`, native object
identity/name, document transform and effective visibility. Outlined glyphs,
symbol-instance expansion, placed-document text and OCR are outside coverage.
The bounded batch/continuation interface is shared with the other cores.

## FigCraft

The source-built design core presents page overviews and top-level frames,
components and sections. Native `.figcraft` input uses the bounded document
loader; `.fig` uses the donor's best-effort binary importer and reports its
approximation notes. A valid embedded Figma thumbnail retains priority in the
existing module. The tested binary fixture is a donor export/import round trip,
not evidence of universal Figma fidelity. CPU frames fit a4096-pixel longest side.
Editable text returns page/tree order with node paths, identities, transforms
and effective visibility. Comments, history, outlined text and OCR are excluded.

The pinned FigCraft source uses an unstable unsigned low-bit API. The tracked
`scripts/Apply-CraftCompatibility.py` verifies the original source hash and
replaces its two uses with the stable equivalent `i & i.wrapping_neg()`.
Sweep budgets and behavior are unchanged; the build/runtime manifest records
the original and patched hashes for reproducible corresponding sources.

## PdfCraft

PdfCraft's selected COS/model/font/text/render libraries provide an optional
PDF module; Windows PDF retains default priority. The owned worker exposes
page navigation and the same additive extraction API. Text comes from the PDF
interpreter, with zero-based page/line positions, glyph ranges and rectangles
in displayed page-view points (top-left origin, y down). Encoding/ToUnicode
fallbacks and reconstructed reading order are not OCR or guaranteed authoring
paragraphs. The worker caches only one interpreted page for continuation.
The cursor encodes page and line; consumers must follow returned tokens.
Blank pages report empty; pages without glyphs that have image resources report
image-only. Resource inspection is bounded to16 levels/10,000 dictionary objects
per page. Failure to identify an image resource is not proof the page is blank.
Interpreter truncation warnings reject extraction rather than implying complete
text. Document scripts, linked resources, OCR and editing APIs are not exposed.
