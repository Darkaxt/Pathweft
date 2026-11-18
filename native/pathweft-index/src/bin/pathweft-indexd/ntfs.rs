//! NTFS volumes: one MFT read, then the USN journal (SPEC IDX-02).
//!
//! Only MFT records are read from the volume (IDX-11). The write lock of the
//! slot's store is held only to apply one journal buffer's changes or to swap
//! in a re-read store (IDX-04).

use crate::log::{info, warning};
use crate::state::{Service, Slot};
use pathweft_index::ffi::*;
use pathweft_index::mft::{self, AttrList, RecordInfo, Run};
use pathweft_index::store::{Entry, Store, ATTR_DIRECTORY};
use std::collections::{HashMap, HashSet};
use std::ffi::c_void;
use std::sync::atomic::Ordering;
use std::sync::Arc;
use std::time::{Duration, Instant};

const READ_CHUNK: usize = 4 << 20;
const USN_BUFFER: usize = 64 << 10;
const POLL: Duration = Duration::from_millis(250);
const JOURNAL_CHECK: Duration = Duration::from_secs(30);
/// Attribute bits kept from $STANDARD_INFORMATION (drops NTFS-internal flags).
const ATTR_MASK: u32 = 0x00FF_FFFF & !ATTR_DIRECTORY;

/// A buffer aligned for unbuffered volume I/O.
struct Aligned {
    raw: Vec<u8>,
    off: usize,
    len: usize,
}

impl Aligned {
    fn new(len: usize) -> Aligned {
        let raw = vec![0u8; len + 4096];
        let off = raw.as_ptr().align_offset(4096);
        Aligned { raw, off, len }
    }
    fn buf(&mut self) -> &mut [u8] {
        &mut self.raw[self.off..self.off + self.len]
    }
}

pub struct Volume {
    pub letter: String,
    h: Handle,
    pub record_size: usize,
    pub cluster: u64,
    pub sector: u64,
    mft_start_lcn: i64,
    mft_valid: u64,
}

#[derive(Clone, Copy, Debug)]
pub struct Journal {
    pub id: u64,
    pub next: i64,
}

pub enum JournalError {
    /// Wrapped, deleted, replaced or inactive: re-read the volume.
    Reset(String),
    /// The volume is gone or failing.
    Io(String),
}

