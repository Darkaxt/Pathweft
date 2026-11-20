//! The content index (SPEC IDX-15..19): content scopes, per-file state, and
//! an FTS5 terms table filled by the user's worker over the pipe.
//!
//! The service never reads file contents. Text arrives in `content_put` and
//! only its terms are kept (the FTS5 table is contentless); chunks keep the
//! worker's location objects, which queries return.
//!
//! Scopes never overlap: adding a folder inside a scope is a no-op and adding
//! a parent absorbs the scopes below it, so every file belongs to one scope.

use crate::log::{info, warning};
use crate::security::LocalSd;
use crate::state::{Kind, Service, Slot};
use pathweft_index::ffi::{last_error, wide, CreateDirectoryW};
use pathweft_index::protocol::{ContentChunk, ContentScope, ContentScopeStatus, ContentStatus, Stamp};
use pathweft_index::query::{self, StoreRef};
use rusqlite::{params, Connection, OptionalExtension, Transaction};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

pub const PENDING: i64 = 0;
pub const CURRENT: i64 = 1;
pub const STALE: i64 = 2;
pub const FAILED: i64 = 3;
pub const UNSUPPORTED: i64 = 4;
pub const UNAVAILABLE: i64 = 5;

const LEASE: Duration = Duration::from_secs(600);
pub const MAX_CHUNK: usize = 64 << 10;
pub const MAX_FILE_TEXT: u64 = 64 << 20;
const MAX_NEXT: u32 = 1000;
const RECONCILE: Duration = Duration::from_secs(60);
/// SYSTEM and Administrators only (SPEC IDX-19).
const CONTENT_SDDL: &str = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

const SCHEMA: &str = "
CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value);
CREATE TABLE IF NOT EXISTS scopes(path TEXT PRIMARY KEY COLLATE NOCASE, added INT NOT NULL, reconciled INT NOT NULL DEFAULT 0, error TEXT);
CREATE TABLE IF NOT EXISTS files(
    id INTEGER PRIMARY KEY,
    path TEXT NOT NULL UNIQUE COLLATE NOCASE,
    size INT NOT NULL,
    modified INT NOT NULL,     -- UTC FILETIME, 0 unknown
    state INT NOT NULL,
    prior INT NOT NULL DEFAULT 0,   -- state before it became unavailable
    error TEXT,
    indexed INT NOT NULL DEFAULT 0,
    published INT NOT NULL DEFAULT 0,
    psize INT NOT NULL DEFAULT 0,   -- stamp the published terms were read at
    pmodified INT NOT NULL DEFAULT 0,
    version INT NOT NULL DEFAULT 0  -- extractor version of the last answer
);
CREATE INDEX IF NOT EXISTS files_state ON files(state);
CREATE TABLE IF NOT EXISTS chunks(id INTEGER PRIMARY KEY, file INT NOT NULL, location TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS chunks_file ON chunks(file);
CREATE VIRTUAL TABLE IF NOT EXISTS terms USING fts5(text, content='', contentless_delete=1, tokenize='unicode61 remove_diacritics 2');
INSERT OR IGNORE INTO meta(key, value) VALUES('schema', 1);
";

pub fn state_name(s: i64) -> &'static str {
    match s {
        PENDING => "pending",
        CURRENT => "current",
        STALE => "stale",
        FAILED => "failed",
        UNSUPPORTED => "unsupported",
        _ => "unavailable",
    }
}

/// UTC FILETIME now.
pub fn now() -> u64 {
    let d = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default();
    116_444_736_000_000_000 + d.as_secs() * 10_000_000 + d.subsec_nanos() as u64 / 100
}

fn db(e: rusqlite::Error) -> String {
    format!("content index: {e}")
}

/// `X:\dir\` -> `X:\dir`, `X:\` -> `X:`. Absolute local or UNC paths only.
pub fn normalize(path: &str) -> Result<String, String> {
    let p = path.trim().trim_end_matches('\\');
    let b = p.as_bytes();
    let drive = b.len() >= 2 && b[0].is_ascii_alphabetic() && b[1] == b':' && (b.len() == 2 || b[2] == b'\\');
    let unc = p.starts_with(r"\\") && p[2..].contains('\\');
    if !(drive || unc) || p.split('\\').any(|c| c == "..") {
        return Err(format!("{path}: a content scope must be an absolute folder path (X:\\... or \\\\server\\share\\...)"));
    }
    Ok(if drive { format!("{}{}", p[..1].to_ascii_uppercase(), &p[1..]) } else { p.to_string() })
}

/// Whether `path` is strictly below folder `dir` (ASCII case-insensitive).
pub fn under(path: &str, dir: &str) -> bool {
    let (p, d) = (path.as_bytes(), dir.as_bytes());
    p.len() > d.len() && p[d.len()] == b'\\' && p[..d.len()].eq_ignore_ascii_case(d)
}

fn same(a: &str, b: &str) -> bool {
    a.len() == b.len() && a.as_bytes().eq_ignore_ascii_case(b.as_bytes())
}

/// A LIKE pattern for everything below `dir` (escape character `^`).
fn below(dir: &str) -> String {
    let mut s = String::with_capacity(dir.len() + 2);
    for c in dir.chars() {
        if matches!(c, '^' | '%' | '_') {
            s.push('^');
        }
        s.push(c);
    }
    s.push_str("\\%");
    s
}

fn extension(path: &str) -> String {
    let name = path.rsplit('\\').next().unwrap_or("");
    name.rfind('.').map(|i| name[i + 1..].to_lowercase()).unwrap_or_default()
}

struct Lease {
    until: Instant,
    /// Text bytes staged so far; `None` until the first `content_put`.
    staged: Option<u64>,
}

struct Row {
    id: i64,
    path: String,
    size: u64,
    modified: u64,
    state: i64,
    prior: i64,
}

