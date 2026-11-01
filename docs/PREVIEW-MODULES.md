# Internal preview inventory

S3 source inventory. Runtime acceptance is tracked separately in the stage plan.
Modules are compiled in, selected lazily and controlled by one persisted disabled
module set in Pathweft Settings → Files → Preview modules. Space, the side pane
and taskbar Preview/Space share the same selection and lifecycle implementation.

| Module | Included formats and behavior | Bounds / dependencies | Source |
| --- | --- | --- | --- |
| Text and code | Files passing bounded text/binary detection; BOM-aware UTF-8/UTF-16; read-only source text, including HTML/RTF/XML/code | First 256 KiB, visible truncation; .NET StreamReader and WPF TextBox | QuickLook TextViewer detection/priority adapted; File Labs read-only text presentation |
| Markdown | .md, .markdown, .mdx, .mmd, .mkd, .mdwn, .mdown, .mdc, .qmd, .rmd, .rmarkdown, .apib, .mdtxt, .mdtext; CommonMark headings, paragraphs, lists, emphasis, code, quotes and rules | First 1 MiB, visible truncation; Markdig 1.5.0 and native WPF FlowDocument; embedded HTML is inert, links are labels, images show alt labels; no remote resource loading or embedded script/diagram execution | QuickLook MarkdownViewer extension/selection/lifecycle adapted; its browser/template pipeline replaced by native WPF |
| Images | .jpg, .jpeg, .png, .gif, .bmp, .webp, .ico, .tif, .tiff, .heic through available WIC decoders; first frame | 64 MiB / 200 megapixels; decoded longest dimension at most 2,000 pixels; optional HEIC/WebP availability depends on installed codecs | Selected QuickLook image lifecycle/format contract; existing File Labs WPF BitmapImage path, now backed by a closed source read |
| PDF | .pdf with Previous/Next page controls and a truthful page indicator; document scripts do not run | 64 MiB; Windows.Data.Pdf with in-memory source, one page render requested at 1,400 DIP per request (physical pixels depend on Windows rendering scale); password/encryption/renderer failures report unavailable | Existing File Labs Windows PDF renderer adapted to module ownership and bounded page navigation |
| Archive and folder listing | Native archive formats listed in [ARCHIVES.md](ARCHIVES.md); expandable folder/file tree with icons, concise names and sizes for containers/virtual directories; existing Stash/Backup Labs explicit actions | All archive metadata, WPF rows created on expansion with virtualization; filesystem folder text remains limited to 1,000 names; no archive extraction or execution | Selected QuickLook selection/lifecycle; File Labs sidebar tree presentation; S4 native archive integration |
| Video and audio | .mp4, .m4v, .mov, .wmv, .avi, .mkv, .webm, .mpg, .mpeg, .3gp, .ts; .mp3, .wav, .wma, .m4a, .aac, .flac, .ogg, .opus | Bundled libmpv/FFmpeg/libplacebo, native D3D11 gpu-next child window with hardware decoding; Dolby Vision metadata maps to BT.709/sRGB SDR. Space autoplays; side pane pauses until Play; icon playback/mute controls, seek and duration. Native events drive state and cleanup. No installed codec/player dependency | Selected native-window/client API approach from Darkaxt/vlc and Darkaxt/NuvioDesktopDV; exact runtime/source pin under vendor/MPV; retained optional Reel metadata |
| Fonts | .ttf, .otf specimen at several sizes; no installation | 16 MiB; WPF font rendering; scrollable sample | Selected QuickLook font selection/lifecycle; File Labs native specimen |
| 3D models (F3D), disabled by default | Actual bundled-reader inventory, including GLB/glTF, OBJ, STL, PLY, FBX, STEP and USD; native materials/resources; orbit/pan/zoom, Fit/Reset and keyboard controls; animation clips/playback/timeline; FXAA, ambient lighting/occlusion, tone mapping and reflective grid | Native libf3d4.0.0 offscreen RGBA frames sized to viewport/DPI, copied directly to WPF BGRA; no model-size/JSON cap, no editing; engine/reader/machine limits apply | F3D native C API and pinned source, integrated under the internal viewer contract |

Higher numerical priority wins; equal priorities use ordinal module ID. Markdown
has priority 100, other typed viewers 90, F3D 80 and text -5. Existing image/archive
viewers retain priority over overlapping F3D readers. Disabled modules are excluded
before construction/Init. A readable text alternative can handle a disabled
specialized format; otherwise the preview explicitly says no enabled module can
display it. Decoder/load failures report an error rather than launching another
product or silently claiming a successful preview.

