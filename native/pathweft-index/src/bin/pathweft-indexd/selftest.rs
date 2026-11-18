//! `selftest --volume X:` (SPEC IDX-14): runs the service core against a
//! disposable NTFS volume and checks load, churn, USN application, forced
//! re-read, folder sizes, visibility and the pipe round trip. Needs an
//! elevated token (raw volume access). Writes only below `X:\pwtest`.

use crate::security::LocalSd;
use crate::service;
use crate::state::Options;
use pathweft_index::client::Client;
use pathweft_index::ffi::*;
use pathweft_index::protocol::*;
use serde::Serialize;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

#[derive(Serialize)]
struct Check {
    name: String,
    ok: bool,
    ms: u64,
    detail: String,
}

#[derive(Serialize)]
struct Report {
    volume: String,
    all_ok: bool,
    checks: Vec<Check>,
    status: Option<Status>,
}

struct T {
    pipe: String,
    checks: Vec<Check>,
}

impl T {
    fn client(&self) -> Result<Client, String> {
        Client::connect_to(&self.pipe, Duration::from_secs(5)).map_err(|e| e.to_string())
    }
    fn req(&self, r: &Request) -> Result<Response, String> {
        let resp = self.client()?.request(r).map_err(|e| e.to_string())?;
        if !resp.ok {
            return Err(resp.error.unwrap_or_default());
        }
        Ok(resp)
    }
    fn paths(&self, q: &str) -> Result<Vec<String>, String> {
        let r = self.req(&Request::Query(QueryRequest { query: q.into(), count: 100_000, sort: SortKey::Path, ..Default::default() }))?;
        Ok(r.hits.unwrap_or_default().into_iter().map(|h| h.path).collect())
    }
    fn hits(&self, q: &str) -> Result<Vec<Hit>, String> {
        let r = self.req(&Request::Query(QueryRequest { query: q.into(), count: 100_000, sort: SortKey::Path, ..Default::default() }))?;
        Ok(r.hits.unwrap_or_default())
    }
    fn status(&self) -> Result<Status, String> {
        self.req(&Request::Status)?.status.ok_or_else(|| "no status".into())
    }
    fn record(&mut self, name: &str, t: Instant, result: Result<String, String>) {
        let (ok, detail) = match result {
            Ok(d) => (true, d),
            Err(d) => (false, d),
        };
        println!("{} {name}: {detail}", if ok { "PASS" } else { "FAIL" });
        self.checks.push(Check { name: name.into(), ok, ms: t.elapsed().as_millis() as u64, detail });
    }
    /// Polls `f` until it returns Ok or `limit` passes.
    fn until(&self, limit: Duration, mut f: impl FnMut(&T) -> Result<String, String>) -> Result<String, String> {
        let end = Instant::now() + limit;
        loop {
            let r = f(self);
            if r.is_ok() || Instant::now() >= end {
                return r;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
    }
}

fn write(path: &Path, len: usize) -> std::io::Result<()> {
    fs::write(path, vec![b'x'; len])
}

fn tree_size(dir: &Path) -> u64 {
    let mut total = 0;
    if let Ok(rd) = fs::read_dir(dir) {
        for e in rd.flatten() {
            let Ok(md) = e.metadata() else { continue };
            if md.is_dir() {
                total += tree_size(&e.path());
            } else {
                total += md.len();
            }
        }
    }
    total
}

fn expect(cond: bool, ok: String, bad: String) -> Result<String, String> {
    if cond { Ok(ok) } else { Err(bad) }
}

pub fn run(volume: &str, report_path: Option<PathBuf>) -> Result<bool, String> {
    let letter = crate::config::letter(volume).ok_or("bad --volume")?;
    let base = PathBuf::from(format!(r"{letter}\pwtest"));
    let b = |rel: &str| format!(r"{letter}\pwtest\{rel}");
    let _ = fs::remove_dir_all(&base);
    fs::create_dir_all(base.join(r"a\deep\deeper")).map_err(|e| e.to_string())?;
    fs::create_dir_all(base.join("b")).map_err(|e| e.to_string())?;
    for (rel, len) in [(r"a\alpha.txt", 100), (r"a\deep\beta.log", 2000), (r"a\deep\deeper\gamma.bin", 30_000), (r"b\delta.txt", 7), (r"b\resize-me.dat", 10)] {
        write(&base.join(rel), len).map_err(|e| e.to_string())?;
    }
    // A folder the test user may add files to but not list.
    let me = current_user_sid().ok_or("no user SID")?;
    let sd = LocalSd::from_sddl(&format!("D:P(A;OICI;FA;;;SY)(A;;0x100006;;;{me})")).ok_or("bad SDDL")?;
    let sa = sd.attributes();
    let secret = base.join("secret");
    let secret_ok = unsafe { CreateDirectoryW(wide(&secret.to_string_lossy()).as_ptr(), &sa) } != 0;
    // Moving a file in needs only FILE_ADD_FILE on the folder.
    let secret_file = secret_ok
        && write(&base.join("hidden-item.txt"), 5000).is_ok()
        && fs::rename(base.join("hidden-item.txt"), secret.join("hidden-item.txt")).is_ok();

    let pipe = format!(r"\\.\pipe\PathweftIndex-selftest-{}", std::process::id());
    let opts = Options { pipe: pipe.clone(), config_path: base.join("no-config.json"), volumes: Some(vec![letter.clone()]), folders: false, persist_config: false };
    let svc = service::run(opts)?;
    let mut t = T { pipe, checks: Vec::new() };

    // 1. initial load
    let s0 = Instant::now();
    let r = t.until(Duration::from_secs(120), |t| {
        let st = t.status()?;
        let v = st.stores.iter().find(|s| s.name == letter).ok_or("volume not listed")?;
        expect(v.state == "ready", format!("ready in {} ms, {} items, journal {:#x}", v.load_ms, v.items, v.journal_id), format!("state {}", v.state))
    });
    t.record("load", s0, r);

    let s = Instant::now();
    let r = (|| {
        let hits = t.hits("alpha.txt")?;
        let h = hits.iter().find(|h| h.path.eq_ignore_ascii_case(&b(r"a\alpha.txt"))).ok_or(format!("alpha.txt missing: {hits:?}"))?;
        expect(h.size == 100 && !h.folder && h.modified > 0, format!("{} size {}", h.path, h.size), format!("wrong fields {h:?}"))
    })();
    t.record("query-after-load", s, r);

    // 2. churn: create, rename, move, resize, delete
    let s = Instant::now();
    let churn = (|| -> std::io::Result<()> {
        write(&base.join(r"b\created.txt"), 1234)?;
        fs::rename(base.join(r"a\alpha.txt"), base.join(r"a\renamed-alpha.txt"))?;
        fs::rename(base.join(r"a\deep"), base.join(r"b\deep-moved"))?;
        write(&base.join(r"b\resize-me.dat"), 5000)?;
        fs::remove_file(base.join(r"b\delta.txt"))?;
        Ok(())
    })();
    let r = match churn {
        Err(e) => Err(format!("churn failed: {e}")),
        Ok(()) => t.until(Duration::from_secs(2), |t| {
            let created = t.hits("created.txt")?;
            let renamed = t.paths("renamed-alpha.txt")?;
            let old = t.paths("alpha.txt")?;
            let moved = t.paths("gamma.bin")?;
            let resized = t.hits("resize-me.dat")?;
            let deleted = t.paths("delta.txt")?;
            let ok = created.iter().any(|h| h.size == 1234)
                && renamed == vec![b(r"a\renamed-alpha.txt")]
                && old.iter().all(|p| !p.ends_with(r"\alpha.txt"))
                && moved == vec![b(r"b\deep-moved\deeper\gamma.bin")]
                && resized.first().map(|h| h.size) == Some(5000)
                && deleted.is_empty();
            expect(ok, "create, rename, move, resize and delete visible".into(), format!("created {created:?} renamed {renamed:?} moved {moved:?} resized {resized:?} deleted {deleted:?}"))
        }),
    };
    let r = r.map(|d| format!("{d} after {} ms", s.elapsed().as_millis()));
    t.record("churn-within-2s", s, r);

    // 3. folder sizes match the file system (visible part only)
    let s = Instant::now();
    let r = t.until(Duration::from_secs(2), |t| {
        let r = t.req(&Request::FolderSizes { paths: vec![b(""), b("b"), b(r"b\deep-moved"), b("missing-folder")] })?;
        let sizes = r.sizes.unwrap_or_default();
        // The walk itself may or may not be able to list the secret folder.
        let hidden = if secret_file && fs::read_dir(&secret).is_ok() { 5000 } else { 0 };
        let want = [tree_size(&base) - hidden, tree_size(&base.join("b")), tree_size(&base.join(r"b\deep-moved"))];
        let got: Vec<Option<u64>> = sizes.iter().map(|f| f.size).collect();
        let ok = got.len() == 4 && got[0] == Some(want[0]) && got[1] == Some(want[1]) && got[2] == Some(want[2]) && got[3].is_none();
        expect(ok, format!("sizes {got:?}"), format!("got {got:?}, want {want:?} and none"))
    });
    t.record("folder-sizes", s, r);

    // 4. visibility: entries of a folder the caller cannot list are hidden
    let s = Instant::now();
    let r = if !secret_file {
        Err("could not create the unlistable test folder".to_string())
    } else {
        (|| {
            let hidden = t.paths("hidden-item")?;
            let folder = t.paths("secret")?;
            expect(hidden.is_empty() && folder == vec![b("secret")], "unlistable folder's entry hidden, folder itself listed".into(), format!("hidden {hidden:?} folder {folder:?}"))
        })()
    };
    t.record("visibility", s, r);

    // 5. bulk churn with concurrent queries
    let s = Instant::now();
    let bulk = base.join("bulk");
    let _ = fs::create_dir_all(&bulk);
    let pipe = t.pipe.clone();
    let writer = std::thread::spawn(move || {
        for i in 0..5000 {
            let _ = fs::write(bulk.join(format!("bulk-{i:05}.tmp")), b"0123456789");
        }
    });
    let mut lat: Vec<u128> = Vec::new();
    let mut failures = 0;
    while !writer.is_finished() {
        let q = Instant::now();
        let ok = Client::connect_to(&pipe, Duration::from_secs(5))
            .and_then(|mut c| c.request(&Request::Query(QueryRequest { query: "bulk-".into(), count: 100, ..Default::default() })))
            .map(|r| r.ok)
            .unwrap_or(false);
        lat.push(q.elapsed().as_micros());
        if !ok {
            failures += 1;
        }
    }
    let _ = writer.join();
    lat.sort_unstable();
    let p = |q: f64| lat.get(((lat.len() as f64 - 1.0) * q) as usize).copied().unwrap_or(0) / 1000;
    let r = t.until(Duration::from_secs(5), |t| {
        let n = t.req(&Request::Query(QueryRequest { query: "bulk-*.tmp".into(), count: 0, ..Default::default() }))?.total.unwrap_or(0);
        expect(n == 5000 && failures == 0, format!("5000 files indexed; {} queries during churn, p50 {} ms, p95 {} ms, max {} ms", lat.len(), p(0.5), p(0.95), p(1.0)), format!("{n} of 5000 indexed, {failures} failed queries"))
    });
    t.record("bulk-churn", s, r);

    // 6. forced re-read keeps answering, then swaps
    let s = Instant::now();
    let reloads = t.status().ok().and_then(|st| st.stores.into_iter().find(|v| v.name == letter)).map(|v| v.reloads).unwrap_or(0);
    let r = (|| {
        t.req(&Request::Rescan { target: letter.clone() })?;
        let during = t.paths("created.txt")?;
        let r = t.until(Duration::from_secs(60), |t| {
            let st = t.status()?;
            let v = st.stores.iter().find(|v| v.name == letter).ok_or("volume missing")?;
            expect(v.reloads > reloads && v.state == "ready", format!("re-read #{} in {} ms", v.reloads, v.load_ms), format!("state {} reloads {}", v.state, v.reloads))
        })?;
        let after = t.paths("renamed-alpha.txt")?;
        expect(after.len() == 1 && during.len() == 1, format!("{r}; answered during and after"), format!("during {during:?} after {after:?}"))
    })();
    t.record("forced-reread", s, r);

    // 7. status and pipe round trip
    let s = Instant::now();
    let status = t.status();
    let r = match &status {
        Ok(st) => expect(st.protocol == PROTOCOL_VERSION && st.private_bytes > 0, format!("pid {} private {} KB", st.pid, st.private_bytes / 1024), "bad status".into()),
        Err(e) => Err(e.clone()),
    };
    t.record("status-round-trip", s, r);

    service::shutdown(&svc);
    let all_ok = t.checks.iter().all(|c| c.ok);
    let report = Report { volume: letter, all_ok, checks: t.checks, status: status.ok() };
    let json = serde_json::to_string_pretty(&report).map_err(|e| e.to_string())?;
    match report_path {
        Some(p) => fs::write(&p, json).map_err(|e| format!("{}: {e}", p.display()))?,
        None => println!("{json}"),
    }
    println!("{}", if all_ok { "SELFTEST PASSED" } else { "SELFTEST FAILED" });
    Ok(all_ok)
}
