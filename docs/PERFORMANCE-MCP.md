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

Agents on this PC reach the MCP through LLM Coordinator's Pathweft gate (since
Coordinator 0.29.31): `coordinator-client gate run --tool pathweft`, described by
the installed `pathweft-monitor` skill. The gate launches the installed
`C:\Program Files\Pathweft\Pathweft.exe --performance-mcp` itself, binds to the
server name "Pathweft" rather than a version, so updates need no rebinding, and
admits only `get_`/`list_`/`find_`/`search_` tools. Do not register the executable
directly as a per-chat MCP server. Pathweft keeps the `--performance-mcp`
argument and the server name stable, and gives every tool one of those prefixes.
Outside the gate, any client can start Pathweft.exe with the single argument
`--performance-mcp` over stdio. Use the same portable location
or PATHWEFT_PROFILE as the browser to select its recorded history. This entry
reads local history without launching a browser, collector, elevation prompt or network
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

The app activity recorder adds get_run_history, get_workloads,
get_top_consumers, get_app_usage and get_activity_intervals. They take
`last_minutes` (default 1440, up to 43200) or `from`/`to`, an optional `name`
(part of the executable path, and for get_run_history also of the command
line; required by get_app_usage), `metric` (cpu, io,
memory or starts) and `limit` (1..500, default 50).

- get_run_history: exited program runs overlapping the range (path, PID, parent,
  start/exit, CPU, I/O, peak memory, exit code, launching workload) and, since
  1.1.88, `command_line` (passwords, tokens, keys and URL credentials masked; at
  most 2,048 characters) and `working_directory` (null when the process ended or
  was protected before it could be read). Runs recorded earlier have nulls.
  Short light processes are only counted in their workload.
- get_workloads: launching process trees, such as one build command, with
  process count, CPU, I/O, largest peak memory, duration and the first
  process's `root_command_line`.
- get_top_consumers: per-executable totals over the range from 5-minute usage
  buckets, long-running programs included.
- get_app_usage: the 5-minute buckets of executables whose path contains `name`.
- get_activity_intervals: recorder intervals with active, idle (no input for 5
  minutes) and locked state, lost-event counts and off gaps.

Since 1.1.68 the same server is named Pathweft and also answers these read-only
tools. Paged tools take `offset` and `limit` (1..500, default 100) and return
Status, Rows, Total, Offset and Truncated. Arguments outside each schema are
structured errors, and paths must be absolute local or UNC.

- get_maintenance(category, target): rows of Startup, Services, Windows tweaks,
  Windows apps or Security check as the Maintenance tab shows them, read as the
  signed-in user. Cleanup lists each target (or one `target` id) with its size,
  file count and the method used: the Pathweft index for the temporary-file,
  error-report and thumbnail folders, the package manager's own report for its
  cache, otherwise a direct measurement. User temporary files means the
  signed-in user's TEMP folder, not the TEMP of the program that started the MCP. This is a size report, not the exact
  review the tab performs before cleaning; nothing is removed.
- get_maintenance_changes: Pathweft's Maintenance change journal (id, kind,
  phase, what it saved, whether it can be restored). It records no times.
- list_folder(path), get_file_details(path): metadata only (sizes, times,
  attributes, link targets, executable version details); contents are never read.
- get_folder_size(path): the Pathweft index's folder size, otherwise Pathweft's
  cached size if still current, otherwise unknown. It never starts a scan.
- search_files(query, match_path, match_case, whole_word, regex, sort,
  descending, offset, limit): the Pathweft index service with Everything-style
  syntax (docs/index-service/PROTOCOL.md); unsupported functions are refused with
  a message. Sort by name, path, size, extension, created or modified. Returns
  path, type, size, created, modified and attributes, with the total; the status
  says when the index is still updating. An unavailable index is reported, and
  the MCP never restarts it.
- get_boots, get_system_changes, get_insights: Windows starts, updates and
  driver/feature/app changes, and derived findings as in the Insights view.
  They take `last_minutes` (default 30 days) or `from`/`to`. Installed-app
  changes come from baselines Pathweft already saved; no new baseline is taken.
  Windows lets only administrators read the boot diagnostics log; see below.

Nothing is applied, cleaned or uninstalled. Since 1.1.69, reads that Windows keeps
for administrators use the administrator session (boot diagnostics, the Security
check, folders only administrators can list). The installed copy starts that session
through its on-demand task after your one approval, never with a prompt, and ends it
when the MCP exits. Without it (a portable copy, never approved, or turned off) these
read as the signed-in user and say what could not be read.

MCP stdio lifecycle and negotiation follow the [protocol specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/lifecycle)
and [transport contract](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports).
Supported versions:2025-06-18 and2024-11-05. Unknown proposals negotiate2025-06-18.
Requests are limited to65,536 characters. Closing stdin ends the server.
