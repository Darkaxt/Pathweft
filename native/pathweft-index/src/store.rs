//! One store per NTFS volume or scanned folder root.
//!
//! Records are stored densely. `set` takes an external key (the NTFS file
//! record number, or a synthetic number for scanned trees) and maps it to an
//! internal index through a 4-byte-per-key table, so unused MFT records cost 4
//! bytes instead of a whole record. Every other accessor, and `Rec::parent`,
//! uses internal indices. Each record is 40 bytes; names live in one UTF-8
//! arena as a 2-byte length followed by the bytes, and while a store is being
//! built (until `shrink`) equal names share one arena copy. A folder's `size`
//! is the recursive sum of the file sizes below it, maintained incrementally.

use std::collections::HashMap;

pub const NONE: u32 = u32::MAX;
pub const ATTR_DIRECTORY: u32 = 0x10;
pub const ATTR_REPARSE: u32 = 0x400;
const MAX_DEPTH: usize = 1024;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rec {
    /// File size, or the recursive size of a folder.
    pub size: u64,
    pub mtime: u64,
    pub ctime: u64,
    pub parent: u32,
    /// Arena offset of the name, `NONE` when the record is not in use.
    pub name: u32,
    pub attrs: u32,
    /// NTFS security ID (0 for scanned trees).
    pub sec: u32,
}

/// Grows `v` in small steps (about 1/64 of its length) instead of doubling,
/// so changes after a large load do not double a trimmed vector.
fn room<T>(v: &mut Vec<T>, extra: usize) {
    if v.capacity() - v.len() < extra {
        v.reserve_exact(extra.max(v.len() / 64 + 1024));
    }
}

pub const EMPTY: Rec = Rec { size: 0, mtime: 0, ctime: 0, parent: NONE, name: NONE, attrs: 0, sec: 0 };

impl Rec {
    #[inline]
    pub fn live(&self) -> bool {
        self.name != NONE
    }
    #[inline]
    pub fn is_dir(&self) -> bool {
        self.attrs & ATTR_DIRECTORY != 0
    }
}

/// The new state of a record.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Entry {
    pub parent: u32,
    pub name: String,
    /// Ignored for folders, whose size is derived.
    pub size: u64,
    pub mtime: u64,
    pub ctime: u64,
    pub attrs: u32,
    pub sec: u32,
}

pub struct Store {
    /// Indexed by internal index.
    pub recs: Vec<Rec>,
    /// External key -> internal index (`NONE` when unknown).
    keys: Vec<u32>,
    arena: Vec<u8>,
    /// Name hash -> arena offset while the store is being built.
    shared: Option<HashMap<u64, u32>>,
    garbage: usize,
    pub root: u32,
    pub live: u64,
    pub dirs: u64,
    /// Changes whenever a folder is created, removed, moved, renamed or gets a
    /// different security ID. Caches of folder structure key on it.
    pub dir_gen: u64,
}

impl Store {
    /// `root_key` is the external key of the root (internal index 0);
    /// `capacity` is the expected number of keys.
    pub fn new(root_key: u32, root_name: &str, capacity: usize) -> Store {
        let mut store = Store {
            recs: Vec::with_capacity(capacity.max(1)),
            keys: Vec::with_capacity(capacity.max(root_key as usize + 1)),
            arena: Vec::with_capacity(capacity.saturating_mul(16)),
            shared: Some(HashMap::new()),
            garbage: 0,
            root: 0,
            live: 0,
            dirs: 0,
            dir_gen: 1,
        };
        let root = store.slot(root_key);
        let name = store.push_name(root_name);
        store.recs[root as usize] = Rec { name, parent: root, attrs: ATTR_DIRECTORY, ..EMPTY };
        store
    }

    /// The internal index of `key`, allocating an empty record if new.
    fn slot(&mut self, key: u32) -> u32 {
        let k = key as usize;
        if self.keys.len() <= k {
            let extra = k + 1 - self.keys.len();
            room(&mut self.keys, extra);
            self.keys.resize(k + 1, NONE);
        }
        let i = self.keys[k];
        if i != NONE {
            return i;
        }
        let i = self.recs.len() as u32;
        room(&mut self.recs, 1);
        self.recs.push(EMPTY);
        self.keys[k] = i;
        i
    }

