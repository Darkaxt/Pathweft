//! Opt-in folder roots (FAT32 drives, network shares): recursive directory
//! listing at background priority as the user who added the root (SPEC IDX-03).
//! Directory enumeration only; reparse points are listed but not followed.
//! While the service runs, a watcher keeps the tree current between scans,
//! and every folder is listed again on an adaptive schedule (`sched`).

use crate::config::FolderRoot;
use crate::log::{info, warning};
use crate::sched::{self, Schedule};
use crate::security::LocalSd;
use crate::state::{Service, Slot};
use crate::watch::{self, Applied, Watcher};
use pathweft_index::ffi::*;
use pathweft_index::store::{Entry, Store, ATTR_DIRECTORY, ATTR_REPARSE};
use std::path::PathBuf;
use std::sync::atomic::Ordering;
use std::sync::{mpsc, Arc, Mutex};
use std::time::{Duration, Instant};

/// The store's root name: the path without a trailing separator (`H:`).
pub fn root_name(path: &str) -> String {
    let t = path.trim_end_matches('\\');
    if t.is_empty() { path.to_string() } else { t.to_string() }
}

pub struct Impersonation;

impl Impersonation {
    pub fn begin(token: &Handle) -> Option<Impersonation> {
        unsafe { (ImpersonateLoggedOnUser(token.raw()) != 0).then_some(Impersonation) }
    }
}

impl Drop for Impersonation {
    fn drop(&mut self) {
        unsafe {
            RevertToSelf();
        }
    }
}

struct Background;

impl Background {
    fn begin() -> Background {
        unsafe {
            SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
        }
        Background
    }
}

impl Drop for Background {
    fn drop(&mut self) {
        unsafe {
            SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END);
        }
    }
}

pub fn long_path(dir: &str) -> String {
    if let Some(unc) = dir.strip_prefix(r"\\") {
        if !unc.starts_with(r"?\") {
            return format!(r"\\?\UNC\{unc}");
        }
        return dir.to_string();
    }
    format!(r"\\?\{dir}")
}

/// Lists one directory (no recursion), giving each entry (parent 0) to `f`.
/// `Err` carries the Win32 error when the directory cannot be listed.
pub fn list_dir(path: &str, mut f: impl FnMut(Entry)) -> Result<(), u32> {
    let pattern = wide(&format!("{}\\*", long_path(path)));
    let mut data: WIN32_FIND_DATAW = unsafe { std::mem::zeroed() };
    let h = unsafe { FindFirstFileExW(pattern.as_ptr(), FIND_EX_INFO_BASIC, &mut data, FIND_EX_SEARCH_NAME_MATCH, std::ptr::null_mut(), FIND_FIRST_EX_LARGE_FETCH) };
    if h == INVALID_HANDLE_VALUE {
        return Err(last_error());
    }
    loop {
        let name = from_wide(&data.cFileName);
        if name != "." && name != ".." {
            f(Entry {
                parent: 0,
                name,
                size: ((data.nFileSizeHigh as u64) << 32) | data.nFileSizeLow as u64,
                mtime: data.ftLastWriteTime.value(),
                ctime: data.ftCreationTime.value(),
                attrs: data.dwFileAttributes,
                sec: 0,
            });
        }
        if unsafe { FindNextFileW(h, &mut data) } == 0 {
            break;
        }
    }
    unsafe {
        FindClose(h);
    }
    Ok(())
}

/// True for folders the walk descends into (reparse points are not followed).
pub fn descend(e: &Entry) -> bool {
    e.attrs & ATTR_DIRECTORY != 0 && e.attrs & ATTR_REPARSE == 0
}

