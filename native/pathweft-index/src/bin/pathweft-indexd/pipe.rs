//! The local named pipe (SPEC IDX-07) and request handling.

use crate::config::FolderRoot;
use crate::content::{self, ContentStore};
use crate::log::{info, warning};
use crate::scan;
use crate::security::{self, CallerAccess, ClientToken, LocalSd};
use crate::state::{Kind, Service, Slot};
use pathweft_index::ffi::*;
use pathweft_index::protocol::*;
use pathweft_index::query::{self, Outcome};
use std::sync::atomic::Ordering;
use std::sync::Arc;
use std::time::Instant;

const MAX_CONNECTIONS: usize = 16;
const ACCESS_ROUNDS: usize = 4;

fn create_instance(name: &[u16], sd: &LocalSd, first: bool) -> Result<Handle, String> {
    let sa = sd.attributes();
    let mode = PIPE_ACCESS_DUPLEX | if first { FILE_FLAG_FIRST_PIPE_INSTANCE } else { 0 };
    let h = unsafe {
        CreateNamedPipeW(name.as_ptr(), mode, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, PIPE_UNLIMITED_INSTANCES, 1 << 16, 1 << 16, 0, &sa)
    };
    Handle::valid(h).ok_or_else(|| format!("CreateNamedPipe failed: error {}", last_error()))
}

/// Accepts connections until shutdown. One thread per connection.
pub fn serve(svc: Arc<Service>) -> Result<(), String> {
    let allowed = svc.config.lock().unwrap().allowed_users.clone();
    let sd = LocalSd::from_sddl(&security::pipe_sddl(&allowed)).ok_or("bad pipe security descriptor")?;
    let name = wide(&svc.opts.pipe);
    let mut first = true;
    info!("listening on {}", svc.opts.pipe);
    while !svc.stopping() {
        let h = create_instance(&name, &sd, first)?;
        first = false;
        let ok = unsafe { ConnectNamedPipe(h.raw(), std::ptr::null_mut()) };
        if ok == 0 && last_error() != ERROR_PIPE_CONNECTED {
            continue;
        }
        if svc.stopping() {
            break;
        }
        if svc.connections.load(Ordering::Relaxed) >= MAX_CONNECTIONS {
            let mut s = PipeStream(h);
            let reply = serde_json::to_vec(&Response::failure("too many connections")).unwrap_or_default();
            let _ = write_frame(&mut s, &reply);
            continue;
        }
        svc.connections.fetch_add(1, Ordering::Relaxed);
        let svc2 = svc.clone();
        std::thread::spawn(move || {
            connection(&svc2, PipeStream(h));
            svc2.connections.fetch_sub(1, Ordering::Relaxed);
        });
    }
    Ok(())
}

/// Unblocks `serve` at shutdown by connecting once.
pub fn wake(pipe: &str) {
    let _ = pathweft_index::client::Client::connect_to(pipe, std::time::Duration::from_millis(200));
}

fn connection(svc: &Arc<Service>, mut stream: PipeStream) {
    let mut client: Option<ClientToken> = None;
    loop {
        let frame = match read_frame(&mut stream) {
            Ok(Some(f)) => f,
            _ => break,
        };
        if client.is_none() {
            match security::client_token(stream.0.raw()) {
                Ok(c) => {
                    svc.tokens.remember(&c);
                    client = Some(c);
                }
                Err(e) => {
                    warning!("caller identity: {e}");
                    let reply = serde_json::to_vec(&Response::failure(format!("caller identity unavailable: {e}"))).unwrap_or_default();
                    let _ = write_frame(&mut stream, &reply);
                    break;
                }
            }
        }
        let c = client.as_ref().unwrap();
        let t = Instant::now();
        let mut response = match serde_json::from_slice::<Request>(&frame) {
            Ok(req) => handle(svc, c, req),
            Err(e) => Response::failure(format!("bad request: {e}")),
        };
        response.elapsed_us = t.elapsed().as_micros() as u64;
        let bytes = serde_json::to_vec(&response).unwrap_or_default();
        if write_frame(&mut stream, &bytes).is_err() {
            break;
        }
    }
    unsafe {
        FlushFileBuffers(stream.0.raw());
        DisconnectNamedPipe(stream.0.raw());
    }
}

fn mark_updating(slots: &[Arc<Slot>], r: &mut Response) {
    r.updating_stores = slots.iter().filter(|s| s.updating()).map(|s| s.name.clone()).collect();
    r.updating = !r.updating_stores.is_empty();
}

