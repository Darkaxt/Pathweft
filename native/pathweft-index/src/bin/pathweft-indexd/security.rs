//! Caller identity and the visibility rule (SPEC IDX-08): an entry is visible
//! when the caller's token is granted FILE_LIST_DIRECTORY on its parent
//! directory. Security descriptors are read as SYSTEM with backup semantics
//! (no data access, no recall) and checked with AccessCheck against the
//! caller's impersonation token. Results are cached per (caller SID,
//! elevation, store, NTFS security ID): the same user's standard and elevated
//! (administrator view) tokens list different folders.

use crate::log::warning;
use crate::state::{Kind, Service};
use pathweft_index::ffi::*;
use pathweft_index::query::{Access, Unknown};
use std::collections::HashMap;
use std::ffi::c_void;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

/// Enables a privilege in the process token (SeBackupPrivilege for reading
/// security descriptors of directories SYSTEM is not granted).
pub fn enable_privilege(name: &str) -> bool {
    unsafe {
        let mut token: HANDLE = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &mut token) == 0 {
            return false;
        }
        let token = Handle(token);
        let mut luid = LUID::default();
        if LookupPrivilegeValueW(std::ptr::null(), wide(name).as_ptr(), &mut luid) == 0 {
            return false;
        }
        let tp = TOKEN_PRIVILEGES { PrivilegeCount: 1, Privileges: [LUID_AND_ATTRIBUTES { Luid: luid, Attributes: SE_PRIVILEGE_ENABLED }] };
        AdjustTokenPrivileges(token.raw(), 0, &tp, 0, std::ptr::null_mut(), std::ptr::null_mut()) != 0 && last_error() == 0
    }
}

/// A security descriptor allocated by ConvertStringSecurityDescriptor.
pub struct LocalSd(pub *mut c_void);

unsafe impl Send for LocalSd {}
unsafe impl Sync for LocalSd {}

impl LocalSd {
    pub fn from_sddl(sddl: &str) -> Option<LocalSd> {
        let mut sd: *mut c_void = std::ptr::null_mut();
        let ok = unsafe { ConvertStringSecurityDescriptorToSecurityDescriptorW(wide(sddl).as_ptr(), SDDL_REVISION_1, &mut sd, std::ptr::null_mut()) };
        (ok != 0 && !sd.is_null()).then_some(LocalSd(sd))
    }
    pub fn attributes(&self) -> SECURITY_ATTRIBUTES {
        SECURITY_ATTRIBUTES { nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32, lpSecurityDescriptor: self.0, bInheritHandle: 0 }
    }
}

impl Drop for LocalSd {
    fn drop(&mut self) {
        unsafe {
            LocalFree(self.0);
        }
    }
}

/// Pipe DACL: network logons denied, SYSTEM full, the allowed users (or
/// interactive users when none are configured) read/write.
pub fn pipe_sddl(allowed: &[String]) -> String {
    let mut s = String::from("D:P(D;;GA;;;NU)(A;;GA;;;SY)");
    if allowed.is_empty() {
        s.push_str("(A;;GRGW;;;IU)");
    }
    for sid in allowed {
        if sid.starts_with("S-1-") && sid.chars().all(|c| c.is_ascii_alphanumeric() || c == '-') {
            s.push_str(&format!("(A;;GRGW;;;{sid})"));
        }
    }
    s
}

/// The caller's token, captured while impersonating the pipe client.
pub struct ClientToken {
    pub token: Handle,
    pub sid: String,
    /// The elevated half of a split token (Pathweft's administrator view).
    pub elevated: bool,
}

