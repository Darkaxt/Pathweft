//! pwindex: command-line client for the Pathweft index service.
//!
//! ```text
//! pwindex [--pipe NAME] status [--json]
//! pwindex query EXPR [--offset N] [--count N] [--sort name|path|size|extension|created|modified]
//!               [--desc] [--path] [--case] [--ww] [--regex] [--visible-only] [--json]
//! pwindex sizes FOLDER...
//! pwindex folder add PATH [--rescan-minutes N] | folder remove PATH
//! pwindex rescan TARGET                         (a drive letter or a folder root)
//! pwindex bench EXPR [--runs N] [--count N]
//! pwindex churn --dir DIR [--seconds S] [--files N] [--query Q] [--es [PATH]]
//! pwindex compare EXPR [--es PATH] [--timeout-ms N] [--max N]
//! pwindex compare-sizes FOLDER... [--es PATH] [--timeout-ms N]
//! ```
//! `compare*` run Everything's es.exe read-only with a timeout; on timeout only
//! that es.exe process is killed, never Everything itself.

#[cfg(not(windows))]
fn main() {
    eprintln!("pwindex runs on Windows only");
    std::process::exit(1);
}

#[cfg(windows)]
mod cli {
    use pathweft_index::client::Client;
    use pathweft_index::protocol::*;
    use std::collections::HashSet;
    use std::io::Read;
    use std::path::PathBuf;
    use std::process::{Command, Stdio};
    use std::time::{Duration, Instant};

    const DEFAULT_ES: &str = r"C:\Users\darka\AppData\Local\Tools\es.exe";

    pub struct Args {
        list: Vec<String>,
    }

    impl Args {
        fn flag(&mut self, name: &str) -> bool {
            match self.list.iter().position(|a| a == name) {
                Some(i) => {
                    self.list.remove(i);
                    true
                }
                None => false,
            }
        }
        fn opt(&mut self, name: &str) -> Option<String> {
            let i = self.list.iter().position(|a| a == name)?;
            if i + 1 >= self.list.len() {
                return None;
            }
            let v = self.list.remove(i + 1);
            self.list.remove(i);
            Some(v)
        }
        /// `--es` with an optional path.
        fn opt_path(&mut self, name: &str) -> Option<String> {
            let i = self.list.iter().position(|a| a == name)?;
            let next = self.list.get(i + 1).cloned();
            match next {
                Some(v) if v.to_ascii_lowercase().ends_with(".exe") => {
                    self.list.remove(i + 1);
                    self.list.remove(i);
                    Some(v)
                }
                _ => {
                    self.list.remove(i);
                    Some(DEFAULT_ES.into())
                }
            }
        }
        fn num<T: std::str::FromStr>(&mut self, name: &str, default: T) -> T {
            self.opt(name).and_then(|v| v.parse().ok()).unwrap_or(default)
        }
    }

    fn connect(pipe: &str) -> Result<Client, String> {
        Client::connect_to(pipe, Duration::from_secs(5)).map_err(|e| e.to_string())
    }

    fn call(pipe: &str, r: &Request) -> Result<Response, String> {
        let resp = connect(pipe)?.request(r).map_err(|e| e.to_string())?;
        if !resp.ok {
            return Err(resp.error.clone().unwrap_or_else(|| "request failed".into()));
        }
        Ok(resp)
    }

    fn mb(b: u64) -> String {
        format!("{:.1} MB", b as f64 / 1_048_576.0)
    }

    fn sort_key(s: &str) -> Result<SortKey, String> {
        Ok(match s {
            "name" => SortKey::Name,
            "path" => SortKey::Path,
            "size" => SortKey::Size,
            "extension" | "ext" => SortKey::Extension,
            "created" => SortKey::Created,
            "modified" | "date" => SortKey::Modified,
            _ => return Err(format!("unknown sort {s}")),
        })
    }

    fn query_request(a: &mut Args, expr: String) -> Result<QueryRequest, String> {
        Ok(QueryRequest {
            query: expr,
            offset: a.num("--offset", 0),
            count: a.num("--count", 100),
            sort: sort_key(&a.opt("--sort").unwrap_or_else(|| "name".into()))?,
            descending: a.flag("--desc"),
            match_path: a.flag("--path"),
            match_case: a.flag("--case"),
            whole_word: a.flag("--ww"),
            regex: a.flag("--regex"),
            visible_only: a.flag("--visible-only"),
        })
    }