HTML (.html/.htm, priority90) renders static document layout, local CSS, images
and fonts in an isolated WebView2 profile, separate from the internal editor.
Scripts, online resources, frames, forms, popups, downloads and native bridges
are disabled. Pinned HtmlSanitizer/AngleSharp HTML and CSS parsers remove active
content and remote URLs before Chromium can preconnect. Sanitized presentation
keeps supported static layout; unsupported/active constructs are removed.
Resources stay within the document folder (or the containing
archive); links/reparse paths and unsupported resource types are rejected.
Archives stream only requested entries without extracting working copies.
Documents are limited to16MiB, individual assets64MiB and the preview's total
resource budget128MiB. Missing WebView2 or load failures are explicit; disabling
HTML in Settings > Files returns to source text. Source styling remains intact.
Selection/close observes browser exit before deleting its unique cache profile.

GLB/glTF animation timelines inspect stored curves once at load and trim a held
ending by default. The timeline's **Trim idle tail** context-menu checkbox restores
the full original duration. Native time coordinates and source files are unchanged.
Internal pauses, later movement and small/slow motion remain included; static,
unsupported curves (including cubic/packed morph) or mismatched native ranges
keep their full duration. No per-frame analysis or additional redraw is added.

Each surface has its own request generation, viewer instance and cancellation
source. A new selection clears the old content immediately. Rendering and page
callbacks check the current generation; canceled work is observed through actual
completion. Closing or disabling releases the active viewer, media and content;
application closing drains owned work before exiting its dispatcher. No fixed
settling delay or timer decides load/cleanup completion.

F3D owns a dedicated STA render thread and request queue for OpenGL affinity.
Disable, selection change and close finish pending requests, destroy the native
engine on that thread and observe its actual exit. Frames cross into WPF in
memory; the native renderer never creates a visible preview window. Presentation
uses raw lower-left RGBA converted to top-left BGRA, without PNG encoding or
decoding during input. Completed monotonically ordered frames remain eligible
while newer gestures are pending; content-generation cancellation still excludes
old selections. Ordered mouse samples use selected pinned F3D default-controller
math, preserving the fixed model-up axis, temporary-up behavior and pole limits.
Camera updates omit the native interactor's immediate per-event Render calls;
the render worker performs the batch's explicit frame capture afterward. The
original controller remains a Debug-only verification oracle, including its
position-before-modifier event ordering. Right/middle/Shift-left pan is retained.
Native frame dimensions follow actual viewport layout/DPI and
camera framing adjusts for portrait aspect ratios. Explicit diagnostic PNG
exports are generated only on demand. The cache
uses the application profile. No F3D executable, shell extension or registry
integration is used. Animation evaluates on the same native thread; playback
follows WPF frame events with one pending render, and releases that subscription
on pause, selection change, disable and close. Gesture requests arriving during
a render are applied together before the next frame. No delay controls completion.

Standalone QuickLook named-pipe dispatch and Windows shell preview handlers are
absent from this governed path. The retained PreviewHandler.cs source is not
instantiated by these modules. Executables/macros/scripts are never invoked by
preview selection. Explicit existing user actions such as Open backup/Reel remain
separate clickable actions, with no automatic dispatch.

## Pinned source and distribution

QuickLook revision: `2ec219aa18416c6e3b89d6aad3d82d10610f917f`. Selected original
files/hashes are in vendor/QuickLook/source-manifest.json and corresponding
`.cs.txt` reference files. The adapted contract/context, module selection and
viewer implementation live under Preview/. Ported QuickLook files carry
GPL-3.0-or-later headers; preserve COPYING-GPL-3.0.txt and vendor notices. The
original File Labs MIT license remains in LICENSE. The combined derived product
is distributed under GPL-3.0-or-later; S8 owns matching binary/source packaging.

Markdig is pinned to 1.5.0, source revision
`9900e46eacd9e17ba32f1b441a349bcab61777af`, BSD-2-Clause. The restored package
contains a net8.0 assembly with no additional runtime dependency in that target.
See its [package metadata](https://www.nuget.org/packages/Markdig/1.5.0),
[parser source](https://github.com/xoofx/markdig/tree/9900e46eacd9e17ba32f1b441a349bcab61777af)
and vendor/Markdig notices. WPF/WIC/PDF/media native components come from Windows
and the selected desktop runtime, except media's bundled MPV dependency. No legacy
.NET Framework QuickLook host or downloaded plugin assembly is introduced.
Static HTML reuses the application's
existing WebView2 dependency in its own environment, without the editor bridge.

F3D is pinned to 4.0.0, source revision
`b72c7b5c9244e4a0c12975ce6546645c1a8b2680`. The official Windows x64 SDK archive
SHA-256 is `98E3DB83A0E486D1A5D69F0D10D0B018432E9D200C8FECD0020AFC7DA26AB71E`.
Selected original API headers/example, their hashes, BSD-3-Clause and dependency
notices are in vendor/F3D. The pinned dependency cache contains native DLLs and
licenses, excluding the shell-extension DLL and all executables. Build with
`scripts/Build-Development.ps1` after preparing that dependency; S8 must verify
the actual delivered native bundle and its corresponding notices.
