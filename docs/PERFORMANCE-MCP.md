# Performance analysis and optional read-only MCP

Click a performance card to open analysis; Back to files or Escape restores
the existing browser. Live activity uses the shared rolling hour. Recorded
ranges require Record history, default off. Retain 1, 7 or 30 days in the
configured Pathweft data location (Storage → Change location includes it).
Aggregate observations are stored every five seconds; a selection of heavy
processes every ten seconds. This is not a complete process execution audit.
Gaps and unavailable counters are not zero activity. Folder/file I/O is live
only and is never added to background history. Recording off preserves old
observations; retention is applied while recording.

Expanded analysis adapts SysOptimizer's History graph: peak-preserving rounded
steps, grouped event markers, a time cursor and overview navigation. Hover to see
recorded apps near a moment; click to pin, click the pin again to release it.
Drag across the graph or overview to select a range, scroll to zoom, or right-drag
to pan. Now clears the selection. Left/right keys inspect moments and up/down
visit event markers. Tables show the selected moment/range; missing snapshots
are explicit. Chart peaks are separate from the interval averages returned by
summary queries. Events remain collection/counter/gap facts and sampled spikes;
this view does not add a process-launch audit, temperatures or new collectors.

No MCP client is activated or configured by Pathweft. After a separate explicit
activation decision, a client can launch the installed Pathweft.exe with the
single argument `--performance-mcp` over stdio. Use the same portable location
or PATHWEFT_PROFILE as the browser to select its recorded history. This entry
reads local history without launching a browser, collector, elevation or network
listener. `get_current_usage` means latest recorded observation plus its age;
it may be absent or stale when recording is off or Pathweft has exited.

Tools: get_current_usage, get_usage_summary, get_top_processes, find_spikes,
get_app_history and get_events. `last_minutes` defaults to60, up to43200, or
use paired `from`/`to` ISO timestamps with an explicit timezone, up to30 days.
Metrics: cpu, ram (percent in summaries, bytes for processes), gpu, disk (bytes/s).
Select a GPU/physical-drive ID from current usage with `device`; historical
process disk activity is unavailable. App history requires `name`. `threshold`
defaults to80; `limit` is1..500 and truncated detail counts are reported.
Aggregates always include all matching stored observations. Sampled spikes
identify observed exceedances, not causes. Events cover collection/gap/counter
state, not security or exact app launches/exits. No mutation tools are exposed.

MCP stdio lifecycle and negotiation follow the [protocol specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/lifecycle)
and [transport contract](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports).
Supported versions:2025-06-18 and2024-11-05. Unknown proposals negotiate2025-06-18.
Requests are limited to65,536 characters. Closing stdin ends the server.
