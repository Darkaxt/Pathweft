# Pathweft index service: pipe protocol, version 1

Pipe: `\\.\pipe\PathweftIndex` (local only; remote clients and network logons
are rejected; SYSTEM and the configured users, by default the user who ran
`install`, may connect).

## Framing

Each message is a 4-byte little-endian payload length followed by that many
bytes of UTF-8 JSON (at most 64 MB). The client sends one request frame and
reads exactly one response frame; a connection may carry any number of
request/response pairs in order. Times are Windows FILETIME values (100 ns
since 1601-01-01 UTC, 0 = unknown); sizes are bytes (-1 = unknown).

The service evaluates visibility with the caller's token, so the client must
allow impersonation: in C# create the stream with
`TokenImpersonationLevel.Impersonation` (or `Identification`). With
`Anonymous` the service cannot identify the caller and refuses the request.

## Requests

```json
{"op":"query","query":"*.cs path:\"D:\\Projects\\\"","offset":0,"count":100,
 "sort":"modified","descending":true,
 "match_path":false,"match_case":false,"whole_word":false,"regex":false,
 "visible_only":false}
{"op":"folder_sizes","paths":["D:\\Projects","C:\\Windows"]}
{"op":"status"}
{"op":"folder_add","path":"H:\\","rescan_minutes":360}
{"op":"folder_remove","path":"H:\\"}
{"op":"rescan","target":"C:"}
```

Query fields other than `query` are optional (defaults: offset 0, count 0 =
total only, sort `name` ascending, switches off, `visible_only` false). `count` is capped at 100,000.
Sort keys: `name`, `path`, `size`, `extension`, `created`, `modified`.

The expression language is the subset of Everything's syntax Pathweft uses
(SPEC IDX-05): space = AND, `|` = OR, `!` = NOT, `< >` grouping, `"..."`
quoting, `*`/`?` wildcards, substrings, terms containing `\` match the full
path, modifiers `path: nopath: case: nocase: ww: wfn: regex: file: folder:`,
`ext:a;b`, `size:` comparisons, `dm:`/`datemodified:` and `dc:`/`datecreated:`
dates, and `parent:<folder>`. `dm:`/`datemodified:` and `dc:`/`datecreated:` take `today`, `yesterday`,
`thisweek`, `lastweek`, `thismonth`, `lastmonth`, `thisyear`, `lastyear`, `YYYY`,
`YYYY-MM` or `YYYY-MM-DD` (`/` also accepted), a range `a..b` (both ends
inclusive) or `>`, `>=`, `<`, `<=`, `=` one of those. Days are local-time days
(daylight saving applied by Windows), weeks start on Monday, and an unknown time
never matches. `parent:<folder>` matches the folder's direct children only
(a drive, a folder-root path or a UNC root also works); a folder that is not
indexed matches nothing.
Anything else (`dupe:`, `content:` ...) is an error, never a silently different
result.

## Response

```json
{"ok":true,"updating":false,"elapsed_us":812,"total":1234,
 "hits":[{"path":"D:\\Projects\\x.cs","visible":true,"folder":false,"size":2048,
          "modified":133700000000000000,"created":133600000000000000,
          "attributes":32}]}
{"ok":true,"sizes":[{"path":"D:\\Projects","size":123456789},{"path":"X:\\nope"}]}
{"ok":false,"error":"..."}
```

`updating` is true while any store is loading, re-reading or scanning, and
`updating_stores` names them; the answer is what the index has at that moment.
A folder size is absent (`size` missing) when the folder is not indexed; it is
never reported as 0 in that case.

`status` returns `status`: version, protocol, pid, uptime, working set and
private bytes, the caller's SID, and per store: name, kind (`ntfs`/`folder`),
state (`loading`, `ready`, `reloading`, `scanning`, `waiting-for-user`,
`offline`, `error`), items, folders, journal ID, next USN, load time,
re-reads, applied changes, estimated memory, last error, owner (folders).

## Visibility flag

Every match is returned. Each hit carries `visible`: whether the caller may
list its parent directory (`FILE_LIST_DIRECTORY`, evaluated with `AccessCheck`
on the caller's token, cached per caller and NTFS security ID for 15 minutes).
Entries of folder roots added by another user are `visible: false`. The client
decides what to show; `"visible_only": true` drops the others before counting
and paging, so `total` and pages stay consistent when the client filters.

Folder sizes are always the administrator view: the recursive size of
everything indexed below the folder, including folders the caller cannot open.
This is what disk analysis needs to find problem folders.

## Clients

- Rust: `pathweft_index::client::Client::connect(timeout)?.request(&Request::Status)`.
- C ABI (`pathweft_index.dll`): `int32_t pwi_request(const char *pipe_or_null,
  const char *request_json, uint32_t connect_timeout_ms, char **response)` and
  `void pwi_free(char *response)`.
- C# without the DLL:

```csharp
using var pipe = new NamedPipeClientStream(".", "PathweftIndex", PipeDirection.InOut,
    PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
await pipe.ConnectAsync(2000, ct);
byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { op = "query", query = "*.pdf", count = 50 });
await pipe.WriteAsync(BitConverter.GetBytes(body.Length), ct);   // little-endian on x64
await pipe.WriteAsync(body, ct);
var len = new byte[4]; await pipe.ReadExactlyAsync(len, ct);
var reply = new byte[BitConverter.ToInt32(len)]; await pipe.ReadExactlyAsync(reply, ct);
using var doc = JsonDocument.Parse(reply);
```