    /// One more than the highest external key ever used (folder trees use
    /// sequential keys, so this is the next free key).
    #[inline]
    pub fn key_count(&self) -> u32 {
        self.keys.len() as u32
    }

    /// The internal index of an external key, if the key was ever set.
    #[inline]
    pub fn index_of(&self, key: u32) -> Option<u32> {
        self.keys.get(key as usize).copied().filter(|&i| i != NONE)
    }

    fn name_hash(bytes: &[u8]) -> u64 {
        // FNV-1a
        let mut h: u64 = 0xcbf2_9ce4_8422_2325;
        for &b in bytes {
            h = (h ^ b as u64).wrapping_mul(0x0000_0100_0000_01b3);
        }
        h
    }

    fn push_name(&mut self, name: &str) -> u32 {
        let mut bytes = name.as_bytes();
        if bytes.len() > u16::MAX as usize {
            let mut end = u16::MAX as usize;
            while !name.is_char_boundary(end) {
                end -= 1;
            }
            bytes = &bytes[..end];
        }
        let hash = self.shared.as_ref().map(|_| Self::name_hash(bytes));
        if let (Some(map), Some(h)) = (self.shared.as_ref(), hash) {
            if let Some(&o) = map.get(&h) {
                if self.name_bytes_at(o) == bytes {
                    return o;
                }
            }
        }
        let offset = self.arena.len() as u32;
        room(&mut self.arena, 2 + bytes.len());
        self.arena.extend_from_slice(&(bytes.len() as u16).to_le_bytes());
        self.arena.extend_from_slice(bytes);
        if let (Some(map), Some(h)) = (self.shared.as_mut(), hash) {
            map.entry(h).or_insert(offset);
        }
        offset
    }

    #[inline]
    fn name_bytes_at(&self, offset: u32) -> &[u8] {
        let o = offset as usize;
        let len = u16::from_le_bytes([self.arena[o], self.arena[o + 1]]) as usize;
        &self.arena[o + 2..o + 2 + len]
    }

    #[inline]
    fn name_at(&self, offset: u32) -> &str {
        // Only whole `&str` values are pushed, so every stored slice is UTF-8.
        unsafe { std::str::from_utf8_unchecked(self.name_bytes_at(offset)) }
    }

    /// Name of a live record ("" if not live).
    #[inline]
    pub fn name(&self, r: u32) -> &str {
        match self.recs.get(r as usize) {
            Some(rec) if rec.live() => self.name_at(rec.name),
            _ => "",
        }
    }

    #[inline]
    pub fn get(&self, r: u32) -> Option<&Rec> {
        self.recs.get(r as usize).filter(|rec| rec.live())
    }

    /// Builds the full path of `r` into `out`. False if the parent chain does
    /// not reach the root (an orphan) or loops.
    pub fn full_path(&self, r: u32, out: &mut String) -> bool {
        out.clear();
        let mut chain = [0u32; 64];
        let mut deep: Vec<u32> = Vec::new();
        let mut n = 0usize;
        let mut cur = r;
        loop {
            let Some(rec) = self.get(cur) else { return false };
            if n < chain.len() {
                chain[n] = cur;
            } else {
                if deep.is_empty() {
                    deep.extend_from_slice(&chain);
                }
                deep.push(cur);
            }
            n += 1;
            if cur == self.root {
                break;
            }
            if n > MAX_DEPTH {
                return false;
            }
            cur = rec.parent;
        }
        let items: &[u32] = if deep.is_empty() { &chain[..n] } else { &deep };
        for (i, &c) in items.iter().rev().enumerate() {
            if i > 0 {
                out.push('\\');
            }
            out.push_str(self.name(c));
        }
        if r == self.root && !out.ends_with('\\') && out.len() == 2 {
            out.push('\\');
        }
        true
    }

    /// True if `ancestor` is a strict ancestor of `r`.
    pub fn is_below(&self, r: u32, ancestor: u32) -> bool {
        let mut cur = r;
        for _ in 0..MAX_DEPTH {
            let Some(rec) = self.get(cur) else { return false };
            if cur == self.root {
                return false;
            }
            cur = rec.parent;
            if cur == ancestor {
                return true;
            }
        }
        false
    }

    fn add_up(&mut self, start: u32, delta: i64) {
        if delta == 0 {
            return;
        }
        let mut p = start;
        for _ in 0..MAX_DEPTH {
            let Some(rec) = self.recs.get_mut(p as usize) else { return };
            rec.size = (rec.size as i64).wrapping_add(delta) as u64;
            if p == self.root || !rec.live() || rec.parent == p {
                return;
            }
            p = rec.parent;
        }
    }

