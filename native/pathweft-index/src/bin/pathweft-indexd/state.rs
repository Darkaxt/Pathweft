//! Shared service state: the stores ("slots"), their health, and the caches
//! the query side uses.

use crate::config::{Config, FolderRoot};
use crate::content::ContentStore;
use crate::log::info;
use crate::security::{AccessCache, TokenCache};
use pathweft_index::query::StoreRef;
use pathweft_index::store::Store;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Condvar, Mutex, RwLock};
use std::time::{Duration, Instant};

#[derive(Clone, Debug, PartialEq)]
pub enum Kind {
    Ntfs,
    Folder(FolderRoot),
}

#[derive(Clone, Debug, Default)]
pub struct Health {
    pub state: &'static str,
    pub journal_id: u64,
    pub next_usn: i64,
    pub load_ms: u64,
    pub reloads: u32,
    pub applied: u64,
    pub error: Option<String>,
}

pub struct Slot {
    pub id: usize,
    /// `C:` for a volume, the root path for a folder.
    pub name: String,
    pub kind: Kind,
    store: Mutex<Arc<RwLock<Store>>>,
    pub health: Mutex<Health>,
    pub stop: AtomicBool,
    /// Set to ask the worker for a full re-read or rescan.
    pub refresh: AtomicBool,
    wake: (Mutex<()>, Condvar),
}

/// Each installed store gets a distinct folder generation base, so caches
/// keyed by `dir_gen` never confuse a replaced store with its predecessor.
static GENERATION: AtomicU64 = AtomicU64::new(1);

impl Slot {
    pub fn new(id: usize, name: String, kind: Kind) -> Slot {
        Slot {
            id,
            name: name.clone(),
            kind,
            store: Mutex::new(Arc::new(RwLock::new(Store::new(0, &name, 1)))),
            health: Mutex::new(Health { state: "loading", ..Default::default() }),
            stop: AtomicBool::new(false),
            refresh: AtomicBool::new(false),
            wake: (Mutex::new(()), Condvar::new()),
        }
    }

    pub fn store(&self) -> Arc<RwLock<Store>> {
        self.store.lock().unwrap().clone()
    }

    /// Replaces the store; queries already running finish on the old one.
    pub fn install(&self, mut store: Store) {
        store.dir_gen = GENERATION.fetch_add(1, Ordering::Relaxed) << 40;
        store.shrink();
        let (recs, names, keys) = store.memory_parts();
        let mb = |b: u64| b as f64 / (1u64 << 20) as f64;
        info!("{}: memory {:.1} MB records ({} slots, {} live), {:.1} MB names, {:.1} MB key map", self.name, mb(recs), store.recs.len(), store.live, mb(names), mb(keys));
        *self.store.lock().unwrap() = Arc::new(RwLock::new(store));
    }

    pub fn set_state(&self, state: &'static str) {
        self.health.lock().unwrap().state = state;
    }

    pub fn state(&self) -> &'static str {
        self.health.lock().unwrap().state
    }

    pub fn fail(&self, state: &'static str, error: String) {
        let mut h = self.health.lock().unwrap();
        h.state = state;
        h.error = Some(error);
    }

    /// Sleeps up to `d`, returning early on stop or refresh.
    pub fn wait(&self, d: Duration) {
        let (m, cv) = &self.wake;
        let guard = m.lock().unwrap();
        if self.stop.load(Ordering::Relaxed) || self.refresh.load(Ordering::Relaxed) {
            return;
        }
        let _ = cv.wait_timeout(guard, d).unwrap();
    }

    pub fn poke(&self) {
        let _g = self.wake.0.lock().unwrap();
        self.wake.1.notify_all();
    }

    pub fn stopping(&self) -> bool {
        self.stop.load(Ordering::Relaxed)
    }

    pub fn updating(&self) -> bool {
        matches!(self.state(), "loading" | "reloading" | "scanning")
    }
}

