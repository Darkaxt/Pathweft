//! Service core (workers, volume discovery, pipe) and the SCM entry points.

use crate::config::{self, Config, FolderRoot};
use crate::log::{info, warn};
use crate::state::{Kind, Options, Service, Slot};
use crate::{ntfs, pipe, scan, security};
use pathweft_index::ffi::*;
use std::ffi::c_void;
use std::sync::atomic::{AtomicPtr, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

pub const SERVICE_NAME: &str = "PathweftIndex";
const DISCOVERY: Duration = Duration::from_secs(30);

pub fn spawn_ntfs(svc: Arc<Service>, slot: Arc<Slot>) {
    std::thread::Builder::new().name(format!("ntfs {}", slot.name)).spawn(move || ntfs::run(svc, slot)).expect("spawn");
}

pub fn spawn_folder(svc: Arc<Service>, slot: Arc<Slot>, root: FolderRoot) {
    std::thread::Builder::new().name(format!("scan {}", slot.name)).spawn(move || scan::run(svc, slot, root)).expect("spawn");
}

/// Adds slots for NTFS volumes that appeared since the last pass.
fn discover(svc: &Arc<Service>) {
    let letters = match &svc.opts.volumes {
        Some(v) => v.clone(),
        None => {
            let cfg = svc.config.lock().unwrap().clone();
            ntfs::local_ntfs_letters().into_iter().filter(|l| cfg.wants_ntfs(l)).collect()
        }
    };
    for letter in letters {
        if svc.find(&letter).is_some() {
            continue;
        }
        if let Some(slot) = svc.add_slot(letter.clone(), Kind::Ntfs) {
            info!("{letter}: indexing");
            spawn_ntfs(svc.clone(), slot);
        }
    }
}

/// Starts everything and serves the pipe until `stop` is set.
pub fn run(opts: Options) -> Result<Arc<Service>, String> {
    let config = Config::load(&opts.config_path)?;
    if !security::enable_privilege("SeBackupPrivilege") {
        warn!("SeBackupPrivilege is not available; some folders' permissions cannot be read");
    }
    let svc = Arc::new(Service::new(opts, config));
    if svc.opts.folders {
        let folders = svc.config.lock().unwrap().folders.clone();
        for root in folders {
            if let Some(slot) = svc.add_slot(scan::root_name(&root.path), Kind::Folder(root.clone())) {
                spawn_folder(svc.clone(), slot, root);
            }
        }
    }
    discover(&svc);
    {
        let svc = svc.clone();
        std::thread::Builder::new()
            .name("discovery".into())
            .spawn(move || {
                while !svc.stopping() {
                    std::thread::sleep(DISCOVERY);
                    discover(&svc);
                }
            })
            .expect("spawn");
    }
    {
        let svc2 = svc.clone();
        std::thread::Builder::new()
            .name("pipe".into())
            .spawn(move || {
                if let Err(e) = pipe::serve(svc2.clone()) {
                    warn!("pipe server stopped: {e}");
                }
            })
            .expect("spawn");
    }
    Ok(svc)
}

pub fn shutdown(svc: &Service) {
    svc.shutdown.store(true, Ordering::Relaxed);
    for s in svc.all() {
        s.stop.store(true, Ordering::Relaxed);
        s.poke();
    }
    pipe::wake(&svc.opts.pipe);
}

pub fn service_options() -> Options {
    Options { pipe: pathweft_index::protocol::PIPE_NAME.into(), config_path: config::default_path(), volumes: None, folders: true, persist_config: true }
}

// ---------------------------------------------------------------- SCM

static STATUS_HANDLE: AtomicPtr<c_void> = AtomicPtr::new(std::ptr::null_mut());
static STOP: Mutex<Option<std::sync::mpsc::Sender<()>>> = Mutex::new(None);

fn report(state: u32, exit: u32, wait_hint: u32) {
    let status = SERVICE_STATUS {
        dwServiceType: SERVICE_WIN32_OWN_PROCESS,
        dwCurrentState: state,
        dwControlsAccepted: if state == SERVICE_RUNNING { SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN } else { 0 },
        dwWin32ExitCode: exit,
        dwServiceSpecificExitCode: 0,
        dwCheckPoint: 0,
        dwWaitHint: wait_hint,
    };
    let h = STATUS_HANDLE.load(Ordering::Relaxed);
    if !h.is_null() {
        unsafe {
            SetServiceStatus(h, &status);
        }
    }
}

unsafe extern "system" fn handler(control: u32, _event: u32, _data: *mut c_void, _context: *mut c_void) -> u32 {
    match control {
        SERVICE_CONTROL_STOP | SERVICE_CONTROL_SHUTDOWN => {
            report(SERVICE_STOP_PENDING, 0, 10_000);
            if let Some(tx) = STOP.lock().unwrap().as_ref() {
                let _ = tx.send(());
            }
            0
        }
        SERVICE_CONTROL_INTERROGATE => 0,
        _ => 120, // ERROR_CALL_NOT_IMPLEMENTED
    }
}

unsafe extern "system" fn service_main(_argc: u32, _argv: *mut *mut u16) {
    let name = wide(SERVICE_NAME);
    let h = RegisterServiceCtrlHandlerExW(name.as_ptr(), handler, std::ptr::null_mut());
    if h.is_null() {
        return;
    }
    STATUS_HANDLE.store(h, Ordering::Relaxed);
    report(SERVICE_START_PENDING, 0, 30_000);
    let (tx, rx) = std::sync::mpsc::channel();
    *STOP.lock().unwrap() = Some(tx);
    match run(service_options()) {
        Ok(svc) => {
            report(SERVICE_RUNNING, 0, 0);
            info!("service running");
            let _ = rx.recv();
            info!("service stopping");
            shutdown(&svc);
            report(SERVICE_STOPPED, 0, 0);
        }
        Err(e) => {
            warn!("service failed to start: {e}");
            report(SERVICE_STOPPED, 1064, 0); // ERROR_EXCEPTION_IN_SERVICE
        }
    }
}

/// Runs under the service control manager.
pub fn dispatch() -> Result<(), String> {
    let _ = std::fs::create_dir_all(config::data_dir());
    crate::log::init(Some(config::data_dir().join("service.log")), false);
    let mut name = wide(SERVICE_NAME);
    let table = [SERVICE_TABLE_ENTRYW { lpServiceName: name.as_mut_ptr(), lpServiceProc: Some(service_main) }, SERVICE_TABLE_ENTRYW { lpServiceName: std::ptr::null_mut(), lpServiceProc: None }];
    if unsafe { StartServiceCtrlDispatcherW(table.as_ptr()) } == 0 {
        return Err(format!("StartServiceCtrlDispatcher failed: error {} (run `console` to start in a terminal)", last_error()));
    }
    Ok(())
}
