//! `install` / `uninstall` / `stop` (SPEC IDX-01). All need an elevated token.
//!
//! The service runs from where Pathweft's setup put it
//! (`%ProgramFiles%\Pathweft\services\file_index`); nothing is copied.

use crate::config::{self, Config};
use crate::security::LocalSd;
use crate::service::SERVICE_NAME;
use pathweft_index::ffi::*;
use std::ffi::c_void;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

const DISPLAY: &str = "Pathweft Index";
const DESCRIPTION: &str = "File-name index for Pathweft: reads NTFS volumes' MFT and follows their USN journals; answers local queries on \\\\.\\pipe\\PathweftIndex.";
/// ProgramData folder: SYSTEM and Administrators full control, users read.
const DATA_SDDL: &str = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FR;;;BU)";

fn program_files() -> PathBuf {
    std::env::var_os("ProgramFiles").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(r"C:\Program Files"))
}

/// Where releases up to 1.1.82 copied the service; removed on install.
fn legacy_dir() -> PathBuf {
    program_files().join("PathweftIndex")
}

/// `\\?\C:\x` -> `C:\x` (the service command line and messages use plain paths).
fn plain(p: PathBuf) -> PathBuf {
    let s = p.to_string_lossy();
    match s.strip_prefix(r"\\?\") {
        Some(rest) if !rest.starts_with(r"UNC\") => PathBuf::from(rest),
        _ => p,
    }
}

fn folded(p: &Path) -> String {
    p.to_string_lossy().trim_end_matches('\\').to_lowercase()
}

/// True if `p` is `dir` or below it.
fn is_within(p: &Path, dir: &Path) -> bool {
    let (p, d) = (folded(p), folded(dir));
    p == d || p.starts_with(&format!("{d}\\"))
}

/// The running executable, canonical. A LocalSystem service must run from a
/// folder only administrators can change, so it has to be under Program Files.
fn installed_exe() -> Result<PathBuf, String> {
    let me = std::env::current_exe().map_err(|e| e.to_string())?;
    let me = plain(std::fs::canonicalize(&me).map_err(|e| format!("{}: {e}", me.display()))?);
    let pf = plain(std::fs::canonicalize(program_files()).unwrap_or_else(|_| program_files()));
    if !is_within(&me, &pf) {
        return Err(format!(
            "{} is not under {}; the service runs from where it is installed, so run the copy Pathweft's setup installed",
            me.display(),
            pf.display()
        ));
    }
    Ok(me)
}

fn remove_dir_retrying(dir: &Path) -> Result<(), String> {
    // The SCM may hold the old image briefly after stopping.
    let mut last = Ok(());
    for _ in 0..20 {
        last = std::fs::remove_dir_all(dir);
        if last.is_ok() {
            break;
        }
        std::thread::sleep(Duration::from_millis(250));
    }
    last.map_err(|e| format!("{}: {e}", dir.display()))?;
    println!("removed {}", dir.display());
    Ok(())
}

/// Removes the pre-1.1.83 copy in `%ProgramFiles%\PathweftIndex` unless `me`
/// runs from it.
fn remove_legacy(me: &Path) -> Result<(), String> {
    let dir = legacy_dir();
    if dir.exists() && !is_within(me, &dir) {
        remove_dir_retrying(&dir)?;
    }
    Ok(())
}

/// Moves config and log from `%ProgramData%\PathweftIndex` (the service is
/// stopped). An existing new config wins; the old folder is removed.
fn migrate_data() -> Result<(), String> {
    let old = config::legacy_data_dir();
    if !old.exists() {
        return Ok(());
    }
    let new = config::data_dir();
    for name in ["config.json", "service.log"] {
        let (from, to) = (old.join(name), new.join(name));
        if from.exists() && !to.exists() {
            std::fs::rename(&from, &to).map_err(|e| format!("move {} to {}: {e}", from.display(), to.display()))?;
            println!("moved {} to {}", from.display(), to.display());
        }
    }
    remove_dir_retrying(&old)
}

fn open_scm() -> Result<ScHandle, String> {
    let h = unsafe { OpenSCManagerW(std::ptr::null(), std::ptr::null(), SC_MANAGER_ALL_ACCESS) };
    if h.is_null() {
        return Err(format!("OpenSCManager failed: error {} (run elevated)", last_error()));
    }
    Ok(ScHandle(h))
}

fn open_service(scm: &ScHandle) -> Option<ScHandle> {
    let h = unsafe { OpenServiceW(scm.0, wide(SERVICE_NAME).as_ptr(), SERVICE_ALL_ACCESS) };
    (!h.is_null()).then_some(ScHandle(h))
}

fn state(svc: &ScHandle) -> u32 {
    let mut st = SERVICE_STATUS::default();
    unsafe {
        QueryServiceStatus(svc.0, &mut st);
    }
    st.dwCurrentState
}

fn wait_for(svc: &ScHandle, want: u32, timeout: Duration) -> bool {
    let end = Instant::now() + timeout;
    while Instant::now() < end {
        if state(svc) == want {
            return true;
        }
        std::thread::sleep(Duration::from_millis(250));
    }
    state(svc) == want
}

fn stop(svc: &ScHandle) -> Result<(), String> {
    if state(svc) == SERVICE_STOPPED {
        return Ok(());
    }
    // The service reports STOPPED before its process has exited (freeing a
    // large index takes a moment), and the binary stays locked until it has.
    let process = service_process(svc);
    let mut st = SERVICE_STATUS::default();
    if unsafe { ControlService(svc.0, SERVICE_CONTROL_STOP, &mut st) } == 0 && last_error() != ERROR_SERVICE_NOT_ACTIVE {
        return Err(format!("stopping the service failed: error {}", last_error()));
    }
    if !wait_for(svc, SERVICE_STOPPED, Duration::from_secs(30)) {
        return Err("the service did not stop within 30 s".into());
    }
    if let Some(h) = process {
        if unsafe { WaitForSingleObject(h.raw(), 30_000) } != WAIT_OBJECT_0 {
            return Err("the service process did not exit within 30 s".into());
        }
    }
    Ok(())
}

/// A handle to wait on the service's process, if it is running.
fn service_process(svc: &ScHandle) -> Option<Handle> {
    let mut sp = SERVICE_STATUS_PROCESS::default();
    let mut needed = 0u32;
    let ok = unsafe {
        QueryServiceStatusEx(svc.0, SC_STATUS_PROCESS_INFO, &mut sp as *mut _ as *mut u8, std::mem::size_of::<SERVICE_STATUS_PROCESS>() as u32, &mut needed)
    };
    if ok == 0 || sp.dwProcessId == 0 {
        return None;
    }
    let h = unsafe { OpenProcess(SYNCHRONIZE, 0, sp.dwProcessId) };
    (!h.is_null()).then(|| Handle(h))
}

fn ensure_data_dir() -> Result<(), String> {
    let dir = config::data_dir();
    if dir.exists() {
        return Ok(());
    }
    if let Some(parent) = dir.parent() {
        std::fs::create_dir_all(parent).map_err(|e| format!("{}: {e}", parent.display()))?;
    }
    let sd = LocalSd::from_sddl(DATA_SDDL).ok_or("bad data folder descriptor")?;
    let sa = sd.attributes();
    if unsafe { CreateDirectoryW(wide(&dir.to_string_lossy()).as_ptr(), &sa) } == 0 {
        return Err(format!("creating {} failed: error {}", dir.display(), last_error()));
    }
    Ok(())
}

/// Registers the service to run this executable where it is, writes the
/// config and starts it. An existing registration (also one pointing at the
/// old `%ProgramFiles%\PathweftIndex` copy) is re-pointed here; the old copy
/// and the old data folder are moved away. `allow` adds pipe users (default:
/// the user running the install).
pub fn install(allow: &[String]) -> Result<(), String> {
    let me = installed_exe()?;
    let scm = open_scm()?;
    let existing = open_service(&scm);
    if let Some(s) = &existing {
        println!("stopping the running service for the update");
        stop(s)?;
    }
    let binary = wide(&format!("\"{}\" service", me.display()));

    ensure_data_dir()?;
    migrate_data()?;
    let cfg_path = config::default_path();
    let mut cfg = Config::load(&cfg_path)?;
    let mut users: Vec<String> = allow.to_vec();
    if users.is_empty() {
        users.push(current_user_sid().ok_or("cannot read the installing user's SID")?);
    }
    for u in users {
        if !cfg.allowed_users.iter().any(|x| x.eq_ignore_ascii_case(&u)) {
            cfg.allowed_users.push(u);
        }
    }
    cfg.save(&cfg_path)?;
    println!("config {} (pipe users: {})", cfg_path.display(), cfg.allowed_users.join(", "));

    let service = match existing {
        Some(s) => {
            let ok = unsafe {
                ChangeServiceConfigW(
                    s.0,
                    SERVICE_NO_CHANGE,
                    SERVICE_NO_CHANGE,
                    SERVICE_NO_CHANGE,
                    binary.as_ptr(),
                    std::ptr::null(),
                    std::ptr::null_mut(),
                    std::ptr::null(),
                    std::ptr::null(),
                    std::ptr::null(),
                    std::ptr::null(),
                )
            };
            if ok == 0 {
                return Err(format!("ChangeServiceConfig failed: error {}", last_error()));
            }
            println!("service {SERVICE_NAME} runs {}", me.display());
            s
        }
        None => {
            let h = unsafe {
                CreateServiceW(
                    scm.0,
                    wide(SERVICE_NAME).as_ptr(),
                    wide(DISPLAY).as_ptr(),
                    SERVICE_ALL_ACCESS,
                    SERVICE_WIN32_OWN_PROCESS,
                    SERVICE_AUTO_START,
                    SERVICE_ERROR_NORMAL,
                    binary.as_ptr(),
                    std::ptr::null(),
                    std::ptr::null_mut(),
                    std::ptr::null(),
                    std::ptr::null(), // LocalSystem
                    std::ptr::null(),
                )
            };
            if h.is_null() {
                return Err(format!("CreateService failed: error {}", last_error()));
            }
            println!("registered service {SERVICE_NAME} (LocalSystem, automatic): {}", me.display());
            ScHandle(h)
        }
    };
    let mut desc_text = wide(DESCRIPTION);
    let mut desc = SERVICE_DESCRIPTIONW { lpDescription: desc_text.as_mut_ptr() };
    let mut actions = [SC_ACTION { Type: SC_ACTION_RESTART, Delay: 5_000 }, SC_ACTION { Type: SC_ACTION_RESTART, Delay: 30_000 }, SC_ACTION { Type: SC_ACTION_RESTART, Delay: 120_000 }];
    let mut failure = SERVICE_FAILURE_ACTIONSW {
        dwResetPeriod: 86_400,
        lpRebootMsg: std::ptr::null_mut(),
        lpCommand: std::ptr::null_mut(),
        cActions: actions.len() as u32,
        lpsaActions: actions.as_mut_ptr(),
    };
    unsafe {
        ChangeServiceConfig2W(service.0, SERVICE_CONFIG_DESCRIPTION, &mut desc as *mut _ as *mut c_void);
        ChangeServiceConfig2W(service.0, SERVICE_CONFIG_FAILURE_ACTIONS, &mut failure as *mut _ as *mut c_void);
    }
    if unsafe { StartServiceW(service.0, 0, std::ptr::null()) } == 0 && last_error() != ERROR_SERVICE_ALREADY_RUNNING {
        return Err(format!("StartService failed: error {}", last_error()));
    }
    if !wait_for(&service, SERVICE_RUNNING, Duration::from_secs(30)) {
        return Err("the service did not reach RUNNING within 30 s; see service.log".into());
    }
    println!("service running");
    remove_legacy(&me)
}

/// Stops the service and waits until its process has exited, so setup can
/// replace the binaries. Not installed or already stopped: success.
pub fn stop_service() -> Result<(), String> {
    let scm = open_scm()?;
    match open_service(&scm) {
        Some(s) => {
            stop(&s)?;
            println!("service {SERVICE_NAME} stopped");
        }
        None => println!("service {SERVICE_NAME} is not installed"),
    }
    Ok(())
}

/// Stops and removes the service, and the old `%ProgramFiles%\PathweftIndex`
/// copy if one is left; its own binaries belong to Pathweft's setup, which
/// removes them. `purge` also removes the config and log folder (old and new).
pub fn uninstall(purge: bool) -> Result<(), String> {
    let scm = open_scm()?;
    match open_service(&scm) {
        Some(s) => {
            stop(&s)?;
            if unsafe { DeleteService(s.0) } == 0 {
                return Err(format!("DeleteService failed: error {}", last_error()));
            }
            println!("service {SERVICE_NAME} removed");
        }
        None => println!("service {SERVICE_NAME} is not installed"),
    }
    if let Ok(me) = std::env::current_exe() {
        remove_legacy(&plain(std::fs::canonicalize(&me).unwrap_or(me)))?;
    }
    if purge {
        for dir in [config::data_dir(), config::legacy_data_dir()] {
            if dir.exists() {
                remove_dir_retrying(&dir)?;
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn within_compares_whole_components_without_case() {
        let pf = PathBuf::from(r"C:\Program Files");
        assert!(is_within(Path::new(r"c:\program files\Pathweft\services\file_index\pathweft-indexd.exe"), &pf));
        assert!(is_within(Path::new(r"C:\Program Files\"), &pf));
        assert!(!is_within(Path::new(r"C:\Program Files (x86)\x.exe"), &pf));
        assert!(!is_within(Path::new(r"D:\Temp\pathweft-indexd.exe"), &pf));
        let legacy = PathBuf::from(r"C:\Program Files\PathweftIndex");
        assert!(!is_within(Path::new(r"C:\Program Files\PathweftIndexer\a.exe"), &legacy));
    }

    #[test]
    fn plain_strips_only_local_verbatim_prefixes() {
        assert_eq!(plain(PathBuf::from(r"\\?\C:\Program Files\a.exe")), PathBuf::from(r"C:\Program Files\a.exe"));
        assert_eq!(plain(PathBuf::from(r"\\?\UNC\srv\share\a.exe")), PathBuf::from(r"\\?\UNC\srv\share\a.exe"));
        assert_eq!(plain(PathBuf::from(r"C:\a")), PathBuf::from(r"C:\a"));
    }
}