    /// Sets (`Some`) or removes (`None`) the record with external key `key`
    /// (`Entry::parent` is an external key too), keeping folder sizes and
    /// counters consistent in any application order.
    pub fn set(&mut self, key: u32, new: Option<&Entry>) {
        match new {
            None => {
                if let Some(r) = self.index_of(key) {
                    self.set_index(r, None, NONE);
                }
            }
            Some(e) => {
                let r = self.slot(key);
                let parent = self.slot(e.parent);
                self.set_index(r, Some(e), parent);
            }
        }
    }

    /// `set` on an internal index; `parent` is the internal index of
    /// `new.parent`.
    fn set_index(&mut self, r: u32, new: Option<&Entry>, parent: u32) {
        if r == self.root {
            if let Some(e) = new {
                let rec = &mut self.recs[r as usize];
                rec.mtime = e.mtime;
                rec.ctime = e.ctime;
                rec.sec = e.sec;
                rec.attrs = e.attrs | ATTR_DIRECTORY;
            }
            return;
        }
        let old = self.recs[r as usize];
        let was_live = old.live();
        if was_live {
            self.add_up(old.parent, -(old.size as i64));
        }
        match new {
            None => {
                if was_live {
                    self.garbage += 2 + self.name_at(old.name).len();
                    let rec = &mut self.recs[r as usize];
                    rec.name = NONE;
                    self.live -= 1;
                    if old.is_dir() {
                        self.dirs -= 1;
                        self.dir_gen += 1;
                    }
                }
            }
            Some(e) => {
                let is_dir = e.attrs & ATTR_DIRECTORY != 0;
                // A folder keeps what its children have already contributed.
                let size = if is_dir {
                    if was_live && !old.is_dir() { 0 } else { old.size }
                } else {
                    e.size
                };
                let name = if was_live && self.name_at(old.name) == e.name {
                    old.name
                } else {
                    if was_live {
                        self.garbage += 2 + self.name_at(old.name).len();
                    }
                    self.push_name(&e.name)
                };
                if is_dir
                    && (!was_live || !old.is_dir() || old.parent != parent || old.name != name || old.sec != e.sec)
                {
                    self.dir_gen += 1;
                }
                if was_live && old.is_dir() && !is_dir {
                    self.dir_gen += 1;
                }
                if !was_live {
                    self.live += 1;
                }
                match (was_live && old.is_dir(), is_dir) {
                    (false, true) => self.dirs += 1,
                    (true, false) => self.dirs -= 1,
                    _ => {}
                }
                self.recs[r as usize] =
                    Rec { size, mtime: e.mtime, ctime: e.ctime, parent, name, attrs: e.attrs, sec: e.sec };
                self.add_up(parent, size as i64);
            }
        }
    }

    /// Removes live records whose parent chain does not reach the root (NTFS
    /// metadata under `$Extend`, records of unattached parents). Run once after
    /// a bulk load.
    pub fn drop_orphans(&mut self) -> u64 {
        // 0 unknown, 1 reaches the root, 2 orphan
        let mut state = vec![0u8; self.recs.len()];
        state[self.root as usize] = 1;
        let mut path = Vec::new();
        let mut orphans = Vec::new();
        for r in 0..self.recs.len() as u32 {
            if !self.recs[r as usize].live() || state[r as usize] != 0 {
                continue;
            }
            path.clear();
            let mut cur = r;
            let verdict = loop {
                match self.recs.get(cur as usize) {
                    Some(rec) if rec.live() => {
                        let s = state[cur as usize];
                        if s != 0 {
                            break s;
                        }
                        if path.len() > MAX_DEPTH {
                            break 2;
                        }
                        state[cur as usize] = 3; // on the current path
                        path.push(cur);
                        cur = rec.parent;
                    }
                    _ => break 2,
                }
            };
            let verdict = if verdict == 3 { 2 } else { verdict };
            for &p in &path {
                state[p as usize] = verdict;
                if verdict == 2 {
                    orphans.push(p);
                }
            }
        }
        // Children before parents does not matter: `set` keeps sizes consistent.
        for &r in &orphans {
            self.set_index(r, None, NONE);
        }
        orphans.len() as u64
    }

