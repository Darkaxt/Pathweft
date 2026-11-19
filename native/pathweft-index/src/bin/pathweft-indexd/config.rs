//! Service configuration: `%ProgramData%\PathweftIndex\config.json`.

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct FolderRoot {
    /// Root path as the user sees it (`H:\`, `M:\`, `\\server\share\dir`).
    pub path: String,
    /// SID of the user who added it; the scan runs with that user's token and
    /// only that user sees the results.
    pub owner: String,
    /// Longest time between two checks of any folder below the root; folders
    /// that change are checked more often, down to every 15 minutes.
    #[serde(default = "default_rescan")]
    pub rescan_minutes: u32,
}

fn default_rescan() -> u32 {
    360
}

fn yes() -> bool {
    true
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct Config {
    /// SIDs allowed to connect to the pipe. Empty: interactive users.
    #[serde(default)]
    pub allowed_users: Vec<String>,
    /// NTFS drive letters to index (`["C:", "D:"]`). Empty: every local NTFS
    /// volume with a letter, minus `ntfs_exclude`.
    #[serde(default)]
    pub ntfs_include: Vec<String>,
    #[serde(default)]
    pub ntfs_exclude: Vec<String>,
    /// Create a USN journal (32 MB) on a volume that has none.
    #[serde(default = "yes")]
    pub create_journals: bool,
    #[serde(default)]
    pub folders: Vec<FolderRoot>,
}

impl Default for Config {
    fn default() -> Config {
        Config { allowed_users: Vec::new(), ntfs_include: Vec::new(), ntfs_exclude: Vec::new(), create_journals: true, folders: Vec::new() }
    }
}

pub fn data_dir() -> PathBuf {
    let base = std::env::var_os("ProgramData").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(r"C:\ProgramData"));
    base.join("PathweftIndex")
}

pub fn default_path() -> PathBuf {
    data_dir().join("config.json")
}

/// Normalizes a drive letter argument to `X:`.
pub fn letter(s: &str) -> Option<String> {
    let c = s.trim().chars().next()?.to_ascii_uppercase();
    c.is_ascii_alphabetic().then(|| format!("{c}:"))
}

impl Config {
    pub fn load(path: &PathBuf) -> Result<Config, String> {
        match std::fs::read(path) {
            Ok(bytes) => serde_json::from_slice(&bytes).map_err(|e| format!("{}: {e}", path.display())),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Config::default()),
            Err(e) => Err(format!("{}: {e}", path.display())),
        }
    }

    /// Writes through a temporary file so a crash never leaves half a file.
    pub fn save(&self, path: &PathBuf) -> Result<(), String> {
        let json = serde_json::to_vec_pretty(self).map_err(|e| e.to_string())?;
        let tmp = path.with_extension("json.tmp");
        std::fs::write(&tmp, json).map_err(|e| format!("{}: {e}", tmp.display()))?;
        std::fs::rename(&tmp, path).map_err(|e| format!("{}: {e}", path.display()))
    }

    pub fn wants_ntfs(&self, letter: &str) -> bool {
        let has = |list: &[String]| list.iter().any(|l| l.eq_ignore_ascii_case(letter));
        (self.ntfs_include.is_empty() || has(&self.ntfs_include)) && !has(&self.ntfs_exclude)
    }
}
