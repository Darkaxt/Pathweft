//! Pipe client for the index service.

use crate::ffi::*;
use crate::protocol::{read_frame, write_frame, Request, Response, PIPE_NAME};
use std::io;
use std::time::{Duration, Instant};

pub struct Client {
    stream: PipeStream,
}

impl Client {
    /// Connects to the default pipe.
    pub fn connect(timeout: Duration) -> io::Result<Client> {
        Client::connect_to(PIPE_NAME, timeout)
    }

    /// Connects to `pipe` (a full `\\.\pipe\...` name), waiting up to
    /// `timeout` for a free instance. The service may impersonate the caller
    /// to evaluate access, so impersonation is allowed.
    pub fn connect_to(pipe: &str, timeout: Duration) -> io::Result<Client> {
        let name = wide(pipe);
        let deadline = Instant::now() + timeout;
        loop {
            let h = unsafe {
                CreateFileW(
                    name.as_ptr(),
                    GENERIC_READ | GENERIC_WRITE,
                    0,
                    std::ptr::null(),
                    OPEN_EXISTING,
                    SECURITY_SQOS_PRESENT | SECURITY_IMPERSONATION,
                    std::ptr::null_mut(),
                )
            };
            if let Some(h) = Handle::valid(h) {
                return Ok(Client { stream: PipeStream(h) });
            }
            let e = last_error();
            let left = deadline.saturating_duration_since(Instant::now());
            if e == ERROR_PIPE_BUSY && !left.is_zero() {
                unsafe {
                    WaitNamedPipeW(name.as_ptr(), left.as_millis().clamp(1, u32::MAX as u128) as u32);
                }
                continue;
            }
            if e == ERROR_FILE_NOT_FOUND {
                return Err(io::Error::new(io::ErrorKind::NotFound, "The Pathweft index service is not running (its pipe does not exist)."));
            }
            return Err(os_error(e));
        }
    }

    pub fn request_raw(&mut self, json: &[u8]) -> io::Result<Vec<u8>> {
        write_frame(&mut self.stream, json)?;
        read_frame(&mut self.stream)?.ok_or_else(|| io::Error::new(io::ErrorKind::UnexpectedEof, "The index service closed the connection."))
    }

    pub fn request(&mut self, request: &Request) -> io::Result<Response> {
        let json = serde_json::to_vec(request).map_err(io::Error::other)?;
        let reply = self.request_raw(&json)?;
        serde_json::from_slice(&reply).map_err(io::Error::other)
    }
}
