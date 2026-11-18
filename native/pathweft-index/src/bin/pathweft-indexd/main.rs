//! pathweft-indexd: the Pathweft index service.
//!
//! ```text
//! pathweft-indexd service                      run under the service control manager
//! pathweft-indexd console [--pipe NAME] [--volumes C:,D:] [--no-folders]
//!                         [--config PATH] [--exit-after SECONDS]
//! pathweft-indexd install [--allow SID]...     copy, register and start (elevated)
//! pathweft-indexd uninstall [--purge]          stop and remove (elevated)
//! pathweft-indexd selftest --volume X: [--report PATH]   disposable-volume test (elevated)
//! ```

#[cfg(not(windows))]
fn main() {
    eprintln!("pathweft-indexd runs on Windows only");
    std::process::exit(1);
}

#[cfg(windows)]
mod log;
#[cfg(windows)]
mod config;
#[cfg(windows)]
mod install;
#[cfg(windows)]
mod ntfs;
#[cfg(windows)]
mod pipe;
#[cfg(windows)]
mod scan;
#[cfg(windows)]
mod security;
#[cfg(windows)]
mod selftest;
#[cfg(windows)]
mod service;
#[cfg(windows)]
mod state;

#[cfg(windows)]
fn value(args: &[String], flag: &str) -> Option<String> {
    args.iter().position(|a| a == flag).and_then(|i| args.get(i + 1)).cloned()
}

#[cfg(windows)]
fn values(args: &[String], flag: &str) -> Vec<String> {
    args.windows(2).filter(|w| w[0] == flag).map(|w| w[1].clone()).collect()
}

#[cfg(windows)]
fn console(args: &[String]) -> Result<(), String> {
    use std::path::PathBuf;
    log::init(None, true);
    let mut opts = service::service_options();
    if let Some(p) = value(args, "--pipe") {
        opts.pipe = if p.starts_with(r"\\") { p } else { format!(r"\\.\pipe\{p}") };
    }
    if let Some(c) = value(args, "--config") {
        opts.config_path = PathBuf::from(c);
    }
    if let Some(v) = value(args, "--volumes") {
        opts.volumes = Some(v.split(',').filter_map(config::letter).collect());
    }
    if args.iter().any(|a| a == "--no-folders") {
        opts.folders = false;
    }
    opts.persist_config = value(args, "--config").is_some() || opts.volumes.is_none();
    let exit_after = value(args, "--exit-after").and_then(|s| s.parse::<u64>().ok());
    let svc = service::run(opts)?;
    match exit_after {
        Some(s) => std::thread::sleep(std::time::Duration::from_secs(s)),
        None => loop {
            std::thread::sleep(std::time::Duration::from_secs(3600));
        },
    }
    service::shutdown(&svc);
    Ok(())
}

#[cfg(windows)]
fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let cmd = args.first().map(String::as_str).unwrap_or("");
    let rest = &args[args.len().min(1)..];
    let result: Result<i32, String> = match cmd {
        "service" => service::dispatch().map(|_| 0),
        "console" => console(rest).map(|_| 0),
        "install" => install::install(&values(rest, "--allow")).map(|_| 0),
        "uninstall" => install::uninstall(rest.iter().any(|a| a == "--purge")).map(|_| 0),
        "selftest" => {
            log::init(None, true);
            match value(rest, "--volume") {
                Some(v) => selftest::run(&v, value(rest, "--report").map(Into::into)).map(|ok| if ok { 0 } else { 2 }),
                None => Err("selftest needs --volume X:".into()),
            }
        }
        _ => {
            eprintln!("usage: pathweft-indexd service | console [options] | install [--allow SID] | uninstall [--purge] | selftest --volume X: [--report PATH]");
            Ok(1)
        }
    };
    match result {
        Ok(code) => std::process::exit(code),
        Err(e) => {
            eprintln!("error: {e}");
            std::process::exit(1);
        }
    }
}
