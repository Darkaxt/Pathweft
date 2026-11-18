//! Pathweft's file-name index: the in-memory store, the query engine, the NTFS
//! record parser and the pipe protocol (platform independent), plus the Windows
//! pipe client and its C ABI. The service and the CLI live in `src/bin`.

pub mod mft;
pub mod protocol;
pub mod query;
pub mod store;

#[cfg(windows)]
pub mod ffi;
#[cfg(windows)]
pub mod client;
#[cfg(windows)]
pub mod capi;
