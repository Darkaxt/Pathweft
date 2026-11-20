//! Pipe protocol, version 2 (version 1 plus the content index ops).
//!
//! Every message is a frame: a 4-byte little-endian payload length followed by
//! that many bytes of UTF-8 JSON. The client sends one `Request` per frame and
//! receives exactly one `Response` frame for it, in order, on the same
//! connection. Times are Windows FILETIME values (100 ns since 1601-01-01 UTC);
//! 0 means unknown. Sizes are bytes; -1 means unknown.

use serde::{Deserialize, Serialize};
use std::io::{self, Read, Write};

pub const PIPE_NAME: &str = r"\\.\pipe\PathweftIndex";
pub const PROTOCOL_VERSION: u32 = 2;
pub const MAX_FRAME: usize = 64 << 20;
pub const MAX_COUNT: u32 = 100_000;

#[derive(Serialize, Deserialize, Debug, Clone)]
#[serde(tag = "op", rename_all = "snake_case")]
pub enum Request {
    Query(QueryRequest),
    FolderSizes { paths: Vec<String> },
    Status,
    FolderAdd {
        path: String,
        #[serde(default)]
        rescan_minutes: Option<u32>,
    },
    FolderRemove { path: String },
    Rescan { target: String },
    ContentScopeAdd { path: String },
    ContentScopeRemove { path: String },
    ContentScopes,
    ContentNext {
        #[serde(default)]
        count: u32,
        #[serde(default)]
        version: u32,
    },
    ContentPut {
        id: i64,
        #[serde(default)]
        version: u32,
        read: Stamp,
        after: Stamp,
        #[serde(default)]
        chunks: Vec<ContentChunk>,
        #[serde(default, rename = "final")]
        last: bool,
    },
    ContentFail {
        id: i64,
        #[serde(default)]
        version: u32,
        read: Stamp,
        #[serde(default)]
        error: String,
        #[serde(default)]
        unsupported: bool,
    },
    ContentState { folder: String },
    ContentStatus,
}

/// A file's size and modified time (UTC FILETIME, 0 unknown) as the worker saw it.
#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct Stamp {
    pub size: u64,
    pub modified: u64,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct ContentChunk {
    /// Stored as sent and returned in query hits.
    #[serde(default)]
    pub location: serde_json::Value,
    pub text: String,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct ContentScope {
    pub path: String,
    pub added: u64,
    pub reconciled: u64,
    pub error: Option<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct ContentScopeStatus {
    pub path: String,
    pub files: u64,
    pub pending: u64,
    pub current: u64,
    pub stale: u64,
    pub failed: u64,
    pub unsupported: u64,
    pub unavailable: u64,
    pub reconciled: u64,
    pub error: Option<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct ContentStatus {
    pub scopes: Vec<ContentScopeStatus>,
    pub store_bytes: u64,
    /// Pending files, and stale files without an error: work for the worker.
    pub waiting: u64,
}

#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq, Eq, Default)]
#[serde(rename_all = "snake_case")]
pub enum SortKey {
    #[default]
    Name,
    Path,
    Size,
    Extension,
    Created,
    Modified,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default)]
#[serde(default)]
pub struct QueryRequest {
    pub query: String,
    pub offset: u32,
    pub count: u32,
    pub sort: SortKey,
    pub descending: bool,
    pub match_path: bool,
    pub match_case: bool,
    pub whole_word: bool,
    pub regex: bool,
    /// Drop matches the caller cannot list before counting and paging.
    pub visible_only: bool,
    /// With `content:` terms: up to this many matching locations per hit.
    pub locations: u32,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct Hit {
    pub path: String,
    /// Whether the caller may list the parent folder.
    #[serde(default = "visible_default")]
    pub visible: bool,
    pub folder: bool,
    pub size: i64,
    pub modified: u64,
    pub created: u64,
    pub attributes: u32,
    /// Matching content locations, when the query asked for them.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub locations: Option<Vec<serde_json::Value>>,
}

fn visible_default() -> bool {
    true
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct FolderSize {
    pub path: String,
    /// Recursive size of everything indexed below it; absent when the folder is not indexed.
    pub size: Option<u64>,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default)]
pub struct StoreStatus {
    pub name: String,
    pub kind: String,
    pub state: String,
    pub items: u64,
    pub folders: u64,
    pub journal_id: u64,
    pub next_usn: i64,
    pub load_ms: u64,
    pub reloads: u32,
    pub applied_changes: u64,
    pub memory_bytes: u64,
    pub error: Option<String>,
    pub owner: Option<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default)]
pub struct Status {
    pub version: String,
    pub protocol: u32,
    pub pid: u32,
    pub uptime_s: u64,
    pub working_set: u64,
    pub private_bytes: u64,
    pub caller: String,
    pub stores: Vec<StoreStatus>,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default)]
pub struct Response {
    pub ok: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// True while any store is loading, re-reading or scanning: the answer is
    /// what the index has now.
    #[serde(default)]
    pub updating: bool,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub updating_stores: Vec<String>,
    #[serde(default)]
    pub elapsed_us: u64,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub total: Option<u64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub hits: Option<Vec<Hit>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sizes: Option<Vec<FolderSize>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub status: Option<Status>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub scopes: Option<Vec<ContentScope>>,
    /// `content_next` work items or `content_state` entries.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub files: Option<Vec<serde_json::Value>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub content: Option<ContentStatus>,
}

impl Response {
    pub fn failure(message: impl Into<String>) -> Response {
        Response { ok: false, error: Some(message.into()), ..Default::default() }
    }
}

pub fn write_frame<W: Write>(w: &mut W, payload: &[u8]) -> io::Result<()> {
    if payload.len() > MAX_FRAME {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "frame too large"));
    }
    let mut frame = Vec::with_capacity(payload.len() + 4);
    frame.extend_from_slice(&(payload.len() as u32).to_le_bytes());
    frame.extend_from_slice(payload);
    w.write_all(&frame)?;
    w.flush()
}

