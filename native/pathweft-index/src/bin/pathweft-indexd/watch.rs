//! Change notifications for folder roots (SPEC IDX-03).
//!
//! A `Watcher` thread follows `ReadDirectoryChangesW` on the whole tree and
//! queues the changed paths. The folder's worker then calls `reconcile`, which
//! re-lists only the folders that contain those paths and applies the
//! difference to the store. A full notification buffer (`overflow`) means
//! changes were lost, and the worker falls back to a full rescan.

use crate::scan::{descend, long_path, Impersonation};
use crate::state::Slot;
use pathweft_index::ffi::*;
use pathweft_index::store::{Entry, Rec, Store, ATTR_DIRECTORY};
use std::collections::{HashMap, HashSet};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, RwLock};
use std::thread::JoinHandle;

const BUFFER_BYTES: usize = 64 * 1024; // the most a network share accepts
const FILTER: u32 = FILE_NOTIFY_CHANGE_FILE_NAME
    | FILE_NOTIFY_CHANGE_DIR_NAME
    | FILE_NOTIFY_CHANGE_ATTRIBUTES
    | FILE_NOTIFY_CHANGE_SIZE
    | FILE_NOTIFY_CHANGE_LAST_WRITE
    | FILE_NOTIFY_CHANGE_CREATION;

/// What the watcher collected since the last `take`.
#[derive(Default)]
pub struct Pending {
    /// Changed paths, relative to the root.
    pub paths: HashSet<String>,
    /// Notifications were lost; only a rescan is reliable.
    pub overflow: bool,
    /// The watcher stopped with this error.
    pub failed: Option<String>,
}

pub struct Watcher {
    stop: Arc<AtomicBool>,
    pending: Arc<Mutex<Pending>>,
    thread: Option<JoinHandle<()>>,
}

impl Watcher {
    /// Opens `root` as the token's user and starts following its changes.
    pub fn start(root: &str, token: &Handle, slot: Arc<Slot>) -> Result<Watcher, String> {
        let path = wide(&format!("{}\\", long_path(root)));
        let dir = {
            let Some(_imp) = Impersonation::begin(token) else {
                return Err(format!("cannot impersonate the folder's owner: error {}", last_error()));
            };
            let h = unsafe {
                CreateFileW(
                    path.as_ptr(),
                    FILE_LIST_DIRECTORY,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    std::ptr::null(),
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED,
                    std::ptr::null_mut(),
                )
            };
            Handle::valid(h).ok_or_else(|| format!("cannot open the folder for change notifications: error {}", last_error()))?
        };
        let event = Handle::valid(unsafe { CreateEventW(std::ptr::null(), 1, 0, std::ptr::null()) })
            .ok_or_else(|| format!("cannot create an event: error {}", last_error()))?;
        let stop = Arc::new(AtomicBool::new(false));
        let pending = Arc::new(Mutex::new(Pending::default()));
        let (s, p) = (stop.clone(), pending.clone());
        let thread = std::thread::Builder::new()
            .name(format!("watch {root}"))
            .spawn(move || follow(&dir, &event, &s, &p, &slot))
            .map_err(|e| format!("cannot start the watcher thread: {e}"))?;
        Ok(Watcher { stop, pending, thread: Some(thread) })
    }

    pub fn has_pending(&self) -> bool {
        let p = self.pending.lock().unwrap();
        !p.paths.is_empty() || p.overflow || p.failed.is_some()
    }

    pub fn take(&self) -> Pending {
        std::mem::take(&mut *self.pending.lock().unwrap())
    }
}

impl Drop for Watcher {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}

