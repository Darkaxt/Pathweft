# Pathweft specification

Version 1.74 - 2026-11-27. Owner: Darkaxt.

### Read-only MCP expansion (S48)

Owner direction (plan, after S47 and before S12): extend the existing explicit stdio
`--performance-mcp` server so a client can read Maintenance, file metadata and boot and
change history as well as performance. MONITOR-12 and ACTIVITY-05 constraints still apply.
The S48 release also carries the process table corrections MONITOR-24 and the Office
preview fix PREVIEW-39 (issue #4).

| ID | Required behavior and acceptance |
| --- | --- |
| MCP-01 | One server, same entry point (`Pathweft.exe --performance-mcp`), renamed "Pathweft" in serverInfo; existing tool names and results are unchanged. Every new tool is read-only: it never applies, restores, deletes, uninstalls, moves or writes anything outside Pathweft's own temporary review files, which it removes before replying. No elevation prompt, WPF window, sampler, network listener, Everything restart or MCP client configuration. Inputs are validated against a closed schema; output is bounded (`limit` 1..500, default 100) with total and truncation reported; failures are structured tool errors, and data that needs administrator rights is read through the administrator session (MCP-06) or, without one, reported as unavailable, never guessed. Requests run one at a time; closing stdin cancels the running one and ends the server. |
| MCP-02 | Maintenance. `get_maintenance(category)` for Startup, Services, Windows tweaks, Windows apps and Security check returns the same rows the Maintenance tab shows: id, name, scope, state, recommendation, goal, risk, publisher/version where present and the one-line summary. Cleanup returns each target with its size and file count from Everything's index (folder sizes where indexed, otherwise the sum of the matching indexed files, e.g. `thumbcache_*.db`), answering in about a second; only a target Everything does not index, or a package-manager cache sized by its own tool, is measured directly, and the row says which method was used. This is a size report, not the exact deletion review the tab performs before cleaning. `get_maintenance_changes` lists Pathweft's journal of applied changes (id, kind, phase, what original settings it saved and whether they can be restored; the journal records no times). No action tools. |
| MCP-03 | Files, metadata only. `list_folder(path)` returns entries (name, folder/file, size, modified, attributes) sorted folders-first then name. `get_file_details(path)` returns size, created/modified/accessed times, attributes, link target, file and product version, product, company and description for executables. `get_folder_size(path)` returns the indexed size when Everything indexes folder sizes, otherwise Pathweft's cached size when it is still current for the folder's modification time, otherwise unknown; it never starts a full scan. `search_files` is at least as capable as the installed everything-search skill (es.exe): the full Everything search syntax passed through unchanged (wildcards, `path:`, `parent:`, `ext:`, `size:`, `dm:`/`dc:`, `regex:`, boolean operators and functions), match-path, case, whole-word and regex switches, sort by name, path, size, extension, created or modified in either direction, `offset` paging over the full result set with the total reported, and per result the full path, file or folder, size (indexed folder sizes for folders), created and modified times and attributes. It reports when Everything is missing, busy or not answering, and never restarts it. File contents are never read. Paths must be absolute local or UNC; access errors are reported per path. |
| MCP-04 | Boot and changes. `get_boots` returns recorded Windows starts (time, seconds to desktop, settled seconds, startup app count, what slowed it and by how much). `get_system_changes` returns Windows updates and driver/feature changes from Windows' logs plus the installed-app changes Pathweft has already saved; it does not take a new installed-app baseline. `get_insights` returns the same derived findings as the Insights view (memory pressure, large memory holders, sustained CPU, slower startup). All three take `last_minutes` or `from`/`to` like the existing tools. |
| MCP-05 | Verify with the real built executable as a subprocess: tools/list contains old and new tools with read-only annotations; each new tool answers on a fixture profile and on this PC; invalid arguments, unknown categories, relative paths and missing files give structured errors; Cleanup measurement leaves no files behind and the Maintenance journal, settings and baseline files are byte-identical before and after; no elevated or windowed process starts; stdin close mid-request exits promptly. Document the tools in PERFORMANCE-MCP.md. |
| MCP-06 | Owner direction 2026-11-15 ("There is no point in restricting read-only operations"): read-only queries that Windows restricts to administrators use the approved administrator session. That covers boot diagnostics (`get_boots`, the boot comparison in `get_insights`, and the Boot diagnostics and Insights views), the Security check, and `list_folder` on a folder only administrators can list. The Pathweft window uses its session when one is running. The MCP server starts its own session through the installed on-demand task (ACCESS-08), which never shows a prompt, and ends it when the server exits. Without a session (portable copy, never approved, or turned off) these read as the signed-in user, as before, and say so. The session adds read-only worker actions only; nothing is changed. Verify that the worker's boot action exists, that an unapproved MCP launches no helper and reports the limit, and that the installed copy's MCP reads boots after the update. |
| MCP-07 | Owner direction 2026-11-21 ("I deleted AppControl to replace it for this. You should have managed with LLM Coordinator the creation of the MCP gate control!"): agents reach the MCP automatically through LLM Coordinator's Pathweft gate (`gate run --tool pathweft`, skill `pathweft-monitor`, Coordinator 0.29.31), never by registering Pathweft.exe directly in a client's configuration. The gate launches the installed `Pathweft.exe --performance-mcp`, binds to the server name "Pathweft" for every version and admits only `get_`/`list_`/`find_`/`search_` tools. Pathweft keeps that argument and server name stable and names every new tool with one of those prefixes. This supersedes MONITOR-12's "no client is enabled". Verified 2026-11-21: tools/list of the installed 1.1.87 returns 20 tools, all with those prefixes, and `get_current_usage` through the gate completed and released. docs/PERFORMANCE-MCP.md describes the gate. |
| MCP-08 | Owner request 2026-11-23, relayed by the Pathweft Index service thread from the MCP audit (20261122-pathweft-mcp.md): "Based on your explanation, it seems that your first 3 acknoledgemens are worth adding", then in this thread "Cover also the other issues". Read-only pass-through of what the installed index service (protocol 2) already serves, with no service change: `get_index_status` returns the service's `status` reply (version, protocol, memory and per store name, kind, state, items, folders, error and owner); `get_content_status` returns `content_status` and `content_scopes` (per scope files, pending, current, stale, failed, unsupported, unavailable, reconciled, error, added; store_bytes, waiting) and says when no folder is content-indexed; `get_content_state(folder)` pages `content_state` for the files directly in a folder. `search_files` returns the structured `updating`, `updating_stores` and `elapsed_us`, takes `visible_only`, and takes `locations` (0..10, default 0) returning each file hit's stored chunk locations verbatim, never text or terms (IDX-18). `get_folder_size` also takes `paths` (1..1,024 folders, one index request). Content scopes stay managed only in Settings; no MCP tool adds, removes or rescans anything. `dupe:` stays refused by the service. |
| MCP-09 | Owner, 2026-11-24, after an agent read a full 100-row search page as an index cap: "If you, the owner of this product, couldn't tell the pagination system in the MCP... something needs improvement there". Every paged tool, search_files included, answers in one shape: `status`, `rows`, `total`, `offset`, `limit`, `returned`, `has_more` and `next_offset` (null on the last page), and the status ends with "Rows 1-100 of 247. More rows: call again with offset 100" or "This is the last page". The offset and limit schemas say the default (100), the maximum (500) and that a full page is not a cap. Time-range tools without an offset state when `limit` cut the range short. Acceptance: the performance-analysis scenario pages list_folder with limit 1 and reads has_more, next_offset and the status on the first and last pages; search_files carries the same fields. |
| MONITOR-24 | Owner feedback 2026-11-15 on the live process table. The process filter, Handle types, End process and a compact column chooser sit on a bar directly above the table, not in the graph toolbar. Right-clicking the header row opens the column chooser; right-clicking blank space opens nothing. CPU and RAM keep separate column choices, saved in settings across restarts. The analysis footer shows only a history recording problem; the normal retention summary is not shown. |
| MONITOR-25 | Owner proposal 2026-11-19 ("move the process filter to the top-center area and use it to filter both the graph and the table ... add a pause button? The constant refresh makes it difficult to analyze"); delivered with the S21 release. In the CPU and RAM views the process filter moves from the table bar (MONITOR-24) to the centre of the top toolbar. A non-empty filter applies to the table and to the graph: the graph then plots the summed CPU or memory of the matching processes from the recorded per-process history (the heavier processes sampled every 10 s, so the plot says "recorded matching processes" and never claims full coverage), live and historical ranges alike; clearing it restores the device total. A Pause button beside Now freezes the graph, the table and their values (sampling and history recording continue); Resume or Now catches up to the present. Pause also holds the table's order, so rows do not move while read. Verify filtering of graph and table on live and historical ranges, the coverage label, pause/resume with recording continuing, and the toolbar in dark, light and original themes at 100% and 150%. |
| MONITOR-26 | Owner question 2026-11-23: "Why is pathweft eating constantly 3 to 4 percent of CPU?" Measured on his PC: with the live CPU analysis open, the UI thread used about 46% of one logical processor (the window process about 66%). Each one-second sample refreshed the panel twice and reset the process table's view three times per refresh; while a UI Automation client listens (accessibility, text and capture tools), every reset makes WPF raise automation events for the rebuilt table, 90 to 230 ms per refresh. Live analysis applies samples incrementally: sort and filter are applied only when they change, the CPU and RAM process tables stay ordered by moving only the rows that are out of order (WPF live sorting was measured moving about 160 of 372 rows per sample, 12% of one processor with no client), an unchanged process sample or row leaves the rows untouched, and the GPU and folder I/O tables are replaced only for a new sample. Acceptance: with an actual out-of-process UI Automation client registered on the window, live CPU analysis raises no table reset per sample, keeps its sort order, and the UI thread's cost above the same live table with no client is at most half of that cost with the view resets of 1.1.89 (two refreshes of three resets per sample); after installation, the owner's window is measured again and its window process must use at most half of its 1.1.89 measurement. Revised 2026-11-23 before acceptance (recorded in S61): the earlier absolute bound of 10% of one logical processor in the verification fixture was unattainable because the fixture's live panel alone measured 9.5 to 18.4% with no client, so the absolute check moved to the owner's installed window; the reset comparison first replayed three resets per sample, half of what 1.1.89 did, and now replays its six. |
| MONITOR-27 | Pending issue #13 from the 1.1.90 release (MONITOR-26 acceptance failed: the listening client's added cost fell from 43.0 to 24.5 points of one processor, against at most half, 21.5). The out-of-process client attributed about 29 Name changes per sample to the main window, whose title is static, and most structure changes to row moves (ChildRemoved ListViewItem 360, ChildAdded TextBlock 563 in 20 s). The live process table reuses row containers when rows move, keeping row identity (selection and pins follow the process, MONITOR-24/25), and the source of the window's Name changes is identified by the client and removed when it is avoidable. Acceptance: MONITOR-26's scenario with the out-of-process client, which also records each window Name change with its window and values: the window raises no Name changes, and the client's added UI-thread cost is at most half of the cost with the view resets of 1.1.89; after installation, the owner's window is measured and must use at most half of its 1.1.89 measurement (65.8% of one processor). |
| CORE-28 | Owner report 2026-11-23 after installing 1.1.90 ("the CPU usage doesn't seem reduced that much"). Measured on his PC: the live analysis's UI thread used about 12% of one logical processor, but a folder-sizing worker used about 80% without end. His panes show C:\; the index service was still loading after the update, so the rows were measured on disk, and a folder changed during its walk was walked again at once, which never ended on C:\Users. A drive-root pane also ignored size changes (its path test treated C:\ as containing nothing). Required: a drive root contains every path on its drive; changes below a row's folder are taken together once a second, and a changed row takes the index service's total after the service has read the change journal past its newest change (measured on disk only where the index does not answer); a walk that a change interrupts is neither remembered nor shown and is not restarted at once (the change starts the next pass); a file changed again and again does not repeat cache deletions until a new size is stored. Acceptance (sizes scenario): drive-root containment; a walk of a continuously changing folder ends; a row whose contents change continuously shows the index's newer totals while the changes go on; existing CORE-18 checks still pass; on the owner's PC with his C:\ panes the window process stays below 10% of one logical processor once the index is ready. Criterion revisions (prospective, assistant-authored): the live-indexed-sizes check "a real write refreshes the row from fresh filesystem metadata" now requires the new total (580) from either source, never a lagging indexed one; the overflow check observes the row dropped and rebuilt and the stale record gone instead of a republished Pathweft record, since the index keeps none. Addendum 2026-11-23 (owner: "If we have the indexer service why is it taking so long in generating the analysis?", Analyze space on C:\ showing every folder Measured, Partially measured or Pending): Analyze space waits for the index service while it is still loading the analysed drive (about 13 s for C:\ after a restart or an update) instead of walking the drive on disk, at most 90 s, and the footer says so; an absent or unresponsive service is not waited for; the administrator analysis takes the index totals like the ordinary one (a full refresh still measures); the index service keeps a new file's size in its folder totals when NTFS reuses a just-deleted file's record for the new folder and the journal lists the file first (the space scenario found its fixture 6,000 bytes short in the installed service). Acceptance: indexed-sizes scenario: an analysis started while the substituted service reports the drive loading waits, then shows the indexed total without walking; index store unit test for record reuse in one batch; space scenario. |
| CORE-29 | Owner report 2026-11-23 after installing 1.1.92 (Analyze space on D:\ showing every folder Measured, Pending, Measuring... or Updating... and none Indexed, "also no indexed?"). D: changes all the time, and each change made the analysis measure the whole changed top-level folder on disk again (a watcher overflow or a change in the drive root measured the whole drive), so the first pass's indexed totals were replaced by disk walks that never ended. Required: a change seen by the analysis's watcher takes the index service's totals once the service has read the change journal past it (as CORE-28 pane rows do): in the changed branch only folders written in the last two seconds (SEARCH-10) and folders the index does not answer are measured on disk; a full refresh caused by a watcher overflow or a drive-root change uses the index too. Ctrl+R and Try again still measure everything on disk. Acceptance (space-refresh scenario, installed index service read-only): after a file in a watched branch grows, the branch shows the new total as Indexed and its unchanged subfolder is not enumerated; the existing refresh, coalescing, rename/removal and explicit-refresh checks still pass. |
| CORE-30 | Owner, 2026-11-23, after the 1.1.93 report: "who cares about the disk analysis be desynced by a couple of minutes at worst? Which disk analyzer measures speed and position?", then "If I want a refresh, allow me to press F5". Supersedes CORE-29's watcher-driven refresh. Analyze space is a snapshot: a change seen by the analysis's watcher (and a non-forced refresh after Pathweft's own operations) reads no folder and measures nothing; it only marks the analysis changed, and the footer says "changed since this snapshot, F5 to refresh". F5 refreshes the whole analysis from the index service's totals (folders written in the last two seconds or not answered by the index are measured on disk, as when the analysis opens); the rows, their values and the selection stay on screen until each is replaced, and F5 pressed again during a refresh adds one more pass. Ctrl+R and Try again measure everything on disk. Rows deleted through Pathweft still leave the analysis at once (S54). Help and What's new describe it. Verified by the space-refresh scenario. |
| CORE-31 | Owner, 2026-11-24, after installing 1.1.94: Analyze space on D:\ measured on disk for about 7 minutes, while F5 in the same analysis was instantaneous; earlier the same day: "as far as I know our indexer runs as admin and we are only filtering out results". Switching between standard and administrator view with an analysis open re-ran it on disk without the index (since the administrator session predated the index service). Required: a view switch takes the index service's totals like F5 (CORE-30), in both views; folders written in the last two seconds or not answered by the index are measured on disk; Ctrl+R and Try again still measure everything on disk. Help and What's new describe it. Verified by the space-refresh scenario (after a view switch the unchanged folder is Indexed with its exact total and none of its folders are read). |
| CORE-32 | Owner, 2026-11-24: "You must have a very complex flow, because with the current index implementation there is no way this behavior makes sense". Analyze space had timed fallbacks to disk walks: a 5-second index wait, a 90-second wait for a loading store and a status timeout, and a disk walk of every folder written in the last two seconds (on a busy drive often a whole top-level folder). Required: Analyze space (opening, F5, view switches, standard and administrator view) reads every folder the index service covers from the index, with no time limit. While the service is still loading that drive it waits, and the footer says so and offers Ctrl+R; Ctrl+R stops the wait and measures on disk. A folder written moments ago is asked for once the index has read the change journal past it (600 ms after its newest write; supersedes SEARCH-10's disk measurement and the "folders written in the last two seconds are measured on disk" clauses of CORE-28 to CORE-31 for Analyze space); likewise F5 after a change the analysis's watcher saw asks the index once 600 ms have passed since that change, since a file's size can change without its folder's write time. Folders the index does not cover are measured on disk, each on its own. An absent or failing service is measured on disk and the footer names the reason. Ctrl+R and Try again still measure everything on disk. Pane folder-size rows are unchanged. Help and What's new describe it. Verified by the space-entry scenario (a loading store is waited for with nothing measured; Ctrl+R measures on disk), the indexed-sizes scenario (a folder written just before is asked for after the catch-up and counted) and the space-refresh, space-ui and space scenarios. |
| CORE-33 | Owner, 2026-11-25: "I am trying to double click in a video and for whatever reason it is considering that I am draging the file to the same folder", "A single click is already raising it. It is a conflict with the preview panel opening and closing in half a second", "This raises 2 bugs, the double click behavior and why ... is it opening a transfer dialogue box when the source and destination are the same". A file drag starts only when the pointer itself moves past the system drag distance on the screen, not when the list moves under a still pointer (the preview panel opening for the clicked file resized the list); the second press of a double-click never starts a drag. Moving items into the folder they are already in does nothing, as in Explorer: no transfer and no failure card, whatever the form of the destination path (with or without a trailing separator). Acceptance: a click and a double-click on a video in a pane with the preview panel start no drag and no transfer; a move onto the items' own folder submits nothing; Ctrl+drop into the same folder still duplicates. | S73 |
| CORE-34 | Owner, 2026-11-26: "Did pathweft crash?" (1.1.103 ended at 23:48:54 with an unhandled ExternalException 0x800401D0, "Requested Clipboard operation did not succeed", from the item menu's Copy path). Another program can hold the Windows clipboard open; a text copy (Copy path, Copy name, Ctrl+Shift+C, copied details and tables, checksums, recognized text, remote addresses, report rows, the search widget) then reports in the status bar that nothing was copied because another program is using the clipboard, and Pathweft keeps running; reading the clipboard's text for a checksum comparison treats a busy clipboard as no text. Acceptance: on the private station, Copy path from the item menu while another thread holds the clipboard open reports it, changes nothing and does not throw; once the clipboard is free it copies the quoted path. | S75 |
| CORE-35 | Owner, 2026-11-26: "Can we make it so the top right area also allows draging?" (with the preview pane open, the strip at its top beside the window's buttons did not move the window). The preview pane's empty header area (above and around the file name and its buttons, beside and under the window's buttons) behaves like the empty part of the tab row: drag moves the window through Windows (Aero Snap, dragging off a maximized window), double-click maximizes or restores, right-click opens the window menu. Clicks on the header's buttons and on the preview below the header are unchanged. Acceptance: source inspection of the hit area; debug build and browse scenario unchanged; release publish and packaged checks; the owner's drag check after install. Revised 2026-11-26 by PREVIEW-41 (S78): the preview no longer reaches the window's top; it sits inside the pane under the tab row, whose empty part (now always the pane's full width, beside and under the window's buttons) moves the window, so the top right area keeps dragging and the preview header no longer does. | S76, S78 |
| CORE-36 | Owner, 2026-11-26: "as soon as I click in a file/folder, the preview opens, the tabs stake and the end click moves to a different list item". A double-click opens the item its first press was on when the list's layout changed between the two presses (a preview appearing or resizing, rows re-wrapping), instead of the item that moved under the pointer. A double-click whose layout did not move is unchanged. Acceptance: the preview check presses one row, changes the list's width, presses a different row as the second press, and the first row opens; release publish and packaged checks. | S78 |
| CORE-37 | Owner, 2026-11-27: "Double click on an html file seems to be opening the code viewer instead of opening it in a browsesr". Double-click and Enter on a web page or drawing Windows opens in a viewer (.html, .htm, .xhtml, .shtml, .svg) use its Windows association, as Explorer does, also with "Open code and text files in the Pathweft editor" on; F4 and Edit still open it in the editor, and other code and text files keep opening in the editor. Revises CORE-06's "supported text/HTML" automatic opening for these types. Acceptance: the open check (PATHWEFT_VERIFY_OPEN1111) double-clicks page.html, page.htm and image.svg with the editor setting on and each goes to the Windows association with no editor, a web page stays editable, and supported text (.css) still opens in the editor; the owner checks an HTML double-click after installing. | S80 |
| CORE-38 | Found 2026-11-27 by the appearance check's real Match path search (the 1.1.110 run): an index search whose first page of hits lies only in hidden folders (for example a scoped path: search in a repository, where .git objects sort first by name) showed an empty list with More results, although visible matches exist further on. When a page adds no visible rows and more results exist, the pane requests the next page itself, until visible hits arrive or the results end; a new search, navigation or Esc cancels it. Applies to the first page and to More results, in panes and widgets. Hidden items stay excluded unless Show hidden is on. Acceptance: the appearance check's real Match path toggle returns scoped hits whose parent path matches; existing scenario passes. | S81 |
| MENU-11 | Owner request 2026-11-23 ("I am missing the integration `Send with Tailscale`"; after finding it under More actions: "it should be mapped under Transfer"). In Pathweft's item menu, a taken-in entry (MENU-06) whose label starts with "Send with" (Tailscale's static verb `*\shell\tailscale`, "Send with Tailscale...") is placed in the Transfer group beside Copy to and Move to, with its own text and icon and the shell's invocation, instead of More actions. Other taken-in entries keep their MENU-06/MENU-10 placement. Help lists it. Verified by the menus-organized scenario on a host with Tailscale installed (a file's Send with Tailscale... is under Transfer). |
| PREVIEW-39 | Issue #4: closing an Office preview tolerates a LibreOffice temporary file that is briefly locked; it never throws; the leftover temporary files are removed when the next preview closes after they are released. |
| PREVIEW-40 | Owner, 2026-11-26: "Can we add a loop button in the playback preview section?" The media preview (side pane, Space and the floating preview, video and audio) has a Loop button beside Mute, in the accent on/off style of the other preview toggles. On, mpv starts the file over at its end without pausing (loop-file); off, the end pauses at the beginning as before. The choice is kept in settings and applies to the next previews until turned off. Acceptance: existing scenario on the real SDR fixture: Loop starts off, the button turns native looping on and shows it, playback passes the end and starts over without pausing or reaching end-of-file, and the button turns it off again; release publish and packaged checks. | S77 |
| PREVIEW-41 | Owner, 2026-11-26: "Due to the tabs stacking instead of extending, when I have the preview column enabled, and I am not selecting anything all the tabs fits in the same row, but as soon as I click in a file/folder, the preview opens, the tabs stake and the end click moves to a different list item"; then: "No, I don't like shrinking. I think that it would be better if the preview instead of being an isolated right column, it opened inside the tab content and pushed left the navigation and content areas. That would allow the tabs section to not be affected. We wouuld still need to make the preview element stylized and not just a cut off"; on two panes: "forcing the PiP mode in each panel and showing both previews would be better". One pane: the docked preview (Alt+P, the toolbar button) opens inside the pane below its tab row; the tab row keeps the pane's full width and its rows and height never change when the preview opens, closes or is resized; the path bar, list, footer and terminal fill the room to its left. The preview is a styled card: rounded corners, the panel edge, an even inset on the pane surface, a short opening slide and fade, the header (renderer icon, name, marks, pin, picture in picture, floating window, close), and a strip on its left edge that resizes it and keeps the width (the list keeps at least 250 px). Picture in picture is a corner card over the pane's list. Two panes: each pane shows its own picture-in-picture card of its own selection, both at the same time, each with its own pin, marks, floating window and close (picture in picture, the only in-app place there, is not offered as a switch); close or Alt+P closes both. The former preview column beside the panes is removed. PREVIEW-37/38 otherwise hold, "sidebar" now meaning the docked card: host changes keep the renderer, content and leases; closing a floating window returns the card to its pane; an empty unpinned selection collapses it. Acceptance: preview check: with nine tabs the tab row's width, rows and height are the same before and after the preview opens and after it is resized; the path bar and list narrow by at least the card width; the card starts below the tabs with its corner, edge and inset; dragging the edge resizes and saves; the list keeps 250 px; two panes show a card in each pane with each pane's own selection, their own pin and window, and one pane docks again; modes scenario (PiP, floating window, restoration to the pane) and packaged checks; release publish. | S78 |
| ACCESS-10 | Owner, 2026-11-27, with administrator view on and C:\DumpStack.log showing "Preview unavailable / Access to the path 'C:\DumpStack.log' is denied.": "the previewer is not properly wired to use the admin toggle?". With administrator browsing on, a local file the signed-in user may not read is read through the approved administrator session: the preview (docked card, picture in picture, floating window and Quick View) shows it from a copy that session makes, at most the preview limit (64 MB), keeping the file's name so its viewer applies; the editor opens it the same way (at most its 20 MB) and already saves through that session. A folder the user may not list is shown in the folder preview from the session's listing, including its expanded subfolders. The copy lives in its own folder under the cache, made only by the session's fixed read-only copy operation (never outside that folder, never over an existing file, never past the limit, never prompting), and goes when the preview moves on or the editor has read it; copies left at exit go. Files the user can read are read directly, and network files keep standard access (ACCESS-09). Switching administrator view re-shows open previews with the new access. Acceptance: the administrator scenario checks the worker's copy and its refusals (outside its folder, over a copy, past the limit), a refused file previewing from the copy under its own name, the copy going with the preview, the editor reading through the copy and keeping none, and a readable file previewing with no copy; the owner checks C:\DumpStack.log in administrator view after installing. | S79 |
| EBOOK-01 | Owner, 2026-11-24: "Can we consider adding eBook previewers like the PDF ones? (M:\Books\Ebooks)", then "Like before, we can borrow the code from https://github.com/kovidgoyal/calibre" and "I hope that you are also considering EPUB". An eBooks preview module shows EPUB (2 and 3), FB2, MOBI, AZW3 (KF8), AZW and PRC books as native WPF pages (no web rendering, scripts or downloads): a header with the cover, title, author, format, publisher, year and language; the PDF viewer's page controls (previous, "Page x of y · Part c of n", next, page number box); a contents picker from the book's navigation (EPUB nav or NCX, FB2 sections, MOBI/KF8 NCX), or its parts when it has none. Next page past a part's last page opens the next part and Previous on a first page opens the previous part's last page; contents entries and links inside the book open their target place. Chapter markup keeps headings, paragraphs, emphasis, lists, tables, block quotes and the book's images. DRM-protected books show their details and cover and say the text cannot be shown; nothing is decrypted. Reading code adapted from calibre keeps its GPL-3.0 attribution (THIRD-PARTY-NOTICES, vendor/Calibre manifest). LIT is not supported (pending issue #14). Acceptance: the ebook scenario on owner-shaped EPUB, DRM EPUB, FB2 and MOBI fixtures, and a read-only pass over the owner's library in which every EPUB, FB2, MOBI and AZW3 book opens with text or is reported DRM-protected. |

### Maintenance tweaks by goal with risk colours (S47a)

User direction 2026-11-12/13: expand Maintenance before S12, drawing on the
owner-listed open-source tweak projects, with dangerous settings colour coded.
S47 has three separately released parts: S47a tweaks by goal (this section),
S47b the Windows apps tab with Bulk Crap Uninstaller capabilities, and S47c a
read-only security check with opt-in hardening. Donor inventory, licences and the
proposed selection: [research/2026-11-13-s47-donor-inventory.md](research/2026-11-13-s47-donor-inventory.md).
MAINT-08..11 and MAINT-20..23 remain in force; nothing is applied automatically.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-25 | Every Windows tweak rule carries one goal: Debloat, Privacy, Performance, Security or Personalize (appearance, Explorer, Start and taskbar preferences). The existing wrapping filter row gains a goal filter beside the category filter; both filter locally over the same table. Categories stay as group headers. No sidebar, preset or "apply all for this goal" action. |
| MAINT-26 | Every rule carries a risk level separate from its recommendation. Low: reversible preference with no protection or stability cost (no accent). Caution: reduces a Windows feature, recovery path or update behavior the user may not notice (warning colour). High: weakens a security protection or can stop Windows, devices or sign-in working (critical colour). The Windows tweaks table shows the level as a small coloured marker with its word in the Recommendation cell; the explanation shows the level, what is at risk and how to restore. Colours come from theme resources with readable contrast in dark, light and high-contrast modes; symbol and word keep it understandable without colour. A High rule is never Optional (recommended), and Caution/High rules are excluded from the pending-recommendation count. Applying a selection that contains Caution or High rules names those rules in the confirmation. Existing rules are assessed and labelled too. |
| MAINT-27 | Tweak actions stay typed and journaled: registry values (current user or machine, including policy keys) and, new, scheduled-task enabled state by exact task path. The journal records the exact original value or task state and Restore returns exactly that. Catalog data cannot add commands, services, packages or arbitrary write targets. Actions that need administrator rights use the existing administrator session and are unavailable, not silently skipped, without it. |
| MAINT-28 | Adopt individual rules from the selection in the research document: AI (Copilot, Recall, Click to Do, agent features, Notepad/Paint/Edge AI, AI service autostart), Privacy (telemetry scheduled tasks, app permissions, input and speech personalisation, cloud clipboard, activity tracking, feedback, cloud search), Debloat (consumer features, Edge promotions, Widgets, auto-installed apps, Store app archiving), Explorer (navigation-pane items, title-bar path, checkboxes, separate process, empty drives, folder-type discovery, context-menu entries, shortcut arrow and suffix, long paths), Start and taskbar, Performance (Delivery Optimization peers, visual effects, last-access updates, Game Mode and GPU scheduling, hibernation, reserved storage) and Updates (drivers, restart while signed in, active hours). Each rule follows MAINT-08: primary Microsoft source where one exists, donor source, exact targets, build range, feature loss, goal and risk. Definitions come only from MIT/Apache donors or Microsoft documentation, pinned in a vendor source manifest with file hashes and shipped notices; copyleft, source-available and unlicensed donors are pointers only. |
| MAINT-29 | Detect settings locked by other tools: policy values under the machine and user Policies keys that match a catalog target and have no Pathweft journal entry are shown as "Set outside Pathweft" with their current value, what Windows does by default and the rule's risk. Restoring the Windows default is an explicit, journaled action. Unknown policies are listed read-only, never removed. |
| MAINT-30 | Excluded: anything that disables or weakens Defender, SmartScreen, Mark-of-the-Web, UAC or its secure desktop, VBS/HVCI, Smart App Control, exploit mitigations, BitLocker or Windows Update; debugger-hijack app blocking; forced Edge removal; downloaded host lists or remote scripts; install-image changes. Rules that weaken protection may appear only in S47c's read-only check, never as actions here. |
| MAINT-31 | Verify catalog completeness and integrity (every rule has goal, risk, source, targets and build range; manifest hashes match), goal/category/search filtering, the pending count, the confirmation naming Caution/High rules, and scheduled-task journaling. Qualify every new action in a disposable Windows Sandbox guest with apply, readback and exact restore; no tweak changes on the host. Capture the risk markers in dark and light at 100 and 150 percent and narrow width. Ship as a normal release with independent asset verification and clean spent scratch. |

### Windows apps with Bulk Crap Uninstaller capabilities (S47b)

User direction 2026-11-13: "Bulk Crap Uninstaller could be merged into the Windows
App tab and repurposed." The tab grows from a fixed list of 24 Store apps into an
inventory and uninstaller for what is actually installed. Capabilities follow Bulk
Crap Uninstaller (Apache-2.0, re-implemented; its GPL list control and online rating
service are not used). User feedback 2026-11-13 on the tweaks list also applies:
the larger list stays virtualized with smooth scrolling, and the details well can
be closed. MAINT-08..11 and MAINT-20..23 remain in force.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-32 | Inventory: Windows apps lists installed programs from registry uninstall keys (machine 64- and 32-bit, current user), Store packages for the current user through Windows' package manager (main packages only; framework, resource and bundle parts are not rows), Scoop apps and Chocolatey packages where those managers are installed. Entries without a display name, system components, Windows Installer patches and updates with a parent are folded away, not listed. Rows show Name, Publisher, Version, Source, Size and Installed date; details show the uninstall command, install location, registry key or package identity and whether the uninstaller is signed. Reading needs no administrator rights, runs off the UI thread, is cancellable and changes nothing. |
| MAINT-33 | Guidance: Store apps get reviewed guidance by exact package name (Optional, Keep enabled or Not assessed) from MIT donor ratings (Win11Debloat, Bloatynosy), each rule with source, reason and what is lost. Desktop programs are never recommended for removal; they show Not assessed unless protected. Protected rows (Windows system packages, frameworks, entries without an uninstall command or marked NoRemove, and Pathweft itself) show why and cannot be selected for removal. The Windows apps card counts installed Optional apps. |
| MAINT-34 | Uninstall one: Store packages are removed for the current user only (no all-users removal, no deprovisioning). Registry and Windows Installer entries run their own registered uninstaller the way Windows Settings does, letting it request elevation itself; Windows Installer products use msiexec by product code. Scoop and Chocolatey entries use their manager's uninstall command. Quiet uninstall is offered only where the entry declares a quiet command or its installer type has documented silent switches (Windows Installer, Inno Setup); otherwise the uninstaller's own window appears. Pathweft never deletes an install folder as an uninstall and never clicks through an uninstaller's window. After the uninstaller exits, the entry is read again and the result says Removed, Still installed or Cancelled; there is no Restore for an uninstall and the confirmation says so. |
| MAINT-35 | Uninstall several: the Windows apps table allows multiple selection; Uninstall selected names every app in its confirmation, offers a Windows restore point first (on by default when any desktop program is included) and runs one uninstaller at a time in Bulk Crap Uninstaller's order (interactive before quiet, Windows Installer products before others, larger before smaller). It does not start a Windows Installer uninstall while another Windows Installer operation is running (that app is reported and left for a later run), reports each result as it goes, can be cancelled between apps and never retries automatically. |
| MAINT-36 | Leftovers: after an app is removed, Find leftovers looks for what it left: its install folder, folders named after the product or publisher in Program Files, ProgramData and the user's AppData, Start menu and desktop shortcuts pointing into removed locations, the product's Software registry keys and startup entries pointing into removed locations. Each finding has a confidence (Likely, Possible, Unlikely) computed from name, publisher and location matches, and only Likely findings are preselected. Removal sends files and folders to the Recycle Bin and exports registry keys to a .reg backup in Pathweft's data folder before deleting them; Restore imports that backup. Findings outside the user's profile that need administrator rights use the existing administrator session. Nothing in Windows, another app's install location or a folder still in use by a registered program is offered. |
| MAINT-37 | Excluded: online ratings or telemetry, removal of Windows optional features or Windows updates, all-user Store deprovisioning, folder-delete uninstalls, UI automation of uninstallers, editing uninstall entries, and any removal without an explicit confirmation. |
| MAINT-38 | Provenance: Bulk Crap Uninstaller ideas (sources, quiet switches, ordering, leftover scanners and confidence scoring) are re-implemented in Pathweft code with the Apache-2.0 licence and NOTICE kept in vendor with the cited files and their hashes, and listed in the third-party notices. Store app ratings come from MIT donors only, pinned the same way. |
| MAINT-39 | Verify inventory parsing, folding and protection, guidance matching, the bulk order and the leftover confidence rules with owned fixtures. Qualify uninstall in a disposable Windows Sandbox guest: an owned fixture program with interactive, quiet and Windows Installer-style entries, a Store package present in the guest where one is removable, the restore-point option, leftover detection, Recycle Bin removal and .reg backup and Restore. No uninstall or leftover removal runs on the host during verification. Capture the tab in dark and light at 100 and 150 percent and narrow width. Ship as a normal release with independent asset verification and clean spent scratch. |

### Security check and opt-in hardening (S47c)

Selection item 9 of the accepted donor inventory: a read-only posture check plus
a few hardening settings the user can turn on and restore individually. Security
hardening is its own objective, separate from debloat and performance. The check
never changes anything; hardening uses the existing journaled Windows tweaks.
MAINT-08..11, MAINT-20..23, MAINT-26..27, MAINT-30 and ACCESS-07 remain in force.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-40 | Security check: a Maintenance category after Windows apps evaluates the HardeningKitty main machine list (MIT, pinned, shipped with its notice) read-only. Evaluated methods: registry values and registry lists, service start type, Windows optional feature state, scheduled task state, Microsoft Defender status and preferences (including attack surface reduction rules) and BitLocker volume status. Rows whose method needs tools Pathweft does not run (audit policy, security and account policy export, user rights, firewall rules, process mitigations, CIM classes, boot configuration) are not listed; the explanation states how many and why. A missing value is judged against the list's Windows default and shown as the Windows default. Operators follow the list (=, !=, >=, <=, contains), numeric when both sides are numbers. Reading uses the shared administrator session's worker when one is approved and otherwise runs as the user; a value the user cannot read shows Needs administrator access, never a guessed result, and the check never requests elevation itself. |
| MAINT-41 | Presentation: rows show Setting, Category, Current, Recommended and Result, grouped High, Medium and Low not met, then Met and Not checked, with the search filter. The card counts not-met findings and the high ones. Details give the exact target, operator, recommended and current values, severity and the list's ID. Where a Pathweft Windows tweak changes the same setting, details name it and Open in Windows tweaks selects it. The category has no Apply, Restore or apply-all action. |
| MAINT-42 | Opt-in hardening as Security-goal Windows tweaks, each journaled with exact Restore: Defender network protection (block), potentially unwanted app blocking, Defender sandbox, LSA protection without UEFI lock (value 2, so Restore works; Caution), firmware-installed software off (WPBT; Caution), PowerShell 2.0 engine off, WMIC removed and VBScript removed (Caution). Sources: Microsoft documentation plus Harden Windows Security or WinUtil (MIT, pinned). Policy-based Defender settings show in Windows Security as managed by the organisation, and the explanation says so. Rules for components absent from this Windows show as not present and cannot be selected. |
| MAINT-43 | Windows optional features and capabilities are a typed action by exact name fixed in code, applied through the shared administrator session with Windows' servicing API. The journal records the original state; Restore re-enables a feature from its retained payload, while a removed capability is re-added from Windows Update and needs internet, which the explanation states. A change Windows completes after restart is reported as pending restart. MAINT-37's exclusion of optional-feature removal still applies to Windows apps. |
| MAINT-44 | Excluded: apply-all or list-driven hardening, firewall rules, user rights, account, audit and security policies, settings locked in firmware (no exact Restore), anything MAINT-30 excludes, and any change from the Security check category. |
| MAINT-45 | Verify the shipped list's hash, method coverage counts, operator and default-value evaluation and grouping with owned fixtures, and that the check writes nothing. Qualify every new hardening rule in a disposable Windows Sandbox guest with apply, readback and exact Restore, including an optional feature change; a capability that is absent from the guest or needs network is recorded, not claimed. Capture the category in dark and light at 100 and 150 percent and narrow width. Ship as a normal release with independent asset verification and clean spent scratch. |

### Startup administrator access and process activity recording (S46)

User direction 2026-11-11: request the shared administrator session when
Pathweft starts instead of when the Performance widgets load, and extend
performance data collection so Pathweft's MCP can replace the third-party
AppControl service for app execution history and associated resource usage.
The purpose is performance monitoring, not security: no hashes, signatures,
publishers, alerts or blocking. Program identity is the executable path only;
agentic build activity (thousands of short compiler processes) must stay cheap to
record and readable. Starting after sign-in is acceptable provided Pathweft
starts with priority, not through a registry Run value that Windows starts among
the last items. The user chose an elevated recorder started by Task Scheduler at
sign-in, approved once in Settings, so that no UAC prompt appears at sign-in.
This narrowly supersedes ACCESS-02's "no scheduled task" for that recorder task
only, and supersedes the S26 Performance view's own first permission request.

Revision 1.16 (implementation findings, same day): Windows' stop event already
carries lifetime CPU cycles, I/O and peak commit, so no per-process handle is held;
long-running programs are attributed by one system process snapshot every 30
seconds. The toggle lives in the Settings Startup card and is registered through
the already approved administrator session. User direction: the memory figure is a
target, not a hard limit; the aim is to keep Pathweft optimal at all times.

| ID | Required behavior and acceptance |
| --- | --- |
| ACCESS-04 | An interactive start requests the shared administrator session once, after the main window's first frame; browsing and navigation never wait for it. A notification-area sign-in start defers the request until the window is first shown. The Performance view never requests permission itself: it reuses an approved session and starts folder tracing when approval arrives. Cancelling leaves Standard browsing and no further automatic request in that run; the explicit Enable detailed I/O and drive-health actions remain Performance's retry paths, and the shield remains browsing's. Settings → "Start administrator access with Pathweft" (default on) controls the request; once approved, the installed copy starts the session without a prompt (ACCESS-08). Maintenance uses the same session (ACCESS-07); explicit verification profiles never request it. |
| ACCESS-06 | User direction 2026-11-12: the shield manages file-browsing authority only. It follows TOOLBAR-01: usable whenever the file panes show, disabled ("Explorer only") while a Performance panel or Maintenance category replaces them. Toggling it never starts, ends or retargets Performance's elevated work, which uses the session requested at launch (ACCESS-04) or its own Enable actions. The shield has no End administrator session action; the shared session lasts until Pathweft exits (supersedes that part of ACCESS-02). |
| ACCESS-07 | User direction 2026-11-13: after the launch approval there are no further UAC prompts. Maintenance's protected Cleanup review, machine-scope Startup, Services and tweak actions, restore points, administrator leftovers, and desktop-program and Chocolatey uninstalls run through the shared session approved at launch (ACCESS-04), so uninstallers inherit its elevation instead of asking Windows again. Store and Scoop removal stay with the current user. Leaving Maintenance does not end the session; it lasts until Pathweft exits, and the shield still controls only browsing (ACCESS-06). Only when no session was approved (the launch request declined or turned off in Settings) does the first action that needs it ask once for that shared session. Supersedes MAINT-23's independent Maintenance session and MAINT-34's unelevated uninstaller launch. |
| ACCESS-08 | Owner decision 2026-11-14 (issue #7): administrator access starts automatically, with no UAC prompt and no user action. The owner accepted that other programs running as the same user could in principle drive the session's operations through the standard window. The first approved session of the Program Files installation registers a per-user, on-demand, hidden Task Scheduler task (no trigger, highest available rights, parallel instances) whose only action is the installed executable with `--administrator-session $(Arg0) $(Arg1) $(Arg2)`. Every later request, including a notification-area sign-in start, runs that task with the pipe name and owner identity instead of `runas`. The standard process accepts the pipe client only if its image is the installed executable, observes its exit through a SYNCHRONIZE/limited-query handle, and gives up after 30 s without a connection. The helper keeps every existing check (owner PID, start time and image; pipe server identity; fixed request kinds; no supplied executable, command line or script). A missing, foreign or failing task falls back to the one UAC prompt; a sign-in start never prompts and leaves that to the first window. Turning the Settings option off removes the task through the session it started; uninstall removes it with the other owned tasks. Portable and profile copies keep asking (ACCESS-04). The read-only MCP never uses the session. |
| ACCESS-09 | Owner report 2026-11-24: "M: break when I enable the admin view". The administrator session runs elevated, where the user's mapped network letters do not exist (EnableLinkedConnections is not set) and administrator rights grant nothing on a network share. Network folders (\\server\share paths and mapped network letters such as M: or the Tresorit T:) are therefore always read with standard access, also in administrator view: listing, prefetch, Analyze space and the access-denied prompt, which says that administrator access does not apply to network folders instead of offering it. Local and device paths keep administrator reads. Acceptance: the indexed-sizes scenario checks the classification of a mapped letter, its share and local and device paths; the owner checks M: in administrator view after installing. |
| TOOLBAR-01 | User direction 2026-11-12: the command bar follows what is on screen, with one rule. While the file panes show, including when the Performance or Maintenance cards only replace the bottom statistics, every command works as in plain browsing: file commands, Hidden (showing its own setting), the shield, Panes, Preview, Analyze space, and both Performance and Maintenance, which switch between their card strips. While a Performance panel or Maintenance category replaces the panes, every command is disabled except the button owning that view, which leads back; Transfers stays available as a global popup. Disabled commands keep their on/off state, dimmed by the shared style. Supersedes MAINT-23's dimmed Performance icon and Maintenance-wide shield disabling, and the earlier rule that hid Hidden's active state in Performance and Maintenance. |
| LAUNCH-01 | "Start with Windows" uses a current-user Task Scheduler logon task with no delay and normal process priority (not the Task Scheduler default below-normal 7) that starts `Pathweft --tray` with standard rights, replacing the HKCU Run value; an existing Run value is migrated and removed. Task actions reference only the protected Program Files executable; other installations keep the Run value and say why. Disabling or uninstalling removes Pathweft's tasks exactly; Startup in Maintenance identifies them as Pathweft's own. |
| ACTIVITY-01 | Settings → Startup "Record app activity" registers, through the shared administrator session (one approval, no extra UAC), a hidden elevated logon task (highest privileges, no delay, normal priority) for a windowless recorder in the protected executable. It consumes Windows kernel process start/stop events (no polling) and records executable path, PID, parent PID and start/exit time; the stop event supplies lifetime CPU cycles, peak private commit and I/O read/write bytes, and a 30-second system process snapshot attributes running programs' use to time. CPU seconds are derived from cycles at the nominal frequency and say so. Inaccessible values stay explicit. No hashing, signature or publisher lookup, blocking, alerts or network. It runs from sign-in to sign-out independently of the browser window and stops and is unregistered when turned off. The browser stays unelevated. |
| ACTIVITY-02 | Roll process trees up into launching workloads (for example one build command) with process count, CPU time, peak memory, I/O and duration. Processes shorter than one second and below small resource thresholds are counted in their workload but not stored individually. Per-executable totals and time ranges aggregate all observations. Lost or dropped events are counted and reported, never silently omitted. |
| ACTIVITY-03 | The recorder owns a separate transactional activity store under DataFolder.Bulk, read (never written) by Pathweft and MCP; batched writes at most every 30 seconds; retention follows the existing 1/7/30-day setting. The store is limited to 1 GiB (raised from 128 MiB by ACTIVITY-07); when full, the recorder trims the oldest history and retries the batch once, and a batch still not written is counted as dropped. On, off, idle and locked intervals come from recording gaps and the input desktop and last-input time sampled at each batch. Without the recorder, the existing ten-second heavy-app snapshots remain and are labelled incomplete. |
| ACTIVITY-04 | Impact budget: recorder at most 1% normalized CPU and a 32 MiB private memory target (kept as low as practical) averaged over a qualified burst of at least 2,000 process starts per minute (a real parallel build), with lossless start/stop counts at that rate or explicit loss reporting. Idle cost stays within the existing recording off-mode budgets. |
| ACTIVITY-05 | Extend the existing read-only stdio `--performance-mcp` with run history, workload totals, per-application usage, top consumers over a range and on/idle intervals, under MONITOR-12's constraints (no WPF, elevation, sampler, listener or client configuration; validated input, bounded output with truncation, structured errors). |
| ACTIVITY-06 | Qualify real event recording, aggregation, store, retention, task registration/removal and priority, sign-in start order relative to Run entries, UAC approval/cancel, burst budget and MCP protocol. Compare MCP history with AppControl over at least three days of normal agentic work before recommending AppControl's removal. One normal independently verified release. |
| ACTIVITY-07 | Owner request 2026-11-22 ("would it be costly to store the basic process details?", then "don't fall sort of anything that is an improvement"): each recorded run also keeps its command line and starting folder. The command line comes from the kernel process start and rundown events, so short processes are not missed; the starting folder is read once from the new process's parameters, null when it already ended or is protected. Before storing, passwords, tokens, keys, bearer values and URL credentials are masked, and text is capped at 2,048 characters with the original length noted. Texts are stored once and referenced; unreferenced texts are removed with expired history. MCP get_run_history returns command_line and working_directory and its name filter also matches the command line; get_workloads returns the first process's command line. Stores from 1.1.87 and earlier keep answering (nulls for those runs). The recorder's mutex, stop event and session names carry the profile instance when PATHWEFT_PROFILE is set, so a test recorder never replaces the signed-in one. Settings and Help say what is kept. Verified by the release self-test (masking and cap), a packaged check of old and new stores, and a private elevated recorder run. |

### Protected installation and packaged folder callers (S45)

The user's 2026-11-10 instruction to move the program to Program Files supersedes
the earlier per-user installation-location requirement for this correction. A
Codex packaged process sees the opted-in Folder/open DelegateExecute value but
cannot see Pathweft's per-user COM class; its Open in Explorer action fails with
"Class not registered". A diagnostic machine COM registration made that same
action open Playground in Pathweft, but was removed immediately when its target
DLL was found writable by Authenticated Users. The machine class is absent again.

| ID | Required behavior and acceptance |
| --- | --- |
| SHELL-04 | Install the executable and native shell DLLs under an administrator-protected Program Files location, with no writable-user machine COM server path. Register the x64 and x86 native class for packaged callers only through the protected installation, while preserving opt-in per-user Folder/Directory/Drive verbs and exact prior-owner restoration. Migrate the currently enabled route from D:/Tools/Pathweft without losing profile, tabs, settings, Win+E ownership or selected-file reveal. Codex's actual Playground Open in Explorer action and the browser's actual Show in folder request must open the requested folder in Pathweft, with selection preserved where supplied. Disable/uninstall restores prior per-user verbs and removes only Pathweft-owned machine COM entries. Verify protected ACLs, packaged class visibility, normal-user and packaged caller behavior, update/restart and rollback. |
| DELIVERY-04 | The normal updater must elevate the protected installer through Windows consent, retain its original caller/update identity and progress, and never acknowledge a partial replacement as success. Deliver one normal release containing the protected installer and native router, independently retrieve its assets, then install and verify the real Program Files version and both live shell workflows. Keep the previous installation until the protected package, machine class and unchanged data location are verified; then retire it through its own uninstaller, restore the opted-in routes and helper with the new executable, and retain the published prior installer as rollback through that transition. No intermediate release. |

### Npm cache cleanup correction (S44)

The installed 1.1.48 Cleanup review reports Npm's package cache, but Clean ends
at zero completed because Npm creates a diagnostic log under its cache root
during Pathweft's own recheck. The reviewed package cache has not changed.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-24 | Measure and compare only Npm's configured `_cacache` package store, which its installed `cache clean --force` command removes. Npm logs, `_npx`, prebuilds and other siblings must not inflate the preview or cause a false changed-cache rejection; a changed configured root or changed `_cacache` still prevents an unreviewed removal. While the native manager command runs without per-entry progress, show an active indeterminate progress bar and truthful status rather than a frozen zero counter. A preflight failure must be identified as a failure, not a completed cleanup. Verify an owned cache fixture where Npm writes logs between review and Clean, the changed-store rejection, the actual popup and delivered package, then publish and installed-verify a normal corrective 1.1.49 release. |

### Automatic Maintenance freshness and concise Cleanup details (S44)

The user's 2026-11-10 instruction supersedes the manual Refresh and generic
Analyze/re-analysis controls in MAINT-06 and MAINT-18. MAINT-06's automatic
initial review and protected-data boundaries remain; MAINT-18's fast cached
navigation remains. No refresh control is required for prior acceptance.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-20 | Remove the shared Refresh and generic Analyze buttons from every Maintenance category. Entering Maintenance automatically reads all category summaries and reviews Cleanup. Activating a category immediately shows cached rows, then refreshes its Windows state asynchronously when needed; returning to Cleanup automatically updates its review without deleting data or prompting for elevation. Completed actions automatically update their affected rows and summary. Protected Cleanup rows require explicit category-specific administrator review through Maintenance's own backend; standard entry must not request elevation. Preserve cancellation, exact cleanup review safety, selected identity, filters, sorting, and the quick Cleanup-to-Startup transition. No polling timer or blocking tab switch. Verify an owned external Startup change appears after tab activation without a button, refreshed Cleanup after an action, button absence across all five categories, and a normal independently and installed-verified release. |
| MAINT-21 | Cleanup's Details column uses brief, consistent descriptions of the action and its consequence. Move cache paths, unavailable-entry counts, scan examples, and similar diagnostics out of the table summary while retaining them in the selected assessment or operation result when needed to explain scope, partial access or failure. Preserve counts, size, truthful state, and useful failure reporting. Verify manager and built-in Cleanup rows in the actual UI and delivered package. |
| MAINT-22 | Show the Recommendation column only in Windows tweaks, where the reviewed catalog covers the displayed settings. Remove it from Startup, Services, and Windows apps, whose current installed inventories have sparse or no qualified matching rules. Do not infer advice for arbitrary startup entries, services, or apps from a donor preset or a name resemblance. Retain exact matched guidance and source in the selected assessment; keep actual State, identity, sort, filters, and explicit actions. Verify all four non-Cleanup headers and selected detail behavior in the actual UI and delivered package. |
| MAINT-23 | In Maintenance, visually subordinate the inactive Performance icon while keeping its mode-switch action available. The administrator shield belongs only to Explorer browsing: disable and dim it while Maintenance is open, regardless of Explorer's background access mode, and restore its correct state when returning to files. Maintenance must use an independent elevated backend session for its protected Cleanup review and elevated Startup, Services, tweaks and app actions; the shield must neither start nor end that session. Protected Cleanup requires an explicit per-category review before Clean and preserves the exact reviewed scope. Verify mode visuals, backend separation, cancellation and release in the actual UI and delivered package. This current instruction supersedes S30's shared-session routing for Maintenance only. |

### UniGetUI package-manager Cleanup (S43)

The user's UniGetUI configuration lists WinGet, Scoop, Chocolatey, Npm, Pip,
Cargo, vcpkg, .NET Tool, PowerShell, PowerShell7, Installer and Bun as enabled.
Scoop remains governed by MAINT-14..17. User2026-11-09 selected reviewed
regenerable caches where a manager has no safe obsolete-version inventory.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-19 | Review the configured/installed UniGetUI managers above and add Cleanup coverage for every independently qualified obsolete-version, download or regenerable-cache action. Resolve each manager's actual configured location or supported native cleanup command, not a guessed global path. Show a manager-specific preview, scope, useful count and size where safely measurable, and expected loss of cached offline/reuse data; require explicit Clean and recheck the reviewed manager/root/current state before mutation. Preserve installed/current packages, active consumers, shared build dependencies, manager databases/indexes, user downloads and unrelated configuration. Never treat WinGet's configured Downloads folder or a vcpkg/Cargo build tree as a disposable cache. The user's 2026-11-09 correction supersedes the placeholder-row presentation: Cleanup must list only installed managers with a real qualified action; document managers without a safe generic action outside the table. Do not show vague states such as `No safe cleanup` or `HTTP cache present`, or a false `Unavailable` for Pip when the configured Scoop Python is installed. Keep UI responsive, allow cancellation at safe boundaries, and report partial failures honestly. Verify owned manager fixtures and actual installed-manager read-only inventory, then deliver and installed-verify a normal corrective release. |

### Maintenance tab navigation (S42)

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-18 | Switching from Cleanup to Startup displays the Startup view without waiting for a second registry, Startup-folder and Task Scheduler enumeration. Reuse rows already loaded for the Maintenance summary; when rows have not yet loaded, show the selected view and an explicit loading state while enumeration runs asynchronously. A user Refresh and a completed Startup action still obtain fresh Windows state. Preserve the selected row, filters, sorting, action availability, card summaries and existing cancellation/lifecycle behavior. Verify an actual Cleanup-to-Startup transition and fresh Refresh/action results before the combined normal release. |

### Direct Maintenance actions (S40)

User2026-11-09 requires guidance when selecting an entry rather than a second
confirmation after an explicit action. This supersedes earlier inline action
confirmation requirements in MAINT-09 and prior Maintenance criteria.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-12 | Show applicable action effects/guidance in the selected-row explanation before acting, including Cleanup's reviewed-file scope. Explicit Apply/Disable/Clean reviewed/Remove/Restore and Create restore point execute directly without an application confirmation step. Double-clicking a real Startup row disables an enabled entry or restores its Pathweft-saved original state; rows with no supported transition, headers and empty space do nothing. Retain explicit action buttons/keyboard accessibility, unsupported/busy gates, exact-state journal/external-change protection, and shared administrator-session/UAC handling. Do not auto-elevate on selection. Verify real owned Startup fixture double-click round trips, direct buttons/Restore, selected information, ignored header/empty gestures and busy handling without mutating user entries; verify one normal final release independently. |
| MAINT-13 | The selected assessment is read-only but supports mouse/keyboard text selection and Copy of the complete identity, registry value/command, effects and guidance. Preserve the existing themed compact explanation and link, with no white editing surface or mutation. Verify selected text exactly without using the user's clipboard. |

### Package-manager cleanup (S41)

User2026-11-09 requests package-installer leftovers in Cleanup, illustrated by
obsolete Scoop app-version folders beside a current junction. Start with Scoop
download-cache and obsolete-version inventory. Preserve the current target,
persistent app data, active consumers and manager bookkeeping. No age-based
folder sweep or Windows Installer-cache purge. Integrate analysis/review/action
with the existing Cleanup tab/card and verify owned package fixtures and one
combined normal release after S40 and S41 acceptance.

MAINT-16 (S41 responsiveness correction, revised by the user2026-11-09): automatic Cleanup analysis must not
lock the browser's navigation or its read-only Maintenance inspection. Keep
selection, sorting, selected details and already loaded category tabs usable
while analysis runs; mutation and duplicate analysis remain gated. Leaving
Maintenance cancels/drains the original scan without withholding the file view. Observe
cancellation throughout Scoop consumer-check enumeration and between native
registration calls; retain active-package protection and release every native
session/handle on cancellation. Show the category being analyzed in its row.
The brush first replaces the bottom cards only, just like Performance. A card
opens its analysis section; Back to files restores the browser and keeps the
cards. Leaving Maintenance mode cancels/drains its original work without blocking
switching modes or file tabs. Keep the hidden-files eye visible; suppress its
selected square in Maintenance/Performance and restore the preserved browser
state on return. Disk-analysis enabled/selected state has one context-aware
writer and must not oscillate on statistics updates.
Verify actual UI actions during an event-held scan, cancellation during native
Scoop resource registration, original-task drainage and repeat entry. The
published1.1.39 independent popup-search assertion failed; preserve and
investigate that result, without weakening the assertion, before independently
qualifying the corrected release. An unreplicated diagnostic is not evidence of
a repaired production search defect.

Delivery correction2026-11-09: independent1.1.41 and1.1.42 probes reproduce
the real-index search rejection while Everything's notification window is
temporarily marked hung; an independent read-only Everything CLI query succeeds.
The SDK's nonwaiting query is asynchronous and the client already uses a
dedicated dispatcher and correlated reply. Remove the advisory hung-window
preflight that rejects an otherwise available query; retain native IPC error,
engine identity, cancellation and reply checks. Qualify the real index search,
compact popup and Maintenance review from the final downloaded package.

MAINT-14: add Scoop obsolete installed versions and completed obsolete download
cache entries to the existing automatic Cleanup queue. Resolve the current-user
Scoop root/cache from its environment/configuration and defaults. Identify
installed versions using Scoop metadata and a valid current junction; ambiguous
or no-junction installations are preserved. Exclude the Scoop manager itself,
current version/cache, partial downloads, unknown files and persistent data.
User2026-11-09 explicitly replaces the per-file review/deletion contract for
obsolete Scoop versions: inventory candidate version folders and estimate bytes
using Everything only; an absent size remains unknown and never causes recursive
enumeration or excludes an otherwise identified obsolete version. Analysis never registers package files with Restart Manager
or creates per-file deletion manifests. On explicit Clean, recheck current binding
and installation metadata, then remove each obsolete version
folder recursively. Contents added/changed within that obsolete version are
included, as requested; never traverse persistent-data links or other reparse
targets. Current versions and external persistent data remain protected. An
in-use obsolete version can be partially deleted before Windows rejects a
locked member; report that incomplete result. Download-cache cleanup retains
its exact reviewed-file contract.
MAINT-17: correct the reported two-minute analysis and unresponsive/opaque Clean.
User correction2026-11-09: do not run a package-use preflight. The user wants
reviewed obsolete folders deleted directly, letting Windows reject files in
use. Register no individual files or executables with Restart Manager; do not
enumerate package members for a use check or require administrator access for
this action. Preserve current-version binding and persistent links. A lock may
make recursive deletion fail after partial work; report the incomplete version
and retained locked member honestly rather than claim it was cleaned. Verify
that Clean begins deletion without a consumer-query delay, that a locked member
is reported, and that no host Scoop package is deleted as a test.
User-reported installed 1.1.44 regression: 15 reviewed obsolete versions were
skipped because Scoop's persistent-data junction names have the ReadOnly bit.
Remove those junction names during the obsolete-version deletion without
changing their persistent targets or contents. Retain honest failure reporting
for genuine locks and permissions. Verify the exact ReadOnly-junction behavior
in an owned fixture and verify the corrected installed release before retrying
the previously selected host obsolete versions.
Installed 1.1.45 retry removed 14 of those 15 versions but stopped at an
ordinary ReadOnly `msys2/.../cacerts` file in the last partially removed tree.
When direct native deletion fails for this reason, clear ReadOnly only on
ordinary remaining members of that obsolete version and retry deletion once;
never change attributes through a reparse point into persistent data. Preserve
the current binding and genuine lock/permission failure reporting. Verify an
owned ReadOnly regular file and the corrected installed release, then remove
the exact remaining MSYS2 residual without sweeping other Scoop versions.
Clean immediately opens a themed, modeless deletion window with phase/current
folder, completed/total progress and cancellation. Native recursive deletion is
one operation per version; cancellation stops before the next folder and must
not claim that an in-flight native operation has already stopped. Navigation,
selection, sorting and loaded Maintenance categories remain usable during Clean;
duplicate actions stay disabled. Report actual completion, skipped folders and
errors, and auto-close successful work. Verify actual owned deletion/cancellation,
busy/current/persistent preservation, missing-index inventory, held-worker UI
navigation and real phase timings. Never mutate host Scoop packages as a test.
Show candidate names/paths in selectable assessment and aggregate their bytes
in Cleanup. Verify owned fixtures for normal cleanup, current retargeting,
whole-version removal including new/changed members, busy files, hardlink names,
persistent links and active executables; never
clean the host's package installation as a test. Final S41 delivery bundles S40
and MAINT-14 in one normal independently verified release.

User refinements2026-11-09: all Maintenance tables use compact single-line
details and the same selectable selected-item well (including Services).
Expand the sparse guidance using the reviewed donors/primary sources, retaining
exact identity and explicit feature-loss conditions. Repair app-owned Startup
matching after the source-detail format changed. Unknown entries must not be
invented as safe. Configuration qualification is required for disable/apply
recommendations; documented Keep enabled guidance can be verified read-only
against exact installed Windows services and their native dependent features.
Show review coverage clearly; lack of a rule is distinct from a recommendation.

MAINT-15: complete the donor adoption requested again on2026-11-09. Add real,
individually selectable Windows settings from the pinned MIT Win11Debloat source,
covering privacy/suggestions, Explorer, Start/taskbar, appearance and local input/
power preferences. Each action has inspectable exact registry targets, current
state detection, build bounds, specific effects and exact journal-based Restore;
no donor preset/default reset, script execution or automatic application. Keep
network/DNS, app installation, security enforcement and thermal controls excluded.
Verify every new action through the real worker Apply/readback/Restore in the
disposable guest. Expand exact service guidance, repair app-owned Startup identity
matching, retain honest unknowns, and show assessed coverage. Verify compact rows
and the complete selectable detail well for Services and Startup, all themes/
scales/narrow layout, package/source notices and combined independent delivery.

### Maintenance recommendations and categorized settings (S39)

User2026-11-08 authorizes implementation of MAINTENANCE-DONOR-REVIEW.md and
Win11Debloat-like categories. This substantial feature has normal final-release
authorization; S38's prohibition on compiling/releasing that tiny fix alone is
preserved. No automatic optimization or mutation of the host is authorized.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-08 | Bundle an inspectable versioned JSON guidance catalog with stable rule IDs, exact targets, Windows build applicability, suggested action, rationale, lost features, primary/donor sources and review/qualification dates. Startup, Services, Windows tweaks and Windows apps show Keep enabled, Optional, Advanced or Not assessed separately from actual state. Unknown identities remain unassessed; no inference from resource use, broad name patterns or a donor preset. Catalog data cannot introduce executable commands or privileged write targets. |
| MAINT-09 | Group existing Windows tweaks into meaningful categories inspired by Win11Debloat (Privacy and suggestions, Appearance, System, File Explorer, Start and taskbar, AI). Search and category filtering operate locally over the existing rows. Keep the five bottom Maintenance cards, sortable table, keyboard selection, selected identity and exact-state Restore. Expose effect, applicability, source and selected service dependencies/account/executable details in the existing pane and action confirmation. No extra category sidebar, blanket recommended selection or new daemon. |
| MAINT-10 | Gate unsupported tweak actions both in UI and worker, retaining Restore of already saved state. Read native service dependencies and distinguish Manual/Disabled and saved original/Windows default. Exact matching for Pathweft's own startup commands may identify its enabled capabilities; third-party entries without verified identity stay unassessed. Never replace exact journal restoration with donor defaults. |
| MAINT-11 | Verify catalog completeness, exact matching, unsupported builds, source integrity, search/group/sort/selection and disabled/loading states. Qualify adopted actionable rules in a disposable Windows guest with native apply/readback, exact original restoration and dependent-feature configuration checks. No host service/startup/tweak changes. Capture compact dark/light100/150 and narrow evidence under the Huashu contract; independently verify one final normal release and clean spent scratch immediately after consumers exit. |

Huashu contract: retain the app's StatCard surfaces, theme/accent resources,
16-DIP spacing, existing body typography and compact five-card navigation.
Use a wrapping filter row above the table, subtle category headers and a bounded
selected-item explanation below it. Recommendation text plus a small symbol
must remain understandable without color. Source/applicability text belongs in
the explanation, not a wide debug column. At narrow widths filters wrap and the
table scrolls horizontally; no white native controls or inaccessible actions.
Network/DNS, package management, security enforcement, remote features and
hardware/thermal monitoring remain excluded. Existing typed actions are the
adoption boundary; this stage adds guidance/categories, not unreviewed presets.

User refinement2026-11-08: category dropdown text and arrow must share vertical
alignment at100/150-percent scaling. The Tweaks card counts applicable pending
recommendations, not every unapplied setting: qualified Optional actions not yet
applied and Keep enabled settings currently disabled by that tweak. Applied
Optional, Advanced, unassessed, unsupported and inaccessible actions are omitted.
Filtering does not change the whole-category summary; no action is auto-selected.

### Updater scratch lifetime and live analysis disclosure (S38)

User report2026-11-08: Runtime/Temp contains26 successful updater downloads,
while analysis temporarily shows their8.9GB aggregate without the file children
and labels a changing snapshot Incomplete. S38 is source-only per the user's
subsequent explicit instruction: no further compilation and no release for this
fix. Retain the already completed checks; bundle the source in a later release.
Keep the installed per-user/relocated profile binding; the user's question about
beside-binary config is explanatory, not authorization to migrate it.

| ID | Required behavior and acceptance |
| --- | --- |
| STORAGE-01 | Successful updater downloads are owned through the exact installer process exit, then removed. Cancelled, rejected and unlaunched downloads are discarded. Recover abandoned owned downloads on the next normal launch; never delete a running installer, unrelated Temp members or a replaced file. Verify actual process-completion and HTTP/cancel/error workflows. Remove the evidenced obsolete live downloads using reviewed exact-file cleanup after provenance/consumer checks. |
| SPACE-08 | Expanding a branch presents its actual direct files during unrelated root scanning, without another recursive scan, unbounded whole-tree file materialization or premature owner completion. Distinguish changed snapshots from terminal partial coverage; display queued refresh as updating, never Incomplete for ordinary active work. Preserve last known values, typed denial, cancellation, navigation and resource bounds. Verify a held unrelated scan, expansion, invalidation and true denied-child cases with actual UI/native operations. |

Huashu contract: retain existing compact tree columns, palette, typography,
chevrons and widths. Use concise status text with contextual cause details;
no modal or new settings. Final delivery for this request is the verified source
commit and exact legacy cleanup; do not create a package or version bump.

### Drive-scoped performance detail (S37)

User request2026-11-08: adapt SysOptimizer's Disks detail into the existing
Performance > Drive I/O investigation view, scoped to the physical drive backing
the current path. Reuse donor logic where applicable and Pathweft's current
collector/history/layout. Include read/write throughput, latency, active time
and available drive health facts (identity, temperature, remaining life, powered
hours, writes, Windows health). Qualify donor source and Windows support before
adoption; never invent missing health facts or match disks by display-name alone.
No unrelated all-drive dashboard, network features, CPU/motherboard/fan monitoring
or automatic driver installation. Broader THRM/Taskbar Thermals remains deferred.

| ID | Required behavior and acceptance |
| --- | --- |
| DRIVE-01 | Existing Drive I/O investigation shows current backing-drive read/write, latency and active-time details with a shared time range and theme/layout consistent with the current investigation. Reuse qualified donor implementation; preserve path-to-drive identity across navigation and truthful unsupported virtual/network cases. |
| DRIVE-02 | Present only available, correctly attributed drive-health facts with source/unit context and clear unavailable/permission states. Reuse approved shared administrator access where needed, without blanket elevation or driver deployment. Bound background work, collection/storage and lifecycle; verify real native mapping, telemetry, permissions and representative rendered sizes/themes. |

S37 follows S36 source/native/UI acceptance. One combined final release includes
S35/S36/S37 after all implementation criteria pass, followed by independent
download/package checks and installed readback when the user updates.

### Protected-folder presentation and analysis access (S36)

User refinements2026-11-08: a denied folder must not look empty. Status-bar
diagnostics do not constitute the user-facing explanation. Preserve typed read
failures and show an in-pane Access denied explanation, a direct action using
the existing shared administrator session, and a faint red diagonal Access denied
ribbon matching the Read-only stripe. Clear it after successful reads or leaving
the destination. Keep normal empty folders normal and generic I/O failures distinct.

| ID | Required behavior and acceptance |
| --- | --- |
| ACCESS-04 | Protected Standard browsing presents Access denied in the pane, actionable administrator retry, accessible text and faint red edge-reaching ribbon. Use the existing shared session without reopening the app. Denied/cancelled elevation preserves destination and explanation; successful retry clears the state. No stale asynchronous error after navigation, no administrator-only rows exposed after switching to Standard, and no reliance on localized exception text. Inspect actual dark/light100/150-percent layouts and narrow presentation. |
| ACCESS-05 | The same shield controls real filesystem access in the existing disk analysis view. Drain the original scan before replacing its access context. Preserve root, mode, expanded identities and appropriate selection; Standard must not retain privileged-only descendants, totals or saved listings. Verify actual pane analysis and repeated Standard/Administrator changes on a real disposable protected fixture, shared-helper reuse and natural retirement. No changes to user ACLs/data. |

Huashu contract: reuse the existing pane typography, theme resources, compact
button and diagonal Read-only ribbon geometry. Put the explanation and action in
the list content, with diagnostic details secondary. No modal error or white busy
surface. S37 owns final combined delivery after both stages' criteria pass; S35's
instruction prohibits a standalone icon-only release, not this functional repair.

### Toolbar action accents (S35)

User refinement2026-11-08: add restrained blue details to the remaining neutral
toolbar artwork. New file uses a blue plus badge like New folder; Hidden uses a
blue slash only while hidden files are concealed; Terminal accents its C:\
prompt; Performance accents the trace; Delete accents its inner slots. Keep
existing neutral silhouettes,16-DIP canvases, compact hit areas, group spacing,
tooltips, accessible states and actions. The user then clarified: map only blue
icon details to the current application accent; keep neutral outlines unchanged.
Reuse the existing accessible accent tone and system ink in high contrast.
Refresh already-open controls without retaining old accent-color variants.
Further refinement: accent the smaller gear of Diskweft's dual-cog glyph and
the brush collar between bristles and handle. Preserve the original terminal
glyph and its exact lettering; recolor that geometry rather than replacing
the characters with a different font. The two-pane icon also accents only its
middle divider; its outer frame and the one-pane icon remain neutral.
Inspect actual dark/light/original,100/150-percent, narrow and hidden/visible
states. This is a source-only refinement: the user explicitly directed holding
publication until more changes are included. No standalone icon release.

| ID | Required behavior and acceptance |
| --- | --- |
| CORE-25 | Consistent action details for New file, Hidden, Terminal, Performance and Delete. Map the icons' blue detail fills/strokes to the existing application accent, retaining neutral outlines, compact geometry, native/fallback support and system high-contrast ink. Verify actual accent changes, rendered artwork, Hidden toggle and existing actions/themes/scales. Commit the verified source; publication remains held by the user's explicit instruction. |

### Automatic Maintenance review and sortable tables (S34)

User refinements2026-11-08: entering Maintenance queues every Cleanup category
and updates its summary without row-by-row Analyze clicks. Every Maintenance
table must sort, particularly enabled/disabled state. The renewed administrator
toggle request activates the already specified S30 after this cohesive usability
stage; deliver S34 and S30 together in one release, not an intermediate release.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-06 | Entering Maintenance automatically performs one sequential, cancellable read-only Cleanup review of all categories, publishing each completed estimate to its row and bottom summary. Preserve completed exact reviews across category navigation. Initial standard-access analysis never prompts repeatedly or deletes files; protected remainder can be explicitly reviewed through the approved administrator session. Refresh/re-analysis remains available without requiring a selected row. Hide/exit cancels and drains the original work and retires manifests. Partial access and cancellation remain truthful without blocking independent categories; deletion still requires selection and confirmation and preserves new/changed/busy files. |
| MAINT-07 | All displayed Maintenance column headers sort in both directions, with visible/accessibility direction. Size uses numeric bytes, not formatted text. Name/identity break ties deterministically. Retain per-category sort and selected identity through refresh, analysis row replacement, apply/restore and navigation. Exercise actual headers across all five tables, numeric ordering, row replacement and dark/light100/150-percent layouts. |

Huashu contract: preserve existing compact WPF main pane, five bottom cards,
palette, table headers and command spacing. Add only a small header chevron and
concise queued-analysis progress. No white busy surface or extra navigation UI.


### Maintenance coverage and table-state corrections (S33)

User refinement2026-11-08: eliminate the temporary white table in every module;
expand common Windows startup coverage with a visible persistence Type; replace
the inherited ten-service shortlist with actual installed service enumeration;
correct the Performance toolbar command while Maintenance owns the main pane.
The original SysOptimizer shortlist was a tune-up catalogue, not a complete
Windows inventory. Preserve its source/license provenance, not its coverage limit.

| ID | Required behavior and acceptance |
| --- | --- |
| MAINT-01 | List tables retain the current dark/light palette while loading, disabled, confirming and cancelling. Fix the shared template/state causing the white surface; preserve real selection, scrolling, virtualization and keyboard access. Verify actual rendered enabled/disabled/loading states across Maintenance categories and other shared table consumers at100/150-percent scale. |
| MAINT-02 | Startup enumerates current-user/all-user Run and RunOnce registry views, policy Run entries, per-user/common Startup-folder files/shortcuts and registered scheduled tasks with logon or boot triggers. Show Type, scope, current state and location/command; distinguish task triggers and disabled Windows startup approval. Keep stable source identities and existing Run restore journals. Do not claim exhaustive Autoruns/security-hook coverage. Reads never launch entries, tasks or shortcuts and require no elevation; inaccessible sources are explicit without hiding independent readable rows. |
| MAINT-03 | Disable/Restore for the supported startup types preserves exact original state and rejects newer external changes. Registry value type/hive/view, Startup approval, shortcut identity/content and scheduled task enabled state remain source-bound; do not delete tasks or recreate their definitions. Journal durably before mutation; cancellation and readback are honest. Native mutation tests use only task-owned fixtures through the appropriate isolated Windows boundary, never user entries. |
| MAINT-04 | Services lists all installed Win32 services via native SCM, not only the donor catalogue or kernel drivers. Show actual service/display name, running state, startup mode, description and scope; keep unreadable properties explicit. Use native service identity for explicit Stop/disable and exact saved configuration Restore, with protected/dependent-service errors surfaced. Summary reports all installed and automatic counts. No blanket recommendation or automatic service change. |
| MAINT-05 | Performance and Maintenance are mutually exclusive pane modes. Entering either exits the other; only the actual visible mode has an active toolbar/accessibility state. Clicking the active mode or Back restores files and ordinary statistics, without resurfacing a hidden prior analysis. Verify both initial statistics/performance modes, category changes, repeated navigation and browser-state preservation. Complete focused source/native/rendered acceptance and one normal independently verified release with installed readback. |

Huashu contract: retain the current main-pane layout and five bottom card tabs,
existing palette/Muted/Accent/row templates and compact actions. Startup gains a
150-DIP Type column; irrelevant Size is omitted from Startup/Services, and Service
startup mode has its own column. Table content keeps its theme during busy states;
no white placeholder, extra sidebar, new collector, external tool dependency,
network/security module or S30 reusable-admin-session work is introduced.


Release authorization 2026-11-08: the user explicitly lifts the one-time pause
and requests publication. Resume S31's final delivery gate for the integrated
MONITOR-21 and completed S32/ADOPT-01..06 result as normal release 1.1.34.
Verify the actual package and independently downloaded assets through the
established release path. No additional publication confirmation is required.

User authorization 2026-11-08: implement the agreed local SysOptimizer adoption
and brush-led Maintenance, reusing pinned donor source as far as compatible.
All network functionality (not only DNS), remote monitoring, package management,
execution security, blanket optimization/memory trimming and duplicate scanners
are excluded. Hardware sensors/disk health/power/fans/FPS/peripheral monitoring
are deferred to a separate THRM/Taskbar Thermals consolidation assessment; they
are not acceptance requirements of this adoption. Preserve existing Diskweft
expandable largest-first folder tree and existing duplicate finding. Publication
was held for user validation; that hold is now lifted by the release instruction
above. S32 owns the following integrated source outcome.

| ID | Required behavior and acceptance |
| --- | --- |
| ADOPT-01 | Compact 16-DIP brush command opens themed Maintenance in the current main-pane area and replaces the bottom statistics with five summary-card tabs: Cleanup, Startup, Services, Windows tweaks and Windows apps (optional preinstalled apps). No additional category sidebar. Show analyzed recoverable bytes, enabled Run entries, reviewed automatic services, unapplied settings to review and installed optional apps; state each count's scope, do not invent estimates or treat every tweak as recommended. Preserve cleanup reviews across tab switches. Opening a category is read-only and never elevates. Active-state/tooltip/keyboard accessibility, narrow layouts, theme/scale behavior and Back to files/toggle restore the actual browser selection/path/scroll. Preserve transfers, preview and performance state. No standalone donor UI or extra collector. |
| ADOPT-02 | Cleanup analyzes explicit curated categories before action. Show review counts/bytes and apply only that review's unchanged files, skipping replacements, links, live/new files and inaccessible members. No recursive directory deletion or default all-category cleanup. Selection, cancellation and accurate partial/error reports are required; clean transient review storage when the review is replaced/closed. |
| ADOPT-03 | Startup and curated Services show actual state and scope. Explicit apply/restore preserves original registry hive/view/name/type/value and service startup configuration; restore must not overwrite an independently changed value. Journal the exact prior state durably before mutation; surface partial/failing actions honestly. No generic command runner. |
| ADOPT-04 | Borrow curated Windows tweak definitions with exact original-state restoration, current-state readback, restore-point action and validated selection export/import. Built-in app removal is explicit and current-user scoped, does not remove provisioning/all-user packages, and describes Store reinstallation rather than claiming undo. Use a narrow authenticated one-operation helper for privileged actions, preserving the unelevated GUI. S30 reusable admin-profile work remains separate. Real native verification uses task-owned fixtures, never modifies the user's chosen services/tweaks/apps as a test. |
| ADOPT-05 | Extend existing System analysis with local app CPU/RAM/GPU charts, user-requested selected-range CSV/JSON/HTML export, boot diagnostics and Windows/app/driver-change events, and on-demand local performance insights using shared retained observations. Compact Folder I/O shows useful read/write rates instead of the persistent debug-like Incomplete observations label; retain honest lower bounds and coverage detail in its tooltip/analysis. Reuse donor readers/rendering/report logic; preserve identity/gap semantics and bounded storage. No network/sensor fields, automatic reports, new recording setting or duplicate sampler. Missing Windows diagnostic access is explicit; UI queries are cancellable and stale results cannot replace current selection. |
| ADOPT-06 | Retain exact donor source/license/pin mapping and verify the actual in-app navigation, native read/apply/restore/permission/cancel boundaries, malformed inputs, lifecycle, existing browser/performance regressions and representative rendered dark/light/100/150-percent layouts. Publish the completed verified result through the resumed S31 delivery gate. |


S31 corrective reopening, user instruction 2026-11-08: the automatic rolling
hour must include process snapshots for hover, moment and range investigation,
not merely aggregate graph points. The earlier aggregate-only interpretation
did not satisfy the request. Capture recent selected CPU/RAM/GPU app observations
every 10 seconds from the existing shared collector even while hidden/in the
tray, bounded to one hour. Queries combine them with older saved observations
without double counting. Preserve true gaps, PID/start/GPU identities and the
automatic persistent history; no command-line retention, new collector, extra
elevation or MCP activation. Latest direct clarification supersedes the earlier
opt-in persistence policy: REMOVE the recording/saving switch and its setting.
Collection and saving start automatically with Pathweft, even hidden/in the tray.
Ignore legacy performance.record values and stop serializing that obsolete key.
Retain the existing 1/7/30-day retention control (default 7 days), a 256 MiB SQLite
database ceiling with oldest-first capacity pruning and bounded WAL/checkpoint
backpressure. Never erase the history on migration or require a manual enable.
Recent aggregate history keeps its existing 8 MiB qualification budget; the new
one-hour process snapshot ring has a separate 8 MiB budget in the 45-process,
two-adapter workload. Existing steady CPU/private-memory budgets remain unchanged.

MONITOR-21 acceptance: from a fresh profile with no database or recording switch, actual
background sampling -> hover/moment -> contemporaneous apps -> selected-range
top apps/app history -> Now works automatically. Verify CPU/RAM/GPU identities,
overlap with saved facts, durable restart/read-only MCP, gap/empty/cancellation
states, one-hour memory/CPU and database/WAL capacity bounds, tray lifecycle and
existing layout/privacy workflows. Optional column metadata remains demand-driven;
no background command-line/file-I/O logs. User explicitly requests
fix/verification and a pause before the next release; do not publish or install.
Reopen S31 only; S30 remains NOT STARTED. Prior 1.1.33 verification is historical
and does not establish MONITOR-21 acceptance.

### Performance continuity and process inspection (S31)

User refinement2026-11-08: range selection emptied the live graph and restoring
Last hour did not recover it. Recording must not be a prerequisite for inspecting
already-visible observations. Add CPU columns and click-to-pin inspection.

| ID | Required behavior and acceptance |
| --- | --- |
| MONITOR-17 | Analysis automatically combines available rolling-hour aggregate observations with older recorded observations, preferring live observations at overlap without double counting. Range, zoom, presets, reset/Now and navigation retain samples/events without depending on recording. Preserve true gaps, empty/error states and device identity. Live aggregates do not imply retained process snapshots; unavailable historical attribution remains explicit. No recording activation or new collection/retention. |
| MONITOR-18 | CPU process table offers a compact themed Columns chooser including executable path and command line. Resolve metadata asynchronously on demand, bound to PID/start identity; missing/exited/protected metadata is explicit. No elevation or persistent command-line recording. Keep column choices through live refreshes. Clicking a process row pins that identity above changing/sorted rows; clicking again or its unpin action restores ordinary ordering. Preserve pin/selection through refresh and never substitute a reused PID. Keyboard operation and accessible state are required. |
| MONITOR-19 | Verify recording-off range -> presets/Now -> recovered graph/events, mixed recorded/live overlap/gaps, repeated/cancelled queries, actual metadata, pin/unpin through refresh/sort/exit/PID reuse, themed narrow/high-DPI layouts, existing resources/privacy/browser state and one normal independently/package/installed verified release. |
| MONITOR-20 | Before this release, compare performance modules with the locally cloned SysOptimizer source and retain a feature/adoption matrix with exact source pins. Reuse applicable existing behavior rather than duplicate a collector or storage engine. Adopt its live process filtering, row reuse and half-percent stable ordering while preserving PID/start identity and explicit pinning. Filter locally without new sampling; executable/command matching uses only metadata already requested by visible columns. Audit background/tray CPU, memory/ring bounds, recording writes, hidden detail/metadata work, cancellation, native ownership and natural shutdown. Verify actual resource observations and regression workflows; do not activate recording, MCP or new drivers. |

Huashu: preserve the graph-led main pane, AuxiliaryAction/PreviewClipCombo,
Muted/Accent tokens, 32-DIP actions and virtualized GridView. Columns uses a
checked menu; optional long values use tooltip and horizontal scrolling. A small
monochrome pin marks pinned identities. No recording/source selector is added.
The CPU filter uses the existing AuxiliaryField palette/radius with a small
search glyph, "Filter processes" hint and visible keyboard focus border; verify
the rendered input and separate popup surface in dark/light and narrow/high-DPI.
S31 is the single ACTIVE stage, before the unstarted S30 administrator feature.
User clarification2026-11-08: remote-PC monitoring/features are excluded; this
integration concerns only the local computer. Donor comparison does not add
remote agents, listeners, credentials, device selectors or remote history.

### Reusable administrator access session (S30)

User refinement2026-11-08: a bottom application-toolbar toggle switches filesystem
browsing between standard and administrator access without reopening Pathweft.
One native UAC approval creates an app-owned elevated access session; existing
features needing elevation reuse that session while it remains alive. The main
browser stays unelevated. This is an access mode for the current signed-in user,
not a second Windows profile or a permission/ACL change.

| ID | Required behavior and acceptance |
| --- | --- |
| ACCESS-01 | Add a compact UAC shield immediately beside Show/Hide hidden files, using blue internal lines and a neutral outer outline matching the latest user reference and existing toolbar proportions. Expose Standard / Administrator browsing through active state, tooltip and accessible name. Initial mode is Standard; switching can be done without reopening or losing pane/tab/selection/scroll state. UAC denial leaves the previous mode and data intact. |
| ACCESS-02 | Authenticate and bind a narrowly scoped elevated helper to the exact Pathweft owner/session. One approved helper serves protected folder enumeration and existing privileged analysis/I/O requests; standard browsing stops issuing elevated enumeration requests but the approved helper can remain available until explicit End administrator session or full app exit. Stop traces when their ordinary visible scope ends. No scheduled task/service, credential storage, UAC setting changes, ACL changes or blanket privileged shell execution. Existing destructive-action confirmations and actual access failures remain explicit. |
| ACCESS-03 | Qualify real approved/denied/reused UAC lifecycle, standard/admin browsing of task-owned protected fixtures, privilege/mode indication, cancellation, owner exit/crash, non-owner rejection and unchanged standard drag/drop/profile identity. Bound idle cost and cleanly retire the elevated session. One normal independently/installed verified release after integrated acceptance. |

Design refinement2026-11-08: place the shield immediately beside Show/Hide
hidden files. Adapt the user-supplied `uac-shield.svg` silhouette: transparent
quarters, full cyan interior dividers and a neutral outer outline (white in dark
mode, dark in light mode, system foreground in high contrast). This latest
direction replaces the earlier monochrome-quarter rendering, not the session
behavior: Standard browsing leaves other privileged features' approved session
available. Active state, tooltip and accessibility distinguish browsing mode
from session availability. No blue/yellow bitmap or unrelated command recoloring.
The existing protected text-editor save must reuse this session too: retain its
explicit Save as administrator confirmation, hash-bound staged contents and
atomic destination replacement with original ACLs. Do not launch a privileged
shell command for saving, or acknowledge a save before replacement completes.
Windows' split-token model and authenticated IPC inform implementation:
https://learn.microsoft.com/windows/security/application-security/application-control/user-account-control/how-it-works
and https://learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights.
S30 follows S29; it does not open a concurrent implementation stage.

### Interactive performance investigation (S29)

User reference2026-11-08: keep compact widgets; their expanded main-pane view
should provide graph-led investigation like the supplied SysOptimizer History
capture. Preserve Pathweft styling and existing recording/privacy boundaries.
Reuse and polish the donor's source implementation rather than rebuilding its
existing graph and interaction logic. Retain the selected original, exact pin and
license; adapt its data/theme/window bindings to Pathweft.

| ID | Required behavior and acceptance |
| --- | --- |
| MONITOR-14 | Expanded CPU/RAM/GPU/drive analysis provides a large time-labelled graph, grouped clickable event/spike markers, an overview with range selection, previous/next range and return-to-now controls. Hover shows the observed time/value; click locks a moment and shows contemporaneous recorded heavy apps. Selecting a range updates the summary/top-app facts for that interval. Keyboard access, explicit selection and empty/gapped/loading/error states are required. Compact cards retain their current layout. |
| MONITOR-15 | Derive markers from actual recording/counter/gap events and sampled threshold spikes; name their type, time and grouped count. Do not invent app launches, exact process lifetimes, cause, temperature or unrecorded metrics. Moment attribution must use a bounded neighbouring recorded sample and preserve PID/start/device identity; missing snapshots and historical drive/folder attribution remain explicit. Recording stays opt-in; no new collector, service, tracing scope, retention default or MCP activation. Reuse existing bounded history queries and display coverage/truncation. |
| MONITOR-16 | Verify real graph interactions -> moment/range queries -> linked tables -> reset/navigation/back to unchanged browser, including gaps, unavailable attribution, repeated selection, cancellation and shutdown. Render dark/light100/150%, narrow/wide with readable axes/markers, no clipping and existing theme/control tokens. Deliver one normal release with independent package checks and applicable installed readback. |
| MONITOR-22 | User direction 2026-11-13 (an emulator grew ~1 GB/min to 30 GB and exhausted RAM): RAM analysis derives a "Memory growth" event from the recorded heavy-app snapshots when one process identity gains at least 15% of installed memory (minimum 2 GB) over at least 8 minutes at 0.25 GB/min or more; working-set trims up to a quarter of the gain and stalls under 5 minutes stay in one interval and level periods before growth are excluded, so start-up bursts are not flagged. The row names the app, rate, duration and start/end working set. Spike markers sit at the observed peak and name the largest app there and, for RAM, the app that grew most since the spike began, marked as sampled and not proof of cause. Spike and growth markers use the theme caution colour; lifecycle and gap markers stay neutral. Selecting a spike or growth row investigates the apps at its peak. Computed at query time from existing history and the automatic recent snapshots; no new collection, notifications or MCP payload change. Verify a steady grower with a trim, a start-up burst, the installed-memory threshold, the unsaved recent-snapshot path, peak-placed attributed spikes and RAM-only growth events. |
| MONITOR-23 | Owner direction 2026-11-14 (issue #5; orphaned root-wide searches leaked ~1.5M handles each and crashed Explorer): handle counts live under RAM. Heavy-app samples (MONITOR-11) record each process's handle count, and also keep the top 15 handle holders; the system handle total and kernel paged/non-paged pool are recorded with the aggregate samples and shown in RAM details. RAM analysis derives a "Handle growth" event, one per process identity, when it reaches 250,000 handles or gains 100,000 within 5 minutes while still growing, with the caution presentation, RAM view only. The RAM table gains a sortable Handles column, hidden by default (unknown shows as —); no Handles column in the CPU table. Live CPU, RAM and GPU tables, app details and investigated moments mark a process whose parent has ended (a parent PID that is gone or now belongs to a later process). For a pinned process, Handle types reads a per-type count on demand only (types, never object names, bounded to a 256 MiB handle table) and Open drive I/O opens the drive card for the current folder's volume (not filtered to the process); End process acts only on an explicit confirmed click and rechecks PID and start time through the terminating handle, refusing a reused PID and Pathweft itself. MCP get_top_processes reports Handles and ParentEnded, get_events reports Handle growth; older history without these fields reads as unknown, not zero. Verify synthetic level/grower/stable cases, older-history compatibility, the column, and a real detached fixture holding handles for the parent-ended mark, type breakdown and identity-checked End process. |

Huashu design contract: the graph owns the available main-pane width and about
half the analysis height; a slim event rail above it and an overview below it
share one time scale. Existing Accent/Muted/StatCard resources, 10-DIP spacing,
32-DIP actions and virtualized tables remain. A selected moment has a vertical
cursor and a persistent textual summary; a selected interval has outlined handles
and a tinted band. Narrow layouts wrap compact controls; a finite-height scroll
area keeps the whole 180-DIP-minimum timeline and virtualized table reachable
when the available height cannot fit both. Keep time labels/hints within width.
No donor-specific window chrome, theme picker or maintenance navigation is added.

### Integrated system analysis (S28)

User-authorized first release: performance/history/read-only MCP before S25.
Maintenance, DNS, winget/apps, security enforcement and new hardware drivers
are excluded. Selectively adapt SysOptimizer MIT source at
6ddd104fe4fc3f990bc2a9b376e5795943541d49, preserving provenance/notices;
keep Pathweft's qualified counter, device identity and tracing ownership.

| ID | Required behavior and acceptance |
| --- | --- |
| MONITOR-09 | Widgets open System analysis in the main browser region: large graphs, range controls, themed selectors and sortable process/file tables. Back to files/Escape restore the same pane objects, tabs, selection and scroll. Hidden file commands cannot act on underlying selections. Keep cards at statistics height and qualify dark/light100/150% narrow/wide layouts using existing Huashu/Pathweft tokens. Supersedes bottom-only expanded details. |
| MONITOR-10 | Opt-in persistent CPU/physical RAM/per-GPU/per-drive history under DataFolder.Bulk, default off; explicit recording and 1/7/30-day retention. One sampler/writer, transactional persistence, indexed streaming queries, retention pruning and bounded memory. No background folder/file logs or new service/startup entry. Off stops recording/process enumeration but preserves old observations. Missing values/gaps/device identities/storage errors remain explicit. |
| MONITOR-11 | Shared UI/MCP queries provide summaries/peaks, top observed apps, app observations and events/spikes. Record a bounded heavy-app selection every ten seconds with explicit incomplete coverage and PID/start identity. Do not call sampled appearances exact launch/exit events or infer causation. Aggregate all matching observations; detailed output bounds report truncation. Missing/stale data and inaccessible processes are explicit. |
| MONITOR-12 | Bundle explicit stdio --performance-mcp with get_current_usage, get_usage_summary, get_top_processes, find_spikes, get_app_history and get_events. Read recorded history without WPF startup/elevation/another sampler/network listeners/client-configuration writes or activation. Finite version negotiation, input validation, structured errors and EOF exit. Qualify real subprocess protocol/read-only queries, restart persistence, retention/gaps/device/identity boundaries. Current usage is the latest recorded observation plus age, not invented live data when recording is off. |
| MONITOR-13 | Verify complete widget -> analysis -> record/query -> return -> restart -> same MCP facts, actual counters, layouts, tracing lifecycle and resource bounds. Keep existing off-mode budgets; recording adds at most 1% normalized CPU and 32MiB retained private memory in the qualified workload. Deliver one normal release with independent retrieval/package checks and applicable installed readback. |

Design contract: existing StatCard/Muted/Accent/AuxiliaryAction/PreviewClipCombo,
10-DIP gaps, minimum32-DIP actions, flexible graph above virtualized tables,
wrapping controls at narrow widths. Analysis replaces the main browser region,
not bottom cards. MCP is packaged but no client is enabled or reconfigured.

User refinement2026-11-07: S27 COMPLETE through verified1.1.30 for MONITOR-06..08 and supersedes
S18's focus-only folder-collection behavior. Graphs span the actual observed
history across their available width (up to one hour), retain real gaps, and
label the observed duration rather than implying missing time was recorded.
Add physical system RAM separately from GPU dedicated memory. Present CPU,
RAM, GPU, Drive I/O and Folder I/O in one compact row with the same vertical
footprint as ordinary drive/folder statistics. Use existing StatCard, StatSub,
StatValue, Accent and Muted tokens, 10-DIP inter-card gaps, ellipsis/tooltips
for secondary labels at narrow widths and the existing expanded detail region.
Folder collection automatically follows a loaded supported path while the
performance view is visible, after the first Windows permission request;
keyboard focus is not a prerequisite. Cancellation leaves an explicit retry
action and never repeatedly prompts. Hidden/minimized/tray/statistics states
stop folder tracing; a permitted idle app-owned worker may remain connected to
avoid repeated UAC on return, and exits with the browser. Existing global
aggregate/tray history, current-path boundaries, ownership and resource budgets
remain. Verify real RAM/counters, short/full-hour/gapped rendered graphs,
five-card heights at dark/light100/150% and narrow/wide widths, automatic
file-pane-focus collection, navigation, visibility suspension/resumption and
worker exit. Complete one normal independently verified release. S26 COMPLETE;
S25 and unrelated SysOptimizer/MCP research remain unimplemented.

Design constraint2026-11-07: any later SysOptimizer-derived performance/MCP
integration must fit Pathweft's existing native app design and layouts. Reuse
the compact statistics/performance cards, type, spacing, theme tokens and current
expanded-detail region; qualify narrow/wide and high-DPI layouts. The proposed
AppControl replacement covers performance tracking/queries only; DNS, app
management and security enforcement are outside that replacement scope.

User request2026-11-07: synchronize the thirteen upstream commits through
File Labs8ceb281f0145aefe3768a2966a4942bf80f89f44 and assess GitHub code-scanning
alert1. S26 owns this cohesive integration and its next normal verified release,
before S25. Preserve Pathweft identity, established storage/lifecycle, native
integration, transfer correctness and current UI contracts. Reconcile incoming
transfer/undo/rename, recycling, filtering/search and icon changes with their
existing counterparts; do not import upstream promotions or replace the fork's
packaging, monthly banners or versioning. Record each adopted or superseded
change and qualify real affected workflows, rollback/persistence and cancellation.
Retain scanning coverage; resolve the alert from exact source/control/sink and
shipped-runtime evidence, correcting an actionable defect or recording a justified
false-positive disposition. Final acceptance includes ancestry at upstream/main,
normal publication, independent delivered sources/runtime checks and applicable
installed readback. S18 is COMPLETE through independently verified and installed
1.1.28/cc9f0ea. S26 is COMPLETE through normal1.1.29/d66f0c6, final163400 and
independent174916 source/native/application acceptance, installed164052 readback,
upstream ancestry and successful retained CodeQL coverage. No stage is ACTIVE;
no tracked deferral. See docs/verification/2026-11-07-s26-upstream.md.

User refinement2026-11-07: remove the duplicate drive-capacity strip and
free-space label below the address bar. Retain capacity in the sidebar and lower
statistics card. The subsequent explicit refinement replaces archive status
text with a subtle diagonal READ-ONLY ribbon at the file pane's bottom-right,
reaching its right and bottom outer pane edges, with text centered in the
visible stripe. The user selected this ImageGen direction over the detached
stamp. The user's rendered refinement extends it through the footer's unused
space to the pane border, with faint fill/lines/text rather than an opaque badge.
It must not intercept input or obscure footer controls; editable archives need no
label. Preserve actual error reporting and real loading indicators. S18 includes
this same-release refinement, with source and delivered
layout/navigation checks; it does not activate another stage.

Additional user refinement2026-11-07: use the address/search field background
for actual foreground folder/search loading, not the removed capacity strip.
The completed/active region keeps the field's current color and the remaining
track is black in dark mode (theme-appropriate in light mode). Unknown-duration
enumeration/query work uses an indeterminate band, never a fabricated percentage.
Routine background size/metadata refresh is quiet. Operation completion,
cancellation, supersession and errors retire only their own loading state;
never replace text, caret, selection or focus to update the indication. Preserve
reduced-motion behavior and qualify actual pending-work/input/navigation flows.

Resumption2026-11-07: the user returned; S18 is ACTIVE again. Native request
900eaac1ed8541d087aa3a85e1646757/ticket13 is submitted through the supported
broker. The prior rejection below remains evidence, not a current pause.

Earlier qualification checkpoint2026-11-07: S18 was BLOCKED on the native privileged
I/O acceptance workflow. Coordinator request6d675d773db2494aaf8ba495572ef098
launched nothing because fullscreen quiet mode was active. Source layouts,
aggregate/history/resource checks, non-admin denial/connection cancellation,
annual artwork selection and Release self-test pass. Native file attribution,
focus/lifecycle/detailed-cost and final packaged/delivery criteria remain
unmet. Resume S18 after user readiness and broker approval; no automatic retry,
no partial release and no later stage activation. This records an external
execution boundary without changing any acceptance criterion.

Implementation authorization2026-11-07: S18 is ACTIVE for MONITOR-01..05 and
CORE-25. The user directed starting this stage and explicitly requires Huashu
for new UI, consistent with existing Pathweft references. The planning-only
restrictions below are superseded for S18 implementation/testing/delivery;
S25 remains NOT STARTED. Use one app-owned aggregate sampler with a one-second
cadence and a bounded3,600-sample/hour ring. No background file tracing or disk
history logs. Windows PDH/GetSystemTimes and GPU engine/adapter counters supply
aggregate observations; detailed file I/O uses an explicitly enabled app-owned
ETW worker with supported Windows elevation, exact process/pipe ownership and
focus/visibility-scoped tracing. Missing permission is distinct from zero.
Acceptance budgets: aggregate history storage at most8MiB, incremental steady
tray private memory at most32MiB, aggregate sampling at most0.5% total-machine
CPU averaged over a measured representative observation interval. Detailed
collection uses bounded records/buffers and reports dropped observations. Its
representative focused-folder qualification budgets are at most1% total-machine
worker CPU and96MiB worker private memory, measured separately during known
file activity and an idle interval; these are acceptance workloads, not a claim
that arbitrary machine-wide tracing workloads have a hard total-CPU cap.
Cross-integrity qualification exposed a cancellation callback self-join in
worker shutdown. Correct the event-owned join without a timeout and verify
actual focused collection -> stop -> full process/session exit before acceptance.
Confirm actual device/adapter mappings and measured budgets during integrated
qualification rather than asserting them from design.

Huashu design contract: reuse StatCard's10-DIP corners, palette resources,
Segoe UI Variable hierarchy,10-DIP card gaps,20-DIP primary values and11-DIP
supporting labels. Performance uses four compact cards across a wide pane,
reflowing into two columns when needed, with small native line graphs. Use the
16-DIP graph-in-a-square command icon and existing32-DIP command hit area,
active tint and semantic names. Card activation opens direct themed table
details with a clear return action, scoped source/state and GPU selection;
retain browsing focus/drafts and reserve layout space rather than covering
rows. Validate light/dark,100/150%,wide/narrow/two-pane actual rendering.
No S25 animations are introduced in S18.

Planning refinement2026-11-07: S18 remains NOT STARTED. Use the Task Manager
Performance graph-in-a-square icon as the compact Statistics/Performance swap
control, with clear active state and tooltip/accessibility labels. Supersede
the earlier visible-only aggregate collection rule: retain a bounded rolling
hour of CPU, GPU and drive/unit-level I/O samples while Pathweft is running,
including in the tray. Folder I/O is explicitly focus-only: collect it only
while its widget is focused, never as part of the background drive history.
Keep detailed process/file tracing demand-driven. Reuse existing process/helper
ownership, measure the additional idle costs and distinguish observation gaps
from zero. No live collector or configuration change is started by this update.

S25 - UI quality review, refinement and motion (formerly Summary-card motion)
is COMPLETE (release 1.1.53); see its section for UIQA-01..03 and MOTION-01..04.
Cards should enter upward while the upper file pane smoothly yields space; the
Statistics/Performance swap should rotate the card region into view. Keep
ordinary browsing snappy and honor disabled/reduced system animation settings.
S25 owns its full verification and subsequent release. Remaining order:
S18 -> S25 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Next-release refinement2026-11-07: CORE-25 records twelve distinct ImageGen
changelog banners, January through December, as a companion to the next planned
release in S18. Use a consistent Pathweft visual style and the existing wide
960:170 presentation, with month-appropriate artwork instead of reusing October's
Halloween image for November. Select artwork by the selected release's month,
not today's date, and provide the complete annual set without year rollover
resource gaps. Inspect every generated image and actual packaged rendering.
This is a specification/plan update only: no generation, runtime change, release
or stage activation is performed now.

Current user-authorized refinement2026-11-07: S24 is COMPLETE for TRANSFER-17,
before S18; S16 remains COMPLETE. Refine the existing transfer popup without
changing transfer, verification, clipboard, conflict or completion semantics.
Remove the empty secondary per-file track; place completed and total file
counts above opposite ends of the single overall progress bar. Show aggregate
processed bytes at the top right beside the combined speed, with a separate
multi-job legend when needed. The user's subsequent refinement removes the
redundant word "processed": render the formatted byte amount alone in both
the header and retained outcomes. Counters read "480 completed" / "1,635 total"
("found" during scanning), without a redundant "files" noun. Keep active-file/current-file context and genuine
errors, partial/skipped outcomes and expandable details. Verification remains
internal: no routine verified/unchecked count in card titles or statistics.
Center the actual visible icon and label ink in Pause/Resume, Cancel and Dismiss
actions: matching text-box centers alone is insufficient across these fonts.
Design contract: retain640-DIP default/460-DIP minimum width,12-DIP card inset,
6-DIP primary bar,11-DIP muted counters,34-DIP minimum actions and6-DIP
icon/label gap. Preserve opaque first layout and content-sized height. Qualify
actual running/paused/conflict/failure/multiple-job layouts in dark/light at
100/150%, normal/minimum widths, actual completion/cancellation, and final
normal release with independent download and applicable installed verification.
Normal1.1.27 is independently downloaded and installed-verified; all assigned
acceptance criteria pass. No S18 or other feature implementation is activated by
this correction.

Implementation authorization2026-11-06: the user directed starting the next
stage, S16, and added advance core text extraction for subsequent content
indexing. S16 and S23 are now COMPLETE through installed1.1.26 acceptance;
no stage is ACTIVE. The existing
source-built DLL/app-owned worker architecture is retained. CRAFT-01..07 below
and CORE-24 define this release; S19 still owns the database/indexer/search UI.
This authorization does not change installed Craft/Coordinator selections or
introduce an MCP/installed application dependency. No OCR implementation is
implied by text extraction; image-only content must be identified honestly.

Next-release refinement2026-11-06: CORE-24 records the requested taskbar icon
adjustment for the next planned release, alongside S16. Increase the visible
folder/compass artwork by approximately10-15% relative to1.1.25, preserving its
design, transparency and Windows application/shortcut/Jump List identity. Fit
within the icon canvas without clipping; compare actual taskbar rendering and
small-size legibility at representative display scales. This records future
work only, without activating S16 or changing the installed icon.

Bug correction2026-11-06: S23 - Missing-folder tab recovery is COMPLETE under the
user's requested fix. CORE-23 requires selectable tabs when an old local folder
has disappeared: resolve confirmed missing descendants to the nearest existing
directory, however many parent levels are needed, and persist the repaired tab.
Preserve tab order/pins/active identity during switching and startup restoration,
normal filesystem/archive navigation, and normal unavailable/access-denied states.
Do not reinterpret a disconnected drive/network provider as a deleted folder or
block the UI on network recovery. Verify actual tab clicks, keyboard/external
activation, restored state and ancestor listing, then deliver one normal verified
release under DELIVERY-01..03. Verified through published/independently retrieved
and installed1.1.25 acceptance; see the S23 verification record. No later feature
stage is activated.

Plan addition2026-11-06: S22 - Focused file workflow enhancements is NOT STARTED,
immediately after Linux-inspired S21 and before S6. OPUS-01..10 register named
project workspaces, paired navigation, expandable/flat browsing, persistent
collections, operation history, listing export and lower-priority aliases,
photo marking and batch embedded-metadata editing. Implement focused behavior
inside Pathweft; use bounded REA only for a concrete unresolved behavior or
interoperability question. No Directory Opus installation/runtime dependency.
This request records future scope only; no implementation, uninstall, settings
change or stage activation. Remaining order:
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Plan addition2026-11-06: S21 - Browsing productivity and smart destinations is
NOT STARTED after S20 and before S22. BROWSE-01..12 register the discussed
Linux-inspired capabilities and browser-style address suggestions, including
recent/pinned folders, open tabs, saved searches and filesystem completions.
The user's latest instruction supersedes the earlier research-only treatment
of those suggestions by adding them to the future plan; it does not authorize
implementation, connection, metadata/source mutation or live configuration.
No implementation stage is ACTIVE. Remaining order:
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Plan addition2026-11-06: S20 - Integrated PowerToys file tools is NOT STARTED
after S19 and before S21. TOOLS-01..09 register the seven discussed capabilities:
PowerRename, File Locksmith, Image Resizer, Text Extractor/OCR, Registry Preview,
local deterministic Advanced Paste and New+. New+ is included at lower priority.
Borrow focused source implementations and integrate them into Pathweft's own
UI/selection/operation lifecycle; do not require a separate PowerToys installation.
This authorizes documentation only, not implementation, installation, registry
changes or process control. No stage is ACTIVE. The subsequent S21 request
explicitly registers the Linux-inspired findings as future scope.

Plan addition2026-11-06: S19 - Selected-folder content indexing is NOT STARTED,
immediately after S12 and before S20. CONTENT-01..06 record folder opt-in,
qualified Craft/plain-text extraction, searchable strings with source-location
arrays, periodic incremental refresh and current-index icon badges. Retaining
whole extracted documents or offline excerpts is superseded by the user's lean
strings/location design. SQLite FTS5 is the leading candidate, not a qualified
implementation. This is a plan-only request; no reads/downloads, indexing,
schedules, settings changes or implementation are activated.

Plan addition2026-11-06: S18 - Contextual performance monitoring is NOT STARTED,
ordered after S16 and before S12. Remaining execution order is
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7. This request authorizes specification/plan changes
only; it does not activate monitoring, tracing, privileged helpers or installation.
MONITOR-01..05 below capture the agreed bottom-card and Resource Monitor-inspired
detail behavior. No implementation stage is ACTIVE.

Execution reconciliation2026-11-06: S17 COMPLETE. TRANSFER-16, PREVIEW-38 and
CORE-22 are delivered in normal v1.1.24 at abacf4171f753616dd50ac2233b58df364036692.
Actual source/package, independent retrieval/full consumer57492 and installed
readback35716 verify all assigned criteria, including preserved Windows identity.
No unresolved S17 blocker or tracked deferral remains; no stage is ACTIVE.
Later unrequested stages remain unchanged. See the S17 verification record.
The authorization descriptions below document the scope at implementation start.

Approved branding direction2026-11-05, CORE-22, assigned to ACTIVE S17:
replace the application icon with the user-approved yellow folder and compass.
Use a clear darker compass silhouette at small title/taskbar sizes; retain
polished depth/detail at larger sizes without fine lettering becoming noise.
Provide transparent multiresolution Windows icon assets, verify 16/24/32/48/64/
128/256 sizes on light/dark surfaces and actual embedded WPF/native/package use.
Preserve Darkaxt.Pathweft application/shortcut/Jump List identity and all settings.
This joins TRANSFER-16/PREVIEW-38 in one final next update, not a separate release.

Additional S17 next-update request2026-11-05, PREVIEW-38: PiP and Windowed
previews follow the active file selection instead of keeping the original item
pinned. This explicitly supersedes PREVIEW-37's pin-while-browsing behavior.
The user subsequently requested an explicit pin icon alongside PiP/Windowed/close:
default unpinned follows selection; pinned retains the displayed item while
browsing/no selection, across host changes. Unpin immediately follows the current
selection in the same host. Keep pin state visibly/accessibly distinct and clear
it on explicit preview close; it is a session control, not a new persisted setting.
The user's subsequent approval activates CORE-22's folder/compass replacement.
Preserve the selected host mode/bounds on file changes; replace content/title/
icon and release the previous renderer/source lease correctly. Obsolete async
completions must not overwrite the latest selection. Keep mode-only transitions
state-preserving and floating-window closure restoring the sidebar. Clear stale
content for no selection under the existing preview enablement rules. Scale the
PiP and Windowed glyphs to the close icon's small visual proportion while keeping
accessible tooltips and usable hit targets. Qualify actual selection changes in
both hosts, rapid replacement, renderer/lease teardown and dark/light/scaled UI.
Implement this alongside TRANSFER-16 in S17's one combined next release; no
second stage is ACTIVE and S15 remains delivered.

New authorized next-update scope2026-11-05, TRANSFER-16, assigned to S17:
prioritize existing destination files newer than their incoming replacements at
the top of the conflict table, using readable orange and a textual/accessibility
cue. By default show files whose size or modification timestamp differs, and
render only the differing properties in their Incoming/Existing cells. Hide
matching property values; offer Show matching files with accurate shown/total
counts and explicit action scope. Matching size/date is not a claim of identical
contents: this review uses existing metadata, without additional content reads,
hashing or cloud hydration. Keep the complete conflict set and existing four
resolution choices; filtering must never silently skip files. Display enough
size/time precision to distinguish unequal values, including subsecond changes.
Use the existing compact table/virtualization and application design; qualify
mixed/all-matching conflicts, newer-destination ordering/cues, show-all, actual
resolution outcomes, keyboard and dark/light100/150% rendering. S17 owns the
normal next release, independent retrieval and installed verification. S15 is
already delivered; S17 alone is ACTIVE, with later stages unchanged.

Execution reconciliation2026-11-05: S11/S14/S15 COMPLETE through normal1.1.23
at fc01ce1. Actual final57312, independently downloaded51296 and installed
readback58076 satisfy the assigned behavior, native boundaries and delivery
criteria, including the transfer addition below. No stage is ACTIVE; no required
blocker or tracked deferral remains in this authorized round. Dated execution
checkpoints below retain the original requirements/history, not current status.

This is the authoritative specification. The derived plan must preserve every
requirement and acceptance criterion. Material scope changes require explicit
user direction recorded here; ordinary implementation choices need no new gate.

2026-11-05 resumed S15 correction: the user reports that1.1.22 still leaves an
empty Pending tree. SPACE-01..03 require startup to proceed after cancelled
ordinary sizing, including when its optional index dispatcher is occupied.
Present initial loading activity while startup is genuinely waiting; respect
reduced motion and retain quiet completed values during background refresh.
CORE-11 is amended in S15: healthy disk-capacity bars stay blue independently
of the theme accent; retain amber from80% and red from90% used on every disk
capacity surface. This does not change ordinary accent-based progress controls.
The user's pause is superseded by their subsequent "continue". S15 remains
ACTIVE; the reported Pending bug, final correction delivery and installed
readback remain acceptance blockers. No later stage is activated.

2026-11-05 current S15 regression: the installed1.1.21 analysis button can leave
an empty tree after overlapping delayed entry/exit/re-entry. SPACE-01..03 and
LIFE-01 require the entire admission/startup operation to remain owned until
completion/cancellation, immediate mode presentation and preservation of the
current tree when old operations finish. Qualify repeated real button events,
navigation during held admission, exact restore and corrected final delivery.
This clarifies existing lifecycle acceptance; no later stage is activated.

User-authorized S15 addition2026-11-05, TRANSFER-15: redesign the Transfers
popup using Huashu within the existing WPF auxiliary design. Keep its compact
640-DIP default width and content-sized height; use the existing palette, Segoe
type,12/13-DIP body and18-DIP aggregate heading,12/16-DIP spacing and32-DIP
actions. Conflict content is an actual virtualized comparison table (File,
Incoming, Existing; paired size/date values), with a bounded240-DIP scrollable
viewport, readable full-path tooltips and keyboard navigation. Populate/layout
new content without an opacity-zero entrance; automatic growth must be driven
by actual content, not a speculative blank area. Preserve user resizing,
multiple jobs and100/150% dark/light layouts. Display aggregate speed once for
one job; show per-job legend rates only when several jobs run. Do not confuse
combined copy/verification throughput with copied-payload throughput.
Explicitly cancelling a transfer dismisses it only after actual worker exit,
unless errors require review; preserve other active/adverse jobs and existing
completion/Undo/arrival semantics. This explicitly supersedes TRANSFER-14's
requirement to retain an otherwise clean cancelled card. Cancelling a paste
must preserve its copied/cut clipboard formats and allow another Paste, without
overwriting newer clipboard contents. Reproduce real copy/conflict/cancel/paste
and compare native formats/sequence on an isolated clipboard. Show actual
file-specific failures first; harmless metadata notes belong in Details, not
an error summary that hides the failure. This addition stays in ACTIVE S15 and
the combined final release; S14 is not reopened and no second stage is ACTIVE.

Current authorization2026-11-05: "continue working then" authorizes implementing
the recorded native-category correction, S14 window/update feedback and the
selected current-pane Diskweft integration. Execute S11 -> S14 -> S15 with one
ACTIVE implementation stage. S11/S14 close their complete source/integration
acceptance before advancement; S15 owns one combined normal release and
independent source/package/installed verification for this round. No per-stage
release. Earlier plan-only wording below is historical for these now-authorized
items. S12/S6/S7 remain outside this request. The user confirms an actual native
folder pin survives restarting1.1.18; preserve existing pins during correction.
The user additionally requests automatic dismissal of successful transfers;
TRANSFER-14 belongs to the currently ACTIVE S14. Keep unsuccessful outcomes
available for review and preserve operation completion, Undo and arrival selection.

The user's subsequent "Follow your best recommendation choice" authorizes
Everything-first folder sizing in S15: reuse available indexed folder totals,
retain metadata-only traversal for uncovered scopes, and replace the unbounded
eager text-cache load with a bounded hot cache and disk-backed fallback records.
Do not change the external Everything index/configuration or require an engine
upgrade. Preserve indexed-versus-scanned coverage and last measured UI values.
On2026-11-05 the user explicitly authorized enabling folder-size indexing in
their existing Everything instance. This is a scoped machine-setting exception:
preserve its other settings, service, index scope and version, restart normally
in the background and verify actual indexed sizing. Product fallback remains
required when another user's index is disabled or a path is uncovered.

The user's1.1.19 feedback extends S15's refresh acceptance: the analysis command
must track actual active-folder readiness after asynchronous navigation. Known
size refreshes must remain quiet (no flashing measuring labels or partial-total
suffixes); unknown initial totals still show honest progress. Background listing
or sizing refresh must preserve an in-progress address edit, text, caret/selection
and focus. Explicit navigation, submit and cancel retain their normal behavior.

While1.1.20 publication was already in flight, the user added these S15
acceptance requirements for the following integrated correction: entering
analysis selects/focuses the first available top row once, without stealing
focus on later updates; expansion arrows render unclipped at existing compact
proportions; ordinary background writes refresh affected analysis branches
without repeatedly cancelling/restarting the whole root or resetting completed
unrelated rows to Pending/Measuring. Preserve truthful initial progress, explicit
full refresh, actual invalidation and exact grow/shrink/rename/delete totals.
Audit all bottom-bar toggles: hidden items have an appropriate crossed/open eye,
pane count depicts the current layout, and analysis/preview/hidden/two-pane
states have clear persistent active styling, action tooltips and accessible
state. One final correction release owns these additions and installed readback;
do not request an intermediate1.1.20 installation merely to advance this work.

The user's2026-11-05 Craft maintenance handoff requests consideration of newer
Office cores and additional installed Craft products. The assessment is recorded
in [CRAFT-CORE-REVIEW.md](CRAFT-CORE-REVIEW.md), with proposed S16 NOT STARTED.
It preserves source-built minimal DLLs/owned workers, not external application or
MCP dependencies. This assessment does not expand the current S15 release or
authorize activating a second implementation stage, replacing PDF rendering
without comparison, or changing installed Craft/Coordinator selections.

On2026-11-04 the user authorized implementation of S13, S9 and S11 together.
Execute them in that order with at most one ACTIVE stage. Current execution
checkpoint: S13 and S9's assigned source/integration acceptance is COMPLETE;
S11 is ACTIVE. Advance only after each predecessor's complete
implementation/integration acceptance passes. Deliver one normal release for the complete combined outcome at the end
of S11, with independent package/source retrieval and actual installed-update
verification for all three stages. This shared final delivery assignment
supersedes the earlier per-stage publication sequence without removing any
verification criterion. S13/S9 close against their complete source/integration
criteria; their release verification is explicitly owned by S11, and overall
completion requires it. S12 and S6/S7 remain outside this implementation request.
Preserve the running product, existing user state and completed S8 evidence.

2026-11-05 delivery readback: normal1.1.18 at8191b55972700af87f963f6266c6373f61d64dc2
is published and independently retrieved/qualified. The actual installed update
matches all10672 manifest members; restarted main/helper, readiness,22 folder
route values and the shortcut AppID pass. The user's screenshot confirms native
StartAllBack folder-menu presentation and reports it working. This does not
independently demonstrate every pin/unpin/remove/restart action in JUMP-02.
Their subsequent correction requires Explorer's native Pinned/Frequent category
structure rather than the custom Quick access/Recent folders and Home/Downloads
Tasks in1.1.18. S11 remains ACTIVE for that correction and its qualification;
do not label the requested revision delivered based on the existing release.

The user reiterates S14's outstanding popup unification and manual update-check
confirmation, ahead of unrelated StartAllBack work. They request an evaluation
of borrowing Diskweft's engine, clarifying that it must use Pathweft's own style
and the currently open path, and then selecting a reversible mode in the current
pane: a compact button beside Terminal, expandable folder tree, Space used
column and descending size order; clicking again restores the standard view.
No separate Diskweft UI/window is wanted. See [the assessment](DISKWEFT-INTEGRATION.md).
This evaluation does not activate another implementation stage or authorize a
standalone-app/MCP dependency. S14 remains NOT STARTED; S6/S7 stay postponed.

The user adds planned S14 (NOT STARTED) for consistent auxiliary-window design
and immediate manual update-check feedback, after the currently authorized
combined S13/S9/S11 delivery. Priority becomes
S13 -> S9 -> S11 -> S14 -> S12 -> S6 -> S7. This is a plan addition, not authority
to activate S14 or expand the current combined release. S9's menu work also
includes search-widget result actions: open containing folder in Pathweft while
preserving result selection, and native Properties for the actual selected
file/folder/shortcut, with equivalent keyboard access and truthful eligibility.
S9 clarification from native diagnosis129196: Windows hosts its Properties
sheets through a same-process StubWindow32, rather than directly owning them
with the invoking search widget. Direct GW_OWNER equality was an over-specified
diagnostic condition, not part of the requested QoL behavior. Preserve the actual
native sheet and selected object, including shortcuts. Like opening a result,
successful Properties dispatch dismisses search; the native sheet must survive
that popup hide and close normally. A failed native dispatch keeps search visible
and reports the row error. Pass the main application window to the existing Shell
API; do not reparent or redesign native property sheets. Complete actual native
target/visibility/lifetime and Delete verification before S9 closure and S11.

User additions2026-11-04 to the current S13 interface work: remove the version
from the sidebar header and move Update, What's new, Help and Settings beside
the app name. Keep Drop bin at the bottom. Use compact icon buttons and retain
tooltips, keyboard access, the draggable brand area and existing actions. At a
narrow sidebar, put those four controls immediately below the brand instead of
clipping them; preserve usability at100%/150% text/sidebar scale. About keeps
Pathweft's version and Darkaxt identity, its own project link and existing help,
settings-folder and update actions. Remove upstream-author branding, feedback
and promotional links from About; preserve all distributed source/license notices
and actual optional integrations. Child settings toggles retain their indentation
and use the standard6-DIP vertical separation, including taskbar embedding,
autoscroll direction, compact summary cards and startup options.

User additions2026-11-04: S13 Office recovery feedback must use a concise orange
bottom-right status, such as `Error loading: {filename}`, with full diagnostics
on hover instead of a wrapped technical error paragraph beneath the document.
Retain the actual renderer icon to show successful LibreOffice recovery. S9 also
owns MENU-03: simplify the bottom command area with familiar Explorer-style icons
and tooltips containing command names and shortcuts, preserving accessible names,
keyboard actions, eligibility and the existing Native/Original theme behavior.
Recover the native toolbar and View-menu artwork from the supplied references;
prefer exact scalable system assets over screenshot crops or approximate symbols.
On2026-11-04 the user then explicitly directed implementing the recovered icons
now, preserving Pathweft's proportions rather than Explorer's larger spacing.
Assign that bounded MENU-03 presentation integration to active S13; MENU-01/02
remain in S9 after S13 acceptance, and final release stays in S11. This addition
does not mark the broader context-menu stage active or complete. Keep the bottom
bar at its compact scale:16-DIP command artwork,30x24-DIP buttons,14-DIP pane
view/sort artwork in28x22-DIP buttons, with no new toolbar row. Keep all existing
actions and shortcuts; show names/shortcuts/current view or sort on hover. Preserve
keyboard focus visibility, disabled/toggled states and narrow/100%/150% layouts.
The user's subsequent grouping correction requires coherent compact groups:
New folder/New file; Open/Rename/Delete; Copy/Move/Transfers; Terminal;
Hidden/Panes/Preview. Separate groups with restrained1-DIP dividers and4-DIP
side margins; preserve command semantics and match keyboard order to visual order.
At narrow widths reduce button width only, down to22 DIP, to keep the16-DIP art
and every command on one row at150% text scale; do not enlarge the bar or hide actions.
The terminal command uses the user-selected terminal-window `>_` glyph (Segoe
Fluent Icons U+E756), replacing the folder/play artwork of Explorer's PowerShell
asset. Keep its existing launch action and Ctrl+` tooltip.
Use installed native SVGs without redistributing Windows assets; preserve glyph
compatibility when that Windows resource set is absent.

On2026-11-04 the user added and clarified PREVIEW-37 in active S13: provide two
distinct preview modes through accessible icon controls next to the sidebar's
close button. Windowed moves the preview into its own resizable floating window;
closing that window MUST restore the same preview to the side column. PiP moves
the preview into a compact corner overlay inside Pathweft, freeing the whole
sidebar column; it is not another external floating window. Provide a return-to-
sidebar control in PiP. Preserve the viewed item, page/sheet/slide, media position/
play state and 3D view/animation state; changing hosts must not restart or duplicate
the renderer. Detached modes stay on their item while the browser navigates
elsewhere. Retain the original close-preview action separately from window-close
restoration. Reuse Pathweft's theme, selection/lifetime and floating-window
infrastructure. Implement after Office integration, within the same ACTIVE stage
and combined release; verify actual relevant native surfaces and app-exit cleanup.

The user also clarified that workload gates coordinate agent threads and resource
use. Borrowed source libraries and the shipped Pathweft worker do not require a
Craft application gate identity, Coordinator or MCP. Keep the prescribed managed
routes for actual registered development-tool workloads without turning them into
product dependencies or inventing a library-admission blocker.

Office architecture decision2026-11-04: resume implementation using the pinned
Craft source cores as they currently behave, with minimal native-library bridges.
Preserve their rendering semantics and current limitations; this does not require
reimplementing Microsoft Office or adding missing formats to Craft. Keep our bridge
separate from upstream so reviewed fixes can be incorporated later. No Craft GUI,
CLI application, MCP service or separately installed Office product is required by
the delivered viewer. A small Pathweft-owned preview worker hosts the bundled
libraries; the user explicitly selected this arrangement for native-crash recovery.

LibreOffice is the required bundled fallback for declared Office formats that Craft
does not import, including legacy PPT/ODP and Visio, and for failed Craft imports,
renders or abnormal worker exits. A fallback must actually render inside Pathweft
with the same page/sheet/slide navigation. Use one fallback attempt per failed
request; observe original worker exit and ownership before switching engines.
Cancellation, disabled modules and source/policy limits must not start fallback
work. Preserve source bytes/MOTW and never execute macros or update external links.
If both engines fail, report an explicit preview error with truthful engine context.
Public interest in future Craft Visio/Project/OneNote applications is a monitoring
signal, not evidence of an existing importer. Resolve their actual LibreOffice
coverage before claiming or extending supported formats; do not substitute a
metadata-only view for promised document rendering.

On2026-11-04 the user explicitly chose full Office page/sheet/slide navigation
with an embedded renderer, accepting an additional runtime of several hundred
MB rather than limiting support to installed Windows Office handlers. The user
subsequently directed qualification of Storytold's WordCraft, GridCraft and
DeckCraft renderers and installed their portable candidates. Assess actual
release-pinned rendering, format coverage and renderer-only footprint before
selection; alpha labels or an assumed coverage percentage are not evidence.
LibreOffice remains a candidate where required formats need it. Do not deploy
MCP/gate features: the user owns enabling those identities. Render
the result inside Pathweft with real navigation. Do not open an associated Office
UI or install/change a system Office application. Use an owned isolated profile,
explicit read-only loading, never-execute macro and never-update link policies,
deterministic process/cancellation/cleanup, original byte/Zone.Identifier
preservation and complete native/source/license provenance. Missing/protected
inputs remain explicit. The earlier native-handler-only matrix is superseded
for Office, not a waiver of PREVIEW-22/36. Stage order and one final release stay
unchanged; S13 remains ACTIVE.

On2026-11-04 the user added live folder-size correction to this round after
installing nested Craft release folders left their ancestor rows at zero until
F5. CORE-18 belongs to current S13 and its shared S11 delivery gate: observe
nested creates/writes/deletes/renames, invalidate affected cached ancestors,
and remeasure/update visible rows, selected totals and folder summary together
without manual refresh. Preserve selection/scroll, unchanged sibling cache,
navigation/cancellation ownership, and prevent an obsolete sizing result from
overwriting newer state. Handle watcher overflow explicitly. Unknown or ongoing
measurements must not claim an exact empty-folder result. Verify real external
nested mutations, growth/shrink, both panes/return navigation, and source/state
preservation; do not add timer polling or periodic full-drive rescans.

On 2026-11-02 the user requested a plan stage implementing all fourteen missing
QuickLook viewer categories identified in the preview comparison, and requested
Pathweft improvements before StartAllBack. S13 owns PREVIEW-21..36 below and is
NOT STARTED. Preserve stage IDs and completed evidence; the remaining execution
order is S13 -> S9 -> S11 -> S12 -> S6 -> S7. S6 and its dependent S7 form the
final Windows-search integration block: S7 cannot reduce indexing scope before
S6's replacement workflows pass. No existing SAB/EVERYWHERE acceptance is waived.
This instruction updates specification/plan only; it does not activate a stage,
authorize implementation, restart Sandbox or change the running system. S8 stays
COMPLETE through installed1.1.17. Future implementation delivery belongs to the
stage owning that outcome, not a retrospective reopening of completed S8.

S8 is COMPLETE through installed1.1.17 for PREVIEW-20. The authorized Marked/
FriBidi implementation, source/native workflows, normal publication and
independently downloaded package/source verification pass at5019884. The user
updated/reopened; all879 installed members, fresh main/helper identities,
helper readiness and22 desktop route values pass. Task scratch is removed
without residuals. No stage is ACTIVE and no blocker or tracked deferral remains.
Other review candidates and S12 remain unstarted.

PREVIEW-20: replace the editor's Marked18.0.14 bundle with a source/hash-pinned
patched stable release containing the Unicode-link cubic-backtracking fix.
Replace FriBidi1.0.16 in the embedded media runtime with1.0.18 using a source-bound
native build; retain existing mpv/FFmpeg/libplacebo revisions, WASAPI patch,
Dolby Vision-to-SDR contract and native-window presentation. Preserve the running
product, donor worktree and retained build inputs. Use the existing gated native
toolchain and isolated task output. Provide exact notices, source/build recipes
and packaged runtime identity. Verify ordinary and malformed/Unicode Markdown in
the actual editor, RTL/isolate-heavy subtitles in actual native playback,
SDR/DV P5/P8 color/seek/teardown, and all affected integration boundaries. Publish
one normal1.1.17 release after both integrated workflows pass; independently
retrieve it and verify the user-controlled installed update. All acceptance and
delivery work remains an internal advancement blocker until verified; no tracked
deferral or new integration stage is introduced.

Previous S8 was COMPLETE through installed1.1.16 for PREVIEW-19. Normal publication at
35f01f6, independently downloaded native/app workflows, matching sources and
all878 installed members pass. Main/helper restart/readiness and22 actual
desktop route values pass; the user confirms the reported HTML now renders.
Scratch cleanup is applied without residuals. No stage is ACTIVE and no current
S8 blocker or tracked deferral remains. S12 remains NOT STARTED and plan-only.

PREVIEW-19: correct static HTML preview on legitimate mapped-drive/UNC aliases.
Opened file identity must match the canonical drive/share
root plus the checked relative path; do not authorize a substituted document
directory or relax reparse, traversal, script or network restrictions. Verify
the reported provider-backed file read-only, extended mapped-path access, normal local
and archived assets, true escape rejection and cleanup. Complete normal1.1.16
publication and independent package verification; preserve the user's update
control and verify the actual installed update.
Tresorit's normalized UNC handle name is synthetic: direct reopening of that
name returns ERROR_INVALID_NAME. Qualify its canonical identity through the
working mapped and extended paths; do not claim direct provider UNC access.

Previous S8 was COMPLETE for the authorized GLB/glTF smart timeline and isolated HTML
viewer. Both features, normal1.1.15 publication and independent delivered-package
verification pass at85de43e. The user updated/reopened; all878 installed members,
main/helper restart/readiness and22 actual desktop route values pass. Spent
scratch cleanup is applied without residuals. No stage is ACTIVE and no current
S8 blocker or tracked deferral remains.

PREVIEW-17: default GLB/glTF timelines omit only a trailing held pose using actual
animation samples and scale-aware movement tolerance. Ignore exporter drift without
discarding intentional small/slow motion or internal pauses. Preserve native clip
identity, starts, playback/seek/drag/keyboard semantics, switching and cleanup.
Offer the original full range through the existing themed timeline's context menu;
do not add another toolbar row. Static, uninspectable/unsupported or genuinely
changing clips retain the native full range. Never rewrite the source model.
Verify the reported SNES model (visible motion to6.875s; native end55.416668s),
late/slow motion, small animated parts, pauses, multiple clips, malformed metadata,
and actual native rendered/playing/seeking/teardown results.

PREVIEW-18: add an HTML module above generic text for .html/.htm, available in the
existing Settings > Files preview-module section. Render static HTML/CSS and
allow-listed local images/fonts, including relative assets inside a compressed
folder. Isolate the WebView2 environment from the editor; do not expose messages,
host objects, credentials or unrestricted disk access. JavaScript, network access,
frames, permissions, form submission, downloads, popups and automatic navigation
are disabled/blocked. Serve only checked document-scope resources; reject escapes,
links/reparse paths and external/custom schemes. Sanitize HTML/CSS before browser
parsing to prevent speculative connections. No blanket directory mapping or
whole-archive extraction. Preserve document presentation inside Pathweft's themed
preview frame; script-dependent controls remain inactive. HTML document/resource/
total-resource budgets are16/64/128MiB, with explicit failure at the boundary. Disabled HTML returns
to source preview; missing runtime/load failures are explicit. Observe completion,
selection/module cancellation, archive source/resource lifetimes and browser teardown with
no arbitrary delay. Verify actual WebView2 rendering, local/archive assets and
negative boundary fixtures, source preservation, dark/light layouts and all preview
surfaces, then the actual delivered package and installed updater result.

Design direction: preserve existing Pathweft controls/spacing, using the installed
Huashu production-UI guidance. One S8 stage owns both fully integrated features,
focused source/native/UI acceptance, final reconciliation/commit, one normal release,
independent retrieval and installed verification. No postponed-stage work.

Historical1.1.14 acceptance follows. The previous S8 extension is COMPLETE through1.1.13: all871 installed members,
reopened main/helper readiness and22 desktop route values pass after the user's
update. The following corrections are published and independently qualified in
normal1.1.14 at1d20122f52768c2760dd1f552d37e19f07ca67b0. S8 is COMPLETE:
after the user's update/reopen, fresh2026-11-02 verification passes all871
installed member hashes, main/helper ownership and readiness, and22 actual
desktop route values. No remaining S8 blocker, ACTIVE stage or tracked deferral.

UPDATE-01: the user requests a themed popup reporting deployment progress during
updates. Retain the existing update action and verified per-user installer.
Show actual downloading/checking progress before exit, then an installer-owned
compact progress popup that remains while Pathweft closes and files are replaced.
Report actual deployment, integration/helper restoration and reopening phases;
percentages refer to measured work, never elapsed-time simulation. Preserve
light/dark, accent, readable contrast and DPI scaling using Pathweft's existing
palette, icon and typography. Visual contract: approximately520x240 DIP,20-DIP
insets, concise heading/status, accent progress track and no debug/path dump.
The deployment popup follows the existing updater UI: the same compact heading,
subtitle, low progress track and bottom status row, including its current palette.
Handle failed preparation/installation visibly without reporting completion.
Keep download cancellation, checksum/size checks, active-work blockers, exact
requesting-process wait and existing opt-in/recovery behavior. Do not introduce
another updater service, arbitrary delays, forced exits or a new administrator
requirement. Existing1.1.13 clients must receive deployment progress when they
install this release, despite using the old very-silent launch switch. Verify
real installer progress/phase/error callbacks and hidden dark/light100/150%
layouts without altering the installed identity or taking desktop focus.

PREVIEW-16: clicking the video/audio or3D animation timeline seeks directly to
the clicked position while paused AND while playing. Playback position updates
must not overwrite user seek intent before it is submitted. Dragging and keyboard
seeking remain usable, and a seek while playing continues from the new position
without restarting or pausing playback. Verify actual WPF timeline actions and
native media/animation state with playback events and renderer completion.
Release both corrections together, independently retrieve/check the delivered
package and verify the installed updater result. No postponed-stage work.

Historical1.1.13 requirements follow. The user restarted Helium from Start and confirmed
folder opening works; native tracing verifies class lookup and handler activation
now succeed. The user explicitly requests preserving file highlighting too.
Complete that native selection handoff without taking over Explorer navigation,
preserve opt-out/virtual-folder behavior and verify the real Helium request before
the final release of this extension. The prior process-context lookup failure is
resolved in the restarted caller; its lower-level cause is not inferred.
The user's current sidebar report also requires consistent drive display names:
Folders uses the same fallback label as Drives for an unlabeled volume. Do not
rename the actual filesystem volume. Media-engine alternatives from the user's
VLC/NuvioDesktopDV forks were initially a feasibility review; the user's subsequent
"go ahead, libmpv" explicitly authorizes PREVIEW-15 in this same S8 extension.

PREVIEW-15 (S8, already authorized): replace Video and audio's WPF/installed-codec
backend with a bundled, version/source-pinned libmpv runtime using the user's
VLC/NuvioDesktopDV Dolby Vision work. Keep the existing module enable/disable
setting, Pathweft theme and preview ownership; no standalone installed player,
Kotlin/Qt UI import, extra engine chooser or codec downloads. Render into a
native child window through gpu-next/libplacebo, without PNG/CPU frame roundtrips
or loss of Dolby Vision metadata. Reuse the proven DV pipeline's exact source,
runtime identity, relevant configuration, notices and corresponding sources;
do not assume that merely using libVLC carries the fork's MPV/DV implementation.
Maintain video/audio play/pause, seek, duration/position, Space-window autoplay,
side-pane pause until Play, keyboard controls, errors and deterministic teardown
when changing selection, disabling/closing preview or shutting down. Retain themed
icon controls and accessible labels. Verify real SDR/audio and representative
DV Profile5/8 rendering against the available known-good fork fixtures/reference,
negative color control, actual native playback/events/EOF/teardown and hidden
pane/popup integration. Qualify the known-good SDR/sRGB conversion; native Dolby
Vision display signaling and full Profile7 FEL are not claimed. Include native
runtime/provenance/source in the normal final release and independently verify
the actual downloaded package and installed integration. No intermediate release
or unrelated stage is authorized by this extension.

SHELL-02 clarification (user: "I clicked Show in Folder", followed by permission
to test while away): the actual failing Helium action is Show in folder. Its
observed boundary is SHOpenFolderAndSelectItems, not the separate Open downloads
folder command assumed below. The real Helium action remains a required S8
integration criterion; shipping a Folder/explore mapping or editable JSON alone
does not satisfy it. Preserve the requested file selection where the native
request supplies it. This clarification supersedes the earlier exclusion of
that action from this correction; it does not declare that API implemented.
Live correction 2026-11-10: an earlier failed COM trial left this user's
Folder/open route disabled in the saved opt-in configuration even after the
native selection handler shipped. The installed executable matched the 1.1.49
release and the handler registration pointed to its 1.1.49 native DLL. Applying
the existing Folder/open route through Pathweft's owned registration restored
the real Helium Show in folder action. The user
confirmed that Pathweft opened and highlighted the requested PDF. Preserve the
route's opt-in ownership and exact recovery record; this configuration repair
does not change the default route selection for other users.
Retain the separate Open downloads folder criterion and prior Explorer/virtual
folder protections. The user permits controlled tests while away; preserve
unrelated running applications, browser state and other workflow ownership.
The released1.1.12 dynamic editor remains verified separately. Do not ship a
speculative follow-up or claim the real browser boundary passes.
Publication occurred before the real Helium acceptance criterion passed; that
delivery gap remains explicit. The release is not evidence of SHELL-02 completion.

SHELL-03 (S8 ACTIVE, user-authorized extension before1.1.12 publication): store
folder-class verb routes in folder-routes.json inside the selected settings
directory. Settings > Windows integration includes a themed row editor with
per-route enable checkboxes, class/verb fields, Add/remove and Apply controls;
support editing/reloading the JSON without recompiling. Classes are the existing
filesystem-folder classes Directory, Drive and Folder; verbs are editable Windows
verb identifiers. Seed the existing routes, including Folder/explore. Master
folder integration remains opt-in. Applying disabled/removed routes restores
their prior values; recovery stores the applied route snapshot so later JSON
edits cannot prevent disable/uninstall. Preserve later-manager changes. Validate
duplicates/invalid identifiers and malformed JSON before native changes; keep
existing configuration and binding when applying fails. Verify actual editor
actions, persistence/reload, active/off behavior, old registration migration and
restoration after removing a route, using isolated registry and hidden100/150%
dark/light layouts. Reuse the current settings design; no additional approval
gate or separate release. The unpublished b3f583c package is superseded; publish
one final1.1.12 only after SHELL-02/03 and their integrated gates pass.

SHELL-02 (S8 ACTIVE, already authorized): cover the explicit Folder-class explore
request used by Chromium/Helium's Open downloads folder action through the
existing opt-in native handler. Preserve navigation within Explorer, native
virtual folders, prior handler values and later third-party changes. Upgrade an
intact existing opt-in registration without enabling an opted-out installation.
Verify the exact Folder/explore registration in the shipping assembly,
registration upgrade/disable restoration and hidden32/64-bit native COM/browser
navigation. Live ShellExecute caller checks escaped their process-local fixture
and opened Explorer despite SW_HIDE; the user is closing those popups. Those
results are failures, not acceptance. Stop that host test and use a user-controlled
Helium Open downloads folder click for the final actual browser boundary.
Deliver one normal1.1.12 release, independently retrieve/verify it and verify the
installed registration. No address-bar change: the user confirmed the apparent
blank-folder problem was an unsubmitted address. Direct reveal-file API coverage
and other postponed stages remain outside this correction.

CORE-17 (S8, user-authorized storage correction): initially keep small settings on C:;
place installed bulk archive working copies, folder-size caches, editor WebView2
data, child-process runtime scratch and preview caches on D: under a per-user Pathweft data directory. A small
data-root.txt setting may select another non-system data drive; never silently
fall back to C:. The user's 2026-11-12 decision, after a clean-install crash on a
PC without a D: drive. With no data-root.txt, the first start uses an existing
D:\AppData\Pathweft\<SID>, else the first ready fixed non-system drive, else
%LOCALAPPDATA%\Pathweft only when the Windows drive is the sole local drive, and
records that choice in data-root.txt so a later drive change never moves it. A
recorded drive that is missing is reported in a message, never replaced or crashed
on. Preserve isolated test/explicit portable profiles. Preserve
existing folder-size cache data and legacy working-copy path bindings until
their consumers finish. The user identifies the historical VLC extraction as
abandoned failed-drag output: release that exact copy with reviewed cleanup.
Known failed/cancelled drags automatically release materialized copies;
accepted external handoffs retain their actual consumer lifetime. Verify real
archive extraction/drag outcomes, cache writes/legacy reads, editor profile paths,
legacy-copy release and installed storage migration/cleanup. Migration must wait
for old writers to exit, verify destination content and preserve settings.
The user extends CORE-17 with Settings > Storage > Change location: select a
data folder, move existing settings/runtime data after graceful app/helper exit,
verify copied content before switching the startup pointer, remove only the
verified old members, and reopen with the same settings/integrations. Only a
small location pointer remains in Roaming. Display current location and a concise
success/failure message. Reject nonempty/overlapping/system-drive destinations;
block while transfers, editor tabs or retained external working copies are in
use. Preserve source data and the current binding on pre-commit failure. Verify
the real relocation worker, UI action and themed layouts before final release.

CORE-16 includes the user's requested dynamic Message grid column for file-open
failures, with full-detail tooltip and clearing on successful retry. Do not bury
these errors in the bottom status path. Verify actual row/column rendering.
Root-cause evidence identifies installer-inherited RedirectionGuard enforcement:
the running main rejects the registered Scoop player junction with error448;
Explorer uses audit mode. Inno6.7.4 documents this inheritance for lowest-privilege
installers. Correct desktop application/helper launch ownership while retaining
the installer's protection; do not disable Windows or installer mitigations,
rewrite user associations, or hardcode resolved player executable paths.

## 1. Intent and current scope

Current correction: S8 COMPLETE for CORE-16/17, delivered as normal1.1.11 at
76694a9. Source, local/retrieved package workflows, actual installed identity,
desktop-context restart, complete profile relocation and exact cleanup pass.
No current S8 blocker or tracked deferral. The diagnostic history follows.

The correction began with CORE-16 after the user's live reproduction
under the prepared, temporary read-only launch trace. MP4 dispatch reaches
ShellExecuteExW and fails with HRESULT0x800401F5; TS/PDF/XLSX return success,
with unexpected association-choice dialogs reported for TS/XLSX. The temporary
trace is detached. Synthetic double-click,
custom context Open and actual Windows association receivers pass without a
production behavior change; they do not establish the cause of the live failure.
The user reports ordinary MP4
double-click and custom context Open doing nothing, while Explorer and native
Open with work. Trace the shared action to its root before changing behavior.
Correct and verify the general open contract across ordinary associations,
double-click/Enter/context Open, editor enabled/disabled, directories and archive
working copies. Preserve explicit edit/preview semantics. No MP4-only routing,
host media playback, desktop takeover or unrelated shell registration changes.
Use synthetic receivers for actual association-boundary checks. Source acceptance
and one final normal release/retrieval/installed verification are required;
previous 1.1.10 evidence below remains historical.
The live trace proved the inherited mitigation root cause and is detached;
the desktop launch correction and general action/message checks pass in source.
Storage correction, integrated verification and final1.1.11 delivery are verified,
including the real desktop-profile worker and ready integration helper. The
installed app retains desktop audit mode; the installer retains its protection.

Current correction: S8 COMPLETE through1.1.10 for PREVIEW-13/14. The installed1.1.9
orbit behavior passed scripted correctness but regressed rendering performance. PREVIEW-14
requires processing every ordered camera sample without drawing for each sample,
retaining one explicit frame capture per render batch and completed-frame
presentation. Port the selected pinned F3D controller math if its input API
couples camera updates to immediate rendering; this implementation choice
preserves the user's native orbit behavior rather than requiring that API.
Compare camera state with the pinned native controller for circular/reverse/pole
and pan paths; measure whole batch input work, capture and transfer for realistic
bursts on the real model. Require zero implicit input renders and no sample/path
loss, quality reduction, arbitrary timer, throttle or input-delay workaround.
For native numeric comparison, position/focal and ordinary up components use
1e-7 absolute tolerance. At the native1e-10-degree pole clamp only, up components
allow1e-4 (<.006 degrees): the projected basis is ill-conditioned there, while
position/focal stay strict. Pole limits/upright behavior remain separately tested.
Deliver and independently verify one final1.1.10 release and installed update.

Reconciliation: camera/native parity, ordered burst/zero implicit draws, full
gesture/preview/frame-pressure acceptance pass. Normalv1.1.10 sourceaf82ca29eb46261ea62e2cf193f664333923d844
and all six assets match; local/retrieved real native/app workflows pass. All792
installed files and reopened mainPID52824 match; the user reports performance
fixed. Current external view has no helper owner/startup/stored wine opt-in, so
no fresh Win+E readiness is claimed and no setting is changed. Exact task cleanup
passes. No ACTIVE stage or S8 blocker/deferral. S9/S11 remain NOT STARTED and
S6/S7 postponed. See the1.1.10 verification record.

PREVIEW-13 was authorized by the user's circular
mouse-gesture regression report. Use the pinned F3D default input controller's
fixed model-up orbit/pole behavior instead of camera-local trackball math.
Circular drags must expose sides/underside without accumulating unwanted roll.
Preserve current right/middle/Shift pan mapping, wheel zoom, camera buttons,
frame presentation, DPI/resize, animation and native worker/cancellation ownership.
Verify an ordered circular/reverse circular gesture, vertical pole limits,
camera-up/focal/distance, pan/zoom/reset, actual large-model previews and teardown
through hidden app actions; compare with pinned native controller source/API.
The1.1.9 normal release, independent retrieval and installed updater hashes/main/
helper readiness passed; the subsequent performance regression reopened S8 and
is resolved in1.1.10. No unrelated S9/S11/S6/S7 work.

Historical extension: S8 is COMPLETE through1.1.8 for the previous refinement round.
PREVIEW-12: correct continuous mouse rotation/frame presentation, remove PNG
encode/decode from interactive rendering, size native frames for the viewport
and DPI, preserve camera/animation/cancellation/resource ownership, and provide
F3D-quality anti-aliasing/lighting/grid presentation consistent with the theme.
Inspect the pinned native API/source; verify sustained mouse gestures produce
intermediate presented frames (not only a final jump), camera motion and native
pixel orientation/alpha, actual large/animated models, resize/DPI and teardown.
Measure relevant render/presentation stages; do not invent a frame-rate guarantee.
CORE-14: replace the Settings paper roll with themed vertical category tabs on
the left, widening the default window to preserve the existing content area.
Display only the selected category; retain every setting, handler/reset/persistence
and keyboard/accessibility behavior. Verify navigation/settings and unclipped
dark/light100/150% layouts, including scrolling within a tall category.
CORE-15: Help's left category becomes bold automatically for the section being
read, including scrolling, category clicks, search filtering and resize. Use
actual layout/scroll events; no polling or feedback loops that move the reader.
JUMP-03: investigate the native pinned/recent folder expansion shown in the
user's StartAllBack screenshot and its taskbar Jump List equivalent. Document
how Pathweft can expose real folder destinations, pin/recent/removal/privacy,
application identity and cold/warm launch under S11/JUMP-01..02. This request
authorizes bounded investigation and a concrete implementation plan, not a
StartAllBack patch, desktop takeover or premature S11 implementation. Deliver
PREVIEW-12/CORE-14/15 together in one verified normal release, retaining current
opt-ins/user settings and the installed updater workflow. No S6/S7/S9 work.

Current1.1.8 reconciliation: PREVIEW-12, CORE-14/15 and JUMP-03 research pass.
Normal release/tagv1.1.8 source64118e927b71d528fdc6626ae3315e4faeafcd12
has six verified assets. Actual local and independently retrieved native/app
checks pass, as do792 installed hashes and reopened main/helper readiness after
the user's updater. Exact task scratch cleanup passes. No ACTIVE stage or S8
blocker/tracked deferral remains. S11 runtime implementation remains NOT STARTED;
S6/S7 are postponed and S9 is NOT STARTED. See the1.1.8 verification record.

Historical1.1.7 reconciliation: CORE-12/13 source,
hidden real-account/navigation/layout acceptance, final package, normal six-asset
publication, independent retrieved native/app verification and the user's actual
installed update pass. Release source is
eeb5d40a0b7ef2a3799445ad352ec21a5bad2c3b, tag v1.1.7. All 792 installed members
match the released manifest; reopened main PID12928/helper PID40692 use the
installed executable, helper ownership/start identity matches and its ready
event is signalled. Recovery remains version2. Exact scratch cleanup passes.
No required S8 blocker or tracked deferral remains; no ACTIVE implementation
stage. S6/S7 remain postponed and S9/S11 are NOT STARTED. No desktop control,
forced close, reinstall or live handler rewrite was performed.
The user updated to1.1.6 through the built-in updater;
the new main application and Win+E helper are running. The user confirms Explorer
navigation stays in Explorer and Win+E works. User testing of external folder
links is expressly left until a real use case; retain the hidden native evidence
and distinguish that from a physical user check. Installed identity reconciliation
is now verified; keep the external-tool registry-view discrepancy separate from
that evidence and do not replace live handlers from that diagnostic view.

CORE-12 (2026-10-31): automatically collapse the bottom statistics strip whenever
it contains no meaningful figures, particularly This PC's fake folder total and
empty latest-change card. Collapse each unavailable card and its columns/gaps,
and reclaim the whole strip's height/margin when none remain. Preserve full,
compact and disabled preferences; navigating to useful folder/archive/search
content or changing active panes restores applicable cards automatically. A real
empty folder's available drive-capacity card can remain, while empty placeholder
folder/latest-change cards stay hidden. This PC's footer should count locations
without inventing folder/file/byte totals. Use the existing Native/Original design,
without new controls or animation. Verify actual hidden single/split navigation,
selection, empty content, global search, native/virtual archive data and settings
changes, plus dark/light100/150% layout bounds and reclaimed height. Deliver one
normal verified update after integrated acceptance; preserve live user work.

CORE-13 (2026-10-31): for Google Drive's streaming mounts, show the actual
per-account cloud capacity, used/free space and percentage consistently in This
PC, the sidebar and applicable capacity cards. The filesystem's cache-volume
capacity is not the account's cloud quota. Associate accounts through provider
mount metadata, never fixed drive letters or screenshot numbers. Prefer a
read-only provider-exposed local source; do not copy the desktop client's auth
tokens or change its streaming/cache settings. Keep local-cache figures distinctly
labelled where relevant. An unavailable quota must be explicit, and must not hide
an otherwise ready cloud mount or present local disk figures as cloud capacity.
Verify both real mounted accounts, numeric interpretation, mount changes and
unknown/unlimited/unavailable states, plus existing local/network drive behavior.
Finish this authorized refinement in the same active S8 integration/delivery.

Previous reconciliation: S8 was BLOCKED on installed verification of the SHELL-01
Explorer-navigation correction. Normal 1.1.6 is published at source
a7648b9d51c63d5aadac9e96b48ecbd8a428581c. Independently retrieved hashes, real
32/64-bit native Shell/COM/browser workflows and packaged app checks pass.
Scratch cleanup is verified. The existing 1.1.5 main application is still running;
the installer requires its normal user-controlled exit. Installation and its
actual installed routing/identity/profile-preservation checks remain required.
Resume them after that exit; do not force-close user work. Overall S8 is not
complete. S9 remains NOT STARTED.
The correction uses a native DelegateExecute handler: a Shell browser site routes
through that browser's BrowseObject, while an external filesystem request launches
Pathweft. Keep 32/64-bit COM registration reversible, migrate only wholly owned
old opt-ins, and retain disabled/third-party owners. An update never needs to
terminate an Explorer caller holding the previous DLL (SHELL-06: one fixed file name,
a held copy is moved aside; the earlier per-version file names are superseded).
Refresh folder integration's Settings state when its window activates; its old
constructor-time label is not evidence of current handler ownership.
Earlier S8 is COMPLETE through the user-requested1.1.5 refinements
below, normally published at source3fa4b465775ce411a89ba2628b1907420facac50.
Independent retrieval, actual packaged workflows, installed hashes/app-host
self-test, preserved settings/routing and exact scratch cleanup pass. There is
no blocker or required deferral in that prior closure. The earlier authorized S8 scope through CORE-09/10 and
PREVIEW-06/07 is COMPLETE in normally published and independently retrieved/
installed1.1.4 (source8552fbdc4a0c38acf7cbb76d98d1ef3d227b6063), with exact
scratch cleanup verified. This does
not complete the separately postponed S6/S7 or unstarted S9/S11 requirements.

Additional user-directed S8 refinements (2026-10-31), CORE-11 and PREVIEW-08:
show rounded free-space percentages beside the measured free-space number in
This PC, computed from each volume's real available/total values. Share usage
warning colors between This PC and sidebar: blue below 80% used,
amber from 80% and red from 90%. These are explicit Pathweft thresholds, not a
claim that Explorer implements an amber tier. The user's subsequent clarification
requires unavailable/no-media drives hidden in This PC as in the sidebar;
refresh must restore them when media is available. This supersedes CORE-09's
earlier placeholder-row presentation, without hiding ready cloud/network volumes.
Identify supported virtual cloud volumes using Windows mount/provider metadata,
never drive letters or account names; show cloud/provider identity instead of
misleading physical FAT32/exFAT labels, retaining raw format in tooltips.
Replace F3D and PDF preview text actions with conventional icon buttons and
tooltips/accessibility names. Theme the animation selector, including its popup,
focus and selection states. Composite model rendering into the preview pane so
there is no contrasting black rectangular cutoff. Keep camera, animation and
PDF navigation behavior. Verify real cloud mounts, warning boundaries and actual
large-model/animated/PDF controls in hidden dark/light layouts at 100/150% text
scale; then deliver one normal 1.1.5 release, independently retrieve and verify
its package, and install with existing settings/routing preserved.
PREVIEW-09, added by the user's executable-properties screenshots: show a themed,
selectable details view for .exe metadata including available description/company,
file/product versions, copyright, original filename, language, architecture,
file size, dates and location. Read resources/PE headers only; never execute the
selected application. Missing version fields stay omitted, malformed headers
must not crash the preview. Verify actual signed/unsigned and no-version files,
source preservation, selection changes and module disable behavior.
PREVIEW-10, subsequent user clarification: extend the themed expandable folder/
file tree to every ordinary native folder and virtual folder inside supported
compressed containers. Read native branches asynchronously only when expanded;
preserve names, type icons, file sizes, natural folder-first sorting and hidden
visibility. Show clear empty/inaccessible-folder states, without extracting or
executing files. Cancel obsolete branch loads on selection change/closure.
PREVIEW-11: when the active pane has no selected item (including the parent row),
collapse the side preview and its splitter/column completely. Restore the saved
preview width on a new selection when preview remains enabled; do not change the
user's enabled preference. Caption buttons must remain correctly reserved in
single/split layouts. Verify actual selection clearing/restoration, disabled
preview, pending folder/model cleanup and themed rendering without desktop input.
These additions supersede the still-unpublished initial1.1.5 package atbb946b7;
no intermediate release is authorized.

Additional S8 user requirement (2026-10-31), CORE-09: This PC must show drive
capacity/free space and usage bars using the same measured volume data as the
sidebar, with useful network share/provider details and capacity bars when
available. Show external-drive connection speed from Windows' actual USB link
information, including the reported E:/G: 10-Gbps expectation; distinguish the
negotiated connection from device capability and measured transfer throughput.
Unknown/unavailable media or unsupported speed reporting must remain honest.
Read device/volume information asynchronously without administrator prompts,
write benchmarks, USB resets or desktop takeover. Verify real local/external/
network drives, navigation/refresh, missing-media states and themed layout.
Integrate this before the pending final release; earlier unpublished packages
are superseded qualification output, not separate deliveries.
CORE-10 adds dynamic detail columns: retain Name; omit columns with no meaningful
data in the current folder/search/drive view, restore them when asynchronous
metadata or incoming results supply values, and preserve widths/applicable sort/
navigation. A drive-only sort resets to Name when entering file search.
Use drive-specific capacity/free/filesystem/connection columns in This PC and
normal file metadata elsewhere. Verify empty/mixed folders, async version/size,
search streaming, drive-to-folder transitions and resize without empty columns.
CORE-10 CRC32 column (user request, 1.1.56): the Details header context menu
offers a CRC32 column (8 uppercase hex digits, sortable, blank for folders and
entries without a value). Inside archives it shows the container's stored CRC32
without extraction and is visible by default; folders hide it by default. When
turned on for folders, files are hashed lazily on a background low-I/O-priority
thread, visible rows first, cancelled on navigation, hiding or close, never
blocking the UI. Values are cached in memory by path, size and last write; cloud
or offline placeholders stay blank and are never hydrated. TAR/GZip, RAR5 entries
hashed with BLAKE2 and other formats without stored CRC32 stay blank. Each
choice persists separately for archives and folders.

Post-v1.1.3 user additions (2026-10-31), owned by S8: replace the About
Diagnostics expander/telemetry with an Open settings folder button (CORE-03).
PREVIEW-06 replaces flat archive text with an expandable, themed folder/file tree
matching the sidebar Folders selector, concise names, keyboard expansion,
scrolling and sizes; enumerate metadata without extracting entries, preserve
empty/implicit directories, and release obsolete preview work.
PREVIEW-07 promotes the F3D module beyond the GLB prototype: remove its file-size
and embedded-resource restrictions, select the formats supported by the bundled
readers, and provide mouse orbit, pan, wheel zoom, fit/reset and keyboard access,
plus native animation clip selection, playback and timeline when present.
Keep module opt-in, native rendering inside Pathweft, asynchronous rendering and
actual worker teardown. Verify a real user model above 64 MiB, camera/frame
changes, supported external-resource models, disabled/malformed cases and
unchanged source assets. Engine reader/rendering limitations remain explicit;
3D editing and formats unavailable in the bundled engine are not promised.
Complete integrated hidden/rendered checks, normal final release, independent
package retrieval and installation verification. No Sandbox or desktop takeover.
The preceding v1.1.3 correction below is delivered and verified.

Post-v1.1.2 correction: clicking outside the search popup must dismiss it even
if WPF still reports keyboard focus in the query field during deactivation.
Moving focus into its owned preview keeps that interaction open; moving outside
the popup/preview dismisses both. Escape remains available, and reopening must
not be closed by a previous deactivation callback. S8 owns this WIDGET-01/02
correction, focused hidden regression checks and final v1.1.3 delivery.
The same correction includes the reported archive-to-folder drag freeze and
delayed drop: internal archive drags must start without extracting on the UI
thread, submit the existing archive transfer queue on drop, preserve source
contents and interoperation, and never drop into a later pointer location after
preparation. Restore the redirected Windows Downloads known folder in Favorites.
Existing Explorer windows must retain their own subfolder navigation while
opt-in external folder launches route to Pathweft; SHELL-01 owns this boundary.

Create a responsive Windows folder browser with connected Everything search,
taskbar search, internal previews and improved transfers in one maintained
product. Keep normal views uncluttered and expose advanced controls in settings.

The setup request established the environment, public fork and specification.
On 2026-10-28 the user authorized **implementation of S1 using Specification-Gated
Implementation**. This includes independent identity, real pane search and its
required verification. After S1 completion, the user authorized **S2, the connected
taskbar workflow**, by requesting the next stage. On 2026-10-29 the user requested
the next stage again, authorizing **S3, internal selectable previews**. The same
day the user added an F3D GLB prototype to S3 and requested a separate **S4 archive
browsing/editing stage**, including rename/copy/cut/paste. REA-driven transfers
remain S5. The user subsequently requested plan review and a final integration task:
Everything inside the existing StartAllBack search, explicitly anticipating a
hook/hack because native support appears absent. This is **S6 StartAllBack search
integration**. The user then explicitly expanded the long-term outcome to
**Everything in every search location and the Windows Search service disabled**.
The user subsequently revised that target: **retain WSearch for Outlook, reduce
its filesystem indexing scope and use Everything for filesystem search**. This
explicitly supersedes service retirement; unavoidable Windows-managed indexing
exceptions must be identified. That scoped migration is **S7**, after S6;
final delivery moves to **S8**. This request authorizes specification/plan review,
not immediate service shutdown, hook activation or modification of installed
applications. The user then authorized the next implementation stage, S4, after
this plan update. S1-S4 are complete. On 2026-10-29, after reviewing unarr and
bitplane/rars, the user authorized trying rars and delegated the integration
choice. S4R extends native archives with a pinned embedded Rust RAR writer;
it precedes S5 and does not authorize a wider Rust port. No
intermediate product release is authorized. User default-handler and Windows
Search changes are not part of the S1-S3 trials.

Source integration is selective: port useful modules and add-on source rather
than requiring the complete donor applications. The 2026-10-28 clarification
makes REA analysis of TeraCopy a required basis for transfer development.
The 2026-10-29 clarification prefers native integration: use source/native SDK
libraries inside the application wherever practical, with Pathweft owning the
interface, settings and resource lifecycle. Do not make standalone viewer or
archive utility windows the default integration. Record an unavoidable external
process boundary explicitly rather than presenting it as an embedded module.

On 2026-10-30 the user clarified S6's presentation: retain StartAllBack's grouped
layout, show a reduced Everything-backed subset within each file category, and
use See more results for the complete EverythingToolbar-derived view. A handoff
that leaves Windows Search producing the compact file results is not the target.
Preserve Programs and Settings functionality if technically achievable and qualify
their actual backend dependencies. The user also requested StartAllBack folder
launches to prefer Pathweft, and investigation of Directory Opus's Explorer
replacement as the reference for replacing the inherited FileLabs window watcher.
StartAllBack-specific routing belongs to S6; broader default-handler replacement
and its final delivered recovery belong to the existing S8 SHELL-01 scope. The
conditional discussion of WSearch being disabled does not authorize disabling the
host service or remove the retained Outlook requirement.

The user's subsequent update clarification makes current-version qualification
the S6 test direction. Do not downgrade Windows or StartAllBack merely to retain
an earlier test baseline. Keep earlier-build findings as comparison evidence and
qualify changed private interfaces on current builds. Exact artifact identities
remain required for each capture and supported combination; they do not impose a
product requirement to freeze updates or promise unverified update compatibility.

The user subsequently explicitly directed execution to **jump to S8** and deliver
the file explorer before continuing the broader Windows search work. S8 now
depends on completed S1-S5/S4R/S4U, not S6/S7. S6 is parked by this user-directed
priority boundary and S7 remains unstarted; their requirements remain required
later work, outside this core file-explorer release. They are not satisfied by
S8 or removed from the program. Overall program completion remains distinct from
this S8 core release. The Sandbox was shut down at the user's request.

S8 must keep shell integration opt-in in its proper Windows integration settings
section, review and selectively adopt useful upstream changes before compilation,
and provide an alternate restrained Windows-style theme with accent support.
Use the user's File Labs and Explorer screenshots plus Huashu design guidance as
references; retain the existing appearance as a selectable option. This explicit
core-first delivery instruction supersedes earlier all-stages-before-release
ordering for this release only. It does not authorize resuming S6/S7 or changing
live Windows Search/Outlook configuration.

The user requires subsequent verification without taking over active host
windows. Prefer the existing app-owned hidden diagnostics and direct native
Shell callers. Do not activate their windows or synthesize desktop input.
Keep any remaining actual menu-caller criterion explicit until user-controlled
or genuinely isolated evidence qualifies it; hidden rendering alone does not
prove StartAllBack's menu action. This execution constraint does not waive
SHELL-01 or authorize restarting the parked Sandbox/S6 work.

The user then explicitly confirmed that the StartAllBack-specific folder-link
check belongs to postponed S6. Normal filesystem folder/shortcut routing has
already been qualified independently; do not make further Start-menu actions
an S8 core-release gate. SAB-06 owns the actual StartAllBack entry points,
including their Shell/known-folder/library distinctions and direct routing.
This preserves that requirement in S6 rather than waiving its eventual acceptance.

On 2026-10-31 the user explicitly stopped all Sandbox work and directed S8
delivery and installation on the host. Use the completed actual per-user host
install/update/uninstall and preservation trials, then verify the final published
and installed package. A separate Sandbox/guest acceptance run is no longer an
S8 gate. Keep integration opt-in and preserve existing settings during installation.

## 2. Source baseline and provenance

| Source | Pinned baseline | Role |
| --- | --- | --- |
| [File Labs](https://github.com/limburatorul/file-labs/tree/a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6) | `a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6` | Fork source, development version 1.0.62; latest published release observed v1.0.61 |
| [EverythingToolbar](https://github.com/srwi/EverythingToolbar/tree/a4379f181ea8e9b4d3ff8b0e5178d8d39491830c) | `a4379f181ea8e9b4d3ff8b0e5178d8d39491830c` | Selected search clients, controls and taskbar integration |
| [QuickLook](https://github.com/QL-Win/QuickLook/tree/2ec219aa18416c6e3b89d6aad3d82d10610f917f) | `2ec219aa18416c6e3b89d6aad3d82d10610f917f` | Viewer host/contract, selected viewers and add-ons; .NET Framework 4.6.2 donor |
| Everything engine | 1.4.1.1032 (until 1.1.79) | Retired by SEARCH-09: no longer used or packaged; the Pathweft index service (native/pathweft-index) replaces it. |
| F3D | 4.0.0, b72c7b5c9244e4a0c12975ce6546645c1a8b2680 | Source-derived C API/offscreen rendering for the S3 GLB prototype |
| 7-Zip | Pin source/SDK/runtime in S4 | Native archive browsing and supported edit operations |
| rars | `f7098c7b9a1e844555daad1d376fd1f043adba2c`, crate 0.13.0 (was e2415a2 / 0.12.3 until 1.1.103) | S4R embedded RAR encoder/rewrite API; Apache-2.0; independently qualify before enabling edits |
| TeraCopy | 4.0.5.0 x64, signed main SHA-256 `1f0cca0be4e6a22e856c3b0de6655a1af8059a06cc66133398307de335098c6f` | Required REA reference, not a product runtime dependency. Evidence and bounded behavior adoption: [S5 transfer contract](S5-TRANSFER-ADOPTION.md). |
| StartAllBack | Host 3.9.25; current isolated qualification 3.9.28 on 2026-10-30; identify exact artifact/hash and Windows dependencies for each capture | Proprietary host for a version-bound native search adapter; current-build qualification replaces routine downgrades; no donor binary redistribution or provider-hook feasibility claimed |

File Labs currently targets .NET 8/WPF. It uses recursive filesystem search
capped at 10,000 results, implements its own previews plus an external QuickLook
bridge, and retains upstream product/update identities. This newer baseline
already includes optional concurrent small-file copying. The older v1.0.53
serial-copy assessment must not be represented as current behavior. Neither
source inspection nor upstream timing claims prove comparative performance.

Pin every imported source/dependency. Reassess relevant drift before adopting
another revision. Preserve upstream history; do not import unrelated product code.

## 3. Setup acceptance

| ID | Requirement | Acceptance evidence |
| --- | --- | --- |
| ENV-01 | Named public fork preserving upstream lineage | GitHub reports `Darkaxt/Pathweft`, public, fork=true, parent `limburatorul/file-labs` |
| ENV-02 | Maintained checkout and correct remote ownership | Origin is Pathweft, upstream is File Labs, default GitHub target is the fork, clean committed worktree and matching remote head |
| ENV-03 | Reviewable development environment | Installed compatible SDK selected; Release compile succeeds; generated scratch reviewed/cleaned; retained dependencies have an ongoing project consumer |
| ENV-04 | Published project documentation | Specification, complete stage map, development notes and truthful setup evidence committed/pushed and read back independently |

## 4. Product requirements

Each row specifies required behavior and its acceptance boundary. Cross-cutting
criteria apply within the implementing stage and again at the packaged boundary.

### Identity, browsing and interface

| ID | Requirement and acceptance |
| --- | --- |
| CORE-01 | Give Pathweft independent executable/product, settings, IPC/single-instance, agent/startup, shell, installer and updater identities. Verify side-by-side isolation from File Labs: no forwarding into, migrating, overwriting or uninstalling it. Establish isolation before ordinary-user-state runtime trials. |
| CORE-02 | Preserve usable folder navigation, tabs, one/two panes, selection, keyboard actions, clipboard and drag/drop. Verify representative real folders and cross-pane operations, including a large listing. Inherited source is not proof of runtime correctness. |
| CORE-03 | One settings interface owns integrations and module controls; normal views remain uncluttered. Settings starts with actionable preferences, not a search implementation/qualification report. Keep search instructions, version/paging/coverage details and verification terminology in Help or developer documentation; preserve concise actionable availability/unsupported-operation messages. Before accepting UI changes, establish concrete layout/display-scale expectations and inspect current rendered screenshots at representative sizes. |
| CORE-04 | S8 provides an alternate Native appearance with neutral surfaces, restrained selection/focus and Windows accent support. Appearance settings select the existing or Native theme and system/custom accent; persist the choice and apply it consistently to panes, settings, taskbar search and product dialogs. Follow Windows light/dark and high-contrast preferences where supported; retain readable text and keyboard focus. Inspect actual rendered single/split layouts and settings at 100%/150% scale and representative window widths, including live theme/accent changes and restart persistence. Keep the existing theme available. |
| CORE-05 | Before S8 compilation, review upstream source/dependency drift against the imported revisions, record concrete adoption/non-adoption decisions and integrate applicable fixes. Preserve fork identity, implemented Everything/native archive/preview/transfer behavior, source provenance and notices. Adopted behavior receives focused regression checks and the final integrated release verification; do not blindly replace fork implementations or upgrade dependencies without a relevant benefit. |
| CORE-06 | Automatic file opening (double-click/Enter) with the internal editor enabled must use the Windows association when the editor cannot read the file, without an editor error or empty window. Check actual readability before creating the editor, reuse decoded content once accepted, and preserve an already-open document's unsaved content. Apply the same decision to archive working copies while preserving their ownership. Disabling the editor uses Windows associations; explicit F4/Edit continues to request editing. Verify unsupported extensions and rejected HTML content/size through the actual double-click route, supported text/HTML, disabled-editor behavior, archive routing and absence of host focus. |
| CORE-07 | Replace the app-wide pink tile/dark-folder icon with a transparent Windows-yellow/gold folder, retaining the two-pane list motif with three left lines, two right lines and the central divider. Use contrasting brown marks, remove the bottom-right line, and fill the icon canvas enough to match Explorer's apparent folder size (about 92% width). Include native Windows icon sizes from 16 through 256 pixels. Apply through the shared WPF resource, executable/window/tray/shortcut identity and installer/uninstaller; verify alpha/color, small-size legibility and final packaged resources. |
| CORE-08 | Replace the sidebar's hard top/bottom scrolling cutoffs with a soft opacity fade over the final18 logical pixels of the content viewport, only on edges with content beyond them. Fade the viewport content, keeping the scrollbar, app title and footer controls sharp and interactive. Respect resize, scroll, section/extent and 100%/150% text-scale changes; show no fade when all content fits or in Windows high contrast. Use transparency over the existing background rather than a hardcoded color. Inspect actual top/middle/bottom WPF renders and verify scrollbar/input ownership without host desktop input. |
| CORE-09 | S8 This PC drive capacity/free/usage and network details plus truthful external connection speeds, as specified in current scope. Verify actual inventory, live refresh/navigation, unknown media/speed, native USB evidence and dark/light responsive layout. |
| CORE-10 | S8 dynamic detail columns described in current scope; verify data-driven visibility, asynchronous metadata/results, width/sort preservation and contextual navigation. |
| CORE-11 | S8 measured free-space percentages, shared capacity warning colors and provider-based cloud drive identity, as specified in current scope. Verify actual mounts and unavailable capacity. |
| CORE-22 | S17 approved yellow-folder/dark-compass application icon. Transparent multiresolution assets must remain legible at small sizes, retain polished larger detail, appear in embedded WPF/native/package surfaces, and preserve the existing Windows application/shortcut/Jump List identity. |
| CORE-24 | Next-release taskbar icon sizing: enlarge visible folder/compass artwork approximately10-15% relative to1.1.25 while preserving design, transparency, canvas bounds and Windows identity. Verify actual packaged taskbar appearance and small-size legibility at representative display scales. |
| CORE-25 | Next-release changelog artwork: use ImageGen to create twelve distinct, visually consistent Pathweft banners for January through December. Preserve the 960:170 banner presentation and release-month selection, bundle the complete annual set, avoid year rollover gaps, inspect each asset and verify all twelve month selections in the actual packaged changelog. S18 owns this next-release companion; no standalone artwork-only release is requested. |
| CORE-26 | Remove the duplicated address-bar drive-capacity strip; retain sidebar/lower-card capacity and real errors/loading. Read-only archives use the user-selected ImageGen diagonal corner ribbon with centered READ-ONLY text, faint fill/lines/text and the user's accepted0.55 overall opacity. Reach the outer pane's right/bottom borders through unused footer space; keep footer/actions readable and clickable. Editable archives stay unlabelled. Verify theme/scale/navigation and real edge alignment. S18 owns this same-release refinement. |
| CORE-27 | Actual foreground folder/address resolution/search work owns a subtle loading background inside the address field. Preserve its current surface as the active region, black remaining track in dark mode and suitable light/high-contrast appearance; use an indeterminate band when there is no meaningful total. Never fake percentages, reset drafts/caret/focus or flash this state for background sizing/metadata refreshes. Complete/error/cancel/superseding operations retire their own indication and animation. Qualify pending real work, actual completion/input preservation, theme/scaling/reduced-motion and delivered behavior in S18. |
| CRAFT-01 | Refresh the three delivered Office libraries to the reviewed WordCraft0.15.2, GridCraft0.16.1 and DeckCraft0.15.1 source pins. Preserve current declared-format support, actual pagination/sheet/slide navigation, import notes, original/MOTW preservation, single-attempt LibreOffice crash/error recovery and owned cancellation/exit. Compare old/new real rendering and qualify the complete workflow before adoption. |
| CRAFT-02 | Add source-built PhotoCraft0.20.2 read-only rendering for native .pcraft and PSD/PSB, retaining composition/layers where its importer supports them. Qualify actual inputs, not only extension registration; retain existing image preview behavior. No editor/CLI/service dependency. |
| CRAFT-03 | Add source-built VectorCraft0.21.1 read-only document/artboard rendering for its native format and qualified SVG/PDF-compatible AI inputs. Qualify real importers and report unsupported legacy AI/EPS explicitly rather than inferring support. Preserve existing working image/PDF paths. |
| CRAFT-04 | Add source-built FigCraft0.15.1 read-only native document/frame rendering. Qualify actual .figcraft input and assess .fig independently; preserve the current embedded-thumbnail route when native Figma parsing is unavailable. |
| CRAFT-05 | Qualify PdfCraft0.18.2's selected parser/text/render libraries against the existing Windows PDF viewer. Retain the existing rendering path unless an actual fidelity/resource comparison supports replacement; provide independently qualified native PDF text extraction through the owned core boundary. Scanned pages are image-only without OCR. |
| CRAFT-06 | Expose reusable read-only text extraction through the selected source-built cores and app-owned workers without breaking existing rendering calls. Return bounded batches of strings with stable native source locations (paragraph/table path, sheet/cell, slide/text object, PDF page and applicable native text layers/objects), continuation/completion and explicit unsupported/image-only/import-warning states. Do not equate successful rendering with text completeness. Qualify Unicode/order/locations, empty/protected/malformed input, cancellation, bounded replies and actual native exit; no index database, scheduler, full-text retention or OCR in this stage. |
| CRAFT-07 | Pin selected donor/font/dependency sources and actual build graphs/notices, inspect used-path advisories and Windows/runtime compatibility, and package only required libraries/runtime assets. Preserve source/preview leases, current storage settings and Windows identity; qualify sidebar/PiP/Windowed/archive and repeated selection/resource reclamation. Deliver one final normal release with complete corresponding sources, independent downloaded-package and applicable installed acceptance. |

On 2026-10-31, the user also requested EverythingToolbar's four inline search
toggles: Match case, Match whole word, Match path and Regex. They belong at the
right end of the single address/search field, with accessible names, tooltips,
visible selected states and actual Everything behavior. Match whole word is
disabled while Regex is enabled, matching the pinned donor. Options stay local
to each pane/search session and cross widget-to-pane handoff with its query;
they must not leak across the shared native client or bypass folder scope.

After verified v1.1.1 installation, the user reported the Settings action
"Restore previous folder handlers" clipped by the shared tab style's width cap.
CORE-03 requires its complete action label to fit or wrap at the minimum440-DIP
Settings width and at 100%/150% text scales. Preserve the width cap for actual
tabs and the existing restore action; do not invoke live recovery during visual
verification. This bounded correction remains S8 and receives a normal v1.1.2
patch through the existing release/install channel; v1.1.1 stays immutable.

On 2026-10-31, after v1.1.0 delivery, the user reported the Original theme's
active header/glow, excessive Settings prose, and the Native window's missing
green outer activation border. This reopens S8 for the bounded CORE-03/04
correction. Native window frames must use the actual Windows accent for the
active outer border when Windows enables colored title bars/borders, revert on
deactivation, refresh on system changes, and retain neutral caption surfaces.
Keep the existing Original theme selectable. Verify native DWM call results and frames
without taking host focus and inspect Settings at 100%/150%; no Sandbox/S6 work.
The user's later color clarification uses the sidebar's near-black background
for the Native dark outer window area behind the panes and summary cards; retain
the gray file-pane surfaces, Light mode and Windows high-contrast behavior.
The user subsequently reported a brown intermediate square on close and requested
black or immediate disappearance. After the header correction, qualify the
closing path and remove any Native client-only fade that exposes a colored DWM
surface. Preserve ordinary Windows closing behavior, settings and active work.

### File visibility and read-only cues (S4U)

On 2026-10-29 the user approved grayscale icons with a small lock and requested
Ctrl+H plus Linux-style dot names. This authorizes S4U before S5.

| ID | Requirement and acceptance |
| --- | --- |
| FILEUI-01 | Preserve icon shapes, filename brightness, selection and cloud/cut cues. Desaturate only icons/thumbnails and add a contrasting lower-right lock for confirmed read-only files or archive contents, in Details/List/icon views and physical unsupported containers/virtual entries. Give specific tooltip and accessible state. Directory ReadOnly customization bits do not prove access denial; do not infer ACL/admin rights or write user files to probe permission. Verify writable/read-only files, capability refusals, member states and refresh. |
| FILEUI-02 | Ctrl+H and the Hidden button share one persisted setting across both panes and widget. Handle file-list/filter/path contexts while preserving text, location/query and usable selection. Preserve inline rename and separate editor shortcuts; ignore auto-repeat/additional modifiers. Verify routed input/actions, both directions and separate-process persistence in an isolated profile. |
| FILEUI-03 | Hidden means Windows Hidden attribute or a basename starting '.', excluding navigation '.'/'..'. Apply the global setting to browsing/caches, breadcrumbs/path suggestions, recursive filesystem/Everything search and archives. Hide descendants of hidden directories from recursive search; direct navigation into a known hidden folder remains usable. Never alter filesystem attributes to implement visibility. Verify dot and Windows-hidden files/directories/descendants, ordinary dotted names, cache revisits, live attributes, paging/counts and real index behavior. |
| FILEUI-04 | While hidden files are concealed, a folder or archive listing's footer adds "· N hidden" for the entries FILEUI-03 left out of that listing (Windows Hidden, including protected system files, or dot-prefixed), counted by the same read with no extra enumeration and kept with the cached listing. Omit it when nothing is concealed or hidden files are shown; selection, filter, search and This PC footers are unchanged. The Hidden button stays a global setting and is never disabled by folder content. Verify the count against the folder on disk, a cached revisit and its absence when shown. |
| FILEUI-05 | The user's 2026-11-12 report: Copy path (menu and Ctrl+Shift+C, in panes and the search widget) puts each selected path in double quotes, one per line, as Explorer's Copy as path does, so a path with spaces pastes into a terminal as one argument. Pasting that text into the address field resolves it per SEARCH-01: surrounding whitespace and quotes are ignored, a folder opens and a file opens its folder with the file selected. Verify the quoted text and its round trip through the real address Enter route. |

Design contract: retain File Labs tokens and row metrics. Small 16-DIP icons
use an 8-DIP lock; tiles a 24-DIP lock. Keep icon opacity apart from existing
cut/inactive styling. Inspect actual hidden WPF Details/List/icon output at
100%/150% text scale. Approved generated concept supplies direction; production
uses existing native icons. Final packaged delivery remains S8; no stage release.
The expanded sidebar and new duplicate-finder scans use this same visibility
rule, retaining the tools' other exclusion rules. Indexed results are filtered
one requested page at a time; native match counts include filtered candidates,
and the footer distinguishes visible rows from candidates checked. Inaccessible
Windows attributes remain explicitly unknown; no writable/permission claim is
made from them. Dot names remain detectable even on an unavailable indexed path.

### File-name index search

| ID | Requirement and acceptance |
| --- | --- |
| SEARCH-01 | Use the actual Everything index inside panes, reusing selected EverythingToolbar client source. The user's 2026-10-31 clarification replaces the mode picker and separate search row with one browser-style address/search field in the existing path row. On Enter, first resolve the text as an existing path (including quoted, environment-variable and relative paths); navigate to a folder or reveal a file. If it does not resolve, search Everything within the current folder. This PC supplies global scope. Remove the second row, scope selector, funnel and search icons. Ctrl+L and Ctrl+F focus this same field. Preserve navigation/history, archive browsing, Unicode/spaces, wildcards/regex, paging and cancellation; compare against the real index. |
| SEARCH-02 | Page/lazily retrieve results and indexed metadata/sorts; do not eagerly decode every icon or enumerate all hits. Superseded queries cannot replace newer results. Verify rapid query changes, paging/sorting, empty results and changing/deleted paths. |
| SEARCH-03 | Share one deliberately selected engine instance/index between panes and widget, reusing an existing compatible instance where appropriate. Pin the binary/API version and own readiness/reconnect/instance lifecycle explicitly; never silently create a duplicate index. Keep the GUI unprivileged and necessary service privileges at the indexing boundary. |
| SEARCH-04 | Display concise true coverage and actionable engine availability. Under the user's 2026-10-31 Everything-only clarification, remove the filesystem-scan mode from the product UI; a failed path lookup falls back to Everything, never to a filesystem crawl. Unindexed/offline/network/non-NTFS coverage depends on the actual existing Everything index and must not be presented as complete. Path resolution must remain responsive and a superseded resolution/query cannot navigate or replace newer state. |
| SEARCH-05 | Owner request 2026-11-14 (issue #6; the Everything instance stopped answering and every search waited): Everything queries are bounded (45 seconds). On a timeout Pathweft reads Everything's status with a bounded window message: while it is loading or busy updating its index, search says so and nothing is restarted, because a restart would start that work over; folder sizing measures the folders itself instead of waiting. If Everything no longer answers status messages, or is idle yet has left queries unanswered for 3 minutes, Pathweft restarts that exact client instance (asks it to exit, stops only that process identity if it remains, relaunches it with its original command line), never the Everything service, and says so in the status. Owner request 2026-11-18 ("a monitor to keep Everything responsive"; 1.1.74 left a hung Everything alone for hours because typing cancelled the queries that counted towards the threshold, and the SDK's unbounded status call held the IPC thread behind the hung window): the app window checks Everything's window every 30 seconds whether or not anyone is searching, and restarts it once it has stayed hung for 2 minutes; a query that times out on a hung window restarts it at once; a caller's cancellations never delay either; the IPC thread reads index state with the bounded status message, never the SDK's unbounded call; restarts are at most one per 30 minutes (not once per session); verification profiles and the read-only MCP server never restart Everything. Verify timeout, loading/busy, immediate hung restart, idle threshold, cooldown, monitor run/reset/restart and caller-cancellation paths with a held native send, a substituted status/restart and a substituted clock. |
| SEARCH-06 | Owner request 2026-11-18 (Everything 1.4 repeatedly hangs and freezes search; the replacement is the Pathweft index service, docs/index-service/SPEC.md IDX-01..14, merged into this repository): every indexed query and indexed folder size goes to the Pathweft index service (`\\.\pipe\PathweftIndex`, framed JSON, PROTOCOL.md) when its pipe connects within 250 ms; otherwise to the existing Everything client (SEARCH-01..05 unchanged), and folder sizes otherwise measure the folders as before. An absent service is remembered for 10 seconds so a missing pipe does not slow every query. A named Everything instance (diagnostics) always goes to Everything. A service refusal (unsupported syntax) is shown as is, never retried on another engine; a lost connection or a service that does not answer within 30 seconds ends that request with a message and the next request reconnects. Pane searches ask for `visible_only` (Explorer shows only what the user can open); the MCP search returns every match with its `visible` flag; folder sizes are the service's administrator view. The search status names the engine that answered ("Pathweft index" or "Everything index"), and while any store is loading or scanning the result count says "index updating" without retrying or blocking. Size sources name the engine that supplied them. Verify engine selection, fallback, refusal, timeout/reconnect, visibility handling, updating hint and folder sizes against a substituted service and against the installed service. |
| SEARCH-07 | Settings > Windows integration gets a "Pathweft index" section: service state (running, not installed, not answering), version, memory and per-store rows (drive or folder, state, items, last error), the folder roots with Add folder... / Remove / Rescan, and Install / Update / Remove buttons. Folder-root changes go over the pipe as the user; installing, updating and removing the service go through the existing administrator session and only from the Program Files installation (the elevated action may only start a protected executable); other copies show the status only. Update appears when the shipped service binary differs from the installed one. Verify status rendering, folder-root add/remove/rescan against a substituted service, and the elevated commands' argument construction. |
| SEARCH-08 | Delivery: the setup and portable packages ship `index\pathweft-indexd.exe` and `index\pwindex.exe`, built from `native/pathweft-index` through the Rust gate; uninstalling Pathweft removes the service it installed. Installing or updating Pathweft does not install or restart the service by itself. Verify package contents and the uninstall entry. |
| SEARCH-09 | Owner request 2026-11-19 ("replacing Everything integration as soon as the Index service thread allows for it since I think the service is working as desired"): the Pathweft index service is Pathweft's only file-name index. Pathweft no longer talks to Everything: the Everything client and SDK DLL, the bundled Everything engine, the Everything monitor and restart (SEARCH-05, retired) and the named-instance selection are removed. Every indexed search (panes, the search widget, the MCP `search_files`, Maintenance and Scoop cleanup sizes) and every indexed folder size (pane rows, space analysis, MCP `get_folder_size`) goes to the service. When the service is absent or not answering, search says so and points to Settings > Windows integration > Pathweft index; it never falls back to a filesystem crawl (SEARCH-04), and folder sizes measure the folders on disk as before. Wherever an earlier requirement names Everything as the file-name index (SEARCH-01..04/06, CORE-20, MCP-02/03, WIDGET-01/02, SAB-02/03, EVERYWHERE-02..05) the engine is the Pathweft index service. The query language is the service's (docs/index-service/PROTOCOL.md), including the date functions `dm:`/`dc:` and `parent:` that MCP-03 relies on; other Everything functions are refused with the service's message, never answered differently. Setup installs or updates the service silently (SEARCH-08 revised) and uninstalling Pathweft removes it; the service keeps its folder roots for a later install. Settings keeps Install/Update/Remove as repair. Pathweft never stops, uninstalls or reconfigures the owner's Everything; it only stops using it. UI text, help, tooltips, MCP descriptions and size labels name the Pathweft index, not Everything. Debug verification profiles query the installed index service read-only (no folder roots added or removed, no rescans), as they previously queried the owner's Everything; scenarios that need an absent, refusing or slow service substitute one. Verify: no Everything binary or reference in the packages; searches, folder sizes, widget and MCP against the installed service; the absent-service message and on-disk folder sizes; the setup install/update and uninstall of the service in the update progress checks; the converted verification scenarios. |
| SEARCH-10 | Pending issue from the 1.1.81 release (verification/2026-11-19-release-1.1.81.md): space analysis, which has no folder watcher of its own, takes an indexed folder size only when the folder has not been written in the last two seconds (the index reads the NTFS change journal every 250 ms); a folder written more recently is measured, so the analysis counts a file written just before it ran. Pane folder-size rows keep their watcher-driven freshness: a folder their watcher reports changed is measured. Verify by the Sizing119 package check, which writes a nested file immediately before the analysis. Revised 2026-11-24 by CORE-32 (spec 1.59): a folder written in the last 600 ms is asked for from the index once 600 ms have passed since that write, instead of being measured on disk; the indexed-sizes scenario verifies that the file written just before is counted. |

Integrating Everything does not itself authorize disabling Windows Search,
changing Outlook indexing or modifying the existing engine configuration.

### Taskbar workflow and shared actions

| ID | Requirement and acceptance |
| --- | --- |
| WIDGET-01 | Integrate selected EverythingToolbar controls/taskbar code with the product's settings/update owner. The user's post-v1.1.1 design correction requires a compact global search field with the four existing match toggles and a clean name/location/size/date results list in Pathweft's theme. Search as text changes, cancel superseded work, preserve paging and hidden-file rules, and show no drive listing for an empty query. Remove the duplicate title/settings row, browser navigation, destination field, view/sort footer and action bars. Enter/double-click opens results; folder/archive results open in the main manager. Escape/deactivation dismisses, Down focuses results, and Ctrl+Enter hands the query/options into a main pane. Verify actual query/result/open/handoff, errors/cancellation/paging and current rendered minimum/normal layouts at100%/150%. |
| WIDGET-02 | Keep query, focus and selection independent per surface and reuse the existing Everything/result-open/preview owners. The user's compact-widget correction removes widget-specific copy/move/destination controls and their dedicated transfer workflow; transfers remain in the main manager and its existing shared queue. No standalone utility dispatch or second indexing/transfer owner. Preserve ordinary result opening and optional Space preview without adding visible feature bars. Original S2 transfer-widget evidence is historical, not acceptance of the new compact UI. |
| WIDGET-03 | Make taskbar integration explicit and optional. Verify the selected search-box mode on the supported Windows build across Explorer restart, focus, keyboard and monitor/DPI changes. Where the box cannot be hosted, provide an explicitly reported supported pinned-icon/hotkey mode. Do not replace the desktop shell. |

### Internal preview modules

| ID | Requirement and acceptance |
| --- | --- |
| PREVIEW-01 | Port the necessary QuickLook host/contract and selected viewer/add-on source into built-in modules for the supported modern .NET runtime. Do not depend on standalone QuickLook, its tray/global hook/updater/installer. Verify previews with standalone QuickLook absent. |
| PREVIEW-02 | Initial source-integration scope: text/code, Markdown, images, PDF and archive listing. Preserve media/audio and font previews as internal selectable modules, reusing current code where suitable. Record the exact shipped module/source/dependency/format inventory before S3 acceptance. Importing every plugin was not required for S3; S13 separately adds the fourteen specialist categories under PREVIEW-21..36. |
| PREVIEW-03 | Persist per-module enable/disable settings. Disabled modules are not selected or initialized; disabling the current module releases its preview resources. Resolve overlaps by deterministic priority or explicit preference. Show an eligible alternative or honest unsupported/disabled state. Verify toggles, active-disable and restart persistence. |
| PREVIEW-04 | Space, side-pane and widget previews share selection/lifecycle rules. Rapid navigation cannot show obsolete content. Close/change releases documents, playback and native resources. Previewing must not execute a selected executable or run document macros/scripts. Verify representative formats, malformed/large/unsupported inputs and file release by rename/delete after closing. |
| PREVIEW-05 | User-authorized S3 prototype addition (2026-10-29): add an opt-in F3D-backed GLB preview module and verify the user-specified Castlevania GLB inside Pathweft. Inspect/adapt F3D source and its supported library API. Keep module settings and resource lifetime under the internal preview host; verify a real model render, camera controls, disable/close cleanup and unchanged original asset. The private asset path/hash belongs in ignored trial input, not public source. This adds a bounded 3D prototype, not arbitrary 3D format parity or editing. |
| PREVIEW-06 | S8 archive preview tree described in current scope; verify the reported real ZIP and nested/empty/implicit folder fixtures, theme/layout, keyboard expansion, metadata-only browsing and lifecycle. |
| PREVIEW-07 | S8 full embedded F3D viewer described in current scope, superseding PREVIEW-05's prototype restrictions; verify actual bundled-reader formats and large-model interaction/rendering, opt-in and resource release across preview surfaces. |
| PREVIEW-08 | S8 themed icon actions in PDF/F3D, animation selector and transparent model composition; preserve interaction and lifecycle, with dark/light responsive evidence. |
| PREVIEW-09 | S8 executable details preview described in current scope; verify actual resource metadata, source preservation, no execution, missing resources and module settings. |
| PREVIEW-10 | S8 common tree presentation for native and virtual compressed folders, lazy asynchronous native branch reads, hidden visibility and resource lifecycle, as specified above. |
| PREVIEW-11 | S8 selection-driven preview collapse/restoration without altering the enabled preference, with correct saved width/caption layout and resource cleanup. |

Compiled-in modules with on-demand initialization are sufficient. Assembly
unloading, a dynamic plugin marketplace and a general extension framework are not
requirements. The explicitly requested F3D GLB prototype is included in S3;
All fourteen missing specialist categories, including Office, are scoped to S13.

### Remaining specialist preview modules (S13)

The comparison baseline is the [QuickLook viewer directory](https://github.com/QL-Win/QuickLook/tree/master/QuickLook.Plugin)
reviewed on2026-11-02:24 viewer categories plus PluginInstaller. Pathweft has ten
corresponding categories, with narrower format/features in several. S13 implements
the fourteen missing categories below; it does not claim exhaustive format parity,
silently expand the existing ten modules, import the separate add-on catalogue or
add QuickLook's plugin installer/marketplace. Existing gaps remain documented.

| ID | Required category | Required behavior and acceptance |
| --- | --- | --- |
| PREVIEW-21 | Shared integration and exact inventory | Pin the selected source/dependencies and record an explicit supported-format/feature matrix for all fourteen categories before implementation. Reuse the existing PreviewSession/IViewer, module settings, priority, side pane, Space and widget paths; initialize only enabled selected modules. Use source/native integration without requiring standalone QuickLook or launching associated applications. Preserve PREVIEW-03/04 and LIFE-01. A generic text view, file icon, external application or metadata-only substitute does not satisfy a specialist rendering requirement. Missing required capabilities are acceptance blockers, not permission to drop a category. |
| PREVIEW-22 | OfficeViewer | Embedded readable document, spreadsheet, presentation and applicable diagram/OpenDocument previews through a qualified renderer or native preview-handler boundary. Verify actual page/sheet/slide content and navigation, required handler/runtime availability and explicit unavailable/error states. Never execute macros, remove Zone.Identifier/protected-view state automatically, or change the document. State actual format and installed-handler dependencies; opening Office externally is not acceptance. |
| PREVIEW-23 | CsvViewer | Render delimited files as navigable tables with headers/columns, quoted delimiters, embedded newlines, encoding handling and explicit large-file limits. Verify CSV/TSV and the adopted delimiter set; raw source text is not table rendering. |
| PREVIEW-24 | DbViewer | Read-only database type/schema/table inspection and bounded row browsing for the adopted SQLite and LiteDB formats. Verify correct rows/types, locked/encrypted/malformed and large-database states; do not migrate schemas, execute document-supplied queries, modify source/sidecars or acquire avoidable write locks. |
| PREVIEW-25 | MailViewer | Render EML/MSG headers, body and attachment inventory without opening Outlook or executing active content. Preserve HTML isolation; remote images, scripts and automatic attachment opening remain inactive. Verify plain/HTML/multipart and malformed messages with source preservation. |
| PREVIEW-26 | CertViewer | Inspect certificate, certificate-store, signature and provisioning metadata for the adopted donor formats, including public identity, validity, issuer and applicable chain/signature details. Verify protected/malformed inputs and explicit password/unavailable states without persisting secrets. Do not install certificates, export private keys, modify trust stores or imply unperformed online validation. |
| PREVIEW-27 | AppViewer | Structured application-package inspection: identity, version, publisher, package contents and available icon/permissions for the adopted Android, Windows and other donor package families. Verify real representative packages for each declared family; archive listing alone is insufficient. Never install, execute, mount through an unsolicited system action or unpack entire packages merely to preview. |
| PREVIEW-28 | ThumbnailViewer | Render real embedded previews/thumbnails for adopted design formats such as Krita, Paint.NET, Sketch and the other source-qualified donor formats. Record per-format support and missing-thumbnail behavior; a Shell file icon or generic metadata view is not a design thumbnail. Verify malformed packages and bounded extraction with no editor launch or source changes. |
| PREVIEW-29 | MediaInfoViewer | A selectable structured technical report with container, codec, duration, video/audio/subtitle tracks and available color/HDR metadata. Verify displayed values against actual files independently of playback; preserve libmpv playback and Dolby Vision behavior. Playback controls alone do not satisfy this report. |
| PREVIEW-30 | BinaryViewer | Read-only hex/ASCII inspection with correct byte offsets, bounded large-file loading and navigation. Verify empty, malformed/unknown and large files; preserve source bytes and ensure binary inspection does not incorrectly win over a more specific viewer. |
| PREVIEW-31 | ELFViewer | Structured ELF and adopted Mach-O/UImage metadata: format, architecture, headers/sections and available dependencies. Verify real representative inputs for each declared family, truncation and invalid headers; do not load or execute inspected code. |
| PREVIEW-32 | DumpViewer | Structured crash/minidump inspection showing available process, exception, module and thread information with explicit unavailable fields. Verify supported dump variants and truncated/large inputs. Do not imply a stack trace or symbol resolution that was not performed, or initiate automatic symbol/network downloads. |
| PREVIEW-33 | PrefetchViewer | Parse supported Windows Prefetch records into executable, run-count/time and referenced-path information where present. Verify source-qualified compressed/version variants, invalid/truncated inputs and truthful unavailable fields; never execute the referenced application or alter system Prefetch data. |
| PREVIEW-34 | ChmViewer | Read-only embedded CHM topic/content navigation with bounded resource access. Block scripts, external navigation, downloads and active content; verify internal topic links, images, malformed archives and source preservation. Opening the system Help application is not acceptance. |
| PREVIEW-35 | CLSIDViewer | Inspect supported Shell namespace inputs of the form ::{GUID} and show resolved identity/registration metadata. This is a virtual-input viewer, not an invented file extension. Wire actual selection/input into the preview host and verify known, unknown and malformed identifiers without COM activation, registry writes or executing a registered command. |
| PREVIEW-36 | Integrated qualification and delivery | Demonstrate every category through actual complete selection -> rendering/inspection -> navigation -> close workflows, including eligible native/archive and side/Space/widget surfaces; document genuine input/surface exclusions. Verify per-module toggles and restart persistence, deterministic overlaps, active-disable, rapid selection, malformed/large/encrypted/locked/missing-runtime states, cancellation and post-close rename/delete with source hashes unchanged. Inspect themed accessible controls at narrow/normal sizes and100%/150% scaling. Revalidate affected existing previews and HTML/media/3D lifecycle boundaries. S13 closes only after all fourteen specialist outcomes pass, source/license/runtime inventory is complete, one normal release is independently retrieved/qualified and its actual installed update is verified. No per-module intermediate product releases. |
| PREVIEW-37 | Windowed and in-app PiP preview | Add themed accessible mode icons beside sidebar close. Show the actual renderer's app icon on the left of the filename instead of labels such as WordCraft. The icon must switch to LibreOffice when fallback takes over, including after primary failure; expose the engine/recovery reason in its tooltip. Keep sidebar, PiP and Windowed identification consistent. Windowed uses a resizable external floating window and restores the same preview to the side column upon window closure. PiP uses a compact corner overlay inside Pathweft, releases the sidebar column and offers return to sidebar. Retain renderer/navigation/playback/camera state during mode changes. The former pin-while-browsing behavior is explicitly superseded by PREVIEW-38. Keep explicit close-preview distinct from window-close restoration. Verify actual Office/media/3D/HTML/ordinary transfers, restoration, compact/narrow sizing, active-disable, cancellation and app-exit cleanup without duplicate renderers or lost source/archive leases. |
| PREVIEW-38 | Selection-following and explicitly pinned preview hosts | S17: default PiP/Windowed follows selection with updated content/title/icon, preserved host mode/bounds, correct previous renderer/lease teardown and stale-async protection. Explicit pin beside the three header buttons retains the displayed item across browsing/empty selection/host changes; unpin follows current selection immediately. Clear pin on explicit close and expose visible/accessibility state. Unpinned empty selection clears stale content. Preserve mode-switch state/restoration semantics; small glyphs with usable accessible targets. Verify actual replacement/pin/unpin, rapid changes, lease/player cleanup, themes and scaling. |
| PREVIEW-39 | Shortcut details | Previewing a .lnk or .url file shows what it points to instead of its bytes, read from the shortcut itself: target (or the Windows item's name when it has no file path), arguments, start folder, run state, shortcut key, run as administrator, icon and comment; an Internet shortcut shows its address. The target state reads File found, Folder found, Not found at this location, Network location (not checked while previewing) or Web address. Nothing is resolved, launched or repaired by previewing. Open (Open target folder for a folder, which opens it in Pathweft) acts as opening the shortcut; Show target in its folder selects the target in a pane. Neither is offered for a missing target, and a web address has no folder. Empty fields are left out. Owner request 2026-11-14: "Currently .lnk shows as an hexdump"; a Target column was not requested. |

At S13 activation, qualify dependencies and exact formats against the pinned
upstream implementation and available runtime rather than accepting marketing
claims. Reuse the current ten module backends; no second preview host, generic
plugin framework, duplicate playback engine or arbitrary timeout/retry machinery.
Keep optional renderer/handler dependencies explicit. A missing implementation
keeps S13 ACTIVE; an externally unavailable required dependency may justify
parking with the exact unsatisfied criterion, not declaring parity or completion.

### Native archive browsing and editing

| ID | Requirement and acceptance |
| --- | --- |
| ARCHIVE-01 | Integrate a pinned 7-Zip-backed library/SDK into the file manager. Record exact read/write format capabilities, selected source/native dependencies and terms. Verify real ZIP/7z and representative additional supported formats without requiring a standalone archive utility window. |
| ARCHIVE-02 | Browse archive paths/folders through normal panes, tabs, breadcrumbs, selection and sorting, as virtual folders. Open an archive directly and navigate its tree without extracting the whole archive first. Preserve ordinary ZIP browsing and existing file-manager behavior. Verify representative nested paths and honest unsupported/corrupt/encrypted/multipart states. |
| ARCHIVE-03 | Provide rename/copy/cut/paste for writable formats, including filesystem-to-archive, archive-to-filesystem and archive-to-archive workflows. Keep one application action/transfer owner and explicit conflict decisions. Read-only formats expose their actual capabilities; do not imply write support where the backend lacks it. Verify real operations and destination contents through the integrated UI. |
| ARCHIVE-04 | Stage and verify archive edits before publishing a replacement. Failed/canceled writes preserve the original archive; a cut removes its source only after the destination operation is verified. Detect concurrent source changes before replacement. Verify conflicts, interrupted writes, invalid/traversal entry paths and data preservation; never silently extract an entry outside its selected destination. |
| ARCHIVE-05 | Open selected archive entries and integrate eligible internal previews through controlled, entry-bound extraction where needed. Own temporary content until its actual consumer is finished; do not delete files merely because a launcher exited or a delay elapsed. Passwords remain ephemeral/private. Verify opening/previewing actual entries, resource release and reviewed cleanup without executing content during preview. |

### Embedded writable RAR extension (S4R)

User authorization: "Give it a try, you can ... choose how to better integrate
it" (2026-10-29). Use the selected Rust library internally without deciding a
whole-application Rust migration. Full compatibility with every historical RAR
variant is not established by the upstream feature list; unsupported preservation
must be detected and reported rather than silently converting or losing data.

| ID | Requirement and acceptance |
| --- | --- |
| RAR-01 | Pin rars source and dependency lock, record licenses/provenance, and build a native library loaded in process by Pathweft. No WinRAR/rars executable dispatch for application operations. Missing/incompatible native support leaves RAR reading available and editing honestly unavailable. Verify actual loaded DLL identity and reproducible build inputs. |
| RAR-02 | Integrate RAR creation and eligible existing-archive rename/delete/copy/cut/paste through existing panes, shared actions and Ferry queue. Include filesystem-to-RAR, RAR-to-filesystem and archive-to-archive transfers, nested/Unicode/empty directories, conflicts and delete-all. Existing ZIP/7z/TAR behavior must remain verified. New archives use an explicitly recorded modern RAR format. |
| RAR-03 | Preflight source preservation through the pinned library before offering/publishing edits. Qualify supported solid and encrypted single-file archives; preserve their supported names, content, timestamps/attributes, comments and encryption settings. Refuse unsupported metadata, links, ambiguous names, codecs and multipart rewrites with a concrete reason. Do not claim every RAR variant writable; maintain an explicit capability table backed by fixtures. Passwords remain session-only and absent from logs. |
| RAR-04 | Reuse staged verification and atomic replacement: independently decode the staged output with 7-Zip and compare all expected names/content before publication; exercise UnRAR interoperability independently on representative writer outputs. Source/input changes, native failure, cancellation and failed verification preserve originals and retain cut sources. Own native buffers, callbacks and scratch through actual completion; verify no orphan process or locked input/output. |
| RAR-05 | Verify the complete integration through hidden real WPF pane/action/queue workflows, including preview and encrypted password handling, plus focused data-preservation and interoperability contracts. Retain actionable evidence, clean expendable scratch, reconcile RAR-01..05 and commit/push/read back. Final package/source/license/install and downloadable delivery remain S8; no intermediate product release. |
| RAR-06 | Upstream review of 2026-11-23 (rars 0.13.0: reader end-of-member and end-of-archive handling, split-volume boundaries, RAR 2.9-4 writing and RAR 5 decoding fixes, plus a breaking Rust API change): adopt rars 0.13.0 (`f7098c7b9a1e844555daad1d376fd1f043adba2c`) only after qualifying archive read, write, recovery and the packaged DLL; the runtime becomes 0.13.0-pathweft.1, native ABI 2 unchanged, with Pathweft's behavior unchanged (the bridge keeps its operations, JSON and timestamps; RAR 1.5-4 timestamps are copied through rars' own time copy). Acceptance: the DLL is built from the locked sources through the Rust gate and its provenance manifest regenerated; the RAR contracts pass (creation, rename/add/delete, 7-Zip decode, failed publication, delete-all, solid and encrypted rar4/rar5/rar7 rewrites, unsupported refusals, native cancellation, transfers, DLL identity) and new contracts refuse truncated and header-damaged archives without changing their bytes; ARCHIVE-12's RAR writer settings are created and read back in the RAR contracts (Maximum, solid, 3% recovery record found by the native reader, password with encrypted names, 1 MB volumes within their size, decoded with the password; the S4 UI trial is not run because it uses the owner's clipboard); official UnRAR extracts the edited fixtures and that volume set; a missing or incompatible DLL leaves browsing available and editing honestly unavailable; the package ships the qualified DLL and corresponding Rust sources vendored from the new lock (the build refuses vendored sources that differ from the lock). If a criterion fails, the 0.12.3 pin stays. |

### REA-driven transfer improvements

After S4U closure, the user requested the next stage on 2026-10-29, authorizing
S5 investigation, evidence-backed adoption and integrated transfer implementation.
No later stage is activated and final product packaging/publication remains S8.

The user clarified S5 on 2026-10-29: reconstruct the observed TeraCopy workflow
inside Pathweft, without the additional staged overwrites or source locks held
across copying/verification. Copies write directly to their selected destination
using normal native sharing. Cancellation removes the current incomplete or
unverified copied output and retains the source; completed outputs remain, and
the previous contents of an overwritten destination are not restored. Extra
protections may be considered only if a later demonstrated need warrants them;
they are not required tracked deferrals. Source preservation, correct conflict
decisions, verification before copied-move source removal, truthful outcomes and
the metadata/filesystem criteria below still apply.

The user then requested continued comparison and implementation: **"keep
comparing our pipeline against TeraCopy, and implementing the findings, until
there are no more lessons to be learned."** This reopens S5 after its initial
verified closure. Maintain the complete comparison/findings inventory, implement
useful supported findings, and continue investigation when evidence exposes
another actionable difference. Do not treat one faster sample or completion of
the first two optimizations as exhaustion of the objective. Preserve the existing
behavioral contract and record unsupported inferences and rejected experiments.

| ID | Requirement and acceptance |
| --- | --- |
| TRANSFER-01 | Use the installed `reverse-engineer-anything` (REA) skill on an exactly identified TeraCopy artifact before selecting transfer changes. Record version, origin, signature/hash and relevant configuration. Trace scheduling, copy/verification, conflicts, pause/cancel, failure/recovery and progress with retained Evidence IDs. Separate static findings, runtime observations, inferences and unknowns. Neither marketing nor upstream changelogs satisfies this criterion. |
| TRANSFER-02 | Derive a bounded adoption table: TeraCopy behavior, evidence, current gap, implementation decision and acceptance check. Reconstruct useful behavior in Pathweft-owned code without a TeraCopy runtime dependency. Resolve design-critical unknowns before dependent implementation; decompiled pseudocode is not original buildable source. Full TeraCopy feature parity is not implied. |
| TRANSFER-03 | Integrate one queue with coherent conflict decisions, truthful progress/outcomes, pause/resume and cancellation. Failed/skipped/unverified/incomplete members must not be presented as wholly successful. Verify copy, same-volume move, cross-volume move, merge and overwrite through real callers. |
| TRANSFER-04 | Preserve source data on failed/cancelled operations and failed verification; remove moved sources only after required copy/verification succeeds. Use direct destination writes with normal source sharing. Cancellation cleans up the current incomplete/unverified copied output; completed outputs remain and overwritten old destination contents are not restored. Define and verify links/reparse points, timestamps, attributes, alternate streams and filesystem limitations. Report unsupported preservation. Use disposable fixtures for denied access, destination-full/disappearing and changing-source cases. |
| TRANSFER-05 | Compare many-small-file and large-file workloads across the identified TeraCopy version, inherited baseline and changed implementation under equivalent conditions. Record files/bytes, storage/filesystems, caching/verification, wall time, CPU and memory. Choose concurrency/buffering/scheduling from evidence; no invented speedup promise or unmeasured aggregation mechanism. Adopted performance changes need measured benefit without correctness regression. |
| TRANSFER-06 | State and verify the recovery guarantee precisely. Live pause/resume, device interruption and process-restart recovery differ. Select the required behavior from REA findings in the adoption table; never call an in-memory queue durably resumable. Keep user data outside fixtures untouched. |
| TRANSFER-07 | Continue comparing the complete transfer pipeline with the qualified TeraCopy reference and implement evidence-backed lessons. Maintain findings for enumeration/metadata/streams, copy/hash/verification, scheduling/buffers, conflicts/progress/control and failure/recovery. Cover representative small, large and mixed workloads and applicable volume paths. Separate I/O profiles from timing; compare relevant before/after and donor samples under matched conditions, repeat measurements to assess variability, and reverify affected integrated behavior. Every discovered candidate must be implemented and verified, experimentally rejected with evidence, or remain explicitly unresolved; do not declare exhaustion while actionable findings or uninvestigated pipeline differences remain. No new donor runtime dependency, optional protective workflow or later-stage implementation is implied. |

TeraCopy evidence drives transfer design. Read inherited source only for actual
integration points/gaps and preservation of valid behavior; repeated upstream
review is not a substitute for REA.

Implementation decision under the user's existing instruction to match TeraCopy
without optional protective workflows: qualified NTFS fixtures show that the
donor copies compressed and sparse contents into ordinary destination files.
Compression policy and sparse allocation are filesystem limitations to report
under TRANSFER-04, not reasons to reject an otherwise correct copy. Keep source
data, verification, cancellation and basic attributes/timestamps unchanged;
report compression-state differences and lack of a sparse-allocation guarantee.
Encrypted files still require encryption preservation or explicit failure;
encrypted directory policy remains unsupported. This decision replaces the
derived adoption document's earlier blanket storage-attribute failure rule.

Before REA use, read the installed skill, gate/reconstruction references and
`E:/Tools/REA/STORAGE.md`. Use the installed Coordinator workflow and version-pinned
launcher, retaining original sessions through completion. Preserve existing
legacy bindings and unrelated projects. For new storage, retain findings and
validation evidence on D:, use E: for disposable generation except native/build
I/O on D:, and G: for long-term archives. Existing E: bindings are migration
constraints, not permission to reconfigure the runtime. Authenticate exported
Evidence, close owned analysis and verify worker exit before cleaning scratch.

### Pathweft Start menu beside StartAllBack (S6, revised 2026-11-27)

Owner decision 2026-11-27, after the S6 sandbox probe (docs/verification/2026-11-26-s6-startmenu-probe.md): "You have my approval to change S6 according to your recomendation, but then you should add other sub stages to replicate all the settings that can affect the start menu including other visual styles."
The recommendation approved: Pathweft owns the Start menu itself, a native Win7-style menu that replaces only StartAllBack's menu; StartAllBack keeps the taskbar, orb, tray and styling, and its own menu returns whenever Pathweft is not running or the feature is off. This replaces the earlier direction of Everything-backed results inside StartAllBack's own result UI (approach A: its results view refused insertion with E_NOINTERFACE and its private offsets move with every update), and Everything itself, which SEARCH-09 replaced with the Pathweft index. S6 is split into the sub-stages S6a-S6e (plan).

| ID | Requirement and acceptance |
| --- | --- |
| SAB-01 | Evidence before each sub-stage, revised for the owned menu. The 2026-11-26 probe established the opening technique on StartAllBack 3.9.28 (hash-recorded): an unelevated Pathweft process takes a lone Win, Ctrl+Esc and an orb press with low-level keyboard and mouse hooks and the unassigned-key foreground tap, with no Explorer injection, hook or patch, and StartAllBack's menu returns when the process exits. Before S6c/S6d, inventory every StartAllBack setting that affects its Start menu from the installed configuration (Start Menu tab, Welcome presets) and its per-user settings (HKCU\Software\StartIsBack), recording each value's meaning from observed behaviour in an isolated guest; licensing and activation state is never read into a guest or copied. |
| SAB-02 | The owned menu. With the feature on, a lone Win press, Ctrl+Esc and a press on StartAllBack's orb open Pathweft's Start menu at the orb, in the foreground, instead of StartAllBack's; Win key combinations (Win+R, Win+E and the rest) keep working; Escape, the Win key again, a click outside or opening an item closes it. The menu is native WPF in Pathweft's process (no web UI) and has StartAllBack's Win7 layout: pinned programs, recent programs, All Programs (cascading flyout or in-place list, Windows apps folder, sorting, new-app highlight, as configured), the search box, the right column (user picture and name, the configured known folders, This PC, Control Panel, PC settings, Run, Terminal, and the others SAB-07 lists), jump lists on programs that have them, and power and sign-out buttons. Acceptance: in the guest and then in the owner's coordinated trial, each opening path opens Pathweft's menu with physical input, each item opens what Windows' menu would, and Win combinations are unaffected. |
| SAB-03 | Search. Typing in the menu searches programs, Settings and control panel items, and files and folders through the Pathweft index service with its read-only queries (SEARCH-09; no other index), as grouped compact results (Programs, Settings, Documents, Folders, and the other groups StartAllBack shows) with keyboard and mouse selection; See more results opens Pathweft's search with the query. Rapid typing cancels earlier queries; Unicode, spaces, empty results, deleted or offline paths and an unavailable index service are handled and never shown as complete when they are not. |
| SAB-04 | Opt-in and recovery. The menu is a Pathweft setting (Settings card), off by default, with its state visible. It runs only in Pathweft's user process: no Explorer injection, no StartAllBack patch, no changed StartAllBack binaries, licensing or settings. When Pathweft exits, crashes or the setting is turned off, the hooks are gone and StartAllBack's menu opens as before; StartAllBack's "Use enhanced classic start menu" option stays the documented fallback. Explorer restarts and StartAllBack updates keep working because nothing depends on StartAllBack's private code; an orb that cannot be found leaves Win and Ctrl+Esc working and reports it. |
| SAB-05 | Validation and delivery. Each sub-stage is checked first in an isolated Windows Sandbox guest with the same StartAllBack build and the owner's Start-menu settings (no licensing copied), then in a coordinated trial on the owner's machine with physical keyboard and mouse, multiple monitors and DPI as he has them, an Explorer restart, Pathweft exit and the setting off. S6e owns the final release; the completed S8 core release does not satisfy this gate. |
| SAB-06 | Folder routing. Folders and known folders opened from the menu (Documents, Pictures, Music, Videos, Downloads, This PC, recent items, search results, jump-list folders) open in Pathweft with the requested folder and the selected file where applicable; programs, Settings and commands launch as they would from Windows' menu; virtual Shell locations Pathweft cannot render are inventoried and open in their own host. Covered routes open no Explorer window, cold or warm. |
| SAB-07 | Settings parity: everything that affects StartAllBack's Start menu is replicated, read from StartAllBack's own settings (HKCU\Software\StartIsBack, read only) and followed live when they change, so the owned menu looks and behaves as StartAllBack's would with the same settings. Layout and contents (S6c): Start_MinMFU (recent programs count), Start_LargeMFUIcons, Start_LargeAllAppsIcons, Start_LargeSearchIcons, Start_LargeFlyoutIcons, AllProgramsFlyout, Start_AutoCascade, StartMetroAppsFolder, Start_SortOverride, Start_NotifyNewApps, Start_ShowRecentDocs, Start_RightPaneIcons, Start_ShowUser, HideUserFrame, BottomDetails, StartMenuFavorites, the right-column items (Start_ShowMyDocs, Start_ShowMyPics, Start_ShowMyMusic, Start_ShowVideos, Start_ShowDownloads, Start_ShowSkyDrive, Start_ShowNetPlaces, Start_ShowNetConn, Start_ShowMyComputer, Start_ShowControlPanel, Start_ShowPCSettings, Start_AdminToolsRoot, Start_ShowPrinters, Start_ShowSetProgramAccessAndDefaults, Start_ShowTerminal, Start_ShowCommandPrompt, Start_ShowRun, each with its shown, menu or link mode), DriveGrouping, Start_AskCortana, WinkeyFunction, CustomFolders, and the Explorer\Advanced and policy values below. S6c inventory (2026-11-27; StartAllBack 3.9.28 configuration's Start Menu tab, the owner's screenshots of it and his HKCU values, read only): Icon size S, L, M (the combo's order) is Start_LargeMFUIcons 0, 1, 2 (owner M = 2; 16, 32 and 24 pixels, the 24 inferred); Icon count is Start_MinMFU (owner 5); the three "Use large icons" boxes (All Programs, Search, flyouts) are Start_LargeAllAppsIcons, Start_LargeSearchIcons and Start_LargeFlyoutIcons; "Override sort order" with Items first, Folders first and No preference is Start_SortOverride (owner Folders first = 1; the other values' order is inferred and verified in the guest); "Display user account picture" off is HideUserFrame 1; "Display glyph icons" is Start_RightPaneIcons 2; "Use search flyout for more results" is Start_AskCortana; Custom folders... is the CustomFolders subkey. Windows' own Start-menu settings that StartAllBack follows, in HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced: "Power button default action" is Start_PowerButtonAction (Windows 7's encoding: 1 sign out, 2 shut down, 4 restart, 0x10 sleep, 0x40 hibernate, 0x100 switch user, 0x200 lock; the owner's 0x40 shows Hibernate on his StartAllBack, and in a guest without hibernation StartAllBack shows Sign out), overridden by the policy "Change Start Menu power button" (PowerButtonAction under Policies\Explorer, machine then user), the Windows tweak the owner pointed to on 2026-11-27 ("There is a windows tweak that allows changing the default operation in the start menu shut down button"); Start_SearchPrograms (search programs and settings) and Start_SearchFiles (0 no files, 1 without the public folders, 2 with them); Start_TrackProgs and Start_TrackDocs (0: no recent programs, no recent items and jump-list recent items); Start_JumpListItems (else JumpListItems_Maximum) for recent items. Visual styles (S6d): every Start-menu visual style StartAllBack offers (its Remastered 7, Kinda 10 and Proper 11 presets and the installed menu styles, Windows 7 and Plain8), FrameStyle, AlterStyle, RestyleControls, the orb image (OrbBitmap, including the bundled orbs), StartMenuAlpha and StartMenuBlur translucency, CustomColors, the Recolor table and the Hilight, MenuHilight and HotTrackingColor colours, light and dark, at each DPI. A value SAB-01 does not find in the installed build is recorded as absent, not guessed. Acceptance: for each setting and each style, the guest shows StartAllBack's menu and Pathweft's side by side with the same values and they match in layout, items, sizes and colours; changing a value in StartAllBack's configuration changes Pathweft's menu without a restart. |

The user calls this "inject Everything"; the required outcome is native in-menu
grouped file/folder subsets plus a full-results view, not preserving Windows
Search filesystem results and redirecting only See more results. A version-bound runtime interception is an allowed design
candidate for S6, not an established hook or a promise of universal compatibility.
No documented replacement-provider API has been established during planning.
If evidence shows that in-menu integration cannot be implemented safely in the
available supported environment, retain the unmet criteria and record the exact
blocker. Do not silently substitute another UI or redefine the task as a hotkey.
S6 leaves Windows Search services and other consumers unchanged while proving the
in-menu adapter. S7 owns the broader filesystem replacement and the user's revised
Outlook-preserving indexing-scope reduction. Do not modify licensing/activation code.

### Everything filesystem search and Outlook-preserving Windows indexing

| ID | Requirement and acceptance |
| --- | --- |
| EVERYWHERE-01 | Establish a finite, evidence-backed inventory of search surfaces/consumers before changing indexing scope: Pathweft, StartAllBack, Explorer, search-capable common Open/Save dialogs, applicable native Start/taskbar surfaces, and classic Outlook/Office. Record actual host/version, entry point, backend, query/result semantics and acceptance workflow. Inventory current file/protocol crawl-scope rules, Classic/Enhanced mode, Outlook inclusion and relevant account/cache/store search behavior. Identify unavoidable Windows-managed indexing locations and their purpose; do not silently classify ordinary filesystem indexing as an Outlook exception. Unanswered applicability/semantics questions remain gaps. |
| EVERYWHERE-02 | Route inventoried filesystem-search surfaces to the selected Everything engine while retaining native entry points/results/actions wherever feasible. Use bounded source/native adapters and REA for unavailable contracts. Verify typed query -> actual Everything result -> correct selection/open/reveal with the selected filesystem locations excluded from Windows indexing. A separate popup does not replace an existing search location. Record exact supported versions and capability limits; prove the file result backend rather than inferring it from WSearch still running. |
| EVERYWHERE-03 | Reconcile filename/path search separately from file contents/properties, app/settings/commands and mail-store search. Everything owns filename/path search; the user-authorized opt-in content index belongs to S19/CONTENT-01..06 and does not replace that filename engine. Outlook retains its native local Windows Search and applicable server-assisted behavior. Do not promise Everything mailbox search or change Outlook account/search providers. Qualify actual Everything APIs and the distinct S19 content coverage/query semantics at each inventoried surface. Missing required behavior remains a feasibility blocker; no silent substitute engine or new non-mail WSearch dependency. Report Windows-managed exceptions explicitly. |
| EVERYWHERE-04 | After replacement workflows pass, apply a reversible, recorded reduction of WSearch filesystem indexing scope while keeping WSearch available and Microsoft Outlook included. Exclude development/document and other selected filesystem roots, minimize duplicated file indexing and preserve required Outlook protocol/store indexing plus identified Windows-managed exceptions. Preserve actual prior crawl-scope/mode/service configuration and a verified recovery procedure. Use supported configuration APIs/UI and the administrator broker where required. Do not disable the service, delete its catalog, rebuild it by default, change unrelated policies or alter mailbox/user data. This revised plan does not itself authorize an immediate live change during S6. |
| EVERYWHERE-05 | Verify native Everything filesystem workflows and Outlook mail search under the reduced indexing scope: scope readback, actual backend ownership, file index freshness, Unicode, unavailable Everything, host/app restart, reboot/sign-in, updates, rollback and uninstall. Qualify local/offline Outlook search for applicable accounts rather than relying only on server results. Report actual residual filesystem indexing and resource effects without promising zero background work. Restore and verify the prior scope/configuration through the recovery path. Close every inventoried consumer gap; S7 owns final delivered-package and migration/recovery verification after S6, rather than reopening completed S8. |

The user's revised direction supersedes the former S7 requirement to stop/disable
WSearch. Microsoft documents both configurable indexed locations and Outlook's
included-location/service requirements. Keep the distinction between local and
server-assisted mail search; do not attribute every Outlook failure to indexing.
This is a specification change, not live configuration authorization during S6.
Native file-surface feasibility and account-specific Outlook compatibility remain
separate questions. The plan cannot turn either into completed integration.

Primary sources: [Windows indexing scope](https://support.microsoft.com/en-us/windows/experience/performance-optimization/search-indexing-in-windows),
[Outlook indexing/service checks](https://support.microsoft.com/en-gb/outlook/troubleshooting-outlook-search-issues?nochrome=true),
[local versus service search](https://support.microsoft.com/en-us/support/known-issues/outlook-search-might-not-display-recent-emails-on-windows-11).

### Window consistency and update feedback (S14, planned)

| ID | Requirement and acceptance |
| --- | --- |
| WINDOW-01 | Inventory Pathweft-owned auxiliary windows, including Settings, Help, What's new, updater/check/deployment, transfers, search/preview and action/error dialogs. Establish one concrete design contract using Native/Original palettes: consistent typography/hierarchy, margins, content/title alignment, navigation/selected states, cards, buttons/icons, scrollbars and resize/minimum-size behavior. Preserve each window's purpose and existing functionality; do not redesign native OS dialogs or third-party interfaces. Reuse existing resources where appropriate without speculative framework work. |
| WINDOW-02 | Apply that contract to all inventoried windows and verify actual screenshots/behavior in Native light/dark and Original at narrow/normal sizes and100%/150% text/DPI scales. Preserve Help's reading-section highlight, settings tabs, keyboard/focus/accessibility, window ownership/reuse/close and preview state. Resolve every in-scope inconsistency or record its exact blocker; palette changes alone are insufficient. |
| UPDATE-02 | Clicking manual Check for updates in the sidebar or Settings immediately shows nearby visible/accessibility feedback, followed by a clear result: Checking, up to date with installed version, update available with version/action, or check failed with explanation/explicit retry. Use a discoverable themed update surface/in-control status; the main bottom-right status line alone is insufficient. Observe the real task, prevent duplicate concurrent checks and avoid artificial completion delays. Automatic checks stay quiet when unchanged; preserve download/hash/cancel/deployment progress, transfer/editor protection and user-controlled restart. Verify no-update/available/network-failure and progress/result behavior through actual application workflows. |
| UPDATE-03 | Owner direction 2026-11-17: an automatic check that finds a newer release opens no window and takes no focus. It shows a colored (accent) dot on the sidebar's update button, whose tooltip and accessible name name the version. Clicking the button then opens that release's notes and Update now at once, without another check; with no release found it is the normal UPDATE-02 manual check. A successful check that finds no newer release clears the dot. Manual checks keep UPDATE-02. "Later" only closes the window (the dot stays). Verify the dot, tooltip, accessible name, click and clearing through the real check workflow. Delivered with 1.1.73 (S49F). |
| UPDATE-04 | Owner, 2026-11-24, after an update from 1.1.95 to 1.1.96 left 1.1.95 in place: "So it did crash on the other update". Setup ran without a log, and its failure ended with setup's own window and Pathweft closed. Required: the updater starts setup with a log in the updates folder (the last attempt's is kept) and records the version it was started for. An update that could not finish reopens Pathweft once the window that started it has exited (setup's window is shown only when Pathweft cannot reopen). On its next start Pathweft compares its version with the record: an update that was not installed, or installed with a problem, opens the update window with the version, setup's reason (the failure setup logged, with the error behind it, such as a file in use), the end of the log, Open log, Try again (when not installed) and Close. The record is reported once; a light update handing over to the full installer is not a failure. Help describes it. Verified by the light-update scenario (setup's log argument, the reason read from a sample log with a suppressed file-in-use message box, the record read back once, and the update window). |
| TRANSFER-14 | On actual successful transfer completion, automatically dismiss that job through the existing Transfers UI owner and hide the window when no cards remain. Retain active/queued/paused/conflicting and failed/partial/error outcomes; explicit clean cancellation follows TRANSFER-15. Preserve completion notifications, Undo, arrival selection, source/destination refresh and reusable close-to-hide behavior. Use completion events, with no simulated click, delay or polling deadline. Verify real copy/move, multiple jobs and unsuccessful outcomes; successful completion must not hide outstanding work or erase its errors. |
| TRANSFER-15 | The S15 transfer popup amendment in current scope governs compact content-ready sizing, a real virtualized conflict table, clean explicit-cancel dismissal after worker completion, unchanged native clipboard data and repeat Paste, nonduplicated speed, and visible file-specific errors with secondary informational details. Qualify real job actions, isolated native clipboard, multiple jobs, resizing, keyboard, dark/light and100/150% rendering. |
| TRANSFER-16 | S17: differences-first metadata review, newer destination files first in orange with noncolor cue, only differing size/date values rendered at distinguishing precision, explicit matching-file reveal/counts/action scope and unchanged full-set transfer decisions. No extra content reads or identity claim from equal metadata. Verify real conflicts/resolution, compact themes/scaling and final independent/installed release. |
| TRANSFER-17 | S24: one overall progress bar with completed/total file counts at opposite ends; processed bytes beside aggregate speed; separately centered icon/label actions; no empty secondary track or routine verified/unchecked counters. Preserve real transfer verification, active-file context, errors/partial outcomes, conflict decisions, clipboard and completion lifecycle. Actual state/theme/scale/minimum-width and delivered-package checks are required under the current design contract. |
| TRANSFER-18 | Owner report 2026-11-14 (a downloaded PDF failed to copy to Tresorit Drive, while Explorer copied it): when the destination cannot store alternate data streams (FAT, exFAT, Tresorit Drive), a file whose only alternate streams are its download marks (Zone.Identifier, SmartScreen) is copied without them, like Explorer, and the job notes "Downloaded-file marks were not kept: the destination can't store them." The source keeps its marks. Any other alternate stream still refuses that file with a message naming the streams, so data is never dropped silently. Verify both cases on a destination reported as stream-less. |
| WINDOW-03 | Reconcile WINDOW-01/02 and UPDATE-02 against the complete inventory, revalidate affected updater/window lifecycle, then deliver one normal S14 release through the established channel. Independently retrieve the actual package, verify matching source/runtime/notices and packaged workflows, and verify the installed update. No per-window intermediate release or unsolicited system/theme/settings changes. |

### Current-pane disk analysis (S15)

The source assessment in [DISKWEFT-INTEGRATION.md](DISKWEFT-INTEGRATION.md) is now
authorized for implementation within the focused mode below. Import selected
scanner source, not Diskweft's application UI, installation, settings or MCP.

| ID | Required outcome and acceptance |
| --- | --- |
| SPACE-01 | A compact Analyze space toggle beside Terminal uses Pathweft's existing proportions, theme, tooltip, accessible name and keyboard/focus conventions; use a disk/blocks motif reflecting the supplied defrag reference. Analyze only the active pane's current filesystem path. Replace the old external Scan with SpaceScan menu/Help routes with this same action; owner decision 2026-11-19 (spec 1.42): the button is the only route, and the file, folder and background menus have no Analyze space item (it only repeated the button, and on a selection it analyzed the current folder, not the one right-clicked). State unsupported virtual/search/This PC eligibility honestly; do not silently extract archives or scan all drives. |
| SPACE-02 | Morph that pane into a virtualized expandable directory/file tree with Space used totals and root-relative usage bars/percentages, descending size ordering among siblings and stable selection/viewport while totals grow. Publish useful progressive recursive totals before completion. Distinguish pending, measured empty, incomplete/denied, cancelled and failed results. The current donor measures logical file lengths: do not present them as allocated disk clusters or cloud account quota. |
| SPACE-03 | The same toggle restores the prior standard view mode, exact columns/order/widths, sort/direction, path, selection and scroll. Own each pane's root/session, reject old generations, and await actual cancellation/worker completion on exit/navigation/close. Preserve ordinary navigation, selection, preview and real file actions; do not create a second mutation owner or let updates from an old scan overwrite the restored view. |
| SPACE-04 | Borrow and pin selected Diskweft traversal/accounting/progressive source with MIT provenance/notices and upstream-review inventory. Scope mutable state/cancellation/statistics to the scan; do not import shared static donor application state, a new endpoint app, background service or Coordinator/MCP requirement. Do not follow reparse points or read cloud file contents merely for sizing. Avoid duplicate measurement of the same scope through existing SizeCache; promote only complete valid totals under its invalidation contract. Ordinary subtree traversal is required; no automatic raw-volume elevation or unrelated donor search/duplicate/history/reporting features. |
| SPACE-05 | Verify the real nested-folder toggle workflow, exact terminal totals, progressive/largest-first/selection/expansion behavior, cancelled/denied/reparse/Unicode/long paths, actual nested mutation invalidation, two-pane ownership and shutdown; compare actual scan/display overhead and retained memory against the selected donor on the same fixture without promising unmeasured performance. Verify Native light/dark/Original, narrow/normal,100/150% and keyboard/accessibility. Reconcile S11/S14/S15 and deliver one normal release with corresponding sources/notices, independently retrieved actual packaged workflows and installed verification. Preserve the running app and user data until its normal updater/restart. |
| SPACE-09 | Owner request 2026-11-14: expanding a folder in the space analyzer showed nothing until that folder's results arrived. While an expanded folder's members are read, a small progress ring sits between its expander and name (static under reduced motion, named "Loading folder" for accessibility) and the status reads "Loading folder…". Its children appear as soon as they are listed; sizes of unindexed subfolders fill in afterwards. Verify with a held listing: ring and status while held, then cleared with the children present. |
| CORE-19 | For ordinary folder-size refreshes, preserve the last measured displayed value while invalidation/recalculation runs. Atomically replace it when a new valid measurement arrives; use the unknown placeholder only before any measurement exists. Cover repeated background writes, row refresh/reconciliation and summary/selection totals, without accepting invalid/incomplete measurements as completed cache values. The user's continuously changing folder reproduction belongs to S15's sizing integration and final release, not a separate intermediate release. |
| CORE-20 | Prefer Everything's available indexed folder sizes for visible ordinary rows and expanded analysis branches through the existing serialized SDK owner. Detect disabled folder-size indexing and unknown/uncovered paths; use metadata traversal when unavailable without altering Everything settings, requiring an engine upgrade or loading its whole index into Pathweft. Identify indexed coverage in the UI; an indexed total may omit excluded files and is not a verified complete physical-disk scan. Sizes remain logical lengths, not allocated bytes or cloud account quota. Archive sizing remains with its archive owner. |
| CORE-21 | Replace eager in-memory folder-size persistence with a bounded hot cache and indexed on-disk fallback measurements under the existing bulk cache root. Migrate existing text records transactionally without loading them all into RAM, preserve correctness across interrupted migration, and use targeted ancestor/subtree invalidation under the existing measurement publication contract. Avoid full-map snapshots/rewrites and per-record invalidation allocations. Verify actual legacy migration, persistence, concurrency, invalidation, bounded retained memory and unchanged storage relocation; compare the installed-code baseline and corrected data path on the same fixture. |

### Context menu reorganization (S9)

The user's 2026-10-31 request adds S9 to the plan, without starting it during the
S8 appearance correction. It covers the Pathweft context menu; it does not
authorize redesigning Windows' native third-party menu or starting S6/S7.

| ID | Requirement and acceptance |
| --- | --- |
| MENU-01 | Inventory current commands and their eligibility for files, folders, mixed/multiple selections, empty space and archive entries. Reorganize related commands into clear groups/submenus, prioritize common file operations and remove duplicate presentation while preserving supported actions, keyboard access and existing shortcuts. Define the final grouping when S9 becomes ACTIVE using the existing Native/Original design contract. |
| MENU-02 | Preserve the selectable native Windows context-menu path, Shell extension behavior and capability-aware unsupported/read-only states. Verify real open/copy/cut/paste/rename/delete and archive operations through the reorganized menu, keyboard navigation, narrow/small-display placement and both themes at 100%/150%. Publish and verify the completed S9 change through the existing release channel. |
| MENU-03 | Replace bottom command text with familiar Explorer-style icons, keeping names and shortcuts in hover tooltips and accessible names. Recover exact toolbar and View-menu SVG artwork where available; use verified Fluent glyphs for structural controls. Preserve supported actions, state/eligibility, shortcuts and Native/Original styling. Verify recognizable active/disabled states, keyboard/focus access, narrow layouts and 100%/150% rendering in both themes. Native icon recovery is research until S9 becomes ACTIVE; source provenance and distribution/runtime-use decisions must be explicit. Shared final delivery belongs to S11. |
| MENU-04 | PowerShell scripts: a single .ps1 file's menu offers Run with PowerShell at the root and Run as administrator under Tools. Both run `pwsh -NoProfile -File <script>` (PowerShell 7 from its install folder or PATH; otherwise Windows PowerShell with `-ExecutionPolicy RemoteSigned` for that process) in its own console, starting in the script's folder; Run as administrator asks for UAC. Opening the script still edits it, as in Windows. Owner request 2026-11-14: .ps1 files could not be launched from Pathweft, and this PC has no Run with PowerShell verb registered. |

### One organized Pathweft menu (S49)

Owner direction 2026-11-16: Pathweft's right-click menu is the one organized menu. Upstream File Labs promotions
leave the program, the useful entries other programs add to the Windows menu join Pathweft's own groups, and
the full standard Windows menu is one Ctrl+right-click away. Windows' and Explorer's own menu is not changed.
This supersedes MENU-02's "Windows menu…" item; MENU-01/03/04 still apply. Planned first, before S20.

| ID | Requirement and acceptance |
| --- | --- |
| MENU-05 | Remove the upstream sister-app tie-ins: Add to Shelf, Add to Reel, Back up with Backup Labs, Pin/Unpin to Lume, opening Stash/Backup Labs backups (Backups.cs), the Reel header in video previews, reading and writing the ProtagonistLabs ecosystem cards and data files (Sisters.cs, lume.txt, the items list), the "Works with other apps" help section and the paid-app discount line. No protagonistlabs.app link or UTM address remains in the program. Files Pathweft itself wrote for that ecosystem are removed once; other programs' files there are left alone. |
| MENU-06 | Pathweft's file, folder, empty-space and drive menus take in the entries other programs and Windows register for that selection (shell verbs and context-menu handlers: e.g. VS Code, Git GUI/Bash, VLC, Tailscale, Notepad++, Google Drive, Defender scan, WSL), shown with their own text and icon inside Pathweft's groups and invoked through the shell as Windows would. Left out: entries that duplicate a Pathweft command (Open, Open in Pathweft, explore, Copy as path, Pin to Quick access/Home, Open terminal here's cmd/PowerShell, Pathweft's own rename/space analysis once S20 delivers them) and Windows features that are not set up on this PC (Work Folders, Offline Files/Sync Center). The exact duplicate and placement lists come from the inventory taken when S49 becomes ACTIVE (the 2026-11-16 audit lists about 100 entries) and are recorded with the stage. Tresorit and Google Drive keep their existing provider placement. |
| MENU-07 | The "Windows menu…" items are removed. Ctrl+right-click (and Ctrl+Shift+F10 or Ctrl+Menu key from the keyboard) opens the complete standard Windows menu for the same selection, folder background or This PC drive, at the pointer or the focused item, with Rename routed to Pathweft's rename as today. Inside an archive, where Windows has no menu, Ctrl+right-click shows Pathweft's archive menu. The existing native-menu setting keeps working. |
| MENU-08 | Nothing is written to the Windows menu registrations: Explorer's own menu is unchanged. A third-party entry that fails or hangs only fails that command, as in Explorer; building the menu stays fast with all registered handlers present (measured on this PC). |
| MENU-09 | Verify the menus-ui inventory for files, folders, mixed and multiple selections, empty space, drives and archive entries: no sister-app item or link, no duplicate, the taken-in entries present and working (at least VS Code, Git Bash here, VLC and a handler-based entry), Ctrl+right-click and its keyboard equivalents, both themes at 100%/150%. Publish and verify through the normal release channel. |
| MENU-10 | Owner direction 2026-11-17 (after 1.1.72): every taken-in command that opens, plays or edits the selection in a program (Open with Code, Open Git GUI/Bash here, Play with VLC, Open in …) is in one "Open with" submenu with an icon, after Windows' own Open with list (or Pathweft's Open with… dialog), on files, folders and a folder's background; a lone entry is not wrapped. All menu icons (glyphs, Pathweft's icons and other programs' bitmaps) share one 16-DIP slot and centre. A leftover registration of an uninstalled program (ZCode) was removed from the owner's registry at his request; Pathweft itself still writes no menu registrations (MENU-08). |

### Native Jump Lists (S11)

The user's 2026-10-31 screenshots add S11 as planned work: Pathweft's expandable
application entry should expose native pinned and recent/frequent folder
destinations, comparable to Explorer, plus useful tasks as demonstrated by
Directory Opus. This is native Shell Jump List integration, separate from S6's
StartAllBack search-provider work; no host Start menu interaction is authorized
as part of recording this plan.

| ID | Requirement and acceptance |
| --- | --- |
| JUMP-01 | Qualify supported native Shell/WPF Jump List APIs and stable Pathweft application identity. Match Explorer's native Pinned and Frequent category structure in the expandable app entry and taskbar Jump List. Windows owns pinning and frequent ordering; do not imitate these with custom categories or substitute Quick access/Recent folders or Home/Downloads tasks. Preserve Windows-controlled pin/unpin/removal and recent-items privacy preferences; do not overwrite or copy Explorer/Opus lists. Respect removed/rejected items and retain pinned state across updates. Qualify actual native Frequent readback for Pathweft's argument-bearing folder links; a property assignment or renamed custom category alone does not pass. A Tasks-only menu does not satisfy folder-destination acceptance. |
| JUMP-02 | Folder destinations launch Pathweft directly with the correct Unicode/quoted path in cold and running-instance states, independently of the optional default-folder setting. Qualify virtual/offline destinations explicitly and preserve existing open/reveal semantics. Add applicable tasks through existing application actions, not a second manager. Verify actual native Jump List contents and user pin/unpin/open/remove behavior, restart/update/uninstall ownership and final release delivery. StartAllBack is a caller-compatibility check for this native feature, not permission to resume S6 or restart Sandbox. |

Native reference: [Microsoft WPF JumpList](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.jumplist).

### Contextual performance monitoring (S18)

Planned after S16/S24 and before S25/S12; NOT STARTED. Reuse the bottom summary area and
Pathweft's current design. Resource Monitor is a behavioral reference for
process/file attribution, not a requirement to reproduce its entire interface.

| ID | Requirement and acceptance |
| --- | --- |
| MONITOR-01 | Provide a directly accessible Statistics/Performance toggle using the Task Manager Performance graph-in-a-square icon at compact Pathweft proportions, with truthful active state, tooltip and accessible name. Preserve the ordinary file view and existing statistics. Performance presents four compact cards in order: CPU utilization/history; GPU utilization/name/memory with adapter selection where multiple GPUs exist; current-drive read/write throughput/activity; current-folder read/write throughput including descendants while its widget is focused. Retain readable theme, scaling and responsive layout. |
| MONITOR-02 | Clicking a card directly reveals its relevant details in Pathweft-styled tables without an intermediary single-action overflow menu. Drive/folder details attribute activity to Process, PID, File, Read/s, Write/s and Total/s; folder scope follows the current path and descendants. CPU/GPU details explain the displayed utilization through the qualified data source. Preserve current browsing and support returning to the compact cards. |
| MONITOR-03 | Establish actual Windows counter/event sources and required privileges before selecting implementation. Distinguish file-level logical I/O, including cached requests, from physical device activity; do not infer throughput from folder-size changes. Define current volume/device mapping and qualify mapped, virtual/cloud and unavailable sources explicitly. Missing access, unsupported measurements or incomplete/lost observations must be visibly distinguished from zero activity. Verify path attribution, process identity/reuse and navigation scope; assess overhead with actual workloads. |
| MONITOR-04 | Keep lightweight aggregate CPU/GPU and drive/unit I/O sampling active while Pathweft runs, including hidden/tray mode, with one shared bounded in-memory rolling hour and no per-pane duplicate collectors or unbounded disk logs. A newly started session has only observed history; sleep, unavailable sources and collection gaps must not become fabricated zero samples. Background I/O history is drive/unit-level only. Folder I/O, including descendants, is collected only while its widget is focused; focus loss, hiding, scope replacement or exit must release that tracing and mark unsampled periods honestly. Other detailed process/file attribution is demand-driven while its relevant detail view is visible. Reuse existing app/helper ownership; preserve transfers, previews, settings and other consumers. Do not add an independently installed collector service. Establish supported privilege handling and numerical sampling/resource budgets before implementation acceptance. This planning update starts no trace and changes no live configuration. |
| MONITOR-05 | Qualify the complete tray sampling -> reopen with rolling-hour history -> toggle -> focused folder widget -> attributed details -> focus loss/scope change -> return/cleanup workflow against real known CPU/GPU and file read/write activity. Prove the hour retention bound and shared collector ownership, background drive-only I/O, absence of folder tracing when unfocused, and honest session/sleep/missing-sample gaps. Cover cached I/O semantics, other-process activity, multiple GPU/device selection where available, unsupported/access-denied states, independent panes, cancellation/exit and dark/light/scaled layouts. Measure incremental visible/tray CPU and memory costs and establish concrete budgets before implementation acceptance; do not claim resource neutrality from appearance. S18 owns normal release, independent retrieval and applicable installed verification after implementation is separately authorized. |
| MONITOR-06 | Display actual observed history across the available graph width, retaining up to one hour without fabricating missing pre-start samples or bridging unavailable/sleep gaps. Label actual observed time. Keep all five performance cards in one row at the same height as the ordinary statistics cards at supported widths/scales; real chart strokes occupy their graph region, not only its trailing fraction. |
| MONITOR-07 | Add physical RAM usage percentage, used/total bytes and rolling history as a separate card between CPU and GPU. Use Windows physical memory availability, not process working-set sums or dedicated VRAM. Expose demand-driven process working-set details, with unavailable values distinct from zero and no extra background process enumeration. |
| MONITOR-08 | Superseding MONITOR-01/04/05 focus restrictions: automatically collect descendant folder I/O for the currently loaded supported folder while performance is visible, independent of card/window focus, after Windows permission. Request permission once, retain a retry after denial, follow acknowledged navigation scopes and clear old rows/history. Pause folder traces on hide/minimize/tray/statistics; resume the permitted worker on return and terminate it on exit. Preserve bounded logical-I/O attribution, gap/loss reporting and no background folder logging. Qualify actual native writes with the file pane focused and across visibility/navigation transitions. |

Before implementation activation, resolve collector feasibility, privilege handling,
measurement definitions and numerical resource budgets within this existing scope.
No extra monitoring categories or system-management actions are implied.

### UI quality review, refinement and motion (S25)

The user's 2026-11-11 instruction widens S25 from summary-card motion into a
full UI QA review with a performance and design focus, an improvement round and
an animation enrichment round. The direction is elegant, not saturated: soft
transitions between views where they help orientation, never decoration for its
own sake. S25 is COMPLETE in release 1.1.53 (docs/verification/2026-11-11-s25-ui-review.md). Existing behavior, layouts, keyboard paths and the
Settings → Animations / Windows animation-effects switches stay authoritative.

| ID | Requirement and acceptance |
| --- | --- |
| UIQA-01 | Review the actual rendered UI (main window, panes, sidebar, command bar, summary cards, Performance, Maintenance, Settings, dialogs, Quick View, widget) in dark/light themes, 100/150% scale and narrow/two-pane layouts. Record concrete findings with evidence: inconsistent spacing, alignment, typography, color/contrast, iconography, empty/loading/error states, focus visuals and leftover compromises. Each finding is fixed, or explicitly rejected with a reason. |
| UIQA-02 | Review UI-thread performance on ordinary browsing paths: first window frame, folder open and large listings, tab/pane switching, view changes, search, summary/Performance/Maintenance switching and resize. Measure before/after frame and layout/CPU costs on the same machine. Fix measured stalls and avoidable layout/render work; no regressions on any measured path. |
| UIQA-03 | Apply the accepted design improvements through the existing shared theme, palette, icon and layout owners, not per-control overrides. Preserve accessible names, keyboard navigation, high contrast, hit targets and existing user settings. |
| MOTION-01 | Animate summary cards appearing upward as the upper file pane smoothly shrinks to make room, with the reverse transition when cards collapse. Preserve pane scroll, selection, path drafts, keyboard focus, minimum usable dimensions and existing auto-collapse rules. Do not animate routine metric samples or restart layout transitions for every background refresh. |
| MOTION-02 | Rotate the existing summary-card region between Statistics and Performance, preserving each view's state and the same occupied space. Keep focus-only folder collection tied to actual active widget focus, not a temporarily visible transition surface. Handle rapid reversal, resize, pane switching and interruption using actual animation completion/cancellation; no input-blocking delay, duplicate collectors or overlapping interactive faces. |
| MOTION-04 | Add soft transitions between views where they aid orientation: view/tab/page switches (for example Maintenance and Performance tabs, Settings categories, preview content changes and panel show/hide). Use short eased fades or small slides (about 120-220 ms) through the shared Motion owner; no bounce, no continuous or attention-seeking animation, no animation of routine data refresh. Folder navigation must never wait on an animation. |
| MOTION-03 | Honor Windows disabled/reduced animation settings and Settings → Animations with an immediate, fully functional state change. Qualify actual expansion/collapse/rotation, view transitions and interrupted transitions at representative refresh rates, dark/light themes, display scales and narrow/two-pane layouts. Measure frame pacing and layout/CPU costs, retain before/after visual evidence and verify ordinary browsing remains responsive. S25 owns one normal final release with independent retrieval and applicable installed acceptance. |

### Tresorit capability investigation (S12)

The user's 2026-11-02 request adds S12 as planned work. COMPLETE 2026-11-15 as an investigation: the capability
matrix, evidence and decision are in research/S12-TRESORIT.md. Decision: no service or internal integration; the
proposed follow-up TRES-05..08 (provider shell commands in Pathweft's menu, Owner/permanence-aware main-folder
delete, Tresorit deleted-files wording, account-storage label) is issue #8, activated 2026-11-15 as S12b below.
Recording this stage does not activate a client integration, change Tresorit
configuration or authorize deleting cloud content. The reported main-folder
deletion restriction is a provider capability boundary, not an NTFS ACL fix.

| ID | Requirement and acceptance |
| --- | --- |
| TRES-01 | Inventory concrete gaps in the mounted-drive integration: main-folder versus child operations, deletion/recovery semantics, ownership/roles, actionable errors, trustworthy metadata/quota and cloud state. Distinguish verified client behavior, provider restrictions and possible improvements; do not promise unsupported capabilities. |
| TRES-02 | Assess supported public APIs/SDKs and documented client commands first. Verify actual content-operation coverage versus administration/audit-only surfaces, account/tier availability, authentication/scopes, limits and terms. Produce a capability matrix with evidence; availability of a suitable API is an open question. |
| TRES-03 | Where documented surfaces are insufficient, use the installed REA workflow and its storage/Coordinator guidance to inspect the exact signed installed client/version. Examine command/URI/Shell/IPC contracts before internal hooks. Qualify any candidate with bounded runtime evidence; record version coupling, maintenance and unsupported operations. Static analysis alone does not establish feasibility. No automatic Sandbox setup or credential extraction. |
| TRES-04 | Deliver an evidence-backed integration decision and a narrow end-to-end implementation proposal with acceptance, lifecycle, recovery and release criteria. Preserve provider ownership, consent and permanent-delete semantics; never bypass permissions or silently substitute permanent deletion. Use disposable fixtures for later mutation tests and preserve existing cloud data. Any implementation remains separate from this plan-only request. |

### Tresorit Drive integration (S12b)

Activated 2026-11-15 by the owner ("lets go with the plan as you suggested") with the folder-router fix, for
release 1.1.70. It integrates only through the mounted drive and the client's own shell extension (TRES-04):
no S3 gateway, `tresorit-core-interface.exe`, client logs or credentials, and no cloud mutation outside
disposable fixtures the owner creates. A tresor is a folder directly on Tresorit Drive (a main folder).

| ID | Requirement and acceptance |
| --- | --- |
| TRES-05 | On Tresorit Drive, Pathweft's own item menu shows the Tresorit shell extension's commands (Sharing and members, Copy Tresorit Path), as Explorer does, through its registered `TresoritExt` handler, initialized and invoked the way the other adopted extensions are. Only that extension is loaded there; other network drives keep today's exclusion. Verify the verbs are listed (never invoked) for a main folder and a child on the real drive. |
| TRES-06 | Deleting a tresor asks first with Tresorit's terms: only its Owner can delete it, it is then deleted for every member and can't be restored, and a non-Owner can leave it in the Tresorit app. The tresor is deleted as one item by the drive or not at all: Pathweft never walks it to delete its contents first (that would delete them for every member even when the tresor itself is then refused) and never clears its ReadOnly attribute. A refused delete or rename reads "Tresorit only lets the tresor's Owner delete or rename it"; a drive that won't delete a non-empty tresor as a whole says nothing in it was deleted. Verify the refusal and wording on a disposable tresor created by the owner, never on existing content. |
| TRES-07 | Deleting items inside a tresor asks with Tresorit's terms instead of Windows' "permanently delete": they go to Tresorit's deleted files and can be restored in the Tresorit app (Show deleted). Verify with disposable files in a disposable tresor; the owner confirms the restore in the client. |
| TRES-08 | The drive's space is labelled as the Tresorit account's storage (shared tresors count toward it), not a disk. |
| SHELL-05 | When the folder router hands a request back to Windows (Setup is running, a virtual location such as This PC, or several folders without a browser site), it forwards what the Shell gave it (the verb's association element and initialization) to Windows' own folder command, so the calling app never crashes. Verify with the router probe's hand-off check against the built DLLs and a real ShellExecuteEx of This PC through a test-only class registration. |
| SHELL-06 | Owner direction 2026-11-18 (repeated complaint about `PathweftShell-1.1.75-x64.dll`): the native shell handlers have one fixed name, `PathweftShell-x64.dll` and `PathweftShell-x86.dll`, with no version in it. Setup deletes the previous copy; when a Shell caller (Explorer, a browser) still has it loaded, Setup renames it aside (Windows allows renaming a loaded DLL, not replacing it) and schedules it for deletion at the next restart, so no app is closed or restarted. Setup also removes the earlier per-version copies the same way, and uninstall removes every handler file it can. The machine class and Pathweft's per-user registration name the fixed files; a per-user registration written by an earlier version is recognised and rewritten. Verify with the actual installer: a held old handler is moved aside and stays loaded, the new one is installed under the fixed name, per-version copies are gone, and uninstall leaves no handler file; the registration upgrade from a per-version path in an isolated registry; and, after the owner installs the update, the Program Files folder and the class registration. |

### Archive parity (S4P)

Owner direction 2026-11-16 on issue #9 ("Please reach full parity with native behavior"), with the earlier
1.1.71 corrections. Items inside an archive behave like items on disk; ARCHIVE-01..05 still apply.

| ID | Requirement and acceptance |
| --- | --- |
| ARCHIVE-06 | The item and background menus inside an archive offer the disk menus' commands, grouped the same way: Open, Open in new tab/other pane, Open with, Run with PowerShell/as administrator, Edit (several), Compare, Notepad++ (on owned working copies), Quick View, Cut/Copy/Paste/Paste into folder, Rename, Rename all (one staged edit, so names can swap), Delete, Copy/Move to other pane, Copy to, Move to, Group into new folder, Add to Drop bin, Copy path/name, Compress to ZIP/RAR (beside the outermost real archive), Checksum SHA-256 from the entry, New folder/New file, View/Sort/Select/Refresh, Archive tools, Show the archive and Properties. Commands Explorer doesn't offer inside archives (shortcut, Windows menu, Pin, Undo, Delete permanently) are absent. A read-only archive disables write commands with the reason as tooltip. Verify the menu inventory and representative actions. |
| ARCHIVE-07 | Extraction, Ctrl+C into other apps (delay-rendered CF_HDROP) and drag-out run as Transfers jobs: one extraction pass per archive, temporary file beside the target, size check and read-back verification when verification is on, stored CRC-32 checks for edits, Keep both keeps the existing file as "name.old.ext", conflicts name source and target. No separate Extract commands where native integration exists. Verify copy-out, clipboard and drag-out contents. |
| ARCHIVE-08 | An archive inside an archive browses like a folder through an owned working copy; edits to it are written back into the outer archive in the same job, and working copies are released at exit. |
| ARCHIVE-09 | Passwords are asked on demand: before opening, copying or editing encrypted entries, and on opening an archive whose names are encrypted (or whose encrypted entry holds the archive being opened). Cancelling leaves the archive closed. Verify on the encrypted-names fixture. |
| ARCHIVE-10 | Properties (Alt+Enter) inside an archive show name, type, location, size, method, CRC-32, encryption, modified/created, attributes and comment; folders and several items show counts and totals; every selection shows the archive's format, packed and unpacked size, ratio and whether it is solid. |
| ARCHIVE-11 | Owner direction 2026-11-17: the Archive group offers Compress to 7z beside ZIP and RAR, on disk and inside archives, through the same native backend, verified staging and Transfers job as ZIP. Verify a real 7z creation and its content. |
| ARCHIVE-12 | Owner direction 2026-11-17 (after 1.1.73): Archive -> Compress..., on disk and inside archives, opens a native settings dialog before anything is written, as 7-Zip's Add to archive does: the archive's path (Browse...), format (ZIP, 7z, RAR, TAR), compression level (Store..Ultra), method per format (7z LZMA2/LZMA/PPMd/BZip2; ZIP Deflate/Deflate64/BZip2/LZMA/PPMd), dictionary and word size, solid block size (7z; solid or not for RAR), CPU threads, split to volumes (presets or a custom K/M/G size), a password entered twice or shown (AES-256; ZIP may choose ZipCrypto and, as in 7-Zip, takes only printable ASCII passwords, refused in the dialog; 7z and RAR may encrypt file names), a RAR recovery record, and Delete files after compression. Options that don't apply to the chosen format are disabled. The archive goes through the same verified staging and Transfers job as Compress to ZIP: it is read back with the password, every file's decrypted content must match its input, encrypted entries and hidden names are checked, and only then is it published, all volumes or none (7z/ZIP/TAR volumes are name.ext.001..., byte pieces of the verified archive; RAR volumes are name.partN.rar; no volume exceeds the size asked for, recovery record and headers included). RAR dictionary and threads are chosen by the native RAR writer (runtime rars 0.12.3-pathweft.2, ABI 2). Delete after sends the inputs to the Recycle Bin, undoable with Ctrl+Z, only after verified publication, and is unavailable inside archives. The last settings are remembered, never the password or Delete after. Verify through the real menu: each format with its settings read back (7z method, dictionary and solid block; ZIP AES; RAR volumes with recovery), encrypted names unreadable without the password, 1 MB volumes joining into the verified archive, Delete after with Undo, a password mismatch refused, remembered settings without the password, creation from inside an archive, and the dialog at both scales in every theme. |
| TRANSFER-19 | Attributes never fail a copy: when the destination can't keep Read-only, Hidden, System or Archive (Google Drive dropped Archive), the file is copied and the job ends with an orange note naming what wasn't kept. |
| BIN-01 | Recycle Bin: Restore and Delete permanently are enabled only with a selection; Empty Recycle Bin takes everything. |
| SPACE-10 | The space analyzer waits at most 5 seconds for Everything's folder sizes, then measures on disk, so a slow index never leaves it pending. |
| SPACE-11 | Owner report 2026-11-17: clicking the analysis tab's own tab reset it to the folder listing, other tabs could not be visited without losing the analysis, and Delete (key or menu) on a tree row seemed to do nothing. As in Explorer, clicking the tab on screen changes nothing. Switching to another tab (or opening, closing or moving other tabs) keeps the analysis with its tab: its measurement continues in the background, folder sizing waits for it, and returning shows the same rows, expansion, selection and scroll without measuring again. Closing the tab ends it. Changes made while the tab is in the background are picked up by F5. Deleting a row through Pathweft (Delete, Shift+Delete, the menu) removes it and its size, files and folders from every folder above it at once, selects the row that moved into its place, then re-measures that branch quietly. Verify with the real tab buttons and context-menu Delete on an owned fixture. |
| ARCHIVE-13 | Owner direction 2026-11-18 (with S20): "the new Compress... has no icon" and "remove Nanazip from the extended context menu since all its features should already be supported by Compress...". Every command in the Archive group, on disk and inside archives, carries an icon in Pathweft's own command style. Before NanaZip leaves Pathweft's menu, the Archive group covers what NanaZip's menu offers for a selection: on an archive file on disk, Open archive (browses into it), Extract files... (choose the destination folder), Extract here, Extract to "<name>\" and Test archive; on any selection, Compress and email (ZIP or 7z, then the default MAPI mail client's new message with the archive attached, as NanaZip does) and a Checksum submenu with CRC-32, CRC-64, MD5, SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256, XXH64 and XXH3-64, plus all of them at once (a value copied to the clipboard is still compared). Extraction runs as a Transfers job with the archive's password, conflict choices, verification and cancellation of copying archive entries out; Test archive reads every entry and reports what fails its stored check. Pathweft's own menus (More actions) then leave out NanaZip's packaged command; Windows' own menu (Ctrl+right-click) and Explorer are unchanged. NanaZip's long tail of hashers (GOST, Whirlpool, EDON-R and others) is not part of Pathweft. Verify through the real menu on owned fixtures: each extract choice with a password-protected archive, Test archive on a good and a corrupted archive, every checksum against NanaZipC on the same file, Compress and email up to the MAPI call (no message is sent), and NanaZip absent from Pathweft's menu while installed. |
| ARCHIVE-14 | Owner direction 2026-11-18: Compress... "is too long". The dialog uses two columns as 7-Zip's Add to archive does: the archive path across the top, the format, level, method, dictionary, word, solid block, threads and volumes on the left, encryption and options on the right, and Cancel/Compress at the bottom, with no scrolling at 100% and 150% in every theme. Behaviour, remembered settings and refusals are those of ARCHIVE-12. |

### Selected-folder content indexing (S19)

Planned after S21 and before S22 (owner order 2026-11-16: S49 -> S20 -> S21 -> S19); NOT STARTED. Because S21 now comes first, S19 also adds content queries to S21's saved searches (BROWSE-03's content-query part). Everything remains the
filename/path engine. Content indexing is opt-in for selected folders and is a
separate capability. This planning request does not authorize extracting user
documents, hydrating cloud files, starting indexers or installing components.

| ID | Requirement and acceptance |
| --- | --- |
| CONTENT-01 | Add a directly accessible toolbar control to enable/disable content indexing for the current folder and descendants, with discoverable scope/progress and explicit supported-format coverage. A small magnifier badge at the file icon corner means extraction/index publication succeeded and the file is current under the qualified change-detection contract. Pending, changed, unsupported, failed and unavailable observations must not falsely show successful current indexing; explain state through accessible text/tooltips. Disabling removes that scope's indexed data without modifying source files; preserve files still included by another selected scope. |
| CONTENT-02 | Use existing bundled source-built Craft cores through owned extraction boundaries where actual text extraction is qualified, and direct streaming readers for plain text, Markdown, JSON/JSONL and other individually supported text types. Derive advertised support from verified extractors, not file extensions or successful preview rendering alone. Preserve native page/sheet/slide/paragraph locations where physical line numbers do not apply; retain line numbers and suitable offsets for text. Distinguish searchable strings/tokenization from any proposed Strings executable; no external Strings dependency or particular extractor is assumed qualified. Preserve originals and existing macro/script/network isolation. |
| CONTENT-03 | Retain a compact searchable strings/terms index with file identity/path, source metadata and arrays of matching source locations; do not retain full extracted document text or require offline snippets. Offline searches may return indexed file/location matches; obtain surrounding text from the original only when requested/available. Qualify SQLite FTS5 contentless storage as the leading candidate, including actual runtime support, terms/literal/phrase/Unicode behavior and location mapping; use measured corpus evidence before selecting another engine. Keep the rebuildable index in Pathweft's configured data/cache location under the existing storage policy, with bounded memory and no corpus-wide in-memory text copy. |
| CONTENT-04 | Once implementation is authorized, initial indexing reads the selected supported files and may fetch their content through the existing cloud provider. Indexed originals need not remain locally available: do not pin them offline, delete cloud/source files or force cache eviction. Periodically reconcile selected folders and incrementally re-extract only new/changed files; combine timestamp and size with stable provider revision/identity where available, qualifying providers with unreliable metadata. Handle renames/deletions, disconnected roots, in-flight writes, missed notifications and restart catch-up without classifying unavailable files as deleted. Metadata equality is a change-detection heuristic, not proof of byte identity. Keep prior failed-refresh results explicitly stale and publish only complete valid replacements. |
| CONTENT-05 | Expose content search through Pathweft's existing browsing/search workflow with clear selected-root coverage, freshness and filename-versus-content semantics. Return original file identity and matching line/page/sheet/cell/slide/paragraph locations; navigate via the appropriate original-file viewer/open boundary where supported. A cached index can answer while its source is offline, but never claim the original is accessible or the result is freshly verified. Do not silently hydrate files merely to draw result rows. |
| CONTENT-06 | Qualify a real folder-toggle -> extraction -> persisted strings/location index -> query -> original-location workflow, including current badges, changed/deleted/renamed files, mixed supported/unsupported formats, large/actively appended JSONL, extraction failure, offline/cloud-cache independence and restart recovery. Measure index size, query/update latency and peak/idle CPU/memory on representative corpora; establish concrete resource budgets and bounded background ownership before acceptance. Verify settings scope, overlapping roots, disable/purge, cancellation/app-exit cleanup, themes/scaling and source preservation. S19 owns normal release, independent retrieval and applicable installed verification after implementation is separately authorized. |

Owner decisions 2026-11-19 (spec 1.44; supersedes the 1.43 split and store bullets, which read the owner's
storage answer as choosing who owns the store, a reading he corrected: "I selected were the information would be
stored but not how", and "I understand that this index is also Pathweft"). Owner's words, relayed by the index
service thread: "S19 should move the installer and its dependencies inside ... "C:\Program Files\Pathweft\services\file_index\"
in case I add more features in the future ... Once that is corrected, the database with the file paths and the file
contents should be stored together. For speed C: makes more sense if we can keep, as per our design, the contents
index under control."
- **Install location (S19a).** The index service ships and is registered in place from `{app}\services\file_index\`
  (the `services` folder is for future Pathweft services). Setup stops the service before replacing its files and
  `install` starts it again; an earlier registration in `%ProgramFiles%\PathweftIndex` is moved there and that copy
  removed; `{app}\index` is deleted on update. The service name and pipe (`PathweftIndex`) are unchanged.
- **Store (S19b).** The terms/locations index (CONTENT-03) is stored with the file-path index, owned and queried by
  the index service on C: (its ProgramData folder, SYSTEM/Administrators only), and kept under control: only the
  selected scopes, terms and location arrays, no extracted text, its size reported in status and Settings. `content:`
  is evaluated in the same service query as name, path and date filters, for every client; content hits are
  filtered by the caller's read access. This replaces CONTENT-03's "Pathweft's configured data/cache location".
- **Split by where it runs.** Service: change tracking, per-scope queue and staleness, the store and content
  queries. Pathweft (as the user): the extraction worker, which reads (and hydrates) files and pushes terms with
  locations to the service; the scope toggle, badges, Settings and results UI.
- **Cloud folders.** Google Drive (H:, J:) and Tresorit (T:) folders can be content-indexed when the owner enables them; the first read fetches each file through the provider (CONTENT-04). The service still never reads contents.
- **Extraction.** An on-demand worker mode of Pathweft.exe (like the Office preview worker), below-normal priority, in a job with a memory cap, per-file timeout; it exits when its queue is empty. Text readers for plain text, Markdown, JSON/JSONL, CSV/TSV, logs and source code; the Craft cores' qualified text extraction (the Office worker's `extract` action) for Word, Excel, PowerPoint and PDF formats.
- **Query.** A `content:` term in the address search (words, prefixes with `*`, "phrases"). Other terms keep their filename/path meaning, in the same service query. Results show the matching locations (line, page, sheet, slide, paragraph).
- **Protocol (S19b, index-service IDX-15..19, PROTOCOL.md "Version 2").** The service reports `protocol` 2 in `status`; Pathweft shows the content feature (toolbar button, Settings section) only then, so a version-1 service hides it. Ops: `content_scope_add`/`content_scope_remove`/`content_scopes`; `content_next` (leases of 10 minutes), `content_put` (chunks of at most 64 KiB, at most 64 MiB of text per file; current only when the read stamp, the stamp after reading and the file's present size and modified time all agree), `content_fail` (failed or unsupported); `content_state` for a folder's children; `content_status` (per-scope counts, store size, files waiting); `query` with `locations: N`. Text locations are `{"line","column","offset"}` (from 1, byte offset); Craft locations keep their page/sheet/cell/slide/paragraph fields with `"engine":1` (counted from 0). Moves are matched by the service (extension, size and modified time, a unique match); there is no file-reference column.
- **Toggle and results (Pathweft, S19b).** Index contents on the command bar indexes the folder shown and its subfolders; on an indexed folder it stops (no confirmation: only index data is removed, never files); inside an indexed folder it is disabled and names the covering folder. Search results for a `content:` query show up to 5 match locations; a text file's context menu offers Open at line for each matching line (the built-in editor), other formats open normally.
- Post-S19 options from the draft, not S19 requirements: SHA-256 dedup of extracted documents, the shared hash cache, `dupe:`.

No implementation criterion is satisfied yet. Extractor APIs, search granularity,
provider metadata reliability and resource budgets remain to qualify in this scope.
There are no tracked deferrals. No system-wide index or search-service change is
authorized by registration of this stage.

### Integrated PowerToys file tools (S20)

Released in 1.1.76 (COMPLETE). Scope is the seven capabilities
accepted in the brainstorming discussion, with New+ last in implementation
priority. This registration does not activate implementation or any live action.
Use the [PowerToys source](https://github.com/microsoft/PowerToys/tree/main/src/modules)
as a donor, not a separately installed product or a promise of drop-in libraries.

| ID | Requirement and acceptance |
| --- | --- |
| TOOLS-01 | Upgrade the existing batch-rename workflow using selected PowerRename source for regex/literal replacement, case conversion, configurable numbering and date substitutions. Keep current pattern workflows compatible, preview old/new names and invalid/colliding results before applying, and integrate actual rename execution, failure handling and Undo with Pathweft's existing owner. Qualify cancellation, name cycles/case-only changes and representative Unicode/path cases without silently substituting the donor's independent undo/settings ownership. |
| TOOLS-02 | Integrate File Locksmith's process/file-use discovery into selected-file/folder context actions and relevant failed transfer/delete details. Show accessible process identity and affected paths, explicit access/privilege limitations, refresh/cancellation and actionable diagnostics. Qualification must identify a real controlled file holder and its release. Process termination, forced handle closure and automatic elevation are not included in the accepted discovery scope. |
| TOOLS-03 | Integrate Image Resizer as a selected-image batch action with size presets/custom dimensions, proportional fit/fill/stretch choices, quality and explicit output destination/naming. Preserve originals by default; replacement must be an explicit choice using existing conflict/operation handling. Qualify actual dimensions/output decoding, orientation, metadata options, errors/cancellation and source preservation on advertised formats. |
| TOOLS-04 | Integrate Text Extractor/OCR into image previews, including selection of a preview region and explicit text-copy action. Qualify supported input conversion, region coordinates/scaling and actual language availability; clearly report missing Windows OCR capabilities/language packs instead of installing them silently or claiming bundled language coverage. OCR output is recognized text, not guaranteed exact transcription. Keep broader automatic OCR indexing outside S19's already defined extractor coverage unless separately requested. |
| TOOLS-05 | Integrate Registry Preview parsing/presentation for selected .reg files as a structured key/value tree in the existing preview hosts, with source-text access and useful invalid-content errors. Preserve file contents and preview isolation; registry import/write and changing the default .reg association are outside this preview scope. Qualify actual supported syntax, large/malformed input, selection replacement and PiP/Windowed lifecycle. |
| TOOLS-06 | Integrate the deterministic Advanced Paste subset as explicit Paste as file actions for clipboard text, HTML and images, producing .txt/.html/.png files in the current writable folder. Qualify actual native clipboard formats, destination naming/conflicts, repeat invocation, clipboard preservation and Undo; preserve existing file Copy/Cut/Paste behavior. AI providers, model installation, media transcoding, global clipboard hooks/history and automatic clipboard transformation are outside this scope. |
| TOOLS-07 | Add lower-priority New+ file/folder templates to existing New File/New Folder workflows, including a configurable template directory, folder structures and supported filename variables. Qualify template discovery, actual creation/rename selection, conflicts, failure/cancellation and Undo without launching template content or changing Explorer's New menu. Reuse Pathweft storage policy and do not require PowerToys settings or template locations. Lower priority is execution order, not a deferral or omission from S20 acceptance. |
| TOOLS-08 | Pin exact donor source/dependency revisions and retain required notices/distribution materials. Inspect each module's actual build/runtime coupling before extraction: existing PowerRename static-library separation does not establish a standalone supported ABI. Use focused bundled native DLLs or managed assemblies as appropriate and existing owned workers where needed; keep one Pathweft settings, selection, command and operation owner. No standalone PowerToys installation, runner, shell-extension registration, telemetry infrastructure, global hooks or endpoint/MCP dependency. Do not introduce a generalized plugin framework. Preserve current theme/proportions, contextual eligibility, accessible names and source/clipboard data. |
| TOOLS-10 | Owner direction 2026-11-18: "make stylized consistent design for all the new features". Every S20 command, dialog, preview and result uses Pathweft's existing look: its command icons and glyph style in menus, the auxiliary window chrome, fields, actions and margins of the existing dialogs (Compress..., Rename all...), the preview hosts' themes, and the same dark, light and original themes at 100% and 150%. No PowerToys or WinUI styling is carried over. Verify each new surface's rendering in every theme at both scales alongside its workflow checks. |
| TOOLS-11 | Owner report 2026-11-19 ("it doesn't feel native... the folder has that under More actions > PowerRename"; "the menus are wired at the file level but not at the folder level"): a folder's background menu offers the integrated tools for the folder itself, in Tools: Rename all… (everything the folder shows, through the TOOLS-01 dialog, Undo and failure handling), Resize images… (the pictures it holds) and What's using this? (the folder). PowerToys' own PowerRename, File Locksmith and Image Resizer entries are left out of every Pathweft menu, as NanaZip's are, so each tool is offered once under Pathweft's name. Verify the background menu's entries and icons, the absence of the PowerToys entries on the file, folder and background menus with PowerToys installed, and Explorer parity otherwise unchanged. |
| TOOLS-09 | Verify all seven complete integrated workflows against actual borrowed implementations, including applicable file/clipboard/Windows boundaries, errors, cancellation, Undo, preview replacement/host transitions and app-exit cleanup. Record exact supported scope and measure added package size, invocation latency and idle/peak resources before making footprint/performance claims. No capability is accepted solely from an upstream feature list or isolated library test. S20 owns normal final release, corresponding sources/notices, independent retrieval and applicable installed verification once implementation is separately authorized. No per-tool intermediate release. |

Initial source review: [PowerRename](https://github.com/microsoft/PowerToys/tree/main/src/modules/powerrename),
[File Locksmith](https://github.com/microsoft/PowerToys/tree/main/src/modules/FileLocksmith),
[Image Resizer](https://github.com/microsoft/PowerToys/tree/main/src/modules/imageresizer),
[OCR](https://github.com/microsoft/PowerToys/tree/main/src/modules/PowerOCR),
[Registry Preview](https://github.com/microsoft/PowerToys/tree/main/src/modules/registrypreview),
[Advanced Paste](https://github.com/microsoft/PowerToys/tree/main/src/modules/AdvancedPaste),
[New+](https://github.com/microsoft/PowerToys/tree/main/src/modules/NewPlus),
and [license](https://github.com/microsoft/PowerToys/blob/main/LICENSE).
These main-branch links are research pointers, not adopted pins. No implementation
criterion has passed; module extraction, runtime compatibility and resource costs
remain unqualified. No tracked deferral or ACTIVE stage is introduced.

### Details columns, file hashes and Copy details (S50)

Owner requests 2026-11-18, authorized for the release after 1.1.76 and ahead of S21:
"I would like to be able add more columns to the disk view like the modified date";
"When I right click in the column header I just get usual folder context menu instead
of a table customization menu"; "I also requested to be able to render the file hashes,
at least the CRC32 for sure"; HashTab-like hashes as columns; "a hash resolution cache
per file"; hash and attribute entries under Copy details shown as `Copy <name>`, and a
`Copy...` popup to choose among all available properties and the output format. Agreed
design: the column is the hashing switch, column sets are kept per view kind, only
on-screen tabs hash, and one cache and queue serve the whole app.

| ID | Requirement and acceptance |
| --- | --- |
| COLS-01 | Right-clicking a column header in Details (folders, search results, archives and the space analyzer) opens Pathweft's column menu, never the folder's context menu: Size column to fit, Size all columns to fit, then checkable columns with Name fixed, then More... . Keyboard and accessible names as other Pathweft menus. |
| COLS-02 | Common columns: Date modified, Type, Size, Date created, Date accessed, Attributes, Version, Owner and the hash columns. More... opens a two-column Pathweft dialog listing every Windows property column (property system descriptions, grouped, searchable) to add or remove, as Explorer's Choose Details. Values come from the shell property store without opening file content beyond what the property handler reads. |
| COLS-03 | Chosen columns, order and widths are remembered per view kind (folders, archives, search, space analyzer, This PC), as Explorer keeps one layout per folder type, and apply at once to every open tab of that kind. A column that no row can fill stays shown when the owner chose it. |
| COLS-04 | The space analyzer offers the same column menu, with Date modified, Date created, Type, Version, file and folder counts and hashes beside Space used; its rows keep SPACE-02 ordering and progressive totals. |
| HASH-01 | Hash columns use the Checksum algorithms (CRC-32, CRC-64, MD5, SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256 where Windows has it, XXH64, XXH3-64), chosen from a Hashes submenu of the column menu, with an upper/lower case option. Inside archives CRC-32 comes from the stored values without reading data. |
| HASH-02 | Each file is read once for all chosen hashes. Only tabs on screen hash: the active tab first, its visible rows first, then the rest of its folder; background tabs read nothing and show cached values. One low-priority app-wide queue reads one file at a time per physical disk. Leaving a folder or removing the column stops its work. |
| HASH-03 | Automatic hashing skips files above a configurable size (default 256 MB, the cell offers Hash), cloud placeholders (never hydrated; shown as online-only) and network locations; the user can still hash them explicitly. |
| HASH-04 | A persistent cache in Pathweft's data folder keyed by volume serial, file ID, size and last write time holds every computed hash of a file; a changed size or time recomputes, a rename or move on the volume keeps it. It is capped (default 100 MB, least recently used pruned first), never writes into files or alternate streams, shows its size in Settings with Clear, and is shared with the Checksum command. |
| COPY-01 | Copy details lists Copy path, Copy name, then `Copy <column>` for each column shown in that view (for example Copy size, Copy date modified, Copy CRC-32, Copy SHA-1), then Copy... . One file copies the value; several copy one line per file (hashes as `hash *name`). A hash not yet computed is computed through the transfer popup, then copied. Same in archives and the space analyzer. |
| COPY-02 | Copy... opens a two-column Pathweft dialog: left, every available property grouped (basic, hashes, version, media, document and the Windows property list) with search and check boxes, starting from the visible columns; right, the output format (Name: value blocks, values per line, tab-separated, CSV, JSON, Markdown table, checksum file), header row, relative or full paths, hash case, and a live preview of the first files. Copy writes the clipboard; choices are remembered. |
| COLS-05 | TOOLS-10 design applies: Pathweft glyphs, dialog chrome and themes (dark, light, original at 100% and 150%). Verify the header menu routing, column persistence across tabs, More... and Copy... renders, hashing order and per-disk queue, cache hits without reads (file-read counts), size and placeholder limits, archive CRC, Copy details entries and every Copy... format on owned fixtures, then one normal release. |
| TABS-08 | Owner request 2026-11-18 ("can we add to the tabs the duplicate context menu, and change the plus behavior to open pointing to `This PC`"): a tab's right-click menu starts with Duplicate, which opens a second, unpinned tab at the same location beside it and makes it active; the + button and Ctrl+T open a new tab at This PC instead of a copy of the current folder (Explorer's new tab also starts at a fixed place). Callers that open a tab for a known folder (Open in new tab, middle click, reopen closed tab, Show in folder) still go straight there. Verify Duplicate's location, position, pin state and activation, and + / Ctrl+T at This PC. |
| TABS-09 | Owner request 2026-11-27 ("Can we move the open new tab to always be at the right of the last tab instead of floting in the header?"): the new-tab + sits right after the last tab, on the last tab's row, whether the tabs fit in one row or wrap into several; the room it needs is still kept, so it never covers a tab or the window buttons. Acceptance: the preview check opens enough tabs to wrap and the + is at the last tab's right edge on its row, with one row and with several. |

### Browsing productivity and smart destinations (S21)

ACTIVE since 2026-11-19 (owner: "You may start with S21"); after S20, before S19. The user registered the Linux
comparison's suggested features and the agreed browser-style address dropdown
as one future stage. This extends existing browsing rather than replacing its
working commands or importing a whole donor application. Scope registration
does not activate implementation or authorize changes to source/user/remote data.

| ID | Requirement and acceptance |
| --- | --- |
| BROWSE-01 | Extend the existing address field and suggestion popup with recent folders, pinned folders, already-open tabs, saved searches and filesystem path completions. Match folder names and full paths without requiring an absolute prefix, highlight matches and identify each result type with its icon/action and location. Rank strong matches first and then recency, deduplicate equivalent destinations without losing a distinct switch-to-tab action, and show recent destinations when the focused field is empty. Reuse existing history/pins/tab state; cached suggestions must not trigger recursive disk scans or cloud hydration on every keystroke. Preserve explicit filesystem completion as a supplement when a path is being entered; qualify matching/ranking against representative Unicode/UNC/archive inputs. |
| BROWSE-02 | Preserve keyboard/mouse behavior for the unified dropdown: Up/Down selects, Tab completes an applicable path, Enter activates the selected destination/tab/query and Escape dismisses suggestions without discarding the draft. A history-row remove control removes only that history entry, never source files, pins or open tabs; integrate clear-history/privacy behavior consistently. Background listing/sizing/search completions and popup interaction must not overwrite typed text, caret, selection or focus; obsolete work cannot replace current suggestions. Qualify actual editing, selection, navigation/tab activation, history removal, restart persistence, keyboard accessibility and light/dark/scaled layouts. |
| BROWSE-03 | Add named saved searches/smart destinations with persisted query, scope and relevant match options, reachable from the existing sidebar/address suggestions. Opening reruns the query against its qualified engine instead of storing a frozen result list. Support editing/renaming/removing the saved query without deleting files. Preserve Everything filename/path semantics and separately identify S19 content searches, selected-root coverage and unavailable/offline sources; content-query support depends on S19's verified capability. Qualify actual save -> restart -> reopen -> changed results and option/scope round trips. |
| BROWSE-04 | Add a user-configurable context-action editor backed by a documented configuration file, reusing Pathweft's configuration approach where appropriate. Actions specify name, icon, optional submenu, executable/arguments and applicability by file type and selection count, with explicit selected-path/current-folder placeholders. Qualify correct argument boundaries for spaces/Unicode/metacharacters, multiple selections, working directory, command errors and owned process lifecycle. Actions run only on explicit invocation; configuration is not automatic execution or a second folder-route registry owner. Integrate actions into the existing command/menu eligibility and presentation without a general-purpose plugin framework. |
| BROWSE-05 | Extend current folder-specific sorting/date grouping into complete per-folder view profiles: view mode, displayed columns/order/widths, grouping, folder-first/hidden visibility and preview preferences. Provide explicit current-folder, descendant and default application semantics, preserving unconfigured folders and user-selected global defaults. Resolve inheritance/reset behavior before implementation; do not let a saved profile silently change unrelated folders or unexpectedly persist temporary disk-analysis mode. Qualify navigation/restart, two-pane/tab independence, defaults/descendants, dynamic empty columns and exact restoration of existing modes. |
| BROWSE-06 | Extend the immediate-entry metadata comparison into recursive folder comparison with optional content comparison and a reviewed synchronization plan. Show proposed copy/replacement/deletion directions and conflicts before mutation, let users exclude/change operations, and distinguish metadata matches from verified content equality. Reuse existing transfer/conflict/Undo ownership and preserve newer destinations and source data under explicit choices; synchronization is manual and reviewed, not an unrequested background mirroring service. Qualify nested differences, equal-size changed content, conflicts, cancellation/partial failure, existing link traversal policy and actual applied outcomes on controlled fixtures. |
| BROWSE-07 | Add file/folder tags, ratings and comments with readable editing/display and filtering/search integration where qualified. Select and document metadata storage/identity behavior before implementation, including moves/renames, disconnected/cloud roots, unsupported filesystems and cross-machine portability. Do not claim universal provider synchronization or silently write alternate streams/sidecars into user content. Qualify actual persistence, mutation/restart, search and unavailable/stale cases under the selected explicit storage contract. |
| BROWSE-08 | Add an optional embedded terminal panel synchronized with the active local folder, including terminal directory changes navigating the browser when supported. Preserve the existing external-terminal shortcut and keep terminal sessions/processes owned across pane/tab switches, hide/close and application exit. Qualify actual terminal hosting, shell directory-change signals, quoting and feedback-loop prevention; display unsupported remote/archive contexts honestly. Do not substitute command injection or inferred prompt text for a qualified directory synchronization contract. |
| BROWSE-09 | Add direct saved SSH/SFTP and WebDAV connections within normal browsing, complementing existing UNC/mapped drives. Qualify protocol/library coverage, authentication/credential storage, SSH host identity/TLS validation, remote path/metadata semantics, listing/preview/transfers and cancellation/disconnection/recovery before advertising support. Preserve existing transfer/source-conflict handling and provider permission boundaries; do not pretend remote items have local paths or local Undo guarantees. Use controlled endpoints for mutation acceptance, never user servers as disposable fixtures. No FTP/NFS expansion is implied by the comparison examples. |
| BROWSE-10 | Add an explicitly invoked temporary live filter of entries in the current listing, distinct from recursive Everything search. Keep the unified address/search design; no permanently added second search bar. Make scope/state clear, retain hidden-file policy, and restore the unfiltered listing/selection appropriately when dismissed. Qualify typing/keyboard/cancellation, navigation, active background refresh and interaction with search/archive/analysis modes without silently changing recursive search semantics. |
| BROWSE-11 | Add contextual symbolic-link, hard-link and directory-junction creation with target inspection and honest type/eligibility, separate from existing .lnk shortcuts. Qualify actual Windows/filesystem/privilege/volume support, relative versus absolute targets, conflicts and broken links. Preserve existing non-following traversal/deletion policies; removing a link must not remove its target. Reuse current operation/Undo ownership where applicable and clearly report unsupported combinations instead of substituting copied contents or .lnk files. |
| BROWSE-12 | Verify every complete registered workflow through its real UI/state/filesystem/terminal/protocol boundaries, including coexistence with S19/S20, errors/cancellation, restart/privacy, source preservation and app-exit cleanup. Inspect any donor's actual code, dependency/license/runtime fit and retain exact adopted pins/notices; Linux feature parity does not imply importing KDE/GNOME runtime infrastructure. Establish measured resource budgets for suggestions, metadata, remote and terminal work; no unbounded corpus/connection polling or performance claims from feature lists. S21 owns one normal final release, corresponding source/notices, independent retrieval and applicable installed verification after implementation is separately authorized. No per-feature intermediate release. |

#### S21 design decisions (2026-11-19, owner start "You may start with S21")

- **BROWSE-01/02 sources.** One suggestion list fed by the open tabs of both panes
  (switch-to-tab action), every Quick access list (pinned), `RecentFolders`,
  saved searches and the existing filesystem completion. Matching is
  case-insensitive on the folder name and the full path, no absolute prefix
  needed; rank: name prefix, name substring, path substring, then kind order
  (tab, pinned, saved search, recent, completion) and recency. Equal paths
  collapse into one row, except that an open tab keeps its own switch row. An
  empty focused field lists recent destinations. Only completion touches the
  disk (one directory listing per typed parent, as today); every other source
  is in memory. A recent row has a remove button that removes only that history
  entry; Settings > Privacy gets Clear history. Results of an older keystroke
  never replace newer ones and never touch the draft, caret or focus.
- **BROWSE-03 storage.** `saved-searches.json` in Pathweft's data folder: name,
  query, scope folder (or This PC), match options. A Saved searches sidebar
  section and the address suggestions open one by rerunning it through the
  normal pane search. Rename, edit and remove change only that entry. Content
  queries arrive with S19; until then a saved search is a filename/path query.
- **BROWSE-04 actions.** `actions.json` in the data folder, documented in
  docs/CUSTOM-ACTIONS.md, edited in Settings > Actions. Fields: name, Fluent
  glyph, submenu, executable, arguments, working folder, file patterns,
  applies to files/folders, selection count (one, several, any). Placeholders:
  `%f` first selected path, `%F` every selected path, `%n`/`%N` names, `%d`
  current folder; each expands to one argument quoted by the Windows
  command-line rules. Actions appear in Pathweft's menu under Actions; they run
  only when clicked; a start failure is shown, the process is not owned past
  its start (as Open with).
- **BROWSE-05 profiles.** A folder profile holds view mode, shown columns with
  order and widths, sort, date grouping, folders first and hidden files. View >
  Folder view: Save for this folder, Save for this folder and its subfolders,
  Reset. The nearest saved ancestor that applies to subfolders, or the folder's
  own profile, wins; otherwise the user's current defaults apply. Existing
  per-folder sorts and date groups become profiles. Space analysis and search
  results are never saved into a profile; each pane and tab applies profiles on
  its own.
- **BROWSE-06 sync.** Compare with other pane gains Compare subfolders and
  Compare contents (xxHash3 through the hash cache). The result window lists
  each difference with a direction (copy left->right, right->left, delete,
  skip); conflicts (both changed or newer destination) default to skip. Apply
  runs through the normal transfer engine, deletions to the Recycle Bin, with
  Undo; nothing runs in the background.
- **BROWSE-07 storage.** A SQLite database in the data folder keyed by volume
  serial and NTFS/ReFS file ID (survives rename and move on the volume), with
  the path stored for display and for filesystems without stable IDs (FAT,
  network, cloud), where a rename loses the record. Nothing is written into
  files, alternate streams or sidecars. Tags, Rating and Comment are columns;
  the live filter (BROWSE-10) accepts `tag:`, `rating:` and `comment:`.
  Records whose file is gone are shown as stale in Settings and can be pruned.
- **BROWSE-08 terminal.** Windows Terminal's WPF control with ConPTY (donor
  inspected and pinned with notices before adoption), hosting the user's
  PowerShell 7 (falling back to Windows PowerShell) with shell integration:
  the prompt reports its folder with OSC 9;9 and prompt/command marks with OSC
  133. The browser follows a reported folder; the terminal follows the browser
  only while it sits at an empty prompt (Set-Location -LiteralPath). One
  session per pane, kept across tab switches and hide, ended on close or exit.
  Archive, search and remote folders show "not a local folder".
- **BROWSE-09 remote.** SFTP through SSH.NET (MIT); WebDAV through Pathweft's
  own PROPFIND/GET/PUT/MOVE/DELETE client on HttpClient. Saved connections in
  `connections.json`; passwords and key passphrases in Windows Credential
  Manager. SSH host keys: first connection shows the fingerprint and asks;
  a changed key is refused. TLS uses Windows validation with no bypass.
  Remote folders browse in a pane under `sftp://` and `webdav(s)://` addresses;
  preview and open download to a task temp copy; copy and move in both
  directions go through the transfer window with cancellation. No Undo and no
  local-path claims for remote items. Mutation tests use local rclone
  endpoints serving a disposable folder, never user servers.
- **BROWSE-10 filter.** Ctrl+Shift+F (and View > Filter) shows a filter chip in
  the address field: typing narrows the current listing by name (and the
  BROWSE-07 fields); Escape or leaving the folder restores the full listing
  and keeps the selection that is still visible. Hidden-file policy and
  background refresh keep applying; recursive search is unchanged.
- **BROWSE-11 links.** New > Link... creates a symbolic link (file or folder),
  hard link (file, same volume) or junction (folder, local volume) with an
  absolute or relative target; unavailable kinds are disabled with the reason
  (Developer Mode or administrator for symbolic links). Properties shows a
  link's kind and target, broken links are marked, and deleting a link never
  touches its target.
- **BROWSE-13 variable suggestions** (spec 1.60, S70; owner 2026-11-24: "what I
  miss is any autocomplete suggestions" after typing `%Lo`). Address text that
  starts with `%` and has no closing `%` or backslash lists, as Explorer does,
  the environment variables whose name starts with the typed text and whose
  value is an existing local folder, as `%NAME%` rows showing the folder. Enter
  or a click opens the folder; Tab writes `%NAME%\` so folder completion
  continues below it. Network values are not probed.

Reference behaviors: [Dolphin filtering/saved searches](https://docs.kde.org/stable_kf6/en/dolphin/dolphin/quick-tips.html),
[Dolphin metadata/terminal panels](https://docs.kde.org/stable_kf6/en/dolphin/dolphin/panels.html),
[Dolphin folder profiles](https://docs.kde.org/stable_kf6/en/dolphin/dolphin/view-properties.html),
[Thunar custom actions/link examples](https://docs.xfce.org/xfce/thunar/custom-actions),
[GNOME remote browsing](https://help.gnome.org/gnome-help/nautilus-connect.html),
and [Krusader recursive synchronization](https://docs.kde.org/trunk_kf6/en/krusader/krusader/synchronizer.html).
The user's browser screenshot supplies the address-suggestion behavior reference,
not permission to import its browser profile/history or recreate its full UI.

No acceptance criterion is implemented or verified for S21. Metadata storage,
terminal hosting/synchronization, protocol libraries and donor suitability remain
ordinary design/qualification work, not asserted external blockers. No tracked
deferral or ACTIVE stage is introduced; S19 and S20 retain their own scope.

### Focused file workflow enhancements (S22)

Planned after S19 and before S6; NOT STARTED. The
user registered the Directory Opus assessment's useful additions, including its
lower-priority candidates. Favor direct implementation using existing tabs,
panes, operations, preview and settings owners. This is not whole-product parity,
a new scripting/plugin framework or a dependency on an installed Opus client.
This plan-only request does not activate implementation or uninstall Opus.

| ID | Requirement and acceptance |
| --- | --- |
| OPUS-01 | Add named project workspaces containing left/right tab sets and active folders/pane. Provide explicit add-to-current versus replace-current restoration, preserving current session/pinned-tab behavior and relevant S21 view-profile rules. Reuse existing settings/session owners rather than creating a separate window-management framework. Verify save, rename, remove, restart and restore with unavailable destinations and unsaved editor state handled through existing preservation contracts; removing a workspace never deletes its files. |
| OPUS-02 | Add explicit paired-folder roots and opt-in linked relative navigation between two panes or a designated tab pair. Navigating below one root resolves the counterpart below the other; missing counterparts are visibly explained without automatic folder creation, copy or synchronization. Define rebase/unlink and tab-close/navigation behavior, preserving independent use when unlinked. Qualify actual source/deployment-style navigation and missing/renamed destinations; S21 continues to own recursive comparison and reviewed file synchronization. |
| OPUS-03 | Extend ordinary browsing with inline expandable folders and reversible flat descendant views, with a Location column where needed. Reuse qualified disk-analysis tree/browsing components while keeping analysis mode distinct. Preserve actual source paths, existing hidden/link traversal policy, selection/focus, view restoration and bounded/cancellable enumeration. Nested selections must not duplicate transfer work; flat/tree copy choices must clearly distinguish preserving relative hierarchy from flattening and use existing conflict/operation ownership. Qualify navigation, refresh, nested actions and actual resulting files, not tree rendering alone. |
| OPUS-04 | Add named persistent file collections referencing items across directories without moving originals. Extend existing Drop bin concepts while preserving its temporary transfer workflow; collections are distinct from S21's live saved searches. Support adding/removing references, rename/remove of a collection, containing-folder navigation and existing explicit file actions. Clearly distinguish Remove from collection from Delete original. Define moved/missing/offline member behavior and verify restart persistence, multi-root actions and original-file preservation. |
| OPUS-05 | Add bounded persistent operation history showing actual result, time, source/destination and containing-folder actions, plus a usable list of currently eligible Undo actions. Reuse transfer/file-operation completion and Undo owners, retaining truthful partial/error/cancelled outcomes after cards close. A persisted log must not serialize executable delegates, replay operations automatically or imply safe undo after restart; preserve existing eligibility and qualify stale/conflicting destinations before any undo action. Verify completed/failed/cancelled jobs, restart history, retention/privacy and actual eligible Undo outcomes. |
| OPUS-06 | Add copying selected table rows/columns and exporting the current visible listing to CSV/TSV, with explicit column/header and current-view scope. Preserve displayed order/filtering and resolve actual nested/virtual paths consistently. Qualify escaping of delimiters, quotes, line breaks and Unicode and round-trip the exported values; preserve clipboard ownership and do not read file contents, hydrate sources or start recursive scans merely to export displayed metadata. A general printing subsystem is outside this requirement. |
| OPUS-07 | Add lower-priority user-defined path aliases as stable named destinations integrated into S21 address suggestions. Define unambiguous syntax, validation and precedence with real paths, history, pins and saved searches; support rename/remove and restart persistence. Missing/offline targets must not redirect to another destination silently. Qualify expansion, completion, Unicode/UNC targets and preservation of address drafts/focus. Aliases change navigation references only, not source paths. |
| OPUS-08 | Add lower-priority image picking/marking in the preview with a collection-backed set and navigation through marked images. Keep marks separate from current file selection, S21 ratings and the preview pin/follow state. Qualify marking/unmarking, persistence, missing members, next/previous navigation and sidebar/PiP/Windowed host transitions without unexpected file moves/deletions or retained renderer resources. |
| OPUS-09 | Add lower-priority batch embedded-metadata editing and timestamp adjustment for explicitly qualified writable formats/properties. Integrate mixed-value presentation and reviewed apply/cancel with existing Properties/operation ownership and S21 metadata contracts. Establish a concrete supported format/property matrix before implementation; preserve unedited metadata, file content and existing filesystem/provider capabilities. Do not assume NTFS alternate streams are portable or write unsupported cloud metadata silently. Qualify actual write/reopen, unrelated-field preservation, unsupported/read-only sources, partial failure and cancellation; document available Undo/recovery honestly. |
| OPUS-10 | Verify all nine complete integrated workflows with actual UI/state/filesystem/clipboard/preview boundaries, errors/cancellation, restart/privacy and source preservation. Measure added package/idle/peak costs and bound collection/history/enumeration work. Public documentation establishes behavior references, not compatibility or binary reuse. Any unresolved question requiring REA must have a bounded objective, identified qualified artifact, retained evidence and matching runtime verification before adopting behavior/hooks. No Opus DLL reuse or installed-app dependency is qualified by this assessment. S22 owns one normal final release, corresponding source/notices, independent retrieval and applicable installed verification when implementation is separately authorized; no per-feature releases. |
| OPUS-09a | The OPUS-09 supported matrix (S58). Writing goes through the file's registered Windows property handler (SHGetPropertyStoreFromParsingName read-write, SetValue, Commit), never a format parser of Pathweft's own and never an alternate stream. Photos (.jpg .jpeg .jfif .tif .tiff): Title, Subject, Rating, Tags, Comments, Authors, Copyright, and Date taken among the dates. Music (.mp3 .flac .m4a .wma): Title, Contributing artists, Album, Album artist, Year, Track number, Genre, Rating, Comments. Video (.mp4 .m4v .wmv): Title, Contributing artists, Year, Genre, Rating, Tags, Comments. Documents (.docx .xlsx .pptx): Title, Subject, Tags, Authors, Comments. Each file is offered only those of its family that its handler reports writable (IPropertyStoreCapabilities). Dates: keep, set to a local time, or shift by a signed duration, for Modified, Created and photos' Date taken; Windows updates a written file's Modified as Explorer does unless the user keeps it. Folders, read-only files, online-only cloud placeholders (never opened, so never hydrated), remote, archive and virtual items, and other formats are listed as left alone. Mixed values show as "(multiple values)" and only edited fields are written; an emptied field removes the property. Review precedes any write; Apply runs file by file, Stop takes effect between files, a failed file is reported with its reason without affecting the others, and a file is written whole or not at all (no Commit after a failed property). Undo is one entry holding each changed file's earlier values and dates, kept in undo.json across restarts; a file changed again since is left alone, as for other Undo steps. At most 2,000 files per edit. Verified on copies of real files of every format in scratch: write and reopen, unrelated details, pixels, durations and Office parts preserved, read-only/unsupported/folder, a file held by another process, Stop, Undo from the saved history, and elapsed/peak cost. |

Priority follows the assessment: workspaces, paired navigation, expandable/flat
browsing, collections, operation history and listing export first; aliases,
image marking and batch metadata afterward. Lower priority does not remove any
registered requirement from S22 acceptance. Existing S20/S21 features remain
with their current owners and are not duplicate implementations in S22.

Owner decision 2026-11-20 (spec 1.46): S22 implements OPUS-01..08: workspaces,
paired navigation, expandable/flat browsing, collections, operation history and
listing export as its body, with aliases and image marking in the same stage.
OPUS-09 (batch embedded-metadata editing and timestamps) moves to its own stage,
S58, after S22 and before S6; S58 establishes its format/property matrix first.
OPUS-10's verification and single final release apply to OPUS-01..08 in S22 and,
separately, to OPUS-09 in S58. No requirement is removed.

Behavior references: [tab groups](https://docs.dopus.com/doku.php?id=preferences:preferences_categories:folder_tabs:groups),
[navigation lock](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:dual_display:navigation_lock),
[expandable folders](https://docs.dopus.com/doku.php?id=basic_concepts:expandable_folders),
[flat view](https://docs.dopus.com/doku.php?id=basic_concepts:flat_view),
[collections](https://docs.dopus.com/doku.php?id=basic_concepts:virtual_file_system:file_collections),
[operation history](https://docs.dopus.com/doku.php?id=file_operations:tracking_and_undoing_file_operations),
[listing export](https://docs.dopus.com/doku.php?id=additional_functionality:print_folder),
[aliases](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:navigation:aliases),
[image marking](https://docs.dopus.com/doku.php?id=additional_functionality:viewing_images:image_marking)
and [metadata editing](https://docs.dopus.com/doku.php?id=file_operations:editing_metadata).
These are reference documentation, not adopted source pins or runtime acceptance.

S22 (OPUS-01..08) is complete: implemented, verified and released in 1.1.86
(docs/verification/2026-11-20-release-1.1.86.md), with no blockers and no tracked
deferrals. Next is S59 (light updates, LIGHT-01..04), then S58 (OPUS-09 batch
metadata), S6 and S7.

### Light updates (S59)

Owner decision 2026-11-20 (spec 1.47): updates must not download the whole
installer again when only a few files changed. A typical release changes only
Pathweft's own files (about 30 MB of the 1.35 GB installation), while LibreOffice,
the Craft cores, F3D and mpv stay the same for many releases. S59 adds a light
update package and runs after S22, before S58. Bundling does not change: the full
installer still contains everything and stays the only package for new
installations. Portable copies keep their current update path.

| ID | Requirement and acceptance |
| --- | --- |
| LIGHT-01 | Each release also publishes `Pathweft-<version>-update.exe` when a previous published release exists. It is built from the difference between the previous release's release-manifest.json and the new one: it contains the new and changed files, the list of files to remove and the new release-manifest.json, and it records its base version and the SHA-256 of the base manifest. It is listed in SHA256SUMS.txt. Its name must not end in `setup.exe`, so earlier updaters, which pick the asset ending in setup.exe, keep downloading the full installer. |
| LIGHT-02 | The updater decides before downloading. It chooses the light package only when the installed version is the package's base version, the installed release-manifest.json is the recorded base manifest, and every file that manifest lists is present with its SHA-256. Anything else (a skipped release, a missing or changed file, no light package) means the full installer. The update dialog shows which package was chosen and its size; the file check shows progress and can be cancelled. |
| LIGHT-03 | The light package keeps the full update's behaviour: the same dialog, administrator consent, progress, closing and restarting Pathweft, stopping and restarting the index service when its files change, shell DLLs in use, the uninstall entry's version and a working uninstaller. Before changing anything it checks the installed files against the base manifest again; on any difference it changes nothing and the updater downloads and runs the full installer in the same dialog. After applying, the installed files must match the new manifest exactly, otherwise the updater runs the full installer. |
| LIGHT-04 | Verification: the release build checks that installing the base release and then the light package gives exactly the files of the new manifest. Update scenarios with the actual installer in the isolated per-user installer harness (`scripts/Invoke-UpdateProgressChecks.ps1`, its own test AppId) cover the light path from the base release, the refusal after a changed installed file and for another base, and the app's light-update scenario covers the choice, its reasons, a skipped release and a cancelled check. (Spec 1.48, 2026-11-21: wording revised from Windows Sandbox to that harness, which runs the same installer without a guest; no acceptance is removed.) The first release that contains the new updater is still fetched whole by earlier updaters; the owner's installed update to the release after it confirms the light path on his PC. |

### Tab-scoped Undo and Help coverage (owner requests 2026-11-21, carried in 1.1.87)

Owner requests 2026-11-21: "Can we make the undos adapt to the tab they are requested in?" (a tab on one
folder offered the Undo of a rename made in another), and "Are you updating the Help section to explain all
of the new features, what they are for, where to find them and how to use them?" (features since 1.1.82 had
no Help entries). Both ship in 1.1.87 with S59.

| ID | Requirement and acceptance |
| --- | --- |
| UNDO-01 | Ctrl+Z and a folder's right-click Undo item take back the latest Undo history entry that touched the active tab's folder: a step's item lies directly in that folder, or a moved item goes back into it (a rename, a new item, an arrival, a move out, a delete). Other folders' entries are not offered there: no Undo item appears and Ctrl+Z says nothing is to be undone in this folder, pointing to History → Operation history…, which still undoes any entry. A transfer card's Undo button is unchanged. The release self-test checks the folder matching (a rename's folder only; both folders of a move and not their parents; a delete's folder, a drive root included). |
| HELP-01 | Help (F1) has an entry for every user-facing feature released since 1.1.82 and for every one a stage adds from now on, in the same release: what it is for, where it is found and how it is used. In 1.1.87: Index contents and content: search (1.1.84); workspaces, linked panes, collections, path aliases, expandable folders, flat view, Copy as table and Export listing, operation history and image marks (1.1.86); light updates and tab-scoped Undo (1.1.87). |

### Lifecycle, shell integration and final delivery

| ID | Requirement and acceptance |
| --- | --- |
| CORE-23 | Missing local folder tabs recover to the nearest confirmed existing ancestor on activation and restoration, retaining order/pins/selection identity and persisting the repaired path. Correct the tab/header/list state together; no old folder rows under a new tab identity. Preserve normal paths, existing archive tabs and access-denied/offline destinations without mistaking failed availability probes for deletion. Verify actual click, keyboard/external activation, multiple vanished levels, restored tabs and normal navigation, plus normal final release and independently retrieved package behavior. |
| LIFE-01 | Own initialization, readiness, cancellation and cleanup through actual state/events. Do not copy fixed sleeps/retries or arbitrary timeout-driven synchronization from donors. Isolate unavoidable external timeouts at their boundary. Verify owned helper exit before scratch cleanup. |
| SHELL-01 | Folder/default-handler and Win+E integration is opt-in, reversible and verified. Investigate Directory Opus's native Explorer replacement behavior and replace the inherited timed hide/poll/close Explorer-window takeover for covered entry points with launch-boundary routing. Qualify normal folder/default and explore verbs, shortcuts, Win+E, and SHOpenFolderAndSelectItems-style folder-plus-selection requests, including applicable caller architectures and cold/warm application states. The user's explicit scope correction assigns StartAllBack-specific links and Shell/known-folder/library routing to postponed S6/SAB-06; they do not gate S8 core delivery. Keep unsupported virtual locations, explicit explorer.exe launches and common Open/Save dialog behavior explicit; do not claim universal interception. Preserve actual prior registrations/owners, respect another default manager, and verify disable/uninstall/restoration, no loops or duplicate/transient Explorer windows for covered routes. Native code stays limited to routing; do not host the managed file manager inside caller processes or substitute global process-creation interception. Folder-browser scope does not imply desktop-shell or common-dialog replacement. |
| DELIVERY-01 | One installation, settings owner and update channel. Pin/package Everything and native preview dependencies; preserve external instances/data and prevent upstream updates replacing Pathweft. Under the user's 2026-10-31 instruction stopping Sandbox work, use the completed isolated-directory per-user host install/update/uninstall and preservation trials, then verify the final published and actual host-installed package. Do not reopen a guest environment as a delivery gate. |
| DELIVERY-02 | After S8 core-release criteria pass under the user's core-first scope, publish one normal versioned Pathweft release here: usable Windows x64 installer and portable package, source/tag, checksums, accurate notes and required source/notices. State signing identity/status honestly. Independently retrieve both assets and verify hashes/version/commit and packaged search/preview/transfer behavior. Identify S6/S7 as undelivered later integration work. Future implementation outcomes, including S13, own equivalent normal release, independent retrieval/qualification and applicable installed verification within their stage after the whole authorized outcome passes; do not publish partial modules or reopen completed S8 as their delivery owner. Plan/investigation-only work does not trigger software publication. No setup/intermediate commit is an application release. |
| DELIVERY-03 | Preserve original notices, exact donor/dependency revisions and terms; provide applicable GPL source/distribution materials when QuickLook code is imported. Keep private traces, credentials, personal sample paths and machine records out of public source/assets. |
| DELIVERY-05 | Owner decision 2026-11-19 (spec 1.45), on seeing about 330 files in the installation root: "is better practice to trigger the .Net dependency installation over bundling the libraries in each insalled program". Pathweft is published framework-dependent on the Microsoft .NET 10 Desktop Runtime (x64), not self-contained. Setup detects the runtime and, when it is missing, downloads Microsoft's runtime installer, refuses it unless it carries a valid Microsoft Corporation signature, installs it quietly and checks it again before any Pathweft file is replaced; the portable package requires it to be installed. An update removes the runtime copy and satellite folders earlier self-contained releases left in the installation (an app-local hostfxr.dll would keep Pathweft on that copy). English only: no runtime satellite folders and no reference API-doc XML files; the MCP notes ship as docs\Performance-MCP.md. Verify the published package has no bundled runtime, Pathweft starts on the installed shared runtime, and the installed update from 1.1.82 leaves no runtime member behind. |

## 5. Acceptance, stages and completion

The [plan](IMPLEMENTATION-PLAN.md) maps every requirement to an owner. Complete
features/real workflows are acceptance units. Compilation, mocks, test counts or
worker reports do not prove runtime integration or delivery. Use focused checks
appropriate to the actual risks; repeat only invalidated checks.

At most one implementation stage is ACTIVE. Missing current-stage work is an
advancement blocker, even if straightforward. Record its exact requirement,
reason, internal/external cause, resolution condition and dependent work. Park
as BLOCKED only when it cannot presently be resolved. A tracked deferral must
name a later stage and verification, preserve current acceptance and close before
overall completion. Do not silently narrow requirements to close a stage.

No performance/memory-saving claim without measured attributable evidence. No
installed/runtime claim from the setup build. Overall product completion requires
every criterion, integration, blocker, deferral and final release verification
resolved. Initial setup delivered the environment/repository/documents; the
current implementation authorization and reconciliation are recorded in the plan.

## 6. Decisions and limits

- Name: **Pathweft**, connecting file workflows in one product.
- Initial development SDK: .NET 8 for baseline comparability. Select a supported
  runtime and verify donors before release; baseline preservation does not justify
  shipping an unsupported runtime.
- No Everything indexer rewrite, standalone donor-product requirement,
  desktop-shell replacement or unrelated expansion. S6 targets StartAllBack;
  S7 explicitly owns filesystem search replacement and Outlook-preserving indexing
  scope reduction, subject to actual consumer/semantic coverage and verified recovery.
- Existing application identity stays unchanged during setup; S1 must isolate it
  before runtime trials. No claim that the inherited installer is ready.
- No releases for intermediate edits or component work. The user-authorized core
  release belongs to S8 after its revised prerequisites/criteria pass; S6/S7
  remain later integration work and do not gate this core release.