/// Lists the tree below `path` (whose key is `start`), giving every entry to
/// `sink` with sequential keys taken from `*next`, parents before children.
/// Returns the number of subfolders that could not be listed.
pub fn walk(start: u32, path: &str, next: &mut u32, slot: &Slot, sink: &mut dyn FnMut(u32, Entry)) -> Result<u64, String> {
    let mut queue: Vec<(u32, String)> = vec![(start, path.to_string())];
    let mut errors = 0u64;
    while let Some((dir, p)) = queue.pop() {
        if slot.stopping() {
            return Err("stopped".into());
        }
        let listed = list_dir(&p, |mut e| {
            let k = *next;
            *next += 1;
            e.parent = dir;
            if descend(&e) {
                queue.push((k, format!("{p}\\{}", e.name)));
            }
            sink(k, e);
        });
        if let Err(e) = listed {
            if dir == start {
                return Err(format!("cannot list {p}: error {e}"));
            }
            if e != ERROR_FILE_NOT_FOUND && e != ERROR_NO_MORE_FILES {
                errors += 1;
            }
        }
    }
    Ok(errors)
}

/// `walk` with up to `threads` folders listed at once (IDX-20): over a
/// network each listing waits for the server, so listing one folder at a
/// time made a full scan of a share take half an hour. Keys are still given
/// out here, parents before children. Each listing thread impersonates
/// `token` at background priority, as the caller does.
pub fn walk_parallel(start: u32, path: &str, next: &mut u32, slot: &Slot, token: &Handle, threads: usize, sink: &mut dyn FnMut(u32, Entry)) -> Result<u64, String> {
    if threads <= 1 {
        return walk(start, path, next, slot, sink);
    }
    type Listed = (u32, String, Result<Vec<Entry>, u32>);
    let (jobs, queue) = mpsc::channel::<(u32, String)>();
    let queue = Mutex::new(queue);
    let (done, results) = mpsc::channel::<Listed>();
    std::thread::scope(|scope| {
        for _ in 0..threads {
            let (queue, done) = (&queue, done.clone());
            scope.spawn(move || {
                let _bg = Background::begin();
                let Some(_imp) = Impersonation::begin(token) else {
                    warning!("{}: cannot impersonate the folder's owner: error {}", slot.name, last_error());
                    return;
                };
                loop {
                    let job = queue.lock().unwrap().recv();
                    let Ok((dir, p)) = job else { break };
                    if slot.stopping() {
                        break;
                    }
                    let mut v = Vec::new();
                    let listed = list_dir(&p, |e| v.push(e)).map(|_| v);
                    if done.send((dir, p, listed)).is_err() {
                        break;
                    }
                }
            });
        }
        drop(done);
        let mut jobs = Some(jobs);
        let _ = jobs.as_ref().unwrap().send((start, path.to_string()));
        let (mut pending, mut errors) = (1usize, 0u64);
        let outcome = loop {
            if pending == 0 {
                break Ok(errors);
            }
            let Ok((dir, p, listed)) = results.recv() else { break Err("every listing thread stopped".to_string()) };
            pending -= 1;
            if slot.stopping() {
                break Err("stopped".into());
            }
            match listed {
                Ok(entries) => {
                    for mut e in entries {
                        let k = *next;
                        *next += 1;
                        e.parent = dir;
                        if descend(&e) && jobs.as_ref().unwrap().send((k, format!("{p}\\{}", e.name))).is_ok() {
                            pending += 1;
                        }
                        sink(k, e);
                    }
                }
                Err(e) if dir == start => break Err(format!("cannot list {p}: error {e}")),
                Err(e) => {
                    if e != ERROR_FILE_NOT_FOUND && e != ERROR_NO_MORE_FILES {
                        errors += 1;
                    }
                }
            }
        };
        // Closing the queue ends the listing threads once they are idle.
        jobs.take();
        outcome
    })
}

/// Folders listed at once in a full scan: a network share answers many
/// listings in parallel; a local drive (FAT32, exFAT) is read one at a time.
fn scan_threads(root: &FolderRoot) -> usize {
    if root.path.starts_with(r"\\") {
        8
    } else {
        1
    }
}

/// Lists the tree below `root` into a new store.
pub fn scan(root: &FolderRoot, slot: &Slot, token: &Handle) -> Result<Store, String> {
    let name = root_name(&root.path);
    let mut store = Store::new(0, &name, 1024);
    let mut next: u32 = 1;
    let errors = walk_parallel(0, &name, &mut next, slot, token, scan_threads(root), &mut |k, e| store.set(k, Some(&e)))?;
    if errors > 0 {
        warning!("{name}: {errors} folders could not be listed");
    }
    Ok(store)
}