fn follow(dir: &Handle, event: &Handle, stop: &AtomicBool, pending: &Mutex<Pending>, slot: &Slot) {
    // u32 elements keep the buffer DWORD-aligned, as the API requires.
    let mut buf = vec![0u32; BUFFER_BYTES / 4];
    let fail = |e: String| {
        pending.lock().unwrap().failed = Some(e);
        slot.poke();
    };
    loop {
        let mut ov = OVERLAPPED { Internal: 0, InternalHigh: 0, Offset: 0, OffsetHigh: 0, hEvent: event.raw() };
        unsafe {
            ResetEvent(event.raw());
        }
        let ok = unsafe {
            ReadDirectoryChangesW(dir.raw(), buf.as_mut_ptr().cast(), BUFFER_BYTES as u32, 1, FILTER, std::ptr::null_mut(), &mut ov, std::ptr::null())
        };
        if ok == 0 {
            return fail(format!("error {}", last_error()));
        }
        while unsafe { WaitForSingleObject(event.raw(), 1000) } != WAIT_OBJECT_0 {
            if stop.load(Ordering::Relaxed) || slot.stopping() {
                let mut n = 0u32;
                unsafe {
                    CancelIoEx(dir.raw(), &mut ov);
                    // Waits for the cancellation, so `buf` and `ov` outlive the request.
                    GetOverlappedResult(dir.raw(), &mut ov, &mut n, 1);
                }
                return;
            }
        }
        let mut n = 0u32;
        let ok = unsafe { GetOverlappedResult(dir.raw(), &mut ov, &mut n, 0) } != 0;
        let error = if ok { 0 } else { last_error() };
        if !ok && error != ERROR_NOTIFY_ENUM_DIR {
            return fail(format!("error {error}"));
        }
        {
            let mut p = pending.lock().unwrap();
            if !ok || n == 0 {
                p.overflow = true;
            } else {
                let bytes = unsafe { std::slice::from_raw_parts(buf.as_ptr().cast::<u8>(), n as usize) };
                parse(bytes, |name| {
                    p.paths.insert(name);
                });
            }
        }
        slot.poke();
    }
}

/// Reads `FILE_NOTIFY_INFORMATION` records.
fn parse(bytes: &[u8], mut f: impl FnMut(String)) {
    let u32_at = |o: usize| bytes.get(o..o + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]));
    let mut o = 0usize;
    loop {
        let (Some(next), Some(len)) = (u32_at(o), u32_at(o + 8)) else { return };
        let start = o + 12;
        let Some(name) = bytes.get(start..start + len as usize) else { return };
        let units: Vec<u16> = name.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
        f(String::from_utf16_lossy(&units));
        if next == 0 {
            return;
        }
        o += next as usize;
    }
}

#[derive(Default, Debug, PartialEq)]
pub struct Applied {
    pub folders: usize,
    pub added: u64,
    pub removed: u64,
    pub updated: u64,
    /// Folders that were listed, with whether their listing differed.
    pub listed: Vec<(u32, bool)>,
    /// Requested folders that no longer exist in the store.
    pub missing: Vec<u32>,
    /// Requested folders that could not be listed.
    pub unlisted: Vec<u32>,
    /// New folders, with their modified time.
    pub new_dirs: Vec<(u32, u64)>,
    /// Existing subfolders whose own entry changed (for example a new
    /// modified time), so their contents probably changed too.
    pub touched_dirs: Vec<u32>,
}

fn is_dir(e: &Entry) -> bool {
    e.attrs & ATTR_DIRECTORY != 0
}

/// True if the stored record (named `name`) already matches the listed entry.
fn same(rec: &Rec, name: &str, e: &Entry) -> bool {
    name == e.name
        && rec.mtime == e.mtime
        && rec.ctime == e.ctime
        && rec.attrs == e.attrs
        && (rec.is_dir() || rec.size == e.size)
}