pub struct ContentStore {
    conn: Mutex<Connection>,
    file: Option<PathBuf>,
    leases: Mutex<HashMap<i64, Lease>>,
}

impl ContentStore {
    /// Opens `dir\content.db`, or an in-memory store for `None` (tests).
    pub fn open(dir: Option<&Path>) -> Result<ContentStore, String> {
        let file = dir.map(|d| d.join("content.db"));
        let conn = match &file {
            Some(f) => Connection::open(f).map_err(db)?,
            None => Connection::open_in_memory().map_err(db)?,
        };
        let _: String = conn.query_row("PRAGMA journal_mode=WAL", [], |r| r.get(0)).map_err(db)?;
        conn.execute_batch("PRAGMA synchronous=NORMAL; PRAGMA cache_size=-16000;").map_err(db)?;
        conn.execute_batch(SCHEMA).map_err(db)?;
        // Chunks staged by a worker the service did not hear from again.
        conn.execute_batch("DELETE FROM terms WHERE rowid IN (SELECT id FROM chunks WHERE file < 0); DELETE FROM chunks WHERE file < 0;").map_err(db)?;
        Ok(ContentStore { conn: Mutex::new(conn), file, leases: Mutex::new(HashMap::new()) })
    }

    fn rows_below(conn: &Connection, dir: &str) -> Result<Vec<Row>, String> {
        let mut st = conn.prepare_cached("SELECT id, path, size, modified, state, prior FROM files WHERE path LIKE ?1 ESCAPE '^'").map_err(db)?;
        let rows = st
            .query_map([below(dir)], |r| {
                Ok(Row { id: r.get(0)?, path: r.get(1)?, size: r.get::<_, i64>(2)? as u64, modified: r.get::<_, i64>(3)? as u64, state: r.get(4)?, prior: r.get(5)? })
            })
            .map_err(db)?
            .collect::<Result<Vec<_>, _>>()
            .map_err(db)?;
        // LIKE folds ASCII only; keep the exact rule.
        Ok(rows.into_iter().filter(|r| under(&r.path, dir)).collect())
    }

    fn purge(tx: &Transaction, id: i64) -> Result<(), String> {
        tx.execute("DELETE FROM terms WHERE rowid IN (SELECT id FROM chunks WHERE file = ?1 OR file = -?1)", [id]).map_err(db)?;
        tx.execute("DELETE FROM chunks WHERE file = ?1 OR file = -?1", [id]).map_err(db)?;
        tx.execute("DELETE FROM files WHERE id = ?1", [id]).map_err(db)?;
        Ok(())
    }

    fn drop_staged(conn: &Connection, id: i64) -> Result<(), String> {
        conn.execute("DELETE FROM terms WHERE rowid IN (SELECT id FROM chunks WHERE file = -?1)", [id]).map_err(db)?;
        conn.execute("DELETE FROM chunks WHERE file = -?1", [id]).map_err(db)?;
        Ok(())
    }

    // ------------------------------------------------------------ scopes

    pub fn scopes(&self) -> Result<Vec<ContentScope>, String> {
        let conn = self.conn.lock().unwrap();
        let mut st = conn.prepare_cached("SELECT path, added, reconciled, error FROM scopes ORDER BY path").map_err(db)?;
        let v = st
            .query_map([], |r| Ok(ContentScope { path: r.get(0)?, added: r.get::<_, i64>(1)? as u64, reconciled: r.get::<_, i64>(2)? as u64, error: r.get(3)? }))
            .map_err(db)?
            .collect::<Result<Vec<_>, _>>()
            .map_err(db)?;
        Ok(v)
    }

    /// Adds a scope (IDX-15). Returns what happened, for the reply and log.
    pub fn scope_add(&self, path: &str) -> Result<String, String> {
        let p = normalize(path)?;
        let existing = self.scopes()?;
        if let Some(s) = existing.iter().find(|s| same(&p, &s.path) || under(&p, &s.path)) {
            return Ok(format!("{p} is already covered by the content scope {}", s.path));
        }
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction().map_err(db)?;
        let absorbed: Vec<&ContentScope> = existing.iter().filter(|s| under(&s.path, &p)).collect();
        for s in &absorbed {
            tx.execute("DELETE FROM scopes WHERE path = ?1", [&s.path]).map_err(db)?;
        }
        tx.execute("INSERT INTO scopes(path, added) VALUES(?1, ?2)", params![p, now() as i64]).map_err(db)?;
        tx.commit().map_err(db)?;
        Ok(if absorbed.is_empty() { format!("{p} added") } else { format!("{p} added; it absorbs {} scope(s) below it", absorbed.len()) })
    }

    /// Removes an exact scope and purges its files (no other scope covers
    /// them, since scopes never overlap). Returns the purged file count.
    pub fn scope_remove(&self, path: &str) -> Result<usize, String> {
        let p = normalize(path)?;
        let scope = self.scopes()?.into_iter().find(|s| same(&s.path, &p)).ok_or_else(|| format!("{p} is not a content scope"))?;
        let mut conn = self.conn.lock().unwrap();
        let rows = Self::rows_below(&conn, &scope.path)?;
        let tx = conn.transaction().map_err(db)?;
        for r in &rows {
            Self::purge(&tx, r.id)?;
        }
        tx.execute("DELETE FROM scopes WHERE path = ?1", [&scope.path]).map_err(db)?;
        tx.commit().map_err(db)?;
        drop(conn);
        let mut leases = self.leases.lock().unwrap();
        for r in &rows {
            leases.remove(&r.id);
        }
        Ok(rows.len())
    }

    // ------------------------------------------------------------ reconcile