    fn percentiles(mut v: Vec<f64>) -> String {
        if v.is_empty() {
            return "no samples".into();
        }
        v.sort_by(|a, b| a.partial_cmp(b).unwrap());
        let p = |q: f64| v[((v.len() as f64 - 1.0) * q).round() as usize];
        format!("n={} p50={:.2} ms p95={:.2} ms max={:.2} ms", v.len(), p(0.5), p(0.95), p(1.0))
    }

    /// Runs a program with a timeout, returning stdout. Kills only that child.
    fn run_timeout(exe: &str, args: &[String], timeout: Duration) -> Result<String, String> {
        let mut child = Command::new(exe).args(args).stdout(Stdio::piped()).stderr(Stdio::null()).spawn().map_err(|e| format!("{exe}: {e}"))?;
        let mut out = child.stdout.take().unwrap();
        let reader = std::thread::spawn(move || {
            let mut s = Vec::new();
            let _ = out.read_to_end(&mut s);
            s
        });
        let end = Instant::now() + timeout;
        loop {
            if let Some(status) = child.try_wait().map_err(|e| e.to_string())? {
                let bytes = reader.join().unwrap_or_default();
                if !status.success() {
                    return Err(format!("es.exe exited with {status}"));
                }
                return Ok(String::from_utf8_lossy(&bytes).into_owned());
            }
            if Instant::now() >= end {
                let _ = child.kill();
                let _ = child.wait();
                return Err(format!("es.exe timed out after {} ms (Everything may be hung)", timeout.as_millis()));
            }
            std::thread::sleep(Duration::from_millis(10));
        }
    }

    fn status(pipe: &str, json: bool) -> Result<(), String> {
        let r = call(pipe, &Request::Status)?;
        let st = r.status.ok_or("no status")?;
        if json {
            println!("{}", serde_json::to_string_pretty(&st).unwrap());
            return Ok(());
        }
        println!(
            "pathweft-indexd {} (protocol {}) pid {} up {} s; working set {}, private {}; caller {}",
            st.version,
            st.protocol,
            st.pid,
            st.uptime_s,
            mb(st.working_set),
            mb(st.private_bytes),
            st.caller
        );
        println!("{:<24} {:<7} {:<16} {:>10} {:>9} {:>9} {:>8} {:>10} {:>18}", "store", "kind", "state", "items", "folders", "load ms", "reloads", "memory", "next USN");
        for s in st.stores {
            println!(
                "{:<24} {:<7} {:<16} {:>10} {:>9} {:>9} {:>8} {:>10} {:>18}",
                s.name,
                s.kind,
                s.state,
                s.items,
                s.folders,
                s.load_ms,
                s.reloads,
                mb(s.memory_bytes),
                s.next_usn
            );
            if let Some(e) = s.error {
                println!("    last error: {e}");
            }
        }
        Ok(())
    }

    fn query(pipe: &str, a: &mut Args) -> Result<(), String> {
        let json = a.flag("--json");
        let req = query_request(a, String::new())?;
        let expr = a.list.join(" ");
        let req = QueryRequest { query: expr, ..req };
        let t = Instant::now();
        let r = call(pipe, &Request::Query(req))?;
        let ms = t.elapsed().as_secs_f64() * 1000.0;
        if json {
            println!("{}", serde_json::to_string_pretty(&r).unwrap());
            return Ok(());
        }
        for h in r.hits.unwrap_or_default() {
            let size = if h.size < 0 { "-".to_string() } else { h.size.to_string() };
            // Third column only for entries the caller cannot list.
            println!("{}{}\t{size}{}", h.path, if h.folder { "\\" } else { "" }, if h.visible { "" } else { "\tnot-visible" });
        }
        eprintln!(
            "{} matches; {:.2} ms round trip, {} us in the service{}",
            r.total.unwrap_or(0),
            ms,
            r.elapsed_us,
            if r.updating { format!("; updating: {}", r.updating_stores.join(", ")) } else { String::new() }
        );
        Ok(())
    }

    fn sizes(pipe: &str, folders: Vec<String>) -> Result<(), String> {
        let r = call(pipe, &Request::FolderSizes { paths: folders })?;
        for f in r.sizes.unwrap_or_default() {
            match f.size {
                Some(s) => println!("{}\t{s}", f.path),
                None => println!("{}\t(not indexed)", f.path),
            }
        }
        Ok(())
    }

