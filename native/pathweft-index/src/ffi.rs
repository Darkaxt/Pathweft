//! The Win32 declarations the client, the service and the CLI use. Declared
//! directly to keep the dependency set small and the signatures reviewable.
#![allow(non_snake_case, non_camel_case_types, clippy::upper_case_acronyms, dead_code)]

use std::ffi::c_void;
use std::io;

pub type HANDLE = *mut c_void;
pub type BOOL = i32;
pub const INVALID_HANDLE_VALUE: HANDLE = -1isize as HANDLE;

pub const GENERIC_READ: u32 = 0x8000_0000;
pub const GENERIC_WRITE: u32 = 0x4000_0000;
pub const READ_CONTROL: u32 = 0x0002_0000;
pub const FILE_READ_ATTRIBUTES: u32 = 0x80;
pub const FILE_LIST_DIRECTORY: u32 = 0x1;
pub const FILE_SHARE_READ: u32 = 1;
pub const FILE_SHARE_WRITE: u32 = 2;
pub const FILE_SHARE_DELETE: u32 = 4;
pub const OPEN_EXISTING: u32 = 3;
pub const FILE_FLAG_BACKUP_SEMANTICS: u32 = 0x0200_0000;
pub const FILE_FLAG_OPEN_REPARSE_POINT: u32 = 0x0020_0000;
pub const FILE_FLAG_OPEN_NO_RECALL: u32 = 0x0010_0000;
pub const FILE_FLAG_FIRST_PIPE_INSTANCE: u32 = 0x0008_0000;
pub const SECURITY_SQOS_PRESENT: u32 = 0x0010_0000;
pub const SECURITY_IMPERSONATION: u32 = 2 << 16;

pub const ERROR_FILE_NOT_FOUND: u32 = 2;
pub const ERROR_PATH_NOT_FOUND: u32 = 3;
pub const ERROR_ACCESS_DENIED: u32 = 5;
pub const ERROR_INVALID_HANDLE: u32 = 6;
pub const ERROR_NOT_READY: u32 = 21;
pub const ERROR_HANDLE_EOF: u32 = 38;
pub const ERROR_INVALID_PARAMETER: u32 = 87;
pub const ERROR_BROKEN_PIPE: u32 = 109;
pub const ERROR_INSUFFICIENT_BUFFER: u32 = 122;
pub const ERROR_MORE_DATA: u32 = 234;
pub const ERROR_NO_MORE_FILES: u32 = 18;
pub const ERROR_PIPE_BUSY: u32 = 231;
pub const ERROR_PIPE_CONNECTED: u32 = 535;
pub const ERROR_JOURNAL_DELETE_IN_PROGRESS: u32 = 1178;
pub const ERROR_JOURNAL_NOT_ACTIVE: u32 = 1179;
pub const ERROR_JOURNAL_ENTRY_DELETED: u32 = 1181;
pub const ERROR_SERVICE_ALREADY_RUNNING: u32 = 1056;
pub const ERROR_SERVICE_DOES_NOT_EXIST: u32 = 1060;
pub const ERROR_SERVICE_NOT_ACTIVE: u32 = 1062;
pub const ERROR_SERVICE_EXISTS: u32 = 1073;
pub const ERROR_BAD_IMPERSONATION_LEVEL: u32 = 1346;
pub const ERROR_NO_TOKEN: u32 = 1008;

pub const FSCTL_GET_NTFS_VOLUME_DATA: u32 = 0x0009_0064;
pub const FSCTL_GET_NTFS_FILE_RECORD: u32 = 0x0009_0068;
pub const FSCTL_QUERY_USN_JOURNAL: u32 = 0x0009_00F4;
pub const FSCTL_READ_USN_JOURNAL: u32 = 0x0009_00BB;
pub const FSCTL_CREATE_USN_JOURNAL: u32 = 0x0009_00E7;

pub const DRIVE_REMOVABLE: u32 = 2;
pub const DRIVE_FIXED: u32 = 3;