    /// Compares a scope with the name index (IDX-16). `listing` is every file
    /// below the scope with size and modified time, or why the store cannot
    /// say (its files become unavailable and keep their data).
    pub fn reconcile(&self, scope: &str, listing: Result<Vec<(String, u64, u64)>, String>) -> Result<(), String> {
        let mut conn = self.conn.lock().unwrap();
        let listing = match listing {
            Ok(l) => l,
            Err(reason) => {
                let rows = Self::rows_below(&conn, scope)?;
                let tx = conn.transaction().map_err(db)?;
                for r in rows.iter().filter(|r| r.state != UNAVAILABLE) {
                    tx.execute("UPDATE files SET prior = state, state = ?2 WHERE id = ?1", params![r.id, UNAVAILABLE]).map_err(db)?;
                }
                tx.execute("UPDATE scopes SET error = ?2 WHERE path = ?1", params![scope, reason]).map_err(db)?;
                return tx.commit().map_err(db);
            }
        };
        let rows = Self::rows_below(&conn, scope)?;
        let mut existing: HashMap<String, Row> = rows.into_iter().map(|r| (r.path.to_lowercase(), r)).collect();
        let mut new: Vec<(String, u64, u64)> = Vec::new();
        let tx = conn.transaction().map_err(db)?;
        for (path, size, modified) in listing {
            let Some(r) = existing.remove(&path.to_lowercase()) else {
                new.push((path, size, modified));
                continue;
            };
            let changed = (size, modified) != (r.size, r.modified);
            let base = if r.state == UNAVAILABLE { r.prior } else { r.state };
            let state = match (changed, base) {
                (true, CURRENT) => STALE,
                (true, FAILED | UNSUPPORTED) => PENDING,
                (_, s) => s,
            };
            if changed {
                tx.execute(
                    "UPDATE files SET path = ?2, size = ?3, modified = ?4, state = ?5, error = NULL WHERE id = ?1",
                    params![r.id, path, size as i64, modified as i64, state],
                )
                .map_err(db)?;
            } else if state != r.state || path != r.path {
                tx.execute("UPDATE files SET path = ?2, state = ?3 WHERE id = ?1", params![r.id, path, state]).map_err(db)?;
            }
        }
        // Moves: a vanished file and a new one with the same extension, size
        // and known modified time, each unique on its side, keep the terms.
        let key = |p: &str, s: u64, m: u64| (extension(p), s, m);
        let mut gone_keys: HashMap<(String, u64, u64), Vec<i64>> = HashMap::new();
        for r in existing.values().filter(|r| r.modified != 0) {
            gone_keys.entry(key(&r.path, r.size, r.modified)).or_default().push(r.id);
        }
        let mut new_keys: HashMap<(String, u64, u64), usize> = HashMap::new();
        for (p, s, m) in &new {
            *new_keys.entry(key(p, *s, *m)).or_default() += 1;
        }
        let mut moved: Vec<i64> = Vec::new();
        let mut fresh: Vec<(String, u64, u64)> = Vec::new();
        for (p, s, m) in new {
            let k = key(&p, s, m);
            match gone_keys.get(&k) {
                Some(ids) if ids.len() == 1 && new_keys.get(&k) == Some(&1) && m != 0 => {
                    let id = ids[0];
                    let r = existing.values().find(|r| r.id == id).unwrap();
                    let state = if r.state == UNAVAILABLE { r.prior } else { r.state };
                    tx.execute("UPDATE files SET path = ?2, state = ?3 WHERE id = ?1", params![id, p, state]).map_err(db)?;
                    moved.push(id);
                }
                _ => fresh.push((p, s, m)),
            }
        }
        for r in existing.values().filter(|r| !moved.contains(&r.id)) {
            Self::purge(&tx, r.id)?;
        }
        for (p, s, m) in &fresh {
            tx.execute("INSERT INTO files(path, size, modified, state) VALUES(?1, ?2, ?3, ?4)", params![p, *s as i64, *m as i64, PENDING]).map_err(db)?;
        }
        tx.execute("UPDATE scopes SET reconciled = ?2, error = NULL WHERE path = ?1", params![scope, now() as i64]).map_err(db)?;
        tx.commit().map_err(db)?;
        let purged = existing.len() - moved.len();
        if purged > 0 || !fresh.is_empty() || !moved.is_empty() {
            info!("content {scope}: {} new, {} moved, {purged} removed", fresh.len(), moved.len());
        }
        Ok(())
    }

    // ------------------------------------------------------------ worker

    /// Files to read (IDX-17), each leased for 10 minutes.
    pub fn next(&self, count: u32, version: u32) -> Result<Vec<Value>, String> {
        let count = if count == 0 { 50 } else { count.min(MAX_NEXT) } as usize;
        let mut leases = self.leases.lock().unwrap();
        let t = Instant::now();
        leases.retain(|_, l| l.until > t);
        let conn = self.conn.lock().unwrap();
        let mut st = conn
            .prepare_cached(
                "SELECT id, path, size, modified FROM files
                 WHERE state = 0 OR (state = 2 AND (error IS NULL OR version < ?1)) OR (state IN (3, 4) AND version < ?1)
                 ORDER BY id LIMIT ?2",
            )
            .map_err(db)?;
        let limit = (count + leases.len()) as i64;
        let rows = st
            .query_map(params![version as i64, limit], |r| Ok((r.get::<_, i64>(0)?, r.get::<_, String>(1)?, r.get::<_, i64>(2)?, r.get::<_, i64>(3)?)))
            .map_err(db)?
            .collect::<Result<Vec<_>, _>>()
            .map_err(db)?;
        let mut out = Vec::new();
        for (id, path, size, modified) in rows {
            if out.len() == count {
                break;
            }
            if leases.contains_key(&id) {
                continue;
            }
            leases.insert(id, Lease { until: t + LEASE, staged: None });
            out.push(json!({"id": id, "path": path, "size": size, "modified": modified}));
        }
        Ok(out)
    }

