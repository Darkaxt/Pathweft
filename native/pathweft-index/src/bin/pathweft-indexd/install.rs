//! `install` / `uninstall` (SPEC IDX-01). Both need an elevated token.

use crate::config::{self, Config};
use crate::security::LocalSd;
use crate::service::SERVICE_NAME;
use pathweft_index::ffi::*;
use std::ffi::c_void;
use std::path::PathBuf;
use std::time::{Duration, Instant};

const DISPLAY: &str = "Pathweft Index";
const DESCRIPTION: &str = "File-name index for Pathweft: reads NTFS volumes' MFT and follows their USN journals; answers local queries on \\\\.\\pipe\\PathweftIndex.";
/// ProgramData folder: SYSTEM and Administrators full control, users read.
const DATA_SDDL: &str = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FR;;;BU)";

pub fn install_dir() -> PathBuf {
    let base = std::env::var_os("ProgramFiles").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(r"C:\Program Files"));
    base.join("PathweftIndex")
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
    let sd = LocalSd::from_sddl(DATA_SDDL).ok_or("bad data folder descriptor")?;
    let sa = sd.attributes();
    if unsafe { CreateDirectoryW(wide(&dir.to_string_lossy()).as_ptr(), &sa) } == 0 {
        return Err(format!("creating {} failed: error {}", dir.display(), last_error()));
    }
    Ok(())
}

/// Copies the binaries, writes the config, registers and starts the service.
/// `allow` adds pipe users (default: the user running the install).
pub fn install(allow: &[String]) -> Result<(), String> {
    let scm = open_scm()?;
    let existing = open_service(&scm);
    if let Some(s) = &existing {
        println!("stopping the running service for the update");
        stop(s)?;
    }

    let dir = install_dir();
    std::fs::create_dir_all(&dir).map_err(|e| format!("{}: {e}", dir.display()))?;
    let me = std::env::current_exe().map_err(|e| e.to_string())?;
    let target = dir.join("pathweft-indexd.exe");
    std::fs::copy(&me, &target).map_err(|e| format!("copy to {}: {e}", target.display()))?;
    let here = me.parent().map(PathBuf::from).unwrap_or_default();
    for extra in ["pwindex.exe", "pathweft_index.dll"] {
        let src = here.join(extra);
        if src.exists() {
            std::fs::copy(&src, dir.join(extra)).map_err(|e| format!("copy {extra}: {e}"))?;
        }
    }
    println!("installed binaries to {}", dir.display());

    ensure_data_dir()?;
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
        Some(s) => s,
        None => {
            let binary = wide(&format!("\"{}\" service", target.display()));
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
            println!("registered service {SERVICE_NAME} (LocalSystem, automatic)");
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
    Ok(())
}

/// Stops and removes the service and its binaries. `purge` also removes the
/// config and log folder.
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
    let dir = install_dir();
    if dir.exists() {
        // The SCM may hold the image briefly after stopping.
        let mut last = Ok(());
        for _ in 0..20 {
            last = std::fs::remove_dir_all(&dir);
            if last.is_ok() {
                break;
            }
            std::thread::sleep(Duration::from_millis(250));
        }
        last.map_err(|e| format!("{}: {e}", dir.display()))?;
        println!("removed {}", dir.display());
    }
    if purge && config::data_dir().exists() {
        std::fs::remove_dir_all(config::data_dir()).map_err(|e| e.to_string())?;
        println!("removed {}", config::data_dir().display());
    }
    Ok(())
}