pub const PIPE_ACCESS_DUPLEX: u32 = 3;
pub const PIPE_TYPE_BYTE: u32 = 0;
pub const PIPE_READMODE_BYTE: u32 = 0;
pub const PIPE_WAIT: u32 = 0;
pub const PIPE_REJECT_REMOTE_CLIENTS: u32 = 8;
pub const PIPE_UNLIMITED_INSTANCES: u32 = 255;

pub const TOKEN_QUERY: u32 = 0x0008;
pub const TOKEN_DUPLICATE: u32 = 0x0002;
pub const TOKEN_IMPERSONATE: u32 = 0x0004;
pub const TOKEN_ADJUST_PRIVILEGES: u32 = 0x0020;
pub const TOKEN_ALL_ACCESS: u32 = 0x000F_01FF;
pub const SE_PRIVILEGE_ENABLED: u32 = 2;
pub const TOKEN_USER_CLASS: u32 = 1;
pub const TOKEN_IMPERSONATION_LEVEL_CLASS: u32 = 9;
pub const SECURITY_IMPERSONATION_LEVEL_IMPERSONATION: i32 = 2;
pub const TOKEN_TYPE_IMPERSONATION: i32 = 2;

pub const OWNER_SECURITY_INFORMATION: u32 = 1;
pub const GROUP_SECURITY_INFORMATION: u32 = 2;
pub const DACL_SECURITY_INFORMATION: u32 = 4;
pub const SDDL_REVISION_1: u32 = 1;

pub const FILE_GENERIC_READ: u32 = 0x0012_0089;
pub const FILE_GENERIC_WRITE: u32 = 0x0012_0116;
pub const FILE_GENERIC_EXECUTE: u32 = 0x0012_00A0;
pub const FILE_ALL_ACCESS: u32 = 0x001F_01FF;

pub const THREAD_MODE_BACKGROUND_BEGIN: i32 = 0x0001_0000;
pub const THREAD_MODE_BACKGROUND_END: i32 = 0x0002_0000;

pub const FIND_EX_INFO_BASIC: i32 = 1;
pub const FIND_EX_SEARCH_NAME_MATCH: i32 = 0;
pub const FIND_FIRST_EX_LARGE_FETCH: u32 = 2;

pub const SC_MANAGER_ALL_ACCESS: u32 = 0x000F_003F;
pub const SC_MANAGER_CONNECT: u32 = 1;
pub const SERVICE_ALL_ACCESS: u32 = 0x000F_01FF;
pub const SERVICE_QUERY_STATUS: u32 = 4;
pub const SERVICE_WIN32_OWN_PROCESS: u32 = 0x10;
pub const SERVICE_AUTO_START: u32 = 2;
pub const SERVICE_ERROR_NORMAL: u32 = 1;
pub const SERVICE_CONTROL_STOP: u32 = 1;
pub const SERVICE_CONTROL_INTERROGATE: u32 = 4;
pub const SERVICE_CONTROL_SHUTDOWN: u32 = 5;
pub const SERVICE_ACCEPT_STOP: u32 = 1;
pub const SERVICE_ACCEPT_SHUTDOWN: u32 = 4;
pub const SERVICE_STOPPED: u32 = 1;
pub const SERVICE_START_PENDING: u32 = 2;
pub const SERVICE_STOP_PENDING: u32 = 3;
pub const SERVICE_RUNNING: u32 = 4;
pub const SERVICE_CONFIG_DESCRIPTION: u32 = 1;
pub const SERVICE_CONFIG_FAILURE_ACTIONS: u32 = 2;
pub const SC_ACTION_RESTART: u32 = 1;

#[repr(C)]
pub struct SECURITY_ATTRIBUTES {
    pub nLength: u32,
    pub lpSecurityDescriptor: *mut c_void,
    pub bInheritHandle: BOOL,
}