    pub fn path_of(&self, id: i64) -> Option<String> {
        let conn = self.conn.lock().unwrap();
        conn.query_row("SELECT path FROM files WHERE id = ?1", [id], |r| r.get(0)).optional().ok().flatten()
    }

    /// Stages chunks; on `last`, publishes them in one transaction (IDX-17).
    /// `present` is the name index's stamp of the file now, if it has one.
    #[allow(clippy::too_many_arguments)]
    pub fn put(&self, id: i64, version: u32, read: Stamp, after: Stamp, chunks: &[ContentChunk], last: bool, present: Option<Stamp>) -> Result<(), String> {
        let mut leases = self.leases.lock().unwrap();
        let mut conn = self.conn.lock().unwrap();
        let state: Option<i64> = conn.query_row("SELECT state FROM files WHERE id = ?1", [id], |r| r.get(0)).optional().map_err(db)?;
        match state {
            None => return Err(format!("content file {id} is unknown (removed from its scope?)")),
            Some(UNAVAILABLE) => return Err(format!("content file {id} is unavailable: its store is not ready")),
            _ => {}
        }
        let lease = leases.entry(id).or_insert(Lease { until: Instant::now() + LEASE, staged: None });
        if lease.staged.is_none() {
            Self::drop_staged(&conn, id)?;
            lease.staged = Some(0);
        }
        let bytes: u64 = chunks.iter().map(|c| c.text.len() as u64).sum();
        if let Some(c) = chunks.iter().find(|c| c.text.len() > MAX_CHUNK) {
            return Err(format!("a chunk has {} bytes of text; the limit is {MAX_CHUNK}", c.text.len()));
        }
        if lease.staged.unwrap() + bytes > MAX_FILE_TEXT {
            Self::drop_staged(&conn, id)?;
            leases.remove(&id);
            return Err(format!("more than {MAX_FILE_TEXT} bytes of text for one file; staged chunks were dropped"));
        }
        let tx = conn.transaction().map_err(db)?;
        {
            let mut chunk = tx.prepare_cached("INSERT INTO chunks(file, location) VALUES(?1, ?2)").map_err(db)?;
            let mut term = tx.prepare_cached("INSERT INTO terms(rowid, text) VALUES(?1, ?2)").map_err(db)?;
            for c in chunks {
                let location = serde_json::to_string(&c.location).unwrap_or_else(|_| "null".into());
                chunk.execute(params![-id, location]).map_err(db)?;
                term.execute(params![tx.last_insert_rowid(), c.text]).map_err(db)?;
            }
        }
        if last {
            tx.execute("DELETE FROM terms WHERE rowid IN (SELECT id FROM chunks WHERE file = ?1)", [id]).map_err(db)?;
            tx.execute("DELETE FROM chunks WHERE file = ?1", [id]).map_err(db)?;
            tx.execute("UPDATE chunks SET file = ?1 WHERE file = -?1", [id]).map_err(db)?;
            let state = if read == after && present == Some(read) { CURRENT } else { STALE };
            let (size, modified) = present.map(|p| (Some(p.size as i64), Some(p.modified as i64))).unwrap_or((None, None));
            tx.execute(
                "UPDATE files SET state = ?2, error = NULL, indexed = ?3, published = 1, psize = ?4, pmodified = ?5, version = ?6,
                 size = COALESCE(?7, size), modified = COALESCE(?8, modified) WHERE id = ?1",
                params![id, state, now() as i64, read.size as i64, read.modified as i64, version as i64, size, modified],
            )
            .map_err(db)?;
        }
        tx.commit().map_err(db)?;
        if last {
            leases.remove(&id);
        } else if let Some(l) = leases.get_mut(&id) {
            l.until = Instant::now() + LEASE;
            l.staged = Some(l.staged.unwrap_or(0) + bytes);
        }
        Ok(())
    }

    /// Records a failed read (IDX-17). A file that changed since the worker
    /// read it stays queued.
    pub fn fail(&self, id: i64, version: u32, read: Stamp, error: &str, unsupported: bool) -> Result<(), String> {
        let mut leases = self.leases.lock().unwrap();
        leases.remove(&id);
        let conn = self.conn.lock().unwrap();
        Self::drop_staged(&conn, id)?;
        let row: Option<(i64, i64, i64, i64)> = conn
            .query_row("SELECT state, size, modified, published FROM files WHERE id = ?1", [id], |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?, r.get(3)?)))
            .optional()
            .map_err(db)?;
        let Some((state, size, modified, published)) = row else { return Err(format!("content file {id} is unknown")) };
        if state == UNAVAILABLE || (read.size as i64, read.modified as i64) != (size, modified) {
            return Ok(());
        }
        let state = if published != 0 { STALE } else if unsupported { UNSUPPORTED } else { FAILED };
        let error: String = error.chars().take(1000).collect();
        conn.execute("UPDATE files SET state = ?2, error = ?3, version = ?4 WHERE id = ?1", params![id, state, error, version as i64]).map_err(db)?;
        Ok(())
    }

    // ------------------------------------------------------------ reporting

    /// Badge states of the files directly in `folder`.
    pub fn state(&self, folder: &str) -> Result<Vec<Value>, String> {
        let dir = folder.trim_end_matches('\\');
        let conn = self.conn.lock().unwrap();
        let mut st = conn.prepare_cached("SELECT path, state, indexed, error FROM files WHERE path LIKE ?1 ESCAPE '^' ORDER BY path").map_err(db)?;
        let rows = st
            .query_map([below(dir)], |r| Ok((r.get::<_, String>(0)?, r.get::<_, i64>(1)?, r.get::<_, i64>(2)?, r.get::<_, Option<String>>(3)?)))
            .map_err(db)?
            .collect::<Result<Vec<_>, _>>()
            .map_err(db)?;
        Ok(rows
            .into_iter()
            .filter(|(p, ..)| under(p, dir) && !p[dir.len() + 1..].contains('\\'))
            .map(|(path, state, indexed, error)| json!({"path": path, "state": state_name(state), "indexed": indexed, "error": error}))
            .collect())
    }

    pub fn status(&self) -> Result<ContentStatus, String> {
        let scopes = self.scopes()?;
        let conn = self.conn.lock().unwrap();
        let mut out = ContentStatus::default();
        for s in scopes {
            let mut c = ContentScopeStatus { path: s.path.clone(), reconciled: s.reconciled, error: s.error.clone(), ..Default::default() };
            for r in Self::rows_below(&conn, &s.path)? {
                c.files += 1;
                match r.state {
                    PENDING => c.pending += 1,
                    CURRENT => c.current += 1,
                    STALE => c.stale += 1,
                    FAILED => c.failed += 1,
                    UNSUPPORTED => c.unsupported += 1,
                    _ => c.unavailable += 1,
                }
            }
            out.scopes.push(c);
        }
        let waiting: i64 = conn.query_row("SELECT count(*) FROM files WHERE state = 0 OR (state = 2 AND error IS NULL)", [], |r| r.get(0)).map_err(db)?;
        out.waiting = waiting as u64;
        if let Some(f) = &self.file {
            for suffix in ["", "-wal", "-shm"] {
                let mut p = f.clone().into_os_string();
                p.push(suffix);
                out.store_bytes += std::fs::metadata(&p).map(|m| m.len()).unwrap_or(0);
            }
        }
        Ok(out)
    }
}