// ---------------------------------------------------------------- saved indexes (IDX-20)

/// SYSTEM and Administrators only: a saved index lists the owner's files.
const SAVED_SDDL: &str = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

/// The saved index of `root`: one file per root path and owner.
pub fn saved_path(svc: &Service, root: &FolderRoot) -> Option<PathBuf> {
    let dir = svc.opts.folder_cache.as_ref()?;
    let id = format!("{}|{}", root_name(&root.path).to_lowercase(), root.owner.to_uppercase());
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for b in id.bytes() {
        h = (h ^ b as u64).wrapping_mul(0x0000_0100_0000_01b3);
    }
    Some(dir.join(format!("{h:016x}.pwfs")))
}

fn saved_dir(svc: &Service, dir: &std::path::Path) -> Result<(), String> {
    if dir.exists() {
        return Ok(());
    }
    if let Some(parent) = dir.parent() {
        std::fs::create_dir_all(parent).map_err(|e| format!("{}: {e}", parent.display()))?;
    }
    if !svc.opts.protect_content {
        return std::fs::create_dir_all(dir).map_err(|e| format!("{}: {e}", dir.display()));
    }
    let sd = LocalSd::from_sddl(SAVED_SDDL).ok_or("bad saved-index folder descriptor")?;
    let sa = sd.attributes();
    if unsafe { CreateDirectoryW(wide(&dir.to_string_lossy()).as_ptr(), &sa) } == 0 {
        return Err(format!("creating {} failed: error {}", dir.display(), last_error()));
    }
    Ok(())
}

/// Writes the slot's current tree, when it has one, replacing the saved index.
pub fn save(svc: &Service, slot: &Slot, root: &FolderRoot) {
    if !matches!(slot.state(), "ready" | "scanning") {
        return;
    }
    let Some(path) = saved_path(svc, root) else { return };
    let t = Instant::now();
    let image = slot.store().read().unwrap().export();
    let temporary = path.with_extension("pwfs.tmp");
    let written = path
        .parent()
        .map_or(Ok(()), |dir| saved_dir(svc, dir))
        .and_then(|_| std::fs::write(&temporary, &image).map_err(|e| format!("{}: {e}", temporary.display())))
        .and_then(|_| std::fs::rename(&temporary, &path).map_err(|e| format!("{}: {e}", path.display())));
    match written {
        Ok(()) => info!("{}: saved the index ({:.1} MB) in {} ms", slot.name, image.len() as f64 / (1u64 << 20) as f64, t.elapsed().as_millis()),
        Err(e) => {
            let _ = std::fs::remove_file(&temporary);
            warning!("{}: the index was not saved: {e}", slot.name);
        }
    }
}

/// The saved index of `root`, if there is a usable one.
fn load(svc: &Service, slot: &Slot, root: &FolderRoot) -> Option<Store> {
    let path = saved_path(svc, root)?;
    let bytes = match std::fs::read(&path) {
        Ok(b) => b,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return None,
        Err(e) => {
            warning!("{}: the saved index cannot be read: {e}", slot.name);
            return None;
        }
    };
    match Store::import(&root_name(&root.path), &bytes) {
        Ok(store) => Some(store),
        Err(e) => {
            warning!("{}: {e}; scanning instead", slot.name);
            let _ = std::fs::remove_file(&path);
            None
        }
    }
}

/// Deletes the saved index of a removed folder root.
pub fn forget(svc: &Service, slot: &Slot, root: &FolderRoot) {
    if let Some(path) = saved_path(svc, root) {
        match std::fs::remove_file(&path) {
            Ok(()) => info!("{}: saved index deleted", slot.name),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => warning!("{}: the saved index was not deleted: {e}", slot.name),
        }
    }
}

