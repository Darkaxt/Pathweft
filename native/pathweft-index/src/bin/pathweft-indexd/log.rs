//! Minimal file logger: one line per event, rotated at 4 MB.

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;
use std::time::{SystemTime, UNIX_EPOCH};

struct Sink {
    path: Option<PathBuf>,
    file: Option<File>,
    stderr: bool,
}

static SINK: Mutex<Sink> = Mutex::new(Sink { path: None, file: None, stderr: true });

pub fn init(path: Option<PathBuf>, stderr: bool) {
    let mut s = SINK.lock().unwrap();
    s.stderr = stderr;
    s.file = path.as_ref().and_then(|p| OpenOptions::new().create(true).append(true).open(p).ok());
    s.path = path;
}

fn stamp() -> String {
    let d = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default();
    let secs = d.as_secs();
    let (days, rem) = (secs / 86_400, secs % 86_400);
    // Civil date from days since 1970-01-01 (UTC).
    let z = days as i64 + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + if month <= 2 { 1 } else { 0 };
    format!(
        "{year:04}-{month:02}-{day:02}T{:02}:{:02}:{:02}.{:03}Z",
        rem / 3600,
        rem % 3600 / 60,
        rem % 60,
        d.subsec_millis()
    )
}

pub fn write(level: &str, message: &str) {
    let line = format!("{} {level} {message}\n", stamp());
    let mut s = SINK.lock().unwrap();
    if s.stderr {
        eprint!("{line}");
    }
    let rotate = s.file.as_ref().and_then(|f| f.metadata().ok()).map(|m| m.len() > 4 << 20).unwrap_or(false);
    if rotate {
        if let Some(p) = s.path.clone() {
            s.file = None;
            let old = p.with_extension("log.old");
            let _ = std::fs::remove_file(&old);
            let _ = std::fs::rename(&p, &old);
            s.file = OpenOptions::new().create(true).append(true).open(&p).ok();
        }
    }
    if let Some(f) = s.file.as_mut() {
        let _ = f.write_all(line.as_bytes());
    }
}

macro_rules! info {
    ($($t:tt)*) => { $crate::log::write("INFO", &format!($($t)*)) };
}
macro_rules! warning {
    ($($t:tt)*) => { $crate::log::write("WARN", &format!($($t)*)) };
}
pub(crate) use info;
pub(crate) use warning;