impl query::Content for ContentStore {
    fn matching(&self, expr: &str) -> Result<Vec<String>, String> {
        let conn = self.conn.lock().unwrap();
        let mut st = conn
            .prepare_cached("SELECT DISTINCT f.path FROM terms JOIN chunks c ON c.id = terms.rowid JOIN files f ON f.id = c.file WHERE terms MATCH ?1 AND f.state IN (1, 2)")
            .map_err(db)?;
        let v = st.query_map([expr], |r| r.get(0)).map_err(db)?.collect::<Result<Vec<String>, _>>().map_err(|e| format!("content search failed: {e}"))?;
        Ok(v)
    }

    fn locations(&self, path: &str, exprs: &[String], limit: u32) -> Vec<Value> {
        let expr = exprs.iter().map(|e| format!("({e})")).collect::<Vec<_>>().join(" OR ");
        let conn = self.conn.lock().unwrap();
        let run = || -> rusqlite::Result<Vec<Value>> {
            let id: Option<i64> = conn.query_row("SELECT id FROM files WHERE path = ?1 AND state IN (1, 2)", [path], |r| r.get(0)).optional()?;
            let Some(id) = id else {
                return Ok(Vec::new());
            };
            let (lo, hi): (Option<i64>, Option<i64>) = conn.query_row("SELECT min(id), max(id) FROM chunks WHERE file = ?1", [id], |r| Ok((r.get(0)?, r.get(1)?)))?;
            let (Some(lo), Some(hi)) = (lo, hi) else { return Ok(Vec::new()) };
            let mut st = conn.prepare_cached(
                "SELECT location FROM chunks WHERE file = ?1 AND id IN (SELECT rowid FROM terms WHERE terms MATCH ?2 AND rowid BETWEEN ?3 AND ?4) ORDER BY id LIMIT ?5",
            )?;
            let v = st
                .query_map(params![id, expr, lo, hi, limit as i64], |r| r.get::<_, String>(0))?
                .filter_map(|s| s.ok().and_then(|s| serde_json::from_str(&s).ok()))
                .collect();
            Ok(v)
        };
        run().unwrap_or_else(|e| {
            warning!("content locations for {path}: {e}");
            Vec::new()
        })
    }
}

// ---------------------------------------------------------------- service side

/// Opens the store in `dir`, creating the folder (protected for the service).
pub fn open_dir(dir: &Path, protect: bool) -> Result<ContentStore, String> {
    if !dir.exists() {
        if let Some(parent) = dir.parent() {
            std::fs::create_dir_all(parent).map_err(|e| format!("{}: {e}", parent.display()))?;
        }
        if protect {
            let sd = LocalSd::from_sddl(CONTENT_SDDL).ok_or("bad content folder descriptor")?;
            let sa = sd.attributes();
            if unsafe { CreateDirectoryW(wide(&dir.to_string_lossy()).as_ptr(), &sa) } == 0 {
                return Err(format!("creating {} failed: error {}", dir.display(), last_error()));
            }
        } else {
            std::fs::create_dir_all(dir).map_err(|e| format!("{}: {e}", dir.display()))?;
        }
    }
    ContentStore::open(Some(dir))
}

/// Whether a slot's store can answer for its files: it has a tree, even if
/// a re-read or rescan is replacing it.
fn ready(slot: &Slot) -> bool {
    matches!(slot.state(), "ready" | "scanning" | "reloading")
}

/// The most specific store whose root is `path` or above it.
fn covering<'a>(slots: &'a [Arc<Slot>], path: &str) -> Option<&'a Arc<Slot>> {
    slots.iter().filter(|s| same(path, s.name.trim_end_matches('\\')) || under(path, s.name.trim_end_matches('\\'))).max_by_key(|s| s.name.len())
}