/// Brings the folders that contain `changed` (paths relative to the root) up
/// to date. `list` lists one folder by full path (`None`: gone or not
/// listable); `walk(key, path, next, sink)` lists a new folder's whole tree
/// with sequential keys. Folder stores use sequential keys, so a record's
/// key is its index. The caller is the store's only writer.
pub fn reconcile(
    store: &RwLock<Store>,
    changed: &[String],
    list: &mut dyn FnMut(&str) -> Option<Vec<Entry>>,
    walk: &mut dyn FnMut(u32, &str, &mut u32, &mut dyn FnMut(u32, Entry)),
) -> Applied {
    // The folders to re-list, shallowest first, so that a folder removed or
    // added by its parent's pass is not listed again.
    let mut targets: Vec<&str> = changed
        .iter()
        .map(|p| p.trim_matches('\\'))
        .map(|p| p.rsplit_once('\\').map_or("", |(dir, _)| dir))
        .collect::<HashSet<_>>()
        .into_iter()
        .collect();
    targets.sort_by_key(|t| (t.matches('\\').count() + usize::from(!t.is_empty()), t.to_string()));

    // Resolve the folders under a read lock.
    let resolved: Vec<u32> = {
        let s = store.read().unwrap();
        let mut dirs: HashMap<(u32, String), u32> = HashMap::new();
        for (r, rec) in s.recs.iter().enumerate() {
            if rec.live() && rec.is_dir() && r as u32 != s.root {
                dirs.insert((rec.parent, s.name(r as u32).to_lowercase()), r as u32);
            }
        }
        let mut resolved: Vec<u32> = Vec::new();
        for t in &targets {
            let mut cur = s.root;
            let mut found = true;
            for part in t.split('\\').filter(|p| !p.is_empty()) {
                match dirs.get(&(cur, part.to_lowercase())) {
                    Some(&r) => cur = r,
                    None => {
                        found = false;
                        break;
                    }
                }
            }
            if found {
                resolved.push(cur);
            }
        }
        resolved
    };
    relist(store, &resolved, list, walk)
}