/// Reads one frame. `Ok(None)` is a clean end of stream before a frame started.
pub fn read_frame<R: Read>(r: &mut R) -> io::Result<Option<Vec<u8>>> {
    let mut len = [0u8; 4];
    let mut got = 0;
    while got < 4 {
        let n = r.read(&mut len[got..])?;
        if n == 0 {
            return if got == 0 { Ok(None) } else { Err(io::ErrorKind::UnexpectedEof.into()) };
        }
        got += n;
    }
    let len = u32::from_le_bytes(len) as usize;
    if len > MAX_FRAME {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "frame too large"));
    }
    let mut payload = vec![0u8; len];
    r.read_exact(&mut payload)?;
    Ok(Some(payload))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn frames_round_trip() {
        let request = Request::Query(QueryRequest { query: "*.cs".into(), count: 10, ..Default::default() });
        let json = serde_json::to_vec(&request).unwrap();
        let mut buf = Vec::new();
        write_frame(&mut buf, &json).unwrap();
        let mut cursor = io::Cursor::new(buf);
        let back = read_frame(&mut cursor).unwrap().unwrap();
        let parsed: Request = serde_json::from_slice(&back).unwrap();
        match parsed {
            Request::Query(q) => {
                assert_eq!(q.query, "*.cs");
                assert_eq!(q.count, 10);
                assert_eq!(q.sort, SortKey::Name);
            }
            _ => panic!("wrong op"),
        }
        assert!(read_frame(&mut cursor).unwrap().is_none());
    }

    #[test]
    fn request_json_shape_is_stable() {
        let parsed: Request = serde_json::from_str(r#"{"op":"query","query":"a","sort":"modified","descending":true}"#).unwrap();
        match parsed {
            Request::Query(q) => {
                assert_eq!(q.sort, SortKey::Modified);
                assert!(q.descending);
                assert_eq!(q.count, 0);
            }
            _ => panic!(),
        }
        let sizes: Request = serde_json::from_str(r#"{"op":"folder_sizes","paths":["C:\\x"]}"#).unwrap();
        assert!(matches!(sizes, Request::FolderSizes { .. }));
        let status: Request = serde_json::from_str(r#"{"op":"status"}"#).unwrap();
        assert!(matches!(status, Request::Status));
    }

    #[test]
    fn content_ops_parse_as_documented() {
        let put: Request = serde_json::from_str(
            r#"{"op":"content_put","id":17,"version":3,"read":{"size":2048,"modified":5},"after":{"size":2048,"modified":5},
                "chunks":[{"location":{"line":1,"column":1,"offset":0},"text":"first lines"}],"final":true}"#,
        )
        .unwrap();
        match put {
            Request::ContentPut { id, version, read, chunks, last, .. } => {
                assert_eq!((id, version, read.size, last), (17, 3, 2048, true));
                assert_eq!(chunks[0].location["line"], 1);
            }
            _ => panic!(),
        }
        let next: Request = serde_json::from_str(r#"{"op":"content_next","count":50,"version":3}"#).unwrap();
        assert!(matches!(next, Request::ContentNext { count: 50, version: 3 }));
        let fail: Request = serde_json::from_str(r#"{"op":"content_fail","id":18,"version":3,"read":{"size":1,"modified":1},"error":"encrypted PDF","unsupported":false}"#).unwrap();
        assert!(matches!(fail, Request::ContentFail { id: 18, unsupported: false, .. }));
        for op in ["content_scopes", "content_status"] {
            serde_json::from_str::<Request>(&format!(r#"{{"op":"{op}"}}"#)).unwrap();
        }
        let q: Request = serde_json::from_str(r#"{"op":"query","query":"content:invoice","locations":5}"#).unwrap();
        assert!(matches!(q, Request::Query(QueryRequest { locations: 5, .. })));
        let hit = serde_json::to_string(&Hit::default()).unwrap();
        assert!(!hit.contains("locations"), "absent unless asked for");
    }
}