/// The name index's view of one scope.
fn listing(scope: &str, slots: &[Arc<Slot>], refs: &[StoreRef]) -> Result<Vec<(String, u64, u64)>, String> {
    let covering = covering(slots, scope);
    let Some(slot) = covering else { return Err("not on an indexed volume or folder".into()) };
    if !ready(slot) {
        return Err(format!("{} is {}", slot.name, slot.state()));
    }
    let Some(sr) = refs.iter().find(|r| r.slot == slot.id) else { return Err(format!("{} is not loaded", slot.name)) };
    match query::resolve_paths(std::slice::from_ref(sr), &[scope.to_string()], true)[0] {
        Some((_, rec)) => Ok(query::subtree_files(sr, rec)),
        None => Err("the folder is not in the index".into()),
    }
}

pub fn reconcile_all(svc: &Service) {
    let Some(store) = &svc.content else { return };
    let scopes = match store.scopes() {
        Ok(s) => s,
        Err(e) => {
            warning!("{e}");
            return;
        }
    };
    if scopes.is_empty() {
        return;
    }
    let (slots, refs) = svc.snapshot();
    for s in scopes {
        if let Err(e) = store.reconcile(&s.path, listing(&s.path, &slots, &refs)) {
            warning!("content {}: {e}", s.path);
        }
    }
}

/// The reconcile loop: every minute, and when poked.
pub fn run(svc: Arc<Service>) {
    while !svc.stopping() {
        reconcile_all(&svc);
        svc.content_wait(RECONCILE);
    }
}

/// The name index's stamp of a file, for `content_put`.
pub fn present(refs: &[StoreRef], path: &str) -> Option<Stamp> {
    let (slot, r) = query::resolve_paths(refs, &[path.to_string()], false)[0]?;
    let sr = refs.iter().find(|s| s.slot == slot)?;
    let st = sr.store.read().unwrap();
    let rec = st.get(r)?;
    (!rec.is_dir()).then_some(Stamp { size: rec.size, modified: rec.mtime })
}

/// Who may see `content_state` for a folder: its folder root's owner, or a
/// caller who may list it.
pub fn may_list(svc: &Service, sid: &str, token: pathweft_index::ffi::HANDLE, folder: &str) -> bool {
    let slots = svc.all();
    let dir = folder.trim_end_matches('\\');
    if let Some(s) = covering(&slots, dir) {
        if let Kind::Folder(f) = &s.kind {
            return f.owner.eq_ignore_ascii_case(sid);
        }
    }
    crate::security::list_access(dir, token) == Some(true)
}

#[cfg(test)]
mod tests {
    use super::*;
    use pathweft_index::query::Content;

    fn st(size: u64, modified: u64) -> Stamp {
        Stamp { size, modified }
    }
    fn files(v: &[(&str, u64, u64)]) -> Result<Vec<(String, u64, u64)>, String> {
        Ok(v.iter().map(|(p, s, m)| (p.to_string(), *s, *m)).collect())
    }
    fn chunk(line: u64, text: &str) -> ContentChunk {
        ContentChunk { location: json!({"line": line, "column": 1, "offset": 0}), text: text.into() }
    }
    fn states(c: &ContentStore, folder: &str) -> Vec<(String, String)> {
        c.state(folder).unwrap().iter().map(|v| (v["path"].as_str().unwrap().to_string(), v["state"].as_str().unwrap().to_string())).collect()
    }
    fn ids(c: &ContentStore, version: u32) -> Vec<(i64, String)> {
        c.next(100, version).unwrap().iter().map(|v| (v["id"].as_i64().unwrap(), v["path"].as_str().unwrap().to_string())).collect()
    }
    fn release(c: &ContentStore) {
        c.leases.lock().unwrap().clear();
    }