    pub fn garbage(&self) -> usize {
        self.garbage
    }

    pub fn needs_compaction(&self) -> bool {
        self.garbage > (16 << 20) && self.garbage * 3 > self.arena.len()
    }

    /// Builds a compacted arena and the new offset of every record without
    /// modifying the store (run under a read lock).
    pub fn compacted(&self) -> (Vec<u8>, Vec<u32>) {
        let mut arena: Vec<u8> = Vec::with_capacity(self.arena.len().saturating_sub(self.garbage) + 1024);
        let mut offsets = Vec::with_capacity(self.recs.len());
        // Equal names share one copy, as after a load.
        let mut seen: HashMap<u64, u32> = HashMap::new();
        for rec in &self.recs {
            if rec.live() {
                let name = self.name_bytes_at(rec.name);
                let h = Self::name_hash(name);
                let existing = seen.get(&h).copied().filter(|&o| {
                    let o = o as usize;
                    let len = u16::from_le_bytes([arena[o], arena[o + 1]]) as usize;
                    &arena[o + 2..o + 2 + len] == name
                });
                if let Some(o) = existing {
                    offsets.push(o);
                    continue;
                }
                let o = arena.len() as u32;
                seen.entry(h).or_insert(o);
                offsets.push(o);
                arena.extend_from_slice(&(name.len() as u16).to_le_bytes());
                arena.extend_from_slice(name);
            } else {
                offsets.push(NONE);
            }
        }
        (arena, offsets)
    }

    /// Installs the result of `compacted` (run under the write lock, with no
    /// change applied since `compacted`).
    pub fn install_compacted(&mut self, arena: Vec<u8>, offsets: Vec<u32>) {
        for (rec, off) in self.recs.iter_mut().zip(offsets) {
            if rec.live() {
                rec.name = off;
            }
        }
        self.arena = arena;
        self.garbage = 0;
    }

    /// (records, names, key map) bytes.
    pub fn memory_parts(&self) -> (u64, u64, u64) {
        (
            (self.recs.capacity() * std::mem::size_of::<Rec>()) as u64,
            self.arena.capacity() as u64,
            (self.keys.capacity() * 4) as u64,
        )
    }

    pub fn memory_bytes(&self) -> u64 {
        let (r, n, k) = self.memory_parts();
        r + n + k
    }