    fn bench(pipe: &str, a: &mut Args) -> Result<(), String> {
        let runs: usize = a.num("--runs", 50);
        let req = query_request(a, String::new())?;
        let req = QueryRequest { query: a.list.join(" "), ..req };
        let mut c = connect(pipe)?;
        let mut rt = Vec::new();
        let mut svc = Vec::new();
        let mut total = 0;
        for _ in 0..runs {
            let t = Instant::now();
            let r = c.request(&Request::Query(req.clone())).map_err(|e| e.to_string())?;
            if !r.ok {
                return Err(r.error.unwrap_or_default());
            }
            rt.push(t.elapsed().as_secs_f64() * 1000.0);
            svc.push(r.elapsed_us as f64 / 1000.0);
            total = r.total.unwrap_or(0);
        }
        println!("query {:?}: {total} matches", req.query);
        println!("round trip: {}", percentiles(rt));
        println!("in service: {}", percentiles(svc));
        Ok(())
    }

    fn churn(pipe: &str, a: &mut Args) -> Result<(), String> {
        let dir = PathBuf::from(a.opt("--dir").ok_or("churn needs --dir (for example E:\\Temp\\pathweft-index\\churn)")?);
        let seconds: u64 = a.num("--seconds", 30);
        let files: usize = a.num("--files", 2000);
        let query_text = a.opt("--query").unwrap_or_else(|| "pwchurn".into());
        let es = a.opt_path("--es");
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        let end = Instant::now() + Duration::from_secs(seconds);
        let d2 = dir.clone();
        let writer = std::thread::spawn(move || {
            let mut ops = 0u64;
            let mut i = 0usize;
            while Instant::now() < end {
                let slot = i % files;
                let a = d2.join(format!("pwchurn-{slot:06}.tmp"));
                let b = d2.join(format!("pwchurn-{slot:06}-renamed.tmp"));
                if b.exists() {
                    let _ = std::fs::remove_file(&b);
                } else if a.exists() {
                    let _ = std::fs::rename(&a, &b);
                } else {
                    let _ = std::fs::write(&a, vec![b'c'; slot % 4096]);
                }
                ops += 1;
                i += 1;
            }
            ops
        });
        let req = QueryRequest { query: query_text.clone(), count: 100, ..Default::default() };
        let mut ours = Vec::new();
        let mut theirs = Vec::new();
        let mut failures = 0;
        let mut es_failures = 0;
        let mut c = connect(pipe)?;
        while Instant::now() < end {
            let t = Instant::now();
            match c.request(&Request::Query(req.clone())) {
                Ok(r) if r.ok => ours.push(t.elapsed().as_secs_f64() * 1000.0),
                _ => {
                    failures += 1;
                    c = connect(pipe)?;
                }
            }
            if let Some(es) = &es {
                let t = Instant::now();
                match run_timeout(es, &["-n".into(), "100".into(), query_text.clone()], Duration::from_secs(5)) {
                    Ok(_) => theirs.push(t.elapsed().as_secs_f64() * 1000.0),
                    Err(_) => es_failures += 1,
                }
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        let ops = writer.join().unwrap_or(0);
        println!("churn: {ops} file operations in {seconds} s in {}", dir.display());
        println!("pathweft-index query latency: {} ({failures} failed)", percentiles(ours));
        if es.is_some() {
            println!("es.exe query latency (includes es.exe process start): {} ({es_failures} failed or timed out)", percentiles(theirs));
        }
        // Leave nothing behind.
        if let Ok(rd) = std::fs::read_dir(&dir) {
            for e in rd.flatten() {
                if e.file_name().to_string_lossy().starts_with("pwchurn-") {
                    let _ = std::fs::remove_file(e.path());
                }
            }
        }
        Ok(())
    }

    fn compare(pipe: &str, a: &mut Args) -> Result<(), String> {
        let es = a.opt("--es").unwrap_or_else(|| DEFAULT_ES.into());
        let timeout = Duration::from_millis(a.num("--timeout-ms", 10_000));
        let max: u32 = a.num("--max", 100_000);
        let expr = a.list.join(" ");
        if expr.trim().is_empty() || expr.trim() == "*" {
            return Err("refusing an empty or root-wide comparison".into());
        }
        let t = Instant::now();
        let r = call(pipe, &Request::Query(QueryRequest { query: expr.clone(), count: max, sort: SortKey::Path, ..Default::default() }))?;
        let ours_ms = t.elapsed().as_secs_f64() * 1000.0;
        let ours: Vec<String> = r.hits.unwrap_or_default().into_iter().map(|h| h.path).collect();
        let t = Instant::now();
        let out = run_timeout(&es, &["-n".into(), max.to_string(), expr.clone()], timeout)?;
        let es_ms = t.elapsed().as_secs_f64() * 1000.0;
        let theirs: Vec<String> = out.lines().map(|l| l.trim_end().to_string()).filter(|l| !l.is_empty()).collect();
        let fold = |v: &[String]| v.iter().map(|s| s.to_lowercase()).collect::<HashSet<_>>();
        let (a_set, b_set) = (fold(&ours[..]), fold(&theirs[..]));
        let only_ours: Vec<&String> = ours.iter().filter(|p| !b_set.contains(&p.to_lowercase())).collect();
        let only_es: Vec<&String> = theirs.iter().filter(|p| !a_set.contains(&p.to_lowercase())).collect();
        println!("query {expr:?}");
        println!("  pathweft-index: {} results (total {}) in {ours_ms:.1} ms", ours.len(), r.total.unwrap_or(0));
        println!("  Everything:     {} results in {es_ms:.1} ms (es.exe, includes process start)", theirs.len());
        println!("  common {}, only ours {}, only Everything {}", ours.len() - only_ours.len(), only_ours.len(), only_es.len());
        for p in only_ours.iter().take(10) {
            println!("    only ours: {p}");
        }
        for p in only_es.iter().take(10) {
            println!("    only Everything: {p}");
        }
        Ok(())
    }

    fn compare_sizes(pipe: &str, a: &mut Args) -> Result<(), String> {
        let es = a.opt("--es").unwrap_or_else(|| DEFAULT_ES.into());
        let timeout = Duration::from_millis(a.num("--timeout-ms", 10_000));
        let folders = a.list.clone();
        let r = call(pipe, &Request::FolderSizes { paths: folders.clone() })?;
        for (f, ours) in folders.iter().zip(r.sizes.unwrap_or_default()) {
            let theirs = run_timeout(&es, &["-get-folder-size".into(), f.clone()], timeout).map(|s| s.trim().to_string());
            let ours = ours.size.map(|s| s.to_string()).unwrap_or_else(|| "(not indexed)".into());
            match theirs {
                Ok(t) => println!("{f}\tours {ours}\tEverything {t}\t{}", if t == ours { "equal" } else { "DIFFERENT" }),
                Err(e) => println!("{f}\tours {ours}\tEverything error: {e}"),
            }
        }
        Ok(())
    }

    pub fn main() -> Result<(), String> {
        let mut a = Args { list: std::env::args().skip(1).collect() };
        let pipe = a.opt("--pipe").map(|p| if p.starts_with(r"\\") { p } else { format!(r"\\.\pipe\{p}") }).unwrap_or_else(|| PIPE_NAME.into());
        if a.list.is_empty() {
            return Err("usage: pwindex [--pipe NAME] status|query|sizes|folder|rescan|bench|churn|compare|compare-sizes ...".into());
        }
        let cmd = a.list.remove(0);
        match cmd.as_str() {
            "status" => {
                let json = a.flag("--json");
                status(&pipe, json)
            }
            "query" => query(&pipe, &mut a),
            "sizes" => sizes(&pipe, a.list.clone()),
            "folder" => {
                let sub = if a.list.is_empty() { String::new() } else { a.list.remove(0) };
                match sub.as_str() {
                    "add" => {
                        let minutes = a.opt("--rescan-minutes").and_then(|m| m.parse().ok());
                        let path = a.list.first().cloned().ok_or("folder add needs a path")?;
                        call(&pipe, &Request::FolderAdd { path, rescan_minutes: minutes }).map(|_| println!("added"))
                    }
                    "remove" => {
                        let path = a.list.first().cloned().ok_or("folder remove needs a path")?;
                        call(&pipe, &Request::FolderRemove { path }).map(|_| println!("removed"))
                    }
                    _ => Err("folder add PATH | folder remove PATH".into()),
                }
            }
            "rescan" => {
                let target = a.list.first().cloned().ok_or("rescan needs a target")?;
                call(&pipe, &Request::Rescan { target }).map(|_| println!("requested"))
            }
            "bench" => bench(&pipe, &mut a),
            "churn" => churn(&pipe, &mut a),
            "compare" => compare(&pipe, &mut a),
            "compare-sizes" => compare_sizes(&pipe, &mut a),
            other => Err(format!("unknown command {other}")),
        }
    }
}

#[cfg(windows)]
fn main() {
    if let Err(e) = cli::main() {
        eprintln!("error: {e}");
        std::process::exit(1);
    }
}