/// Longest time between two saves while changes arrive, so a crash loses little.
const SAVE_EVERY: Duration = Duration::from_secs(10 * 60);

/// What to bring up to date: changed paths from the watcher, or folders
/// (store keys) whose scheduled check is due.
enum Work {
    Paths(Vec<String>),
    Folders(Vec<u32>),
}

/// Re-lists the folders behind `work` and applies the difference to the
/// store (SPEC IDX-03). `None` when the owner cannot be impersonated.
fn apply(slot: &Slot, token: &Handle, work: Work) -> Option<Applied> {
    let t = Instant::now();
    let store = slot.store();
    let scheduled = matches!(work, Work::Folders(_));
    let applied = {
        let _bg = Background::begin();
        let Some(_imp) = Impersonation::begin(token) else {
            warning!("{}: cannot impersonate the folder's owner: error {}", slot.name, last_error());
            return None;
        };
        let mut list = |p: &str| {
            let mut v = Vec::new();
            list_dir(p, |e| v.push(e)).ok().map(|_| v)
        };
        let mut sub = |k: u32, p: &str, next: &mut u32, sink: &mut dyn FnMut(u32, Entry)| {
            let _ = walk(k, p, next, slot, sink);
        };
        match work {
            Work::Paths(paths) => watch::reconcile(&store, &paths, &mut list, &mut sub),
            Work::Folders(keys) => watch::relist(&store, &keys, &mut list, &mut sub),
        }
    };
    let needs = store.read().unwrap().needs_compaction();
    if needs {
        let (arena, offsets) = store.read().unwrap().compacted();
        // This thread is the store's only writer, so nothing changed since.
        store.write().unwrap().install_compacted(arena, offsets);
    }
    let total = applied.added + applied.removed + applied.updated;
    if total > 0 {
        info!(
            "{}: {} changes in {} folders ({} added, {} removed, {} updated) in {} ms{}",
            slot.name,
            total,
            applied.folders,
            applied.added,
            applied.removed,
            applied.updated,
            t.elapsed().as_millis(),
            if scheduled { " (found by a scheduled check)" } else { "" }
        );
        slot.health.lock().unwrap().applied += total;
    }
    Some(applied)
}

/// Plans every folder of a freshly scanned store.
fn plan_all(slot: &Slot, root: &FolderRoot) -> Schedule {
    let max = root.rescan_minutes.max(5) as u64 * 60;
    let mut plan = Schedule::new(MIN_CHECK_SECS, max);
    let now = sched::now();
    let store = slot.store();
    let s = store.read().unwrap();
    plan.add(s.root, 0, now);
    for (r, rec) in s.recs.iter().enumerate() {
        if r as u32 != s.root && rec.live() && rec.is_dir() && rec.attrs & ATTR_REPARSE == 0 {
            plan.add(r as u32, sched::change_time(rec.mtime, now), now);
        }
    }
    plan
}

/// Feeds the outcome of a pass back into the schedule.
fn learn(plan: &mut Schedule, a: &Applied, checked: bool) {
    let now = sched::now();
    for &(k, changed) in &a.listed {
        if changed {
            plan.changed(k, now);
        } else if checked {
            plan.unchanged(k, now);
        }
    }
    if checked {
        for &k in &a.unlisted {
            plan.unchanged(k, now);
        }
    }
    for &k in &a.missing {
        plan.forget(k);
    }
    for &(k, mtime) in &a.new_dirs {
        plan.add(k, sched::change_time(mtime, now), now);
    }
    for &k in &a.touched_dirs {
        plan.soon(k, now);
    }
}

/// Shortest time between two checks of a folder (owner, 2026-11-18).
const MIN_CHECK_SECS: u64 = 15 * 60;
/// Folders checked per pass, so notifications are not held up for long.
const CHECK_BATCH: usize = 256;

