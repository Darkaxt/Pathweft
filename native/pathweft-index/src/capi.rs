//! C ABI for callers that prefer a DLL over speaking the pipe protocol.
//!
//! ```c
//! // Sends one JSON request (UTF-8, NUL-terminated) and returns 0 with the JSON
//! // response in *response (free it with pwi_free), or a nonzero Win32 error
//! // code with a JSON {"ok":false,"error":...} in *response.
//! int32_t pwi_request(const char *pipe_or_null, const char *request_json,
//!                     uint32_t connect_timeout_ms, char **response);
//! void pwi_free(char *response);
//! ```

use crate::client::Client;
use crate::protocol::PIPE_NAME;
use std::ffi::{c_char, CStr, CString};
use std::time::Duration;

fn error_json(message: &str) -> CString {
    let body = serde_json::json!({ "ok": false, "error": message }).to_string();
    CString::new(body).unwrap_or_default()
}

/// # Safety
/// `request_json` must be a valid NUL-terminated string, `pipe` null or a
/// valid NUL-terminated string, and `response` a valid pointer.
#[no_mangle]
pub unsafe extern "C" fn pwi_request(pipe: *const c_char, request_json: *const c_char, connect_timeout_ms: u32, response: *mut *mut c_char) -> i32 {
    if response.is_null() {
        return 87;
    }
    *response = std::ptr::null_mut();
    if request_json.is_null() {
        *response = error_json("request_json is null").into_raw();
        return 87;
    }
    let pipe = if pipe.is_null() { PIPE_NAME.to_string() } else { CStr::from_ptr(pipe).to_string_lossy().into_owned() };
    let request = CStr::from_ptr(request_json).to_bytes();
    let result = Client::connect_to(&pipe, Duration::from_millis(connect_timeout_ms as u64)).and_then(|mut c| c.request_raw(request));
    match result {
        Ok(bytes) => match CString::new(bytes) {
            Ok(s) => {
                *response = s.into_raw();
                0
            }
            Err(_) => {
                *response = error_json("response contained a NUL byte").into_raw();
                13
            }
        },
        Err(e) => {
            *response = error_json(&e.to_string()).into_raw();
            e.raw_os_error().unwrap_or(1)
        }
    }
}

/// # Safety
/// `response` must come from `pwi_request` and be freed once.
#[no_mangle]
pub unsafe extern "C" fn pwi_free(response: *mut c_char) {
    if !response.is_null() {
        drop(CString::from_raw(response));
    }
}
