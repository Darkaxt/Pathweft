//! Opt-in folder roots (FAT32 drives, network shares): recursive directory
//! listing at background priority as the user who added the root (SPEC IDX-03).
//! Directory enumeration only; reparse points are listed but not followed.

use crate::config::FolderRoot;
use crate::log::{info, warn};
use crate::state::{Service, Slot};
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

struct Impersonation;

impl Impersonation {
    fn begin(token: &Handle) -> Option<Impersonation> {
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

fn long_path(dir: &str) -> String {
    if let Some(unc) = dir.strip_prefix(r"\\") {
        if !unc.starts_with(r"?\") {
            return format!(r"\\?\UNC\{unc}");
        }
        return dir.to_string();
    }
    format!(r"\\?\{dir}")
}

/// Lists the tree below `root` into a new store.
pub fn scan(root: &FolderRoot, slot: &Slot) -> Result<Store, String> {
    let name = root_name(&root.path);
    let mut store = Store::new(0, &name, 1024);
    let mut next: u32 = 1;
    let mut queue: Vec<(u32, String)> = vec![(0, name.clone())];
    let mut data: WIN32_FIND_DATAW = unsafe { std::mem::zeroed() };
    let mut errors = 0u64;
    while let Some((dir, path)) = queue.pop() {
        if slot.stopping() {
            return Err("stopped".into());
        }
        let pattern = wide(&format!("{}\\*", long_path(&path)));
        let h = unsafe { FindFirstFileExW(pattern.as_ptr(), FIND_EX_INFO_BASIC, &mut data, FIND_EX_SEARCH_NAME_MATCH, std::ptr::null_mut(), FIND_FIRST_EX_LARGE_FETCH) };
        if h == INVALID_HANDLE_VALUE {
            let e = last_error();
            if dir == 0 {
                return Err(format!("cannot list {path}: error {e}"));
            }
            if e != ERROR_FILE_NOT_FOUND && e != ERROR_NO_MORE_FILES {
                errors += 1;
            }
            continue;
        }
        loop {
            let file = from_wide(&data.cFileName);
            if file != "." && file != ".." {
                let attrs = data.dwFileAttributes;
                let is_dir = attrs & ATTR_DIRECTORY != 0;
                let r = next;
                next += 1;
                let e = Entry {
                    parent: dir,
                    name: file.clone(),
                    size: ((data.nFileSizeHigh as u64) << 32) | data.nFileSizeLow as u64,
                    mtime: data.ftLastWriteTime.value(),
                    ctime: data.ftCreationTime.value(),
                    attrs,
                    sec: 0,
                };
                store.set(r, Some(&e));
                if is_dir && attrs & ATTR_REPARSE == 0 {
                    queue.push((r, format!("{path}\\{file}")));
                }
            }
            if unsafe { FindNextFileW(h, &mut data) } == 0 {
                break;
            }
        }
        unsafe {
            FindClose(h);
        }
    }
    if errors > 0 {
        warn!("{name}: {errors} folders could not be listed");
    }
    Ok(store)
}

/// Worker thread body for one folder slot.
pub fn run(svc: Arc<Service>, slot: Arc<Slot>, root: FolderRoot) {
    let mut have_tree = false;
    while !slot.stopping() && !svc.stopping() {
        let Some(token) = svc.tokens.get(&root.owner) else {
            // The owner's token arrives with that user's next pipe connection.
            slot.set_state("waiting-for-user");
            slot.wait(Duration::from_secs(5));
            continue;
        };
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
                warn!("{}: {e}", slot.name);
                slot.fail(if have_tree { "ready" } else { "error" }, e);
                slot.wait(Duration::from_secs(60));
                continue;
            }
        }
        let minutes = root.rescan_minutes.max(5) as u64;
        let until = Instant::now() + Duration::from_secs(minutes * 60);
        while Instant::now() < until && !slot.stopping() && !svc.stopping() && !slot.refresh.load(Ordering::Relaxed) {
            slot.wait(until.saturating_duration_since(Instant::now()).min(Duration::from_secs(60)));
        }
    }
}