impl Volume {
    pub fn open(letter: &str) -> Result<Volume, String> {
        let path = wide(&format!(r"\\.\{letter}"));
        let h = unsafe {
            CreateFileW(path.as_ptr(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, std::ptr::null(), OPEN_EXISTING, 0, std::ptr::null_mut())
        };
        let h = Handle::valid(h).ok_or_else(|| format!("open {letter}: error {}", last_error()))?;
        let mut data = [0u8; 128];
        let mut n = 0u32;
        let ok = unsafe {
            DeviceIoControl(h.raw(), FSCTL_GET_NTFS_VOLUME_DATA, std::ptr::null(), 0, data.as_mut_ptr() as *mut c_void, data.len() as u32, &mut n, std::ptr::null_mut())
        };
        if ok == 0 {
            return Err(format!("FSCTL_GET_NTFS_VOLUME_DATA {letter}: error {}", last_error()));
        }
        let u32_at = |o: usize| u32::from_le_bytes(data[o..o + 4].try_into().unwrap());
        let i64_at = |o: usize| i64::from_le_bytes(data[o..o + 8].try_into().unwrap());
        Ok(Volume {
            letter: letter.to_string(),
            h,
            sector: u32_at(40) as u64,
            cluster: u32_at(44) as u64,
            record_size: u32_at(48) as usize,
            mft_valid: i64_at(56) as u64,
            mft_start_lcn: i64_at(64),
        })
    }

    fn read_at(&self, offset: u64, buf: &mut [u8]) -> Result<(), String> {
        let mut ov = OVERLAPPED { Internal: 0, InternalHigh: 0, Offset: offset as u32, OffsetHigh: (offset >> 32) as u32, hEvent: std::ptr::null_mut() };
        let mut n = 0u32;
        let ok = unsafe { ReadFile(self.h.raw(), buf.as_mut_ptr(), buf.len() as u32, &mut n, &mut ov) };
        if ok == 0 || n as usize != buf.len() {
            return Err(format!("read {} at {offset}: error {} ({n} bytes)", self.letter, last_error()));
        }
        Ok(())
    }

    /// Reads whole sectors covering `len` bytes at `offset`.
    fn read_bytes(&self, offset: u64, len: usize) -> Result<Vec<u8>, String> {
        let start = offset - offset % self.sector;
        let end = (offset + len as u64).div_ceil(self.sector) * self.sector;
        let mut a = Aligned::new((end - start) as usize);
        self.read_at(start, a.buf())?;
        let skip = (offset - start) as usize;
        Ok(a.buf()[skip..skip + len].to_vec())
    }

    fn read_runs(&self, runs: &[Run], size: u64) -> Result<Vec<u8>, String> {
        let mut out = Vec::with_capacity(size as usize);
        for run in runs {
            if out.len() as u64 >= size {
                break;
            }
            let bytes = run.clusters * self.cluster;
            let take = bytes.min(size - out.len() as u64) as usize;
            match run.lcn {
                Some(lcn) => out.extend(self.read_bytes(lcn as u64 * self.cluster, take)?),
                None => out.resize(out.len() + take, 0),
            }
        }
        Ok(out)
    }

    pub fn query_journal(&self) -> Result<Option<Journal>, String> {
        let mut data = [0u8; 80];
        let mut n = 0u32;
        let ok = unsafe {
            DeviceIoControl(self.h.raw(), FSCTL_QUERY_USN_JOURNAL, std::ptr::null(), 0, data.as_mut_ptr() as *mut c_void, data.len() as u32, &mut n, std::ptr::null_mut())
        };
        if ok == 0 {
            let e = last_error();
            if e == ERROR_JOURNAL_NOT_ACTIVE || e == ERROR_JOURNAL_DELETE_IN_PROGRESS {
                return Ok(None);
            }
            return Err(format!("FSCTL_QUERY_USN_JOURNAL {}: error {e}", self.letter));
        }
        Ok(Some(Journal { id: u64::from_le_bytes(data[0..8].try_into().unwrap()), next: i64::from_le_bytes(data[16..24].try_into().unwrap()) }))
    }

    pub fn create_journal(&self) -> Result<(), String> {
        let input: [u64; 2] = [32 << 20, 8 << 20];
        let mut n = 0u32;
        let ok = unsafe {
            DeviceIoControl(self.h.raw(), FSCTL_CREATE_USN_JOURNAL, input.as_ptr() as *const c_void, 16, std::ptr::null_mut(), 0, &mut n, std::ptr::null_mut())
        };
        if ok == 0 {
            return Err(format!("FSCTL_CREATE_USN_JOURNAL {}: error {}", self.letter, last_error()));
        }
        Ok(())
    }

    /// Reads journal records from `journal.next`. Returns the next USN and the
    /// changed file record numbers.
    pub fn read_journal(&self, journal: Journal, buf: &mut [u8]) -> Result<(i64, Vec<u64>), JournalError> {
        #[repr(C)]
        struct ReadData {
            start: i64,
            reason_mask: u32,
            only_on_close: u32,
            timeout: u64,
            bytes_to_wait: u64,
            journal_id: u64,
        }
        let input = ReadData { start: journal.next, reason_mask: u32::MAX, only_on_close: 0, timeout: 0, bytes_to_wait: 0, journal_id: journal.id };
        let mut n = 0u32;
        let ok = unsafe {
            DeviceIoControl(
                self.h.raw(),
                FSCTL_READ_USN_JOURNAL,
                &input as *const ReadData as *const c_void,
                std::mem::size_of::<ReadData>() as u32,
                buf.as_mut_ptr() as *mut c_void,
                buf.len() as u32,
                &mut n,
                std::ptr::null_mut(),
            )
        };
        if ok == 0 {
            let e = last_error();
            return Err(match e {
                ERROR_JOURNAL_ENTRY_DELETED | ERROR_JOURNAL_NOT_ACTIVE | ERROR_JOURNAL_DELETE_IN_PROGRESS | ERROR_INVALID_PARAMETER => {
                    JournalError::Reset(format!("journal read error {e}"))
                }
                _ => JournalError::Io(format!("FSCTL_READ_USN_JOURNAL {}: error {e}", self.letter)),
            });
        }
        let Some((next, records)) = mft::parse_usn_buffer(&buf[..n as usize]) else {
            return Ok((journal.next, Vec::new()));
        };
        Ok((next, records.into_iter().map(|r| r.frn).collect()))
    }

    /// The current state of one file record. `None` when it is not in use.
    fn fetch_record(&self, frn: u64, buf: &mut Vec<u8>) -> Result<Option<RecordInfo>, String> {
        buf.resize(self.record_size + 16, 0);
        let input = frn as i64;
        let mut n = 0u32;
        let ok = unsafe {
            DeviceIoControl(
                self.h.raw(),
                FSCTL_GET_NTFS_FILE_RECORD,
                &input as *const i64 as *const c_void,
                8,
                buf.as_mut_ptr() as *mut c_void,
                buf.len() as u32,
                &mut n,
                std::ptr::null_mut(),
            )
        };
        if ok == 0 {
            return Err(format!("FSCTL_GET_NTFS_FILE_RECORD {} {frn}: error {}", self.letter, last_error()));
        }
        let got = u64::from_le_bytes(buf[0..8].try_into().unwrap()) & mft::FRN_MASK;
        if got != frn {
            return Ok(None); // the nearest lower record in use was returned
        }
        let len = (u32::from_le_bytes(buf[8..12].try_into().unwrap()) as usize).min((n as usize).saturating_sub(12));
        let rec = &mut buf[12..12 + len];
        if !mft::apply_fixups(rec) {
            return Ok(None);
        }
        Ok(mft::parse_record(rec).filter(|i| i.in_use && i.base == 0))
    }

    /// Adds the names and data size held in extension records.
    fn complete(&self, frn: u64, info: &mut RecordInfo, buf: &mut Vec<u8>) -> Result<(), String> {
        if is_complete(info) {
            return Ok(());
        }
        let list = match info.attr_list.take() {
            Some(AttrList::Resident(v)) => v,
            Some(AttrList::NonResident { runs, size }) => self.read_runs(&runs, size)?,
            None => return Ok(()),
        };
        for ext in mft::extension_records(&mft::parse_attr_list(&list), frn) {
            buf.resize(self.record_size + 16, 0);
            let input = ext as i64;
            let mut n = 0u32;
            let ok = unsafe {
                DeviceIoControl(self.h.raw(), FSCTL_GET_NTFS_FILE_RECORD, &input as *const i64 as *const c_void, 8, buf.as_mut_ptr() as *mut c_void, buf.len() as u32, &mut n, std::ptr::null_mut())
            };
            if ok == 0 || u64::from_le_bytes(buf[0..8].try_into().unwrap()) & mft::FRN_MASK != ext {
                continue;
            }
            let len = (u32::from_le_bytes(buf[8..12].try_into().unwrap()) as usize).min((n as usize).saturating_sub(12));
            let rec = &mut buf[12..12 + len];
            if mft::apply_fixups(rec) {
                if let Some(e) = mft::parse_record(rec) {
                    info.merge(e);
                }
            }
        }
        Ok(())
    }
}

fn is_complete(info: &RecordInfo) -> bool {
    info.primary_name().is_some() && (info.directory || info.data_size.is_some())
}

fn entry_of(info: &RecordInfo) -> Option<Entry> {
    let name = info.primary_name()?;
    let std = info.std.unwrap_or_default();
    Some(Entry {
        parent: name.parent as u32,
        name: name.name.clone(),
        size: if info.directory { 0 } else { info.data_size.unwrap_or(0) },
        mtime: std.mtime,
        ctime: std.ctime,
        attrs: (std.attrs & ATTR_MASK) | if info.directory { ATTR_DIRECTORY } else { 0 },
        sec: std.sec,
    })
}

/// Whether a record belongs in the index (user files and the root).
fn indexed(frn: u64) -> bool {
    frn == mft::ROOT_RECORD || frn >= mft::FIRST_USER_RECORD
}

/// Reads the whole MFT into a new store.
pub fn load(vol: &Volume, slot: &Slot) -> Result<Store, String> {
    let rs = vol.record_size;
    if rs == 0 || vol.cluster == 0 || vol.sector == 0 {
        return Err("bad NTFS geometry".into());
    }
    // $MFT's own record gives the runs of the MFT.
    let mut first = vol.read_bytes(vol.mft_start_lcn as u64 * vol.cluster, rs)?;
    if !mft::apply_fixups(&mut first) {
        return Err("bad $MFT record".into());
    }
    let mft_rec = mft::parse_record(&first).ok_or("bad $MFT record")?;
    let mut extents: Vec<(u64, Vec<Run>)> = mft_rec.data_runs.clone().into_iter().collect();
    if let Some(list) = &mft_rec.attr_list {
        let bytes = match list {
            AttrList::Resident(v) => v.clone(),
            AttrList::NonResident { runs, size } => vol.read_runs(runs, *size)?,
        };
        for ext in mft::data_extension_records(&mft::parse_attr_list(&bytes), 0) {
            let runs: Vec<Run> = extents.iter().flat_map(|(_, r)| r.iter().copied()).collect();
            let off = record_offset(&runs, ext, rs as u64, vol.cluster).ok_or("$MFT extension outside known runs")?;
            let mut rec = vol.read_bytes(off, rs)?;
            if mft::apply_fixups(&mut rec) {
                if let Some(info) = mft::parse_record(&rec) {
                    if let Some(dr) = info.data_runs {
                        extents.push(dr);
                    }
                }
            }
        }
    }
    extents.sort_by_key(|(vcn, _)| *vcn);
    let runs: Vec<Run> = extents.into_iter().flat_map(|(_, r)| r).collect();
    let total_records = (vol.mft_valid / rs as u64) as usize;

    let mut store = Store::new(mft::ROOT_RECORD as u32, &vol.letter, total_records);
    let mut pending: HashMap<u64, RecordInfo> = HashMap::new();
    let mut exts: HashMap<u64, Vec<RecordInfo>> = HashMap::new();
    let mut chunk = Aligned::new(READ_CHUNK);
    let mut vcn_bytes: u64 = 0; // byte offset within the MFT
    let mft_bytes = total_records as u64 * rs as u64;
    for run in &runs {
        let run_bytes = run.clusters * vol.cluster;
        let mut done = 0u64;
        while done < run_bytes && vcn_bytes + done < mft_bytes {
            if slot.stopping() {
                return Err("stopped".into());
            }
            // Whole clusters are read; records past the valid length are ignored.
            let take = (run_bytes - done).min(READ_CHUNK as u64) as usize;
            let first_record = (vcn_bytes + done) / rs as u64;
            match run.lcn {
                None => {} // sparse: no records
                Some(lcn) => {
                    let buf = &mut chunk.buf()[..take];
                    vol.read_at(lcn as u64 * vol.cluster + done, buf)?;
                    for (i, rec) in buf.chunks_exact_mut(rs).enumerate() {
                        let frn = first_record + i as u64;
                        if frn >= total_records as u64 {
                            break;
                        }
                        if !indexed(frn) || !mft::apply_fixups(rec) {
                            continue;
                        }
                        let Some(info) = mft::parse_record(rec) else { continue };
                        if !info.in_use {
                            continue;
                        }
                        if info.base != 0 {
                            if !info.names.is_empty() || info.data_size.is_some() {
                                exts.entry(info.base).or_default().push(info);
                            }
                            continue;
                        }
                        if is_complete(&info) {
                            if let Some(e) = entry_of(&info) {
                                store.set(frn as u32, Some(&e));
                            }
                        } else {
                            pending.insert(frn, info);
                        }
                    }
                }
            }
            done += take as u64;
        }
        vcn_bytes += run_bytes;
    }
    for (frn, mut info) in pending {
        for e in exts.remove(&frn).unwrap_or_default() {
            info.merge(e);
        }
        if let Some(e) = entry_of(&info) {
            store.set(frn as u32, Some(&e));
        }
    }
    let dropped = store.drop_orphans();
    info!("{}: {} records, {} folders, {dropped} unattached dropped", vol.letter, store.live, store.dirs);
    Ok(store)
}

fn record_offset(runs: &[Run], record: u64, rs: u64, cluster: u64) -> Option<u64> {
    let mut byte = record * rs;
    for run in runs {
        let len = run.clusters * cluster;
        if byte < len {
            return run.lcn.map(|l| l as u64 * cluster + byte);
        }
        byte -= len;
    }
    None
}

/// Re-reads changed records and applies them in one write-locked batch.
fn apply(vol: &Volume, slot: &Slot, frns: &[u64], buf: &mut Vec<u8>) -> Result<u64, String> {
    let mut seen = HashSet::new();
    let mut changes: Vec<(u32, Option<Entry>)> = Vec::new();
    for &frn in frns {
        if !indexed(frn) || frn > u32::MAX as u64 || !seen.insert(frn) {
            continue;
        }
        let entry = match vol.fetch_record(frn, buf)? {
            Some(mut info) => {
                vol.complete(frn, &mut info, buf)?;
                entry_of(&info)
            }
            None => None,
        };
        changes.push((frn as u32, entry));
    }
    if changes.is_empty() {
        return Ok(0);
    }
    let store = slot.store();
    let mut st = store.write().unwrap();
    for (r, e) in &changes {
        st.set(*r, e.as_ref());
    }
    // Entries that do not hang below the root ($Extend metadata) are removed.
    let mut path = String::new();
    for (r, e) in &changes {
        if e.is_some() && *r != st.root && !st.full_path(*r, &mut path) {
            st.set(*r, None);
        }
    }
    Ok(changes.len() as u64)
}

fn compact(slot: &Slot) {
    let store = slot.store();
    let needs = store.read().unwrap().needs_compaction();
    if needs {
        let (arena, offsets) = store.read().unwrap().compacted();
        // This thread is the store's only writer, so nothing changed since.
        store.write().unwrap().install_compacted(arena, offsets);
    }
}

/// Opens, loads and follows one volume until stopped. Errors mean the volume
/// went away or failed; the caller retries.
fn follow(svc: &Service, slot: &Slot) -> Result<(), String> {
    let vol = Volume::open(&slot.name)?;
    let mut first = true;
    loop {
        // Record the position, read the MFT, then replay from that position.
        let journal = match vol.query_journal()? {
            Some(j) => j,
            None => {
                if !svc.config.lock().unwrap().create_journals {
                    return Err("no USN journal (create_journals is off)".into());
                }
                vol.create_journal()?;
                vol.query_journal()?.ok_or("USN journal did not start")?
            }
        };
        slot.set_state(if first { "loading" } else { "reloading" });
        slot.refresh.store(false, Ordering::Relaxed);
        let t = Instant::now();
        let store = load(&vol, slot)?;
        let load_ms = t.elapsed().as_millis() as u64;
        slot.install(store);
        {
            let mut h = slot.health.lock().unwrap();
            h.load_ms = load_ms;
            h.journal_id = journal.id;
            h.next_usn = journal.next;
            h.error = None;
            if !first {
                h.reloads += 1;
            }
        }
        info!("{}: loaded in {load_ms} ms, journal {:#x} from USN {}", slot.name, journal.id, journal.next);
        first = false;
        let mut j = journal;
        let mut buf = vec![0u8; USN_BUFFER];
        let mut rec_buf = Vec::new();
        let mut checked = Instant::now();
        let mut ready = false;
        let reason = loop {
            if slot.stopping() || svc.stopping() {
                return Ok(());
            }
            if slot.refresh.load(Ordering::Relaxed) {
                break "re-read requested".to_string();
            }
            match vol.read_journal(j, &mut buf) {
                Ok((next, frns)) => {
                    let applied = apply(&vol, slot, &frns, &mut rec_buf)?;
                    let idle = next == j.next;
                    j.next = next;
                    {
                        let mut h = slot.health.lock().unwrap();
                        h.next_usn = next;
                        h.applied += applied;
                    }
                    if idle {
                        if !ready {
                            // Caught up with the changes made during the read.
                            slot.set_state("ready");
                            ready = true;
                        }
                        compact(slot);
                        slot.wait(POLL);
                    }
                }
                Err(JournalError::Reset(e)) => break e,
                Err(JournalError::Io(e)) => return Err(e),
            }
            if checked.elapsed() > JOURNAL_CHECK {
                checked = Instant::now();
                match vol.query_journal()? {
                    Some(now) if now.id == j.id => {}
                    _ => break "journal replaced".to_string(),
                }
            }
        };
        warning!("{}: re-reading the volume ({reason})", slot.name);
    }
}

/// Worker thread body for one NTFS slot.
pub fn run(svc: Arc<Service>, slot: Arc<Slot>) {
    while !slot.stopping() && !svc.stopping() {
        match follow(&svc, &slot) {
            Ok(()) => return,
            Err(e) => {
                if slot.stopping() || svc.stopping() {
                    return;
                }
                warning!("{}: {e}", slot.name);
                slot.fail("offline", e);
                // Its entries are no longer reachable; answer with nothing.
                slot.install(Store::new(0, &slot.name, 1));
                slot.wait(Duration::from_secs(15));
                slot.refresh.store(false, Ordering::Relaxed);
            }
        }
    }
}

/// Local NTFS volumes with a drive letter (fixed or removable).
pub fn local_ntfs_letters() -> Vec<String> {
    let mask = unsafe { GetLogicalDrives() };
    let mut out = Vec::new();
    for i in 0..26u32 {
        if mask & (1 << i) == 0 {
            continue;
        }
        let letter = format!("{}:", (b'A' + i as u8) as char);
        let root = wide(&format!("{letter}\\"));
        let ty = unsafe { GetDriveTypeW(root.as_ptr()) };
        if ty != DRIVE_FIXED && ty != DRIVE_REMOVABLE {
            continue;
        }
        let mut fs = [0u16; 32];
        let ok = unsafe {
            GetVolumeInformationW(root.as_ptr(), std::ptr::null_mut(), 0, std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null_mut(), fs.as_mut_ptr(), fs.len() as u32)
        };
        if ok != 0 && from_wide(&fs) == "NTFS" {
            out.push(letter);
        }
    }
    out
}