/// Captures the pipe client's impersonation token. Must be called after the
/// first read on the pipe.
pub fn client_token(pipe: HANDLE) -> Result<ClientToken, String> {
    unsafe {
        if ImpersonateNamedPipeClient(pipe) == 0 {
            return Err(format!("ImpersonateNamedPipeClient failed: {}", last_error()));
        }
        let mut token: HANDLE = std::ptr::null_mut();
        let ok = OpenThreadToken(GetCurrentThread(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, 1, &mut token);
        let err = last_error();
        RevertToSelf();
        if ok == 0 {
            return Err(format!("OpenThreadToken failed: {err}"));
        }
        let token = Handle(token);
        let sid = token_user_sid(token.raw()).ok_or("caller SID unavailable")?;
        let elevated = token_elevated(token.raw());
        Ok(ClientToken { token, sid, elevated })
    }
}

/// Duplicated user tokens, kept so folder scans can run as the user who
/// added the folder. Refreshed on every standard connection from that user.
/// An elevated connection (Pathweft's administrator view) is never kept: its
/// token belongs to another logon session, which has none of the user's drive
/// mappings or share credentials, so T:, H: and network roots read as "path
/// not found" (SPEC IDX-03).
#[derive(Default)]
pub struct TokenCache {
    map: Mutex<HashMap<String, Arc<Handle>>>,
}

impl TokenCache {
    pub fn remember(&self, client: &ClientToken) {
        if !scans_with(client) {
            return;
        }
        unsafe {
            let mut dup: HANDLE = std::ptr::null_mut();
            if DuplicateTokenEx(
                client.token.raw(),
                TOKEN_QUERY | TOKEN_IMPERSONATE | TOKEN_DUPLICATE,
                std::ptr::null(),
                SECURITY_IMPERSONATION_LEVEL_IMPERSONATION,
                TOKEN_TYPE_IMPERSONATION,
                &mut dup,
            ) != 0
            {
                self.map.lock().unwrap().insert(client.sid.clone(), Arc::new(Handle(dup)));
            }
        }
    }
    pub fn get(&self, sid: &str) -> Option<Arc<Handle>> {
        self.map.lock().unwrap().get(sid).cloned()
    }
}

/// Whether folder scans may run with this caller's token (SPEC IDX-03).
fn scans_with(client: &ClientToken) -> bool {
    !client.elevated
}

/// (caller SID, elevated, slot, security ID) -> listable, cleared every 15
/// minutes so changed permissions take effect.
pub struct AccessCache {
    map: Mutex<(Instant, HashMap<(String, bool, usize, u32), bool>)>,
}

impl Default for AccessCache {
    fn default() -> AccessCache {
        AccessCache { map: Mutex::new((Instant::now(), HashMap::new())) }
    }
}

const ACCESS_TTL: Duration = Duration::from_secs(15 * 60);

impl AccessCache {
    fn get(&self, client: &ClientToken, slot: usize, sec: u32) -> Option<bool> {
        let mut m = self.map.lock().unwrap();
        if m.0.elapsed() > ACCESS_TTL {
            m.0 = Instant::now();
            m.1.clear();
        }
        m.1.get(&(client.sid.clone(), client.elevated, slot, sec)).copied()
    }
    fn put(&self, client: &ClientToken, slot: usize, sec: u32, value: bool) {
        self.map.lock().unwrap().1.insert((client.sid.clone(), client.elevated, slot, sec), value);
    }
}

/// The visibility oracle for one request.
pub struct CallerAccess<'a> {
    pub svc: &'a Service,
    pub client: &'a ClientToken,
    /// Answers decided for this request only (descriptor unreadable).
    local: Mutex<HashMap<(usize, u32), bool>>,
    folder_owner: HashMap<usize, String>,
}

impl<'a> CallerAccess<'a> {
    pub fn new(svc: &'a Service, client: &'a ClientToken) -> CallerAccess<'a> {
        let folder_owner = svc
            .all()
            .iter()
            .filter_map(|s| match &s.kind {
                Kind::Folder(f) => Some((s.id, f.owner.clone())),
                Kind::Ntfs => None,
            })
            .collect();
        CallerAccess { svc, client, local: Mutex::new(HashMap::new()), folder_owner }
    }

    /// Evaluates the unknown security IDs reported by a query.
    pub fn resolve(&self, unknown: &[Unknown]) {
        let slots = self.svc.all();
        for u in unknown {
            let Some(slot) = slots.iter().find(|s| s.id == u.slot) else {
                self.local.lock().unwrap().insert((u.slot, u.sec), false);
                continue;
            };
            let store = slot.store();
            let mut path = String::new();
            let found = store.read().unwrap().full_path(u.dir, &mut path);
            let verdict = if found { list_access(&path, self.client.token.raw()) } else { None };
            match verdict {
                Some(v) => self.svc.access.put(self.client, u.slot, u.sec, v),
                None => {
                    self.local.lock().unwrap().insert((u.slot, u.sec), false);
                }
            }
        }
    }
}