    /// Ends the build phase (drops the name-sharing table) and releases spare
    /// capacity. Names set later are appended without sharing.
    pub fn shrink(&mut self) {
        self.shared = None;
        self.recs.shrink_to_fit();
        self.keys.shrink_to_fit();
        self.arena.shrink_to_fit();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ix(s: &Store, key: u32) -> u32 {
        s.index_of(key).expect("key was set")
    }

    pub fn entry(parent: u32, name: &str, size: u64, dir: bool) -> Entry {
        Entry {
            parent,
            name: name.into(),
            size,
            mtime: 1,
            ctime: 1,
            attrs: if dir { ATTR_DIRECTORY } else { 0x20 },
            sec: 7,
        }
    }

    fn sample() -> Store {
        let mut s = Store::new(5, "C:", 64);
        // Deliberately children before parents, as in MFT order.
        s.set(30, Some(&entry(20, "b.txt", 100, false)));
        s.set(31, Some(&entry(21, "c.bin", 1000, false)));
        s.set(21, Some(&entry(20, "sub", 0, true)));
        s.set(20, Some(&entry(5, "dir", 0, true)));
        s.set(32, Some(&entry(5, "top.txt", 7, false)));
        s
    }

    #[test]
    fn growth_after_shrink_is_small() {
        let mut s = sample();
        for k in 100..10_000u32 {
            s.set(k, Some(&entry(5, "f.txt", 1, false)));
        }
        s.shrink();
        let (r0, n0, k0) = s.memory_parts();
        s.set(10_500, Some(&entry(5, "new-name.txt", 1, false)));
        let (r1, n1, k1) = s.memory_parts();
        assert!(r1 < r0 * 2 && n1 < n0 + 4096 && k1 < k0 * 2, "{r0}->{r1} {n0}->{n1} {k0}->{k1}");
    }

    #[test]
    fn folder_sizes_are_order_independent() {
        let s = sample();
        assert_eq!(s.recs[ix(&s, 21) as usize].size, 1000);
        assert_eq!(s.recs[ix(&s, 20) as usize].size, 1100);
        assert_eq!(s.recs[ix(&s, 5) as usize].size, 1107);
        assert_eq!(s.live, 5);
        assert_eq!(s.dirs, 2);
        let mut p = String::new();
        assert!(s.full_path(ix(&s, 31), &mut p));
        assert_eq!(p, r"C:\dir\sub\c.bin");
        assert!(s.full_path(ix(&s, 5), &mut p));
        assert_eq!(p, r"C:\");
    }

    #[test]
    fn moves_resizes_and_deletes_keep_sizes() {
        let mut s = sample();
        // Move sub (1000) to the root.
        s.set(21, Some(&entry(5, "sub", 0, true)));
        assert_eq!(s.recs[ix(&s, 20) as usize].size, 100);
        assert_eq!(s.recs[ix(&s, 21) as usize].size, 1000);
        assert_eq!(s.recs[ix(&s, 5) as usize].size, 1107);
        // Resize b.txt.
        s.set(30, Some(&entry(20, "b.txt", 150, false)));
        assert_eq!(s.recs[ix(&s, 20) as usize].size, 150);
        assert_eq!(s.recs[ix(&s, 5) as usize].size, 1157);
        // Delete c.bin.
        s.set(31, None);
        assert_eq!(s.recs[ix(&s, 21) as usize].size, 0);
        assert_eq!(s.recs[ix(&s, 5) as usize].size, 157);
        assert_eq!(s.live, 4);
        assert!(s.is_below(ix(&s, 30), ix(&s, 20)));
        assert!(s.is_below(ix(&s, 30), ix(&s, 5)));
        assert!(!s.is_below(ix(&s, 20), ix(&s, 20)));
    }

    #[test]
    fn orphans_are_dropped_and_compaction_keeps_names() {
        let mut s = sample();
        s.set(40, Some(&entry(11, "$Orphan", 5, false)));
        s.set(41, Some(&entry(40, "deeper", 5, false)));
        assert_eq!(s.drop_orphans(), 2);
        assert!(s.get(ix(&s, 40)).is_none());
        for i in 0..2000u32 {
            s.set(30, Some(&entry(20, &format!("renamed-{i}"), 100, false)));
        }
        assert!(s.garbage() > 0);
        let (arena, offsets) = s.compacted();
        s.install_compacted(arena, offsets);
        assert_eq!(s.garbage(), 0);
        assert_eq!(s.name(ix(&s, 30)), "renamed-1999");
        assert_eq!(s.name(ix(&s, 21)), "sub");
        assert_eq!(s.recs[ix(&s, 5) as usize].size, 1107);
    }

    #[test]
    fn sparse_keys_and_shared_names() {
        let mut s = Store::new(5, "C:", 16);
        s.set(2_000_000, Some(&entry(5, "dir", 0, true)));
        s.set(1_500_000, Some(&entry(2_000_000, "index.js", 10, false)));
        s.set(900_000, Some(&entry(5, "index.js", 20, false)));
        // Only the keys that were set have records.
        assert_eq!(s.recs.len(), 4);
        assert_eq!(s.index_of(7), None);
        let (a, b) = (ix(&s, 1_500_000), ix(&s, 900_000));
        // Equal names share one arena copy while the store is built.
        assert_eq!(s.recs[a as usize].name, s.recs[b as usize].name);
        let mut p = String::new();
        assert!(s.full_path(a, &mut p));
        assert_eq!(p, r"C:\dir\index.js");
        assert_eq!(s.recs[ix(&s, 5) as usize].size, 30);
        // After the build phase a rename of one copy leaves the other intact.
        s.shrink();
        s.set(900_000, Some(&entry(5, "main.js", 20, false)));
        assert_eq!(s.name(a), "index.js");
        assert_eq!(s.name(b), "main.js");
        // Compaction shares equal names again.
        s.set(900_000, Some(&entry(5, "index.js", 20, false)));
        assert_ne!(s.recs[a as usize].name, s.recs[b as usize].name);
        let (arena, offsets) = s.compacted();
        s.install_compacted(arena, offsets);
        assert_eq!(s.recs[a as usize].name, s.recs[b as usize].name);
        assert_eq!(s.name(b), "index.js");
        // Removing a key that was never set is a no-op.
        s.set(42, None);
        assert_eq!(s.live, 3);
    }
}
