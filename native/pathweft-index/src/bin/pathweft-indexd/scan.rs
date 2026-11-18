//! Opt-in folder roots (FAT32 drives, network shares): recursive directory
//! listing at background priority as the user who added the root (SPEC IDX-03).
//! Directory enumeration only; reparse points are listed but not followed.
//! While the service runs, a watcher keeps the tree current between scans.

use crate::config::FolderRoot;
use crate::log::{info, warning};
use crate::state::{Service, Slot};
use crate::watch::{self, Watcher};
use pathweft_index::ffi::*;
use pathweft_index::store::{Entry, Store, ATTR_DIRECTORY, ATTR_REPARSE};
use std::sync::atomic::Ordering;
use std::sync::Arc;
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

/// Lists the tree below `root` into a new store.
pub fn scan(root: &FolderRoot, slot: &Slot) -> Result<Store, String> {
    let name = root_name(&root.path);
    let mut store = Store::new(0, &name, 1024);
    let mut next: u32 = 1;
    let errors = walk(0, &name, &mut next, slot, &mut |k, e| store.set(k, Some(&e)))?;
    if errors > 0 {
        warning!("{name}: {errors} folders could not be listed");
    }
    Ok(store)
}

/// Applies the changes a watcher collected: re-lists the folders that
/// contain them and updates the store (SPEC IDX-03).
fn apply_changes(slot: &Slot, token: &Handle, paths: Vec<String>) {
    let t = Instant::now();
    let store = slot.store();
    let applied = {
        let _bg = Background::begin();
        let Some(_imp) = Impersonation::begin(token) else {
            warning!("{}: cannot impersonate the folder's owner: error {}", slot.name, last_error());
            return;
        };
        watch::reconcile(
            &store,
            &paths,
            &mut |p| {
                let mut v = Vec::new();
                list_dir(p, |e| v.push(e)).ok().map(|_| v)
            },
            &mut |k, p, next, sink| {
                let _ = walk(k, p, next, slot, sink);
            },
        )
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
            "{}: {} changes in {} folders ({} added, {} removed, {} updated) in {} ms",
            slot.name,
            total,
            applied.folders,
            applied.added,
            applied.removed,
            applied.updated,
            t.elapsed().as_millis()
        );
        slot.health.lock().unwrap().applied += total;
    }
}

/// Worker thread body for one folder slot.
pub fn run(svc: Arc<Service>, slot: Arc<Slot>, root: FolderRoot) {
    let mut have_tree = false;
    let mut watcher: Option<Watcher> = None;
    while !slot.stopping() && !svc.stopping() {
        let Some(token) = svc.tokens.get(&root.owner) else {
            // The owner's token arrives with that user's next pipe connection.
            slot.set_state("waiting-for-user");
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
                Some(_imp) => scan(&root, &slot),
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
                have_tree = true;
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
        let minutes = root.rescan_minutes.max(5) as u64;
        let until = Instant::now() + Duration::from_secs(minutes * 60);
        // The first pass applies what changed while the scan ran.
        let mut during_scan = true;
        while Instant::now() < until && !slot.stopping() && !svc.stopping() && !slot.refresh.load(Ordering::Relaxed) {
            if let Some(w) = &watcher {
                if !during_scan && !w.has_pending() {
                    slot.wait(until.saturating_duration_since(Instant::now()).min(Duration::from_secs(5)));
                    continue;
                }
                if !during_scan {
                    // Let a burst of changes settle into one pass.
                    std::thread::sleep(Duration::from_millis(500));
                }
                let p = w.take();
                if p.overflow {
                    if during_scan {
                        warning!("{}: too many changes while scanning for the notification buffer; they are picked up by the next rescan", slot.name);
                    } else {
                        info!("{}: too many changes for the notification buffer; rescanning", slot.name);
                        slot.refresh.store(true, Ordering::Relaxed);
                        break;
                    }
                }
                if !p.paths.is_empty() {
                    apply_changes(&slot, &token, p.paths.into_iter().collect());
                }
                if let Some(e) = p.failed {
                    warning!("{}: change notifications stopped ({e}); changes are picked up by rescans only", slot.name);
                    watcher = None;
                }
                during_scan = false;
            } else {
                slot.wait(until.saturating_duration_since(Instant::now()).min(Duration::from_secs(60)));
            }
        }
    }
}