pub struct Options {
    pub pipe: String,
    pub config_path: PathBuf,
    /// Only these NTFS letters (console/self-test); `None` uses the config.
    pub volumes: Option<Vec<String>>,
    pub folders: bool,
    pub persist_config: bool,
    /// Where the content index lives; `None` disables it.
    pub content_dir: Option<PathBuf>,
    /// Create the content folder for SYSTEM and Administrators only.
    pub protect_content: bool,
    /// Where folder roots' saved indexes live (IDX-20); `None` keeps them in
    /// memory only. Protected like the content folder when `protect_content`.
    pub folder_cache: Option<PathBuf>,
}

pub struct Service {
    pub opts: Options,
    pub config: Mutex<Config>,
    pub slots: RwLock<Vec<Arc<Slot>>>,
    next_id: AtomicUsize,
    pub tokens: TokenCache,
    pub access: AccessCache,
    pub started: Instant,
    pub shutdown: AtomicBool,
    pub connections: AtomicUsize,
    pub content: Option<ContentStore>,
    /// Why the content index is off, when `content_dir` was set.
    pub content_error: Option<String>,
    content_wake: (Mutex<bool>, Condvar),
}

impl Service {
    pub fn new(opts: Options, config: Config, content: Result<Option<ContentStore>, String>) -> Service {
        let (content, content_error) = match content {
            Ok(c) => (c, None),
            Err(e) => (None, Some(e)),
        };
        Service {
            opts,
            config: Mutex::new(config),
            slots: RwLock::new(Vec::new()),
            next_id: AtomicUsize::new(0),
            tokens: TokenCache::default(),
            access: AccessCache::default(),
            started: Instant::now(),
            shutdown: AtomicBool::new(false),
            connections: AtomicUsize::new(0),
            content,
            content_error,
            content_wake: (Mutex::new(false), Condvar::new()),
        }
    }

    /// Asks the content reconcile loop to run now.
    pub fn content_poke(&self) {
        *self.content_wake.0.lock().unwrap() = true;
        self.content_wake.1.notify_all();
    }

    /// Sleeps up to `d` or until poked or stopping. Returns whether it was
    /// poked (or is stopping).
    pub fn content_wait(&self, d: Duration) -> bool {
        let (m, cv) = &self.content_wake;
        let mut poked = m.lock().unwrap();
        if !*poked && !self.stopping() {
            poked = cv.wait_timeout(poked, d).unwrap().0;
        }
        std::mem::replace(&mut *poked, false) || self.stopping()
    }

    pub fn add_slot(&self, name: String, kind: Kind) -> Option<Arc<Slot>> {
        let mut slots = self.slots.write().unwrap();
        if slots.iter().any(|s| s.name.eq_ignore_ascii_case(&name)) {
            return None;
        }
        let slot = Arc::new(Slot::new(self.next_id.fetch_add(1, Ordering::Relaxed), name, kind));
        slots.push(slot.clone());
        Some(slot)
    }

    pub fn remove_slot(&self, name: &str) -> Option<Arc<Slot>> {
        let mut slots = self.slots.write().unwrap();
        let i = slots.iter().position(|s| s.name.eq_ignore_ascii_case(name))?;
        let slot = slots.remove(i);
        slot.stop.store(true, Ordering::Relaxed);
        slot.poke();
        Some(slot)
    }

    pub fn find(&self, name: &str) -> Option<Arc<Slot>> {
        self.slots.read().unwrap().iter().find(|s| s.name.eq_ignore_ascii_case(name)).cloned()
    }

    pub fn all(&self) -> Vec<Arc<Slot>> {
        self.slots.read().unwrap().clone()
    }

    /// The current store of every slot.
    pub fn snapshot(&self) -> (Vec<Arc<Slot>>, Vec<StoreRef>) {
        let slots = self.all();
        let refs = slots.iter().map(|s| StoreRef { slot: s.id, store: s.store() }).collect();
        (slots, refs)
    }

    pub fn stopping(&self) -> bool {
        self.shutdown.load(Ordering::Relaxed)
    }

    pub fn save_config(&self) -> Result<(), String> {
        if !self.opts.persist_config {
            return Ok(());
        }
        let cfg = self.config.lock().unwrap().clone();
        cfg.save(&self.opts.config_path)
    }
}