impl Access for CallerAccess<'_> {
    fn principal(&self) -> &str {
        &self.client.sid
    }
    fn listable(&self, slot: usize, sec: u32) -> Option<bool> {
        if let Some(owner) = self.folder_owner.get(&slot) {
            return Some(owner.eq_ignore_ascii_case(&self.client.sid));
        }
        if let Some(&v) = self.local.lock().unwrap().get(&(slot, sec)) {
            return Some(v);
        }
        self.svc.access.get(self.client, slot, sec)
    }
}

const FILE_MAPPING: GENERIC_MAPPING =
    GENERIC_MAPPING { GenericRead: FILE_GENERIC_READ, GenericWrite: FILE_GENERIC_WRITE, GenericExecute: FILE_GENERIC_EXECUTE, GenericAll: FILE_ALL_ACCESS };

/// Whether `token` may list directory `path`. `None` when the descriptor
/// cannot be read (the directory vanished).
pub fn list_access(path: &str, token: HANDLE) -> Option<bool> {
    let mut p = path.to_string();
    if p.len() == 2 && p.ends_with(':') {
        p.push('\\');
    }
    let long = if p.starts_with(r"\\") { p.clone() } else { format!(r"\\?\{p}") };
    unsafe {
        let h = CreateFileW(
            wide(&long).as_ptr(),
            READ_CONTROL,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            std::ptr::null(),
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_OPEN_NO_RECALL,
            std::ptr::null_mut(),
        );
        let Some(h) = Handle::valid(h) else {
            let e = last_error();
            if e != ERROR_FILE_NOT_FOUND && e != ERROR_PATH_NOT_FOUND {
                warning!("security descriptor of {path} unreadable: {e}");
            }
            return None;
        };
        let info = OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION;
        let mut needed = 0u32;
        GetKernelObjectSecurity(h.raw(), info, std::ptr::null_mut(), 0, &mut needed);
        if needed == 0 {
            return None;
        }
        let mut sd = vec![0u64; (needed as usize).div_ceil(8)];
        if GetKernelObjectSecurity(h.raw(), info, sd.as_mut_ptr() as *mut c_void, needed, &mut needed) == 0 {
            return None;
        }
        let mut privileges = [0u64; 32];
        let mut plen = std::mem::size_of_val(&privileges) as u32;
        let mut granted = 0u32;
        let mut status: BOOL = 0;
        let ok = AccessCheck(
            sd.as_mut_ptr() as *mut c_void,
            token,
            FILE_LIST_DIRECTORY,
            &FILE_MAPPING,
            privileges.as_mut_ptr() as *mut c_void,
            &mut plen,
            &mut granted,
            &mut status,
        );
        if ok == 0 {
            warning!("AccessCheck on {path} failed: {}", last_error());
            return Some(false);
        }
        Some(status != 0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// This process's own token as a pipe client of the given elevation.
    fn client(elevated: bool) -> ClientToken {
        unsafe {
            let mut token: HANDLE = std::ptr::null_mut();
            assert_ne!(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, &mut token), 0);
            let token = Handle(token);
            let sid = token_user_sid(token.raw()).unwrap();
            ClientToken { token, sid, elevated }
        }
    }

    #[test]
    fn folder_scans_keep_the_standard_token() {
        let tokens = TokenCache::default();
        let elevated = client(true);
        tokens.remember(&elevated);
        assert!(tokens.get(&elevated.sid).is_none(), "an elevated-only user waits for a standard connection");
        let standard = client(false);
        tokens.remember(&standard);
        let kept = tokens.get(&standard.sid).expect("standard token kept");
        tokens.remember(&elevated);
        let after = tokens.get(&standard.sid).unwrap();
        assert!(Arc::ptr_eq(&kept, &after), "an elevated connection must not replace the standard token");
        tokens.remember(&standard);
        assert!(!Arc::ptr_eq(&after, &tokens.get(&standard.sid).unwrap()), "a standard connection refreshes it");
    }

    #[test]
    fn visibility_is_cached_per_elevation() {
        let cache = AccessCache::default();
        let (standard, elevated) = (client(false), client(true));
        cache.put(&elevated, 1, 7, true);
        assert_eq!(cache.get(&standard, 1, 7), None, "the administrator view's answer is not the standard one");
        cache.put(&standard, 1, 7, false);
        assert_eq!(cache.get(&standard, 1, 7), Some(false));
        assert_eq!(cache.get(&elevated, 1, 7), Some(true));
    }
}