/// Runs `f` until it stops asking for access decisions.
fn with_access<T>(
    access: &CallerAccess,
    mut f: impl FnMut() -> Result<Outcome<T>, String>,
) -> Result<T, String> {
    for _ in 0..ACCESS_ROUNDS {
        match f()? {
            Outcome::Done(v) => return Ok(v),
            Outcome::NeedAccess(u) => access.resolve(&u),
        }
    }
    Err("access evaluation did not converge".into())
}

pub fn handle(svc: &Arc<Service>, client: &ClientToken, req: Request) -> Response {
    match req {
        Request::Query(q) => {
            let (slots, refs) = svc.snapshot();
            let access = CallerAccess::new(svc, client);
            let lookup = svc.content.as_ref().map(|c| c as &dyn query::Content);
            match with_access(&access, || query::query(&refs, &q, &access, lookup)) {
                Ok(result) => {
                    let mut r = Response { ok: true, total: Some(result.total), hits: Some(result.hits), ..Default::default() };
                    mark_updating(&slots, &mut r);
                    r
                }
                Err(e) => Response::failure(e),
            }
        }
        Request::FolderSizes { paths } => {
            let (slots, refs) = svc.snapshot();
            let sizes = query::folder_sizes(&refs, &paths);
            let sizes = paths.into_iter().zip(sizes).map(|(path, size)| FolderSize { path, size }).collect();
            let mut r = Response { ok: true, sizes: Some(sizes), ..Default::default() };
            mark_updating(&slots, &mut r);
            r
        }
        Request::Status => {
            let slots = svc.all();
            let mut r = Response { ok: true, status: Some(status(svc, client, &slots)), ..Default::default() };
            mark_updating(&slots, &mut r);
            r
        }
        Request::FolderAdd { path, rescan_minutes } => folder_add(svc, client, path, rescan_minutes),
        Request::FolderRemove { path } => {
            let name = scan::root_name(&path);
            let Some(slot) = svc.find(&name) else { return Response::failure(format!("{name} is not an indexed folder")) };
            match &slot.kind {
                Kind::Folder(f) if f.owner.eq_ignore_ascii_case(&client.sid) => {}
                Kind::Folder(_) => return Response::failure("only the user who added a folder can remove it"),
                Kind::Ntfs => return Response::failure(format!("{name} is an NTFS volume; exclude it in the config instead")),
            }
            svc.remove_slot(&name);
            svc.config.lock().unwrap().folders.retain(|f| !scan::root_name(&f.path).eq_ignore_ascii_case(&name));
            if let Err(e) = svc.save_config() {
                return Response::failure(format!("removed, but the config was not saved: {e}"));
            }
            info!("folder removed: {name}");
            Response { ok: true, ..Default::default() }
        }
        Request::Rescan { target } => {
            let name = if target.len() <= 3 && target.chars().nth(1) == Some(':') { target[..2].to_ascii_uppercase() } else { scan::root_name(&target) };
            let slot = svc.find(&name).or_else(|| svc.find(&scan::root_name(&target)));
            match slot {
                Some(s) => {
                    s.refresh.store(true, Ordering::Relaxed);
                    s.poke();
                    info!("re-read requested for {}", s.name);
                    Response { ok: true, ..Default::default() }
                }
                None => Response::failure(format!("{target} is not indexed")),
            }
        }
        other => match store(svc) {
            Ok(c) => content_op(svc, client, c, other),
            Err(r) => r,
        },
    }
}

fn store(svc: &Service) -> Result<&ContentStore, Response> {
    svc.content.as_ref().ok_or_else(|| {
        Response::failure(match &svc.content_error {
            Some(e) => format!("the content index is unavailable: {e}"),
            None => "this instance has no content index".into(),
        })
    })
}

fn done(r: Result<Response, String>) -> Response {
    r.unwrap_or_else(Response::failure)
}

