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