/// Worker thread body for one folder slot.
pub fn run(svc: Arc<Service>, slot: Arc<Slot>, root: FolderRoot) {
    let mut have_tree = false;
    let mut watcher: Option<Watcher> = None;
    // IDX-20 (owner, 2026-11-24: "Why is browsing M: so slow and lagging? Isn't everything indexed?"): the
    // index saved at the last stop answers at once; the scan below then replaces it with the current tree.
    let t = Instant::now();
    if let Some(store) = load(&svc, &slot, &root) {
        let entries = store.live;
        slot.install(store);
        {
            let mut h = slot.health.lock().unwrap();
            h.state = "ready";
            h.load_ms = t.elapsed().as_millis() as u64;
        }
        have_tree = true;
        info!("{}: loaded {entries} entries from the saved index in {} ms", slot.name, t.elapsed().as_millis());
    }
    let mut saved_at = Instant::now();
    let mut saved_applied = slot.health.lock().unwrap().applied;
    while !slot.stopping() && !svc.stopping() {
        let Some(token) = svc.tokens.get(&root.owner) else {
            // The owner's token arrives with that user's next standard (not elevated) pipe connection.
            // A saved index keeps answering meanwhile.
            if !have_tree {
                slot.set_state("waiting-for-user");
            }
            slot.wait(Duration::from_secs(5));
            continue;
        };
        // Started before the scan, so changes made while it runs are queued.
        if watcher.is_none() {
            match Watcher::start(&slot.name, &token, slot.clone()) {
                Ok(w) => {
                    info!("{}: watching for changes", slot.name);
                    watcher = Some(w);
                }
                Err(e) => warning!("{}: {e}; changes are picked up by rescans only", slot.name),
            }
        }
        slot.set_state(if have_tree { "scanning" } else { "loading" });
        slot.refresh.store(false, Ordering::Relaxed);
        let t = Instant::now();
        let result = {
            let _bg = Background::begin();
            match Impersonation::begin(&token) {
                Some(_imp) => scan(&root, &slot, &token),
                None => Err(format!("cannot impersonate the folder's owner: error {}", last_error())),
            }
        };
        match result {
            Ok(store) => {
                let ms = t.elapsed().as_millis() as u64;
                info!("{}: scanned {} entries in {ms} ms", slot.name, store.live);
                slot.install(store);
                let mut h = slot.health.lock().unwrap();
                h.state = "ready";
                h.load_ms = ms;
                h.error = None;
                if have_tree {
                    h.reloads += 1;
                }
                drop(h);
                have_tree = true;
                save(&svc, &slot, &root);
                saved_at = Instant::now();
                saved_applied = slot.health.lock().unwrap().applied;
            }
            Err(e) => {
                if slot.stopping() {
                    return;
                }
                warning!("{}: {e}", slot.name);
                slot.fail(if have_tree { "ready" } else { "error" }, e);
                slot.wait(Duration::from_secs(60));
                continue;
            }
        }
        let mut plan = plan_all(&slot, &root);
        info!(
            "{}: {} folders scheduled, {} checked at least hourly",
            slot.name,
            plan.len(),
            plan.checked_within(3600, sched::now())
        );
        // The first pass applies what changed while the scan ran.
        let mut during_scan = true;
        let mut hour = Instant::now();
        let (mut checked, mut found) = (0usize, 0usize);
        while !slot.stopping() && !svc.stopping() && !slot.refresh.load(Ordering::Relaxed) {
            if let Some(w) = &watcher {
                if during_scan || w.has_pending() {
                    if !during_scan {
                        // Let a burst of changes settle into one pass.
                        std::thread::sleep(Duration::from_millis(500));
                    }
                    let p = w.take();
                    if p.overflow {
                        if during_scan {
                            warning!("{}: too many changes while scanning for the notification buffer; scheduled checks pick them up", slot.name);
                        } else {
                            info!("{}: too many changes for the notification buffer; rescanning", slot.name);
                            slot.refresh.store(true, Ordering::Relaxed);
                            break;
                        }
                    }
                    if !p.paths.is_empty() {
                        if let Some(a) = apply(&slot, &token, Work::Paths(p.paths.into_iter().collect())) {
                            learn(&mut plan, &a, false);
                        }
                    }
                    if let Some(e) = p.failed {
                        warning!("{}: change notifications stopped ({e}); changes are picked up by scheduled checks only", slot.name);
                        watcher = None;
                    }
                    during_scan = false;
                    continue;
                }
            }
            during_scan = false;
            let due = plan.due(sched::now(), CHECK_BATCH);
            if !due.is_empty() {
                match apply(&slot, &token, Work::Folders(due.clone())) {
                    Some(a) => {
                        checked += a.listed.len();
                        found += a.listed.iter().filter(|(_, c)| *c).count();
                        learn(&mut plan, &a, true);
                    }
                    None => {
                        // Not impersonated: try these again later.
                        let now = sched::now();
                        for k in due {
                            plan.unchanged(k, now);
                        }
                        slot.wait(Duration::from_secs(60));
                    }
                }
                continue;
            }
            let applied = slot.health.lock().unwrap().applied;
            if applied != saved_applied && saved_at.elapsed() >= SAVE_EVERY {
                save(&svc, &slot, &root);
                saved_at = Instant::now();
                saved_applied = applied;
            }
            if hour.elapsed() >= Duration::from_secs(3600) {
                if checked > 0 {
                    info!(
                        "{}: checked {checked} folders in the last hour, {found} had changed; {} of {} folders are checked at least hourly",
                        slot.name,
                        plan.checked_within(3600, sched::now()),
                        plan.len()
                    );
                }
                hour = Instant::now();
                checked = 0;
                found = 0;
            }
            let wait = plan.next_due().map_or(60, |t| t.saturating_sub(sched::now())).clamp(1, if watcher.is_some() { 5 } else { 60 });
            slot.wait(Duration::from_secs(wait));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::state::Kind;

    fn own_token() -> Handle {
        let mut token = std::ptr::null_mut();
        let ok = unsafe { OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, &mut token) };
        assert_ne!(ok, 0, "own token");
        Handle::valid(token).expect("token handle")
    }

    fn tree(walked: &[(u32, Entry)], start: &str) -> Vec<(String, u64, bool)> {
        let mut paths: std::collections::HashMap<u32, String> = std::collections::HashMap::new();
        paths.insert(0, start.to_string());
        let mut out = Vec::new();
        for (k, e) in walked {
            let p = format!("{}\{}", paths[&e.parent], e.name);
            paths.insert(*k, p.clone());
            out.push((p, e.size, e.attrs & ATTR_DIRECTORY != 0));
        }
        out.sort();
        out
    }

    // IDX-20: listing several folders at once finds exactly what the one-at-a-time walk finds,
    // with every parent keyed before its children.
    #[test]
    fn a_parallel_walk_lists_the_same_tree() {
        let root = std::env::temp_dir().join(format!("pwidx20-walk-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        for a in 0..6 {
            for b in 0..5 {
                let dir = root.join(format!("a{a}")).join(format!("b{b} é")).join("deep");
                std::fs::create_dir_all(&dir).unwrap();
                std::fs::write(dir.join(format!("f{a}{b}.bin")), vec![0u8; a * 10 + b]).unwrap();
            }
        }
        let start = root.to_string_lossy().to_string();
        let slot = Slot::new(0, start.clone(), Kind::Ntfs);
        let token = own_token();
        let (mut serial, mut parallel) = (Vec::new(), Vec::new());
        let mut next = 1;
        walk(0, &start, &mut next, &slot, &mut |k, e| serial.push((k, e))).unwrap();
        let mut next = 1;
        walk_parallel(0, &start, &mut next, &slot, &token, 8, &mut |k, e| parallel.push((k, e))).unwrap();
        let _ = std::fs::remove_dir_all(&root);
        assert_eq!(serial.len(), 6 + 30 * 3);
        assert!(parallel.iter().all(|(k, e)| e.parent < *k), "parents are keyed before their children");
        assert_eq!(tree(&serial, &start), tree(&parallel, &start));
        assert!(walk_parallel(0, &format!("{start}-absent"), &mut 1, &slot, &token, 8, &mut |_, _| {}).is_err(), "an unlistable root fails");
    }
}
