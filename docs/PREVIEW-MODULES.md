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
| Archive and folder listing | .zip, .jar, .nupkg, .apk, .vsix, .whl; directories; existing Stash/Backup Labs metadata and explicit Open action where recognized | First 5,000 ZIP names / 1,000 folder names with visible limits; System.IO.Compression; no extraction, execution or implicit external utility | Selected QuickLook archive selection/lifecycle; File Labs listing and backup metadata/actions |
| Video and audio | .mp4, .m4v, .mov, .wmv, .avi, .mkv, .webm, .mpg, .mpeg, .3gp, .ts; .mp3, .wav, .wma, .m4a, .aac, .flac, .ogg, .opus | Installed Windows codecs via WPF MediaElement. Space playback, side-pane pause until Play; Enter toggles and Left/Right seek in the Space window; position refresh is display-only. No codec downloads or external player dispatch | File Labs playback and optional Reel metadata, owned per selected module instance |
| Fonts | .ttf, .otf specimen at several sizes; no installation | 16 MiB; WPF font rendering; scrollable sample | Selected QuickLook font selection/lifecycle; File Labs native specimen |
| 3D prototype (F3D), disabled by default | Version-2 .glb with embedded resources; textured initial-pose rendering; Orbit left/right, Zoom in/out and Reset view | 64 MiB model / 4 MiB JSON chunk; external resource references rejected; native libf3d 4.0.0 C API, offscreen 960×720 render; no animation playback or editing | F3D's native library API and offscreen example adapted to the internal viewer contract |

Higher numerical priority wins; equal priorities use ordinal module ID. Markdown
and F3D have priority 100, other typed viewers 90 and text -5. Disabled modules are excluded
before construction/Init. A readable text alternative can handle a disabled
specialized format; otherwise the preview explicitly says no enabled module can
display it. Decoder/load failures report an error rather than launching another
product or silently claiming a successful preview.

Each surface has its own request generation, viewer instance and cancellation
source. A new selection clears the old content immediately. Rendering and page
callbacks check the current generation; canceled work is observed through actual
completion. Closing or disabling releases the active viewer, media and content;
application closing drains owned work before exiting its dispatcher. No fixed
settling delay or timer decides load/cleanup completion.

F3D owns a dedicated STA render thread and request queue for OpenGL affinity.
Disable, selection change and close finish pending requests, destroy the native
engine on that thread and observe its actual exit. Frames cross into WPF in
memory; the native renderer never creates a visible preview window. Its cache
uses the application profile. No F3D executable, shell extension or registry
integration is used. The prototype renders the model's initial pose; the presence
of animation clips in a GLB does not imply animation playback support.

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
is distributed under GPL-3.0-or-later; S6 owns matching binary/source packaging.

Markdig is pinned to 1.5.0, source revision
`9900e46eacd9e17ba32f1b441a349bcab61777af`, BSD-2-Clause. The restored package
contains a net8.0 assembly with no additional runtime dependency in that target.
See its [package metadata](https://www.nuget.org/packages/Markdig/1.5.0),
[parser source](https://github.com/xoofx/markdig/tree/9900e46eacd9e17ba32f1b441a349bcab61777af)
and vendor/Markdig notices. WPF/WIC/PDF/media native components come from Windows
and the selected desktop runtime. No legacy .NET Framework QuickLook host, browser
preview runtime or downloaded plugin assembly is introduced.

F3D is pinned to 4.0.0, source revision
`b72c7b5c9244e4a0c12975ce6546645c1a8b2680`. The official Windows x64 SDK archive
SHA-256 is `98E3DB83A0E486D1A5D69F0D10D0B018432E9D200C8FECD0020AFC7DA26AB71E`.
Selected original API headers/example, their hashes, BSD-3-Clause and dependency
notices are in vendor/F3D. The pinned dependency cache contains native DLLs and
licenses, excluding the shell-extension DLL and all executables. Build with
`scripts/Build-Development.ps1` after preparing that dependency; S6 must verify
the actual delivered native bundle and its corresponding notices.