    #[test]
    fn scope_paths_normalize_and_compare_by_component() {
        assert_eq!(normalize(r"d:\Docs\").unwrap(), r"D:\Docs");
        assert_eq!(normalize(r"D:\").unwrap(), "D:");
        assert_eq!(normalize(r"\\server\share\x").unwrap(), r"\\server\share\x");
        for bad in ["Docs", r"D:\a\..\b", r"\\server", ""] {
            assert!(normalize(bad).is_err(), "{bad}");
        }
        assert!(under(r"D:\Docs\a.txt", r"d:\docs"));
        assert!(!under(r"D:\Docs2\a.txt", r"D:\Docs"));
        assert!(!under(r"D:\Docs", r"D:\Docs"));
        assert!(under(r"D:\x", "D:"));
        assert_eq!(below(r"D:\a_b%"), r"D:\a^_b^%\%");
    }

    #[test]
    fn scopes_absorb_children_and_ignore_covered_folders() {
        let c = ContentStore::open(None).unwrap();
        c.scope_add(r"D:\Docs\Work").unwrap();
        c.scope_add(r"D:\Docs\Home\").unwrap();
        assert!(c.scope_add(r"d:\docs\work\2026").unwrap().contains("already covered"));
        assert!(c.scope_add(r"D:\Docs\Work").unwrap().contains("already covered"));
        let msg = c.scope_add(r"D:\Docs").unwrap();
        assert!(msg.contains("absorbs 2"), "{msg}");
        let paths: Vec<String> = c.scopes().unwrap().into_iter().map(|s| s.path).collect();
        assert_eq!(paths, vec![r"D:\Docs".to_string()]);
        assert!(c.scope_remove(r"D:\Docs\Work").is_err(), "only exact scopes are removed");
    }

    #[test]
    fn remove_purges_only_the_scope_files() {
        let c = ContentStore::open(None).unwrap();
        c.scope_add(r"D:\A").unwrap();
        c.scope_add(r"D:\AB").unwrap();
        c.reconcile(r"D:\A", files(&[(r"D:\A\1.txt", 1, 1)])).unwrap();
        c.reconcile(r"D:\AB", files(&[(r"D:\AB\2.txt", 2, 2)])).unwrap();
        let (id, _) = ids(&c, 1)[0].clone();
        c.put(id, 1, st(1, 1), st(1, 1), &[chunk(1, "alpha")], true, Some(st(1, 1))).unwrap();
        assert_eq!(c.scope_remove(r"d:\a\").unwrap(), 1);
        assert_eq!(states(&c, r"D:\AB"), vec![(r"D:\AB\2.txt".into(), "pending".into())]);
        assert!(c.matching("\"alpha\"").unwrap().is_empty());
        let left: i64 = c.conn.lock().unwrap().query_row("SELECT count(*) FROM chunks", [], |r| r.get(0)).unwrap();
        assert_eq!(left, 0);
    }

    #[test]
    fn reconcile_tracks_new_changed_moved_gone_and_unavailable_files() {
        let c = ContentStore::open(None).unwrap();
        c.scope_add(r"D:\Docs").unwrap();
        c.reconcile(r"D:\Docs", files(&[(r"D:\Docs\a.txt", 10, 100), (r"D:\Docs\b.txt", 20, 200), (r"D:\Docs\c.md", 30, 300), (r"D:\Docs\sub\d.txt", 40, 400)])).unwrap();
        assert_eq!(c.status().unwrap().scopes[0].pending, 4);
        let work = ids(&c, 1);
        assert_eq!(work.len(), 4);
        assert!(ids(&c, 1).is_empty(), "leased files are not handed out again");
        let id = |p: &str| work.iter().find(|(_, x)| x.ends_with(p)).unwrap().0;
        c.put(id("a.txt"), 1, st(10, 100), st(10, 100), &[chunk(1, "invoice 2026")], true, Some(st(10, 100))).unwrap();
        c.put(id("b.txt"), 1, st(20, 200), st(20, 200), &[chunk(1, "receipt")], true, Some(st(20, 200))).unwrap();
        c.fail(id("c.md"), 1, st(30, 300), "bad encoding", false).unwrap();
        c.fail(id("d.txt"), 1, st(40, 400), "binary", true).unwrap();
        assert_eq!(
            states(&c, r"D:\Docs"),
            vec![(r"D:\Docs\a.txt".into(), "current".into()), (r"D:\Docs\b.txt".into(), "current".into()), (r"D:\Docs\c.md".into(), "failed".into())]
        );
        release(&c);
        // a.txt changes, b.txt moves, c.md changes, d.txt is deleted, e.txt is new.
        c.reconcile(
            r"D:\Docs",
            files(&[(r"D:\Docs\a.txt", 11, 101), (r"D:\Docs\moved\b.txt", 20, 200), (r"D:\Docs\c.md", 31, 301), (r"D:\Docs\e.txt", 50, 500)]),
        )
        .unwrap();
        let s = states(&c, r"D:\Docs");
        assert_eq!(s, vec![(r"D:\Docs\a.txt".into(), "stale".into()), (r"D:\Docs\c.md".into(), "pending".into()), (r"D:\Docs\e.txt".into(), "pending".into())]);
        assert_eq!(states(&c, r"D:\Docs\moved"), vec![(r"D:\Docs\moved\b.txt".into(), "current".into())]);
        assert!(states(&c, r"D:\Docs\sub").is_empty(), "deleted while the store was ready: purged");
        // Stale files keep answering; the moved file keeps its terms.
        assert_eq!(c.matching("\"invoice\"").unwrap(), vec![r"D:\Docs\a.txt".to_string()]);
        assert_eq!(c.matching("\"receipt\"").unwrap(), vec![r"D:\Docs\moved\b.txt".to_string()]);
        // The store goes offline: everything becomes unavailable, nothing is lost.
        c.reconcile(r"D:\Docs", Err("D: is offline".into())).unwrap();
        let st0 = c.status().unwrap();
        assert_eq!((st0.scopes[0].unavailable, st0.scopes[0].files), (4, 4));
        assert_eq!(st0.scopes[0].error.as_deref(), Some("D: is offline"));
        assert!(ids(&c, 1).is_empty(), "unavailable files are not handed out");
        // Back, with c.md changed again: earlier states return.
        c.reconcile(
            r"D:\Docs",
            files(&[(r"D:\Docs\a.txt", 11, 101), (r"D:\Docs\moved\b.txt", 20, 200), (r"D:\Docs\c.md", 32, 302), (r"D:\Docs\e.txt", 50, 500)]),
        )
        .unwrap();
        let s = states(&c, r"D:\Docs");
        assert_eq!(s[0].1, "stale");
        assert_eq!(s[1].1, "pending");
        assert_eq!(states(&c, r"D:\Docs\moved")[0].1, "current");
        assert!(c.status().unwrap().scopes[0].error.is_none());
    }

    #[test]
    fn ambiguous_moves_are_new_files() {
        let c = ContentStore::open(None).unwrap();
        c.scope_add(r"D:\M").unwrap();
        c.reconcile(r"D:\M", files(&[(r"D:\M\a.txt", 5, 9), (r"D:\M\b.txt", 5, 9)])).unwrap();
        for (id, _) in ids(&c, 1) {
            c.put(id, 1, st(5, 9), st(5, 9), &[chunk(1, "same")], true, Some(st(5, 9))).unwrap();
        }
        c.reconcile(r"D:\M", files(&[(r"D:\M\x\a.txt", 5, 9), (r"D:\M\x\b.txt", 5, 9)])).unwrap();
        assert!(c.matching("\"same\"").unwrap().is_empty());
        assert_eq!(states(&c, r"D:\M\x").iter().filter(|(_, s)| s == "pending").count(), 2);
    }

    #[test]
    fn staged_chunks_publish_atomically_and_stale_on_change() {
        let c = ContentStore::open(None).unwrap();
        c.scope_add(r"D:\P").unwrap();
        c.reconcile(r"D:\P", files(&[(r"D:\P\big.txt", 100, 7)])).unwrap();
        let id = ids(&c, 1)[0].0;
        c.put(id, 1, st(100, 7), st(100, 7), &[chunk(1, "first version")], true, Some(st(100, 7))).unwrap();
        c.reconcile(r"D:\P", files(&[(r"D:\P\big.txt", 120, 8)])).unwrap();
        let id2 = ids(&c, 1)[0].0;
        assert_eq!(id2, id, "a stale file is queued again");
        c.put(id, 1, st(120, 8), st(120, 8), &[chunk(1, "second part one")], false, Some(st(120, 8))).unwrap();
        assert_eq!(c.matching("\"first\"").unwrap().len(), 1, "staged chunks do not replace the published ones yet");
        assert!(c.matching("\"second\"").unwrap().is_empty(), "staged chunks are not searchable");
        // The file changed again while it was read: published, but stale.
        c.put(id, 1, st(120, 8), st(130, 9), &[chunk(9, "second part two")], true, Some(st(130, 9))).unwrap();
        assert!(c.matching("\"first\"").unwrap().is_empty());
        assert_eq!(c.matching("\"second\"").unwrap().len(), 1);
        assert_eq!(states(&c, r"D:\P")[0].1, "stale");
        let locs = c.locations(r"d:\p\big.txt", &["\"two\"".into()], 5);
        assert_eq!(locs, vec![json!({"line": 9, "column": 1, "offset": 0})]);
        let locs = c.locations(r"D:\P\big.txt", &["\"two\"".into(), "\"one\"".into()], 1);
        assert_eq!(locs, vec![json!({"line": 1, "column": 1, "offset": 0})], "document order, limited");
        // A later stamp check: the published stamp is what was read.
        let (ps, pm): (i64, i64) = c.conn.lock().unwrap().query_row("SELECT psize, pmodified FROM files WHERE id = ?1", [id], |r| Ok((r.get(0)?, r.get(1)?))).unwrap();
        assert_eq!((ps, pm), (120, 8));
        // Limits.
        let huge = ContentChunk { location: json!(null), text: "x".repeat(MAX_CHUNK + 1) };
        assert!(c.put(id, 1, st(130, 9), st(130, 9), &[huge], false, None).is_err());
        assert!(c.put(999, 1, st(1, 1), st(1, 1), &[], true, None).is_err());
    }

    #[test]
    fn failures_retry_by_version_and_keep_published_terms() {
        let c = ContentStore::open(None).unwrap();
        c.scope_add(r"D:\F").unwrap();
        c.reconcile(r"D:\F", files(&[(r"D:\F\a.pdf", 1, 1), (r"D:\F\b.bin", 2, 2), (r"D:\F\c.txt", 3, 3)])).unwrap();
        let w = ids(&c, 1);
        c.fail(w[0].0, 1, st(1, 1), "encrypted", false).unwrap();
        c.fail(w[1].0, 1, st(2, 2), "binary", true).unwrap();
        c.put(w[2].0, 1, st(3, 3), st(3, 3), &[chunk(1, "kept")], true, Some(st(3, 3))).unwrap();
        c.reconcile(r"D:\F", files(&[(r"D:\F\a.pdf", 1, 1), (r"D:\F\b.bin", 2, 2), (r"D:\F\c.txt", 4, 4)])).unwrap();
        release(&c);
        let cid = ids(&c, 1);
        assert_eq!(cid.len(), 1, "same version: only the stale file");
        c.fail(cid[0].0, 1, st(4, 4), "locked", false).unwrap();
        release(&c);
        assert!(ids(&c, 1).is_empty(), "a stale file with an error waits for a change or a new version");
        assert_eq!(c.matching("\"kept\"").unwrap().len(), 1, "a failed re-read keeps the published terms");
        let s = c.status().unwrap();
        assert_eq!((s.scopes[0].failed, s.scopes[0].unsupported, s.scopes[0].stale, s.waiting), (1, 1, 1, 0));
        assert_eq!(ids(&c, 2).len(), 3, "a newer extractor retries failures");
        release(&c);
        // A failure for a file that changed since it was read is ignored.
        c.fail(w[0].0, 2, st(9, 9), "old read", false).unwrap();
        let err: Option<String> = c.conn.lock().unwrap().query_row("SELECT error FROM files WHERE id = ?1", [w[0].0], |r| r.get(0)).unwrap();
        assert_eq!(err.as_deref(), Some("encrypted"));
    }

    #[test]
    fn restart_drops_staged_chunks_and_keeps_data() {
        // Beside the test binary (the build's target folder), not the system temp.
        let dir = std::env::current_exe().unwrap().parent().unwrap().join(format!("pwcontent-test-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        {
            let c = ContentStore::open(Some(&dir)).unwrap();
            c.scope_add(r"D:\R").unwrap();
            c.reconcile(r"D:\R", files(&[(r"D:\R\a.txt", 1, 1), (r"D:\R\b.txt", 2, 2)])).unwrap();
            let w = ids(&c, 1);
            c.put(w[0].0, 1, st(1, 1), st(1, 1), &[chunk(1, "durable")], true, Some(st(1, 1))).unwrap();
            c.put(w[1].0, 1, st(2, 2), st(2, 2), &[chunk(1, "halfway")], false, Some(st(2, 2))).unwrap();
            assert!(c.status().unwrap().store_bytes > 0);
        }
        let c = ContentStore::open(Some(&dir)).unwrap();
        assert_eq!(c.matching("\"durable\"").unwrap().len(), 1);
        let staged: i64 = c.conn.lock().unwrap().query_row("SELECT count(*) FROM chunks WHERE file < 0", [], |r| r.get(0)).unwrap();
        assert_eq!(staged, 0);
        assert_eq!(ids(&c, 1).len(), 1, "the unfinished file is queued again");
        drop(c);
        std::fs::remove_dir_all(&dir).unwrap();
    }
}