/// The content index ops (SPEC IDX-15..19, protocol 2).
fn content_op(svc: &Arc<Service>, client: &ClientToken, c: &ContentStore, req: Request) -> Response {
    let ok = || Response { ok: true, ..Default::default() };
    match req {
        Request::ContentScopeAdd { path } => done((|| {
            let (_, refs) = svc.snapshot();
            if query::resolve_paths(&refs, &[path.clone()], true)[0].is_none() {
                return Err(format!("{path} is not an indexed folder"));
            }
            let msg = c.scope_add(&path)?;
            info!("content scope: {msg}");
            svc.content_poke();
            Ok(ok())
        })()),
        Request::ContentScopeRemove { path } => done(c.scope_remove(&path).map(|n| {
            info!("content scope removed: {path} ({n} files purged)");
            ok()
        })),
        Request::ContentScopes => done(c.scopes().map(|s| Response { ok: true, scopes: Some(s), ..Default::default() })),
        Request::ContentNext { count, version } => done(c.next(count, version).map(|f| Response { ok: true, files: Some(f), ..Default::default() })),
        Request::ContentPut { id, version, read, after, chunks, last } => {
            let present = if last {
                let (_, refs) = svc.snapshot();
                c.path_of(id).and_then(|p| content::present(&refs, &p))
            } else {
                None
            };
            done(c.put(id, version, read, after, &chunks, last, present).map(|_| ok()))
        }
        Request::ContentFail { id, version, read, error, unsupported } => done(c.fail(id, version, read, &error, unsupported).map(|_| ok())),
        Request::ContentState { folder } => {
            if !content::may_list(svc, &client.sid, client.token.raw(), &folder) {
                return Response::failure(format!("{folder}: access denied"));
            }
            done(c.state(&folder).map(|f| Response { ok: true, files: Some(f), ..Default::default() }))
        }
        Request::ContentStatus => done(c.status().map(|s| Response { ok: true, content: Some(s), ..Default::default() })),
        _ => Response::failure("unexpected request"),
    }
}

fn folder_add(svc: &Arc<Service>, client: &ClientToken, path: String, rescan_minutes: Option<u32>) -> Response {
    if !svc.opts.folders {
        return Response::failure("folder roots are disabled in this instance");
    }
    let absolute = path.starts_with(r"\\") || (path.len() >= 2 && path.as_bytes()[1] == b':');
    if !absolute || path.contains("..") {
        return Response::failure("the folder must be an absolute path (X:\\... or \\\\server\\share\\...)");
    }
    let name = scan::root_name(&path);
    if svc.find(&name).is_some() {
        return Response::failure(format!("{name} is already indexed"));
    }
    let root = FolderRoot { path: path.clone(), owner: client.sid.clone(), rescan_minutes: rescan_minutes.unwrap_or(360) };
    let Some(slot) = svc.add_slot(name.clone(), Kind::Folder(root.clone())) else {
        return Response::failure(format!("{name} is already indexed"));
    };
    svc.config.lock().unwrap().folders.push(root.clone());
    let saved = svc.save_config();
    crate::service::spawn_folder(svc.clone(), slot, root);
    info!("folder added: {name} by {}", client.sid);
    match saved {
        Ok(()) => Response { ok: true, ..Default::default() },
        Err(e) => Response::failure(format!("indexing, but the config was not saved: {e}")),
    }
}

fn status(svc: &Service, client: &ClientToken, slots: &[Arc<Slot>]) -> Status {
    let mut mem = PROCESS_MEMORY_COUNTERS_EX { cb: std::mem::size_of::<PROCESS_MEMORY_COUNTERS_EX>() as u32, ..Default::default() };
    let cb = mem.cb;
    unsafe {
        K32GetProcessMemoryInfo(GetCurrentProcess(), &mut mem, cb);
    }
    let stores = slots
        .iter()
        .map(|s| {
            let h = s.health.lock().unwrap().clone();
            let store = s.store();
            let st = store.read().unwrap();
            let (kind, owner) = match &s.kind {
                Kind::Ntfs => ("ntfs", None),
                Kind::Folder(f) => ("folder", Some(f.owner.clone())),
            };
            StoreStatus {
                name: s.name.clone(),
                kind: kind.into(),
                state: h.state.into(),
                items: st.live,
                folders: st.dirs,
                journal_id: h.journal_id,
                next_usn: h.next_usn,
                load_ms: h.load_ms,
                reloads: h.reloads,
                applied_changes: h.applied,
                memory_bytes: st.memory_bytes(),
                error: h.error.clone(),
                owner,
            }
        })
        .collect();
    Status {
        version: env!("CARGO_PKG_VERSION").into(),
        protocol: PROTOCOL_VERSION,
        pid: unsafe { GetCurrentProcessId() },
        uptime_s: svc.started.elapsed().as_secs(),
        working_set: mem.WorkingSetSize as u64,
        private_bytes: mem.PrivateUsage as u64,
        caller: client.sid.clone(),
        stores,
    }
}