/// Lists the folders `dirs` (store keys) again and applies the difference,
/// shallowest first. Same `list` and `walk` contract as `reconcile`.
pub fn relist(
    store: &RwLock<Store>,
    dirs: &[u32],
    list: &mut dyn FnMut(&str) -> Option<Vec<Entry>>,
    walk: &mut dyn FnMut(u32, &str, &mut u32, &mut dyn FnMut(u32, Entry)),
) -> Applied {
    let mut applied = Applied::default();
    // Resolve paths and collect the current children under a read lock.
    let (resolved, mut children, mut next) = {
        let s = store.read().unwrap();
        let mut resolved: Vec<(u32, String)> = Vec::new();
        let mut path = String::new();
        for &d in dirs.iter().collect::<HashSet<_>>() {
            if d == s.root {
                // The root's path is the store's name (`H:`, `\\server\share`).
                resolved.push((d, s.name(s.root).to_string()));
            } else if s.get(d).is_some_and(|r| r.is_dir()) && s.full_path(d, &mut path) {
                resolved.push((d, path.clone()));
            } else {
                applied.missing.push(d);
            }
        }
        resolved.sort_by_key(|(_, p)| (p.matches('\\').count(), p.clone()));
        let wanted: HashSet<u32> = resolved.iter().map(|&(r, _)| r).collect();
        let mut children: HashMap<u32, HashMap<String, (u32, Rec, String)>> = HashMap::new();
        for (r, rec) in s.recs.iter().enumerate() {
            let r = r as u32;
            if rec.live() && r != s.root && wanted.contains(&rec.parent) {
                let name = s.name(r).to_string();
                children.entry(rec.parent).or_default().insert(name.to_lowercase(), (r, *rec, name));
            }
        }
        (resolved, children, s.key_count())
    };

    // List the folders without holding the lock.
    let mut removed: Vec<u32> = Vec::new();
    let mut updates: Vec<(u32, Entry)> = Vec::new();
    let mut adds: Vec<(u32, Entry)> = Vec::new();
    let mut gone: HashSet<u32> = HashSet::new();
    for (dir, path) in resolved {
        if gone.contains(&dir) {
            applied.missing.push(dir);
            continue;
        }
        let Some(listed) = list(&path) else {
            applied.unlisted.push(dir);
            continue;
        };
        applied.folders += 1;
        let before = applied.added + applied.removed + applied.updated;
        let mut old = children.remove(&dir).unwrap_or_default();
        for mut e in listed {
            e.parent = dir;
            match old.remove(&e.name.to_lowercase()) {
                Some((r, rec, name)) if rec.is_dir() == is_dir(&e) => {
                    if !same(&rec, &name, &e) {
                        if rec.is_dir() {
                            applied.touched_dirs.push(r);
                        }
                        updates.push((r, e));
                        applied.updated += 1;
                    }
                    continue;
                }
                Some((r, rec, _)) => {
                    // A file replaced by a folder of the same name, or the reverse.
                    removed.push(r);
                    if rec.is_dir() {
                        gone.insert(r);
                    }
                    applied.removed += 1;
                }
                None => {}
            }
            let k = next;
            next += 1;
            let sub = descend(&e).then(|| format!("{path}\\{}", e.name));
            adds.push((k, e));
            applied.added += 1;
            if let Some(sub) = sub {
                walk(k, &sub, &mut next, &mut |key, entry| {
                    adds.push((key, entry));
                    applied.added += 1;
                });
            }
        }
        for (_, (r, rec, _)) in old {
            removed.push(r);
            if rec.is_dir() {
                gone.insert(r);
            }
            applied.removed += 1;
        }
        applied.listed.push((dir, applied.added + applied.removed + applied.updated != before));
    }
    applied.new_dirs = adds.iter().filter(|(_, e)| descend(e)).map(|(k, e)| (*k, e.mtime)).collect();

    if removed.is_empty() && updates.is_empty() && adds.is_empty() {
        return applied;
    }
    let mut s = store.write().unwrap();
    if !gone.is_empty() {
        // Everything below a removed folder goes with it.
        let root = s.root;
        for r in 0..s.recs.len() as u32 {
            if !s.recs[r as usize].live() || gone.contains(&r) {
                continue;
            }
            let mut cur = s.recs[r as usize].parent;
            for _ in 0..1024 {
                if gone.contains(&cur) {
                    removed.push(r);
                    applied.removed += 1;
                    break;
                }
                if cur == root {
                    break;
                }
                cur = s.recs[cur as usize].parent;
            }
        }
    }
    for r in removed {
        s.set(r, None);
    }
    for (r, e) in updates {
        s.set(r, Some(&e));
    }
    for (k, e) in adds {
        debug_assert_eq!(k, s.key_count(), "folder keys are sequential");
        s.set(k, Some(&e));
    }
    applied
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(name: &str, size: u64, mtime: u64) -> Entry {
        Entry { parent: 0, name: name.into(), size, mtime, ctime: 1, attrs: 0x20, sec: 0 }
    }

    fn dir(name: &str) -> Entry {
        Entry { parent: 0, name: name.into(), size: 0, mtime: 1, ctime: 1, attrs: ATTR_DIRECTORY, sec: 0 }
    }

    /// A fake file system: full path -> entries.
    type Fs = HashMap<String, Vec<Entry>>;

    fn build(fs: &Fs) -> Store {
        let mut s = Store::new(0, "R:", 16);
        let mut next = 1u32;
        let mut sink = |k: u32, e: Entry| s.set(k, Some(&e));
        fake_walk(fs, 0, "R:", &mut next, &mut sink);
        s
    }

    fn fake_walk(fs: &Fs, start: u32, path: &str, next: &mut u32, sink: &mut dyn FnMut(u32, Entry)) {
        for e in fs.get(path).cloned().unwrap_or_default() {
            let k = *next;
            *next += 1;
            let sub = descend(&e).then(|| format!("{path}\\{}", e.name));
            sink(k, Entry { parent: start, ..e });
            if let Some(sub) = sub {
                fake_walk(fs, k, &sub, next, sink);
            }
        }
    }

    fn run(store: &RwLock<Store>, fs: &Fs, changed: &[&str]) -> Applied {
        let changed: Vec<String> = changed.iter().map(|s| s.to_string()).collect();
        reconcile(store, &changed, &mut |p| fs.get(p).cloned(), &mut |k, p, next, sink| fake_walk(fs, k, p, next, sink))
    }

    /// Path -> (size, mtime) of every live entry, built the same way a scan does.
    fn view(s: &Store) -> Vec<(String, u64, u64)> {
        let mut out = Vec::new();
        let mut p = String::new();
        for r in 0..s.recs.len() as u32 {
            if r != s.root && s.get(r).is_some() && s.full_path(r, &mut p) {
                let rec = s.get(r).unwrap();
                out.push((p.clone(), rec.size, rec.mtime));
            }
        }
        out.sort();
        out
    }

    fn sample() -> Fs {
        let mut fs = Fs::new();
        fs.insert("R:".into(), vec![dir("Docs"), file("top.txt", 10, 1)]);
        fs.insert(r"R:\Docs".into(), vec![dir("Old"), file("a.txt", 100, 1)]);
        fs.insert(r"R:\Docs\Old".into(), vec![file("x.bin", 1000, 1), dir("Deep")]);
        fs.insert(r"R:\Docs\Old\Deep".into(), vec![file("y.bin", 5, 1)]);
        fs
    }

    #[test]
    fn applies_adds_removals_updates_and_renames_like_a_rescan() {
        let mut fs = sample();
        let store = RwLock::new(build(&fs));
        // Edit a file, add one, rename a folder (with its subtree) and add a new folder with content.
        fs.insert(r"R:\Docs".into(), vec![dir("Renamed"), file("a.txt", 150, 2), file("b.txt", 7, 2), dir("New")]);
        let old = fs.remove(r"R:\Docs\Old").unwrap();
        fs.insert(r"R:\Docs\Renamed".into(), old);
        let deep = fs.remove(r"R:\Docs\Old\Deep").unwrap();
        fs.insert(r"R:\Docs\Renamed\Deep".into(), deep);
        fs.insert(r"R:\Docs\New".into(), vec![file("n.txt", 3, 2)]);
        let a = run(&store, &fs, &[r"Docs\a.txt", r"Docs\b.txt", r"Docs\Old", r"Docs\Renamed", r"Docs\New", r"Docs\New\n.txt", r"Docs\Old\x.bin"]);
        assert_eq!(view(&store.read().unwrap()), view(&build(&fs)));
        assert_eq!((a.updated, a.folders), (1, 1));
        // Folder sizes follow.
        let s = store.read().unwrap();
        assert_eq!(s.get(s.root).unwrap().size, 10 + 150 + 7 + 1000 + 5 + 3);
    }

    #[test]
    fn removes_a_deleted_folder_with_everything_below_it() {
        let mut fs = sample();
        let store = RwLock::new(build(&fs));
        fs.insert(r"R:\Docs".into(), vec![file("a.txt", 100, 1)]);
        fs.remove(r"R:\Docs\Old");
        fs.remove(r"R:\Docs\Old\Deep");
        let a = run(&store, &fs, &[r"Docs\Old", r"Docs\Old\Deep\y.bin"]);
        assert_eq!(view(&store.read().unwrap()), view(&build(&fs)));
        assert_eq!(a.removed, 4);
        let s = store.read().unwrap();
        assert_eq!((s.live, s.dirs), (3, 1)); // Docs, top.txt, a.txt; the root is not counted
    }

    #[test]
    fn unchanged_listing_changes_nothing() {
        let fs = sample();
        let store = RwLock::new(build(&fs));
        let gen = store.read().unwrap().dir_gen;
        let a = run(&store, &fs, &[r"Docs\a.txt", "top.txt", r"Docs\Gone\z"]);
        assert_eq!((a.added, a.removed, a.updated), (0, 0, 0));
        assert_eq!(store.read().unwrap().dir_gen, gen);
    }

    #[test]
    fn case_only_rename_updates_the_name() {
        let mut fs = sample();
        let store = RwLock::new(build(&fs));
        fs.insert("R:".into(), vec![dir("Docs"), file("TOP.txt", 10, 1)]);
        let a = run(&store, &fs, &["TOP.txt"]);
        assert_eq!(a.updated, 1);
        assert_eq!(view(&store.read().unwrap()), view(&build(&fs)));
    }

    fn key(s: &Store, name: &str) -> u32 {
        (0..s.recs.len() as u32).find(|&r| r != s.root && s.get(r).is_some() && s.name(r) == name).unwrap()
    }

    fn relist_keys(store: &RwLock<Store>, fs: &Fs, keys: &[u32]) -> Applied {
        relist(store, keys, &mut |p| fs.get(p).cloned(), &mut |k, p, next, sink| fake_walk(fs, k, p, next, sink))
    }

    #[test]
    fn relist_reports_changed_touched_new_and_missing_folders() {
        let mut fs = sample();
        let store = RwLock::new(build(&fs));
        let (root, docs, old, a_txt) = {
            let s = store.read().unwrap();
            (s.root, key(&s, "Docs"), key(&s, "Old"), key(&s, "a.txt"))
        };
        // A change made on the server inside Docs\Old (no notification): the
        // folder's own entry gets a new modified time; a new folder appears too.
        fs.insert(r"R:\Docs".into(), vec![Entry { mtime: 9, ..dir("Old") }, file("a.txt", 100, 1), dir("Fresh")]);
        fs.insert(r"R:\Docs\Fresh".into(), vec![]);
        fs.insert(r"R:\Docs\Old".into(), vec![file("x.bin", 1000, 1), dir("Deep"), file("srv.log", 4, 9)]);
        let a = relist_keys(&store, &fs, &[docs, root, a_txt]);
        assert_eq!(a.listed, vec![(root, false), (docs, true)], "shallowest first");
        assert_eq!(a.missing, vec![a_txt], "a file is not a folder");
        assert_eq!(a.touched_dirs, vec![old]);
        assert_eq!(a.new_dirs.len(), 1);
        // The touched folder's own check finds the server-side file.
        let b = relist_keys(&store, &fs, &[old]);
        assert_eq!(b.listed, vec![(old, true)]);
        assert_eq!(view(&store.read().unwrap()), view(&build(&fs)));
        // A removed folder is reported missing on its next check.
        fs.insert(r"R:\Docs".into(), vec![file("a.txt", 100, 1), dir("Fresh")]);
        relist_keys(&store, &fs, &[docs]);
        assert_eq!(relist_keys(&store, &fs, &[old]).missing, vec![old]);
    }

    #[test]
    fn parses_notify_records() {
        let mut b = Vec::new();
        for (i, name) in ["a.txt", r"Docs\b"].iter().enumerate() {
            let units: Vec<u16> = name.encode_utf16().collect();
            let len = (units.len() * 2) as u32;
            let size = (12 + len as usize + 3) & !3;
            let next = if i == 0 { size as u32 } else { 0 };
            b.extend_from_slice(&next.to_le_bytes());
            b.extend_from_slice(&1u32.to_le_bytes());
            b.extend_from_slice(&len.to_le_bytes());
            for u in units {
                b.extend_from_slice(&u.to_le_bytes());
            }
            b.resize(b.len() + (size - 12 - len as usize), 0);
        }
        let mut got = Vec::new();
        parse(&b, |n| got.push(n));
        assert_eq!(got, vec!["a.txt".to_string(), r"Docs\b".to_string()]);
    }
}