#[repr(C)]
pub struct OVERLAPPED {
    pub Internal: usize,
    pub InternalHigh: usize,
    pub Offset: u32,
    pub OffsetHigh: u32,
    pub hEvent: HANDLE,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct SYSTEMTIME {
    pub year: u16,
    pub month: u16,
    pub day_of_week: u16,
    pub day: u16,
    pub hour: u16,
    pub minute: u16,
    pub second: u16,
    pub milliseconds: u16,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct FILETIME {
    pub low: u32,
    pub high: u32,
}

impl FILETIME {
    pub fn value(&self) -> u64 {
        ((self.high as u64) << 32) | self.low as u64
    }
}

#[repr(C)]
pub struct WIN32_FIND_DATAW {
    pub dwFileAttributes: u32,
    pub ftCreationTime: FILETIME,
    pub ftLastAccessTime: FILETIME,
    pub ftLastWriteTime: FILETIME,
    pub nFileSizeHigh: u32,
    pub nFileSizeLow: u32,
    pub dwReserved0: u32,
    pub dwReserved1: u32,
    pub cFileName: [u16; 260],
    pub cAlternateFileName: [u16; 14],
}

#[repr(C)]
#[derive(Default)]
pub struct PROCESS_MEMORY_COUNTERS_EX {
    pub cb: u32,
    pub PageFaultCount: u32,
    pub PeakWorkingSetSize: usize,
    pub WorkingSetSize: usize,
    pub QuotaPeakPagedPoolUsage: usize,
    pub QuotaPagedPoolUsage: usize,
    pub QuotaPeakNonPagedPoolUsage: usize,
    pub QuotaNonPagedPoolUsage: usize,
    pub PagefileUsage: usize,
    pub PeakPagefileUsage: usize,
    pub PrivateUsage: usize,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LUID {
    pub LowPart: u32,
    pub HighPart: i32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct LUID_AND_ATTRIBUTES {
    pub Luid: LUID,
    pub Attributes: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct TOKEN_PRIVILEGES {
    pub PrivilegeCount: u32,
    pub Privileges: [LUID_AND_ATTRIBUTES; 1],
}

#[repr(C)]
pub struct GENERIC_MAPPING {
    pub GenericRead: u32,
    pub GenericWrite: u32,
    pub GenericExecute: u32,
    pub GenericAll: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct SERVICE_STATUS {
    pub dwServiceType: u32,
    pub dwCurrentState: u32,
    pub dwControlsAccepted: u32,
    pub dwWin32ExitCode: u32,
    pub dwServiceSpecificExitCode: u32,
    pub dwCheckPoint: u32,
    pub dwWaitHint: u32,
}

#[repr(C)]
#[derive(Default)]
pub struct SERVICE_STATUS_PROCESS {
    pub status: SERVICE_STATUS,
    pub dwProcessId: u32,
    pub dwServiceFlags: u32,
}

pub const SC_STATUS_PROCESS_INFO: u32 = 0;
pub const SYNCHRONIZE: u32 = 0x0010_0000;

pub type ServiceMain = unsafe extern "system" fn(argc: u32, argv: *mut *mut u16);
pub type HandlerEx = unsafe extern "system" fn(control: u32, event: u32, data: *mut c_void, context: *mut c_void) -> u32;

#[repr(C)]
pub struct SERVICE_TABLE_ENTRYW {
    pub lpServiceName: *mut u16,
    pub lpServiceProc: Option<ServiceMain>,
}

#[repr(C)]
pub struct SERVICE_DESCRIPTIONW {
    pub lpDescription: *mut u16,
}

#[repr(C)]
pub struct SC_ACTION {
    pub Type: u32,
    pub Delay: u32,
}

#[repr(C)]
pub struct SERVICE_FAILURE_ACTIONSW {
    pub dwResetPeriod: u32,
    pub lpRebootMsg: *mut u16,
    pub lpCommand: *mut u16,
    pub cActions: u32,
    pub lpsaActions: *mut SC_ACTION,
}

#[link(name = "kernel32")]
extern "system" {
    pub fn CreateFileW(name: *const u16, access: u32, share: u32, sa: *const SECURITY_ATTRIBUTES, disposition: u32, flags: u32, template: HANDLE) -> HANDLE;
    pub fn CloseHandle(h: HANDLE) -> BOOL;
    pub fn ReadFile(h: HANDLE, buf: *mut u8, len: u32, read: *mut u32, ov: *mut OVERLAPPED) -> BOOL;
    pub fn WriteFile(h: HANDLE, buf: *const u8, len: u32, written: *mut u32, ov: *mut OVERLAPPED) -> BOOL;
    pub fn FlushFileBuffers(h: HANDLE) -> BOOL;
    pub fn DeviceIoControl(h: HANDLE, code: u32, input: *const c_void, input_len: u32, output: *mut c_void, output_len: u32, returned: *mut u32, ov: *mut OVERLAPPED) -> BOOL;
    pub fn GetLastError() -> u32;
    pub fn GetLogicalDrives() -> u32;
    pub fn GetDriveTypeW(root: *const u16) -> u32;
    pub fn GetVolumeInformationW(root: *const u16, name: *mut u16, name_len: u32, serial: *mut u32, max_component: *mut u32, flags: *mut u32, fs: *mut u16, fs_len: u32) -> BOOL;
    pub fn FindFirstFileExW(name: *const u16, level: i32, data: *mut WIN32_FIND_DATAW, op: i32, filter: *mut c_void, flags: u32) -> HANDLE;
    pub fn FindNextFileW(h: HANDLE, data: *mut WIN32_FIND_DATAW) -> BOOL;
    pub fn FindClose(h: HANDLE) -> BOOL;
    pub fn GetCurrentProcess() -> HANDLE;
    pub fn GetCurrentThread() -> HANDLE;
    pub fn GetCurrentProcessId() -> u32;
    pub fn SetThreadPriority(h: HANDLE, priority: i32) -> BOOL;
    pub fn K32GetProcessMemoryInfo(process: HANDLE, counters: *mut PROCESS_MEMORY_COUNTERS_EX, cb: u32) -> BOOL;
    pub fn CreateNamedPipeW(name: *const u16, open_mode: u32, pipe_mode: u32, max_instances: u32, out_size: u32, in_size: u32, timeout: u32, sa: *const SECURITY_ATTRIBUTES) -> HANDLE;
    pub fn ConnectNamedPipe(h: HANDLE, ov: *mut OVERLAPPED) -> BOOL;
    pub fn DisconnectNamedPipe(h: HANDLE) -> BOOL;
    pub fn WaitNamedPipeW(name: *const u16, timeout: u32) -> BOOL;
    pub fn LocalFree(mem: *mut c_void) -> *mut c_void;
    pub fn CreateDirectoryW(path: *const u16, sa: *const SECURITY_ATTRIBUTES) -> BOOL;
    pub fn ReadDirectoryChangesW(h: HANDLE, buf: *mut c_void, len: u32, subtree: BOOL, filter: u32, returned: *mut u32, ov: *mut OVERLAPPED, completion: *const c_void) -> BOOL;
    pub fn CreateEventW(sa: *const SECURITY_ATTRIBUTES, manual_reset: BOOL, initial: BOOL, name: *const u16) -> HANDLE;
    pub fn ResetEvent(h: HANDLE) -> BOOL;
    pub fn WaitForSingleObject(h: HANDLE, ms: u32) -> u32;
    pub fn OpenProcess(access: u32, inherit: BOOL, pid: u32) -> HANDLE;
    pub fn GetLocalTime(st: *mut SYSTEMTIME);
    pub fn TzSpecificLocalTimeToSystemTime(tz: *const c_void, local: *const SYSTEMTIME, utc: *mut SYSTEMTIME) -> BOOL;
    pub fn SystemTimeToFileTime(st: *const SYSTEMTIME, ft: *mut FILETIME) -> BOOL;
    pub fn GetOverlappedResult(h: HANDLE, ov: *mut OVERLAPPED, transferred: *mut u32, wait: BOOL) -> BOOL;
    pub fn CancelIoEx(h: HANDLE, ov: *mut OVERLAPPED) -> BOOL;
}

pub const FILE_FLAG_OVERLAPPED: u32 = 0x4000_0000;
pub const FILE_NOTIFY_CHANGE_FILE_NAME: u32 = 0x1;
pub const FILE_NOTIFY_CHANGE_DIR_NAME: u32 = 0x2;
pub const FILE_NOTIFY_CHANGE_ATTRIBUTES: u32 = 0x4;
pub const FILE_NOTIFY_CHANGE_SIZE: u32 = 0x8;
pub const FILE_NOTIFY_CHANGE_LAST_WRITE: u32 = 0x10;
pub const FILE_NOTIFY_CHANGE_CREATION: u32 = 0x40;
pub const WAIT_OBJECT_0: u32 = 0;
pub const ERROR_OPERATION_ABORTED: u32 = 995;
pub const ERROR_NOTIFY_ENUM_DIR: u32 = 1022;

#[link(name = "advapi32")]
extern "system" {
    pub fn OpenProcessToken(process: HANDLE, access: u32, token: *mut HANDLE) -> BOOL;
    pub fn OpenThreadToken(thread: HANDLE, access: u32, open_as_self: BOOL, token: *mut HANDLE) -> BOOL;
    pub fn LookupPrivilegeValueW(system: *const u16, name: *const u16, luid: *mut LUID) -> BOOL;
    pub fn AdjustTokenPrivileges(token: HANDLE, disable_all: BOOL, new: *const TOKEN_PRIVILEGES, len: u32, previous: *mut TOKEN_PRIVILEGES, returned: *mut u32) -> BOOL;
    pub fn GetTokenInformation(token: HANDLE, class: u32, info: *mut c_void, len: u32, returned: *mut u32) -> BOOL;
    pub fn ConvertSidToStringSidW(sid: *mut c_void, out: *mut *mut u16) -> BOOL;
    pub fn ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl: *const u16, revision: u32, sd: *mut *mut c_void, size: *mut u32) -> BOOL;
    pub fn ImpersonateNamedPipeClient(pipe: HANDLE) -> BOOL;
    pub fn ImpersonateLoggedOnUser(token: HANDLE) -> BOOL;
    pub fn RevertToSelf() -> BOOL;
    pub fn DuplicateTokenEx(token: HANDLE, access: u32, sa: *const SECURITY_ATTRIBUTES, level: i32, token_type: i32, out: *mut HANDLE) -> BOOL;
    pub fn GetKernelObjectSecurity(h: HANDLE, info: u32, sd: *mut c_void, len: u32, needed: *mut u32) -> BOOL;
    pub fn AccessCheck(sd: *mut c_void, token: HANDLE, desired: u32, mapping: *const GENERIC_MAPPING, privileges: *mut c_void, privileges_len: *mut u32, granted: *mut u32, status: *mut BOOL) -> BOOL;
    pub fn OpenSCManagerW(machine: *const u16, database: *const u16, access: u32) -> HANDLE;
    pub fn CreateServiceW(scm: HANDLE, name: *const u16, display: *const u16, access: u32, service_type: u32, start: u32, error: u32, binary: *const u16, group: *const u16, tag: *mut u32, dependencies: *const u16, account: *const u16, password: *const u16) -> HANDLE;
    pub fn OpenServiceW(scm: HANDLE, name: *const u16, access: u32) -> HANDLE;
    pub fn StartServiceW(service: HANDLE, argc: u32, argv: *const *const u16) -> BOOL;
    pub fn ControlService(service: HANDLE, control: u32, status: *mut SERVICE_STATUS) -> BOOL;
    pub fn QueryServiceStatus(service: HANDLE, status: *mut SERVICE_STATUS) -> BOOL;
    pub fn QueryServiceStatusEx(service: HANDLE, level: u32, buffer: *mut u8, size: u32, needed: *mut u32) -> BOOL;
    pub fn DeleteService(service: HANDLE) -> BOOL;
    pub fn CloseServiceHandle(h: HANDLE) -> BOOL;
    pub fn ChangeServiceConfig2W(service: HANDLE, level: u32, info: *mut c_void) -> BOOL;
    pub fn StartServiceCtrlDispatcherW(table: *const SERVICE_TABLE_ENTRYW) -> BOOL;
    pub fn RegisterServiceCtrlHandlerExW(name: *const u16, handler: HandlerEx, context: *mut c_void) -> HANDLE;
    pub fn SetServiceStatus(handle: HANDLE, status: *const SERVICE_STATUS) -> BOOL;
}

pub fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

pub fn from_wide(buf: &[u16]) -> String {
    let end = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..end])
}

pub fn last_error() -> u32 {
    unsafe { GetLastError() }
}

pub fn os_error(code: u32) -> io::Error {
    io::Error::from_raw_os_error(code as i32)
}

/// An owned kernel handle, closed on drop.
pub struct Handle(pub HANDLE);

unsafe impl Send for Handle {}
unsafe impl Sync for Handle {}

impl Handle {
    pub fn valid(h: HANDLE) -> Option<Handle> {
        if h.is_null() || h == INVALID_HANDLE_VALUE { None } else { Some(Handle(h)) }
    }
    pub fn raw(&self) -> HANDLE {
        self.0
    }
}

impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}

/// An owned service-manager handle.
pub struct ScHandle(pub HANDLE);

impl Drop for ScHandle {
    fn drop(&mut self) {
        unsafe {
            CloseServiceHandle(self.0);
        }
    }
}

/// Byte-stream access to a pipe handle.
pub struct PipeStream(pub Handle);

impl io::Read for PipeStream {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let mut n = 0u32;
        let len = buf.len().min(1 << 20) as u32;
        let ok = unsafe { ReadFile(self.0.raw(), buf.as_mut_ptr(), len, &mut n, std::ptr::null_mut()) };
        if ok == 0 {
            let e = last_error();
            if e == ERROR_BROKEN_PIPE || e == ERROR_HANDLE_EOF {
                return Ok(0);
            }
            if e != ERROR_MORE_DATA {
                return Err(os_error(e));
            }
        }
        Ok(n as usize)
    }
}

impl io::Write for PipeStream {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        let mut n = 0u32;
        let len = buf.len().min(1 << 20) as u32;
        let ok = unsafe { WriteFile(self.0.raw(), buf.as_ptr(), len, &mut n, std::ptr::null_mut()) };
        if ok == 0 {
            return Err(os_error(last_error()));
        }
        Ok(n as usize)
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

/// Converts a SID pointer to its string form.
pub fn sid_string(sid: *mut c_void) -> Option<String> {
    let mut out: *mut u16 = std::ptr::null_mut();
    unsafe {
        if ConvertSidToStringSidW(sid, &mut out) == 0 || out.is_null() {
            return None;
        }
        let mut len = 0;
        while *out.add(len) != 0 {
            len += 1;
        }
        let s = String::from_utf16_lossy(std::slice::from_raw_parts(out, len));
        LocalFree(out as *mut c_void);
        Some(s)
    }
}

/// The user SID of a token.
pub fn token_user_sid(token: HANDLE) -> Option<String> {
    let mut needed = 0u32;
    unsafe {
        GetTokenInformation(token, TOKEN_USER_CLASS, std::ptr::null_mut(), 0, &mut needed);
        if needed == 0 {
            return None;
        }
        // u64 storage keeps the pointer inside TOKEN_USER aligned.
        let mut buf = vec![0u64; (needed as usize).div_ceil(8)];
        if GetTokenInformation(token, TOKEN_USER_CLASS, buf.as_mut_ptr() as *mut c_void, needed, &mut needed) == 0 {
            return None;
        }
        // TOKEN_USER { SID_AND_ATTRIBUTES { Sid: PSID, Attributes: u32 } }
        let sid = *(buf.as_ptr() as *const *mut c_void);
        sid_string(sid)
    }
}

/// The current process token's user SID.
pub fn current_user_sid() -> Option<String> {
    let mut token: HANDLE = std::ptr::null_mut();
    unsafe {
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return None;
        }
    }
    let token = Handle(token);
    token_user_sid(token.raw())
}
