//! In-process RAR rewrite boundary. The host owns queueing, source snapshots,
//! independent content verification and final publication. No process dispatch.
// Metadata-copy sequence adapted from bitplane/rars crates/rars-python/src/lib.rs
// at e2415a2 (Apache-2.0); see vendor/rars/NOTICES.txt and LICENSE.txt.
use rars::{
    Archive, ArchiveFamily, ArchiveMember, ArchiveMemberDetail, ArchiveReadOptions, ArchiveReader,
    ArchiveVersion, AttrSource, Builder, EntrySource, RewriteStaging, WriteProgress,
    WriteProgressEvent, WriterResources,
};
use serde::Deserialize;
use serde_json::{json, Value};
use std::{
    collections::HashMap,
    ffi::{c_char, CStr, CString},
    path::Path,
    sync::Arc,
};

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Request {
    operation: String,
    input: Option<String>,
    output: Option<String>,
    work: Option<String>,
    password: Option<String>,
    #[serde(default)]
    changes: HashMap<String, Option<String>>,
    #[serde(default)]
    additions: HashMap<String, Option<String>>,
}
type Control = Option<extern "C" fn() -> i32>;
struct Progress(Control);
impl WriteProgress for Progress {
    fn report(&self, _: WriteProgressEvent<'_>) {}
    fn is_cancelled(&self) -> bool {
        self.0.is_some_and(|callback| callback() != 0)
    }
}
fn normal(name: &[u8]) -> rars::Result<String> {
    let text = std::str::from_utf8(name)
        .map_err(|_| rars::Error::InvalidArgument("non-UTF8 legacy names are read-only"))?;
    Ok(text.replace('\\', "/").trim_end_matches('/').to_string())
}
fn options(password: Option<&[u8]>) -> ArchiveReadOptions<'_> {
    ArchiveReadOptions::with_optional_password(password)
        .with_max_header_count(1_000_000)
        .with_max_header_bytes(256 * 1024 * 1024)
        .with_rar50_dictionary_size_limit(256 * 1024 * 1024)
}
fn issues(archive: &Archive) -> Vec<String> {
    let mut result = archive.rewrite_preservation_issues();
    for member in archive.members() {
        if normal(&member.meta.name).is_err() {
            result.push("non-UTF8 legacy names".into());
        }
        if member.meta.family != ArchiveFamily::Rar50Plus
            && !member.meta.name.is_ascii()
            && !member.name_is_unicode()
        {
            result.push("legacy filename encoding is not qualified".into());
        }
        if member.meta.is_redirection || member.is_legacy_unix_symlink() {
            result.push("archive links/redirections are read-only in Pathweft".into());
        }
    }
    if let Archive::Rar50Plus(a) = archive {
        if a.blocks
            .iter()
            .any(|block| matches!(block, rars::rar50::Block::Service(s) if s.name == b"RR"))
        {
            result.push("recovery-record preservation is not qualified".into());
        }
    }
    if let Archive::Rar15To40(a) = archive {
        if a.files().any(|file| file.has_file_comment()) {
            result.push(
                "legacy embedded member comments are not qualified with the reference decoder"
                    .into(),
            );
        }
    }
    result
}
fn required<'a>(value: &'a Option<String>, error: &'static str) -> rars::Result<&'a str> {
    value.as_deref().ok_or(rars::Error::InvalidArgument(error))
}
// Adapted from rars/src/filename.rs at the pinned revision (Apache-2.0).
// Its encoder is crate-private; importing this small format routine avoids
// modifying the dependency or reproducing an entire archive implementation.
fn legacy_unicode(name: &[u8]) -> rars::Result<Vec<u8>> {
    rars::builder::validate_entry_name(name.to_vec())?;
    let text = std::str::from_utf8(name)
        .map_err(|_| rars::Error::InvalidArgument("legacy Unicode name requires UTF-8"))?;
    let mut raw: Vec<u8> = text
        .chars()
        .map(|ch| if ch.is_ascii() { ch as u8 } else { b'_' })
        .collect();
    raw.extend_from_slice(&[0, 0]);
    let units: Vec<u16> = text.encode_utf16().collect();
    for group in units.chunks(4) {
        raw.push(0xaa);
        for unit in group {
            raw.extend_from_slice(&unit.to_le_bytes());
        }
    }
    if raw.len() > usize::from(u16::MAX) - 32 {
        return Err(rars::Error::InvalidArgument("legacy Unicode name too long"));
    }
    Ok(raw)
}
fn copy_member(
    builder: &mut Builder,
    member: &ArchiveMember,
    name: Vec<u8>,
    source: Option<EntrySource>,
    comment: Option<Vec<u8>>,
    comment_encrypted: bool,
    password: Option<&[u8]>,
) -> rars::Result<()> {
    let meta = &member.meta;
    let mode = (meta.attr_source() == AttrSource::Unix).then_some(meta.file_attr as u32);
    if meta.is_directory {
        builder.add_directory(meta.name.clone(), meta.file_time, mode)?;
    } else {
        builder.add_source(
            meta.name.clone(),
            source.ok_or(rars::Error::InvalidArgument("missing staged source"))?,
            meta.file_time,
            mode,
        )?;
    }
    let id = builder
        .member_ids()
        .last()
        .ok_or(rars::Error::InvalidArgument("missing builder member"))?;
    builder.set_file_comment_by_id(id, comment)?;
    builder.set_entry_encryption_by_id(
        id,
        if meta.is_encrypted {
            password.map(<[u8]>::to_vec)
        } else {
            None
        },
        if comment_encrypted {
            password.map(<[u8]>::to_vec)
        } else {
            None
        },
    )?;
    match &member.detail {
        ArchiveMemberDetail::Rar15To40 {
            unicode_name,
            extended_times,
            ..
        } => {
            if let Some(raw) = unicode_name {
                builder.set_legacy_unicode_name_by_id(id, raw.clone())?;
            }
            if !extended_times.is_empty() {
                builder.set_legacy_extended_times_by_id(id, Some(extended_times.clone()))?;
            }
            if name != meta.name {
                builder.rename_by_id(id, name)?;
            }
            // Legacy readers interpret raw UTF-8 as a code page unless the RAR
            // Unicode-name record is present, including an ASCII -> Unicode rename.
            let output_name = builder.names().last().unwrap().to_vec();
            if !output_name.is_ascii() {
                builder.set_legacy_unicode_name_by_id(id, legacy_unicode(&output_name)?)?;
            }
        }
        _ => {
            builder.set_file_times_by_id(id, member.file_times()?)?;
            if name != meta.name {
                builder.rename_by_id(id, name)?;
            }
        }
    }
    if meta.attr_source() == AttrSource::Dos {
        builder.set_dos_attributes_by_id(id, meta.file_attr)?;
    }
    Ok(())
}
fn add_inputs(
    builder: &mut Builder,
    additions: &HashMap<String, Option<String>>,
) -> rars::Result<()> {
    let mut entries: Vec<_> = additions.iter().collect();
    entries.sort_by_key(|(name, _)| *name);
    for (name, path) in entries {
        if let Some(path) = path {
            let info = std::fs::symlink_metadata(path)?;
            if !info.is_file() || info.file_type().is_symlink() {
                return Err(rars::Error::InvalidArgument("input must be a regular file"));
            }
            let modified = info
                .modified()?
                .duration_since(std::time::UNIX_EPOCH)
                .map_err(|_| rars::Error::InvalidArgument("input timestamp predates Unix epoch"))?;
            let seconds = u32::try_from(modified.as_secs())
                .map_err(|_| rars::Error::InvalidArgument("input timestamp exceeds RAR range"))?;
            builder.add_source(
                name.as_bytes().to_vec(),
                EntrySource::from_path(path),
                Some(seconds),
                None,
            )?;
            if builder.format().family() == ArchiveFamily::Rar50Plus {
                builder.set_mtime_nanoseconds(name.as_bytes(), modified.subsec_nanos())?;
            }
        } else {
            builder.add_directory(name.as_bytes().to_vec(), None, None)?;
        }
        if builder.format().family() != ArchiveFamily::Rar50Plus && !name.is_ascii() {
            builder.set_legacy_unicode_name(name.as_bytes(), legacy_unicode(name.as_bytes())?)?;
        }
    }
    Ok(())
}
fn metadata(
    member: &ArchiveMember,
    comment: Option<Vec<u8>>,
    encrypted_comment: bool,
) -> rars::Result<Value> {
    Ok(
        json!({"directory":member.meta.is_directory,"attr":member.meta.file_attr,
        "host":member.meta.host_os,"time":member.meta.file_time,
        "times":format!("{:?}", member.file_times()?),
        "encrypted":member.meta.is_encrypted,"comment":comment,"commentEncrypted":encrypted_comment}),
    )
}
fn verify_metadata(
    before: &Archive,
    after: &Archive,
    request: &Request,
    password: Option<&[u8]>,
) -> rars::Result<()> {
    fn settings(archive: &Archive) -> Value {
        match archive {
            Archive::Rar13(a) => json!(["legacy13", a.main.is_solid()]),
            Archive::Rar15To40(a) => {
                json!(["legacy", a.main.is_solid(), a.main.has_encrypted_headers()])
            }
            Archive::Rar50Plus(a) => json!([
                "modern",
                a.main.is_solid(),
                a.main.encrypted_headers,
                a.main.is_locked()
            ]),
            _ => json!("unknown"),
        }
    }
    if settings(before) != settings(after) {
        return Err(rars::Error::InvalidArgument(
            "staged archive solid/encryption settings changed",
        ));
    }
    if before.comment_with_options(options(password))?
        != after.comment_with_options(options(password))?
    {
        return Err(rars::Error::InvalidArgument(
            "staged archive comment changed",
        ));
    }
    let old_comments = before.member_comments(password)?;
    let new_comments = after.member_comments(password)?;
    let old_encryption = before.member_comment_encryption();
    let new_encryption = after.member_comment_encryption();
    let actual: HashMap<_, _> = after
        .members()
        .enumerate()
        .map(|(index, member)| {
            Ok((
                normal(&member.meta.name)?,
                metadata(&member, new_comments[index].clone(), new_encryption[index])?,
            ))
        })
        .collect::<rars::Result<_>>()?;
    for (index, member) in before.members().enumerate() {
        let name = normal(&member.meta.name)?;
        let target = match request.changes.get(&name) {
            Some(None) => continue,
            Some(Some(name)) => name.clone(),
            None => name,
        };
        if actual.get(&target)
            != Some(&metadata(
                &member,
                old_comments[index].clone(),
                old_encryption[index],
            )?)
        {
            return Err(rars::Error::InvalidArgument(
                "staged member metadata changed",
            ));
        }
    }
    Ok(())
}
fn run(request: &Request, control: Control) -> rars::Result<Value> {
    if request.operation == "version" {
        return Ok(
            json!({"abi":1,"rars":"0.12.3","source":"e2415a2eb12e6b62deaa0cca919864eabe4671b1"}),
        );
    }
    let password = request.password.as_deref().map(str::as_bytes);
    let archive = request
        .input
        .as_ref()
        .map(|path| ArchiveReader::read_path_with_options(path, options(password)))
        .transpose()?;
    let limitations = archive.as_ref().map(issues).unwrap_or_default();
    if request.operation == "probe" {
        return Ok(json!({"issues":limitations}));
    }
    if request.operation != "write" {
        return Err(rars::Error::InvalidArgument("unknown native operation"));
    }
    if !limitations.is_empty() {
        return Err(rars::Error::InvalidArgument(
            "archive preservation preflight failed",
        ));
    }
    let output = Path::new(required(&request.output, "output path required")?);
    let work = Path::new(required(
        &request.work,
        "private scratch directory required",
    )?);
    let progress = Arc::new(Progress(control));
    if progress.is_cancelled() {
        return Err(rars::Error::Cancelled);
    }
    let mut builder = if let Some(source) = &archive {
        let mut builder = source
            .preserving_builder(password)?
            .comment(source.comment_with_options(options(password))?);
        let members: Vec<_> = source.members().collect();
        let selected = members
            .iter()
            .enumerate()
            .filter_map(|(i, m)| {
                let name = normal(&m.meta.name).ok()?;
                (!m.meta.is_directory && !matches!(request.changes.get(&name), Some(None)))
                    .then_some(i)
            })
            .collect::<Vec<_>>();
        let sources = source.stage_rewrite_sources_with_progress(
            &selected,
            options(password),
            &RewriteStaging {
                directory: work.to_path_buf(),
                max_staged_bytes: u64::MAX,
            },
            Some(progress.clone()),
        )?;
        let mut sources = sources.into_iter();
        let comments = source.member_comments(password)?;
        let encrypted_comments = source.member_comment_encryption();
        for (index, member) in members.iter().enumerate() {
            let old = normal(&member.meta.name)?;
            let name = match request.changes.get(&old) {
                Some(None) => continue,
                Some(Some(name)) => name.as_bytes().to_vec(),
                None => member.meta.name.clone(),
            };
            let payload = if member.meta.is_directory {
                None
            } else {
                sources.next()
            };
            copy_member(
                &mut builder,
                member,
                name,
                payload,
                comments[index].clone(),
                encrypted_comments[index],
                password,
            )?;
        }
        builder
    } else {
        Builder::new(ArchiveVersion::Rar50).compression_level(Some(3))
    };
    add_inputs(&mut builder, &request.additions)?;
    builder.write_to_path_with_resources(
        output,
        &WriterResources::default().with_temp_dir(work),
        Some(progress.as_ref()),
    )?;
    if let Some(before) = &archive {
        let after = ArchiveReader::read_path_with_options(output, options(password))?;
        verify_metadata(before, &after, request, password)?;
    }
    if progress.is_cancelled() {
        return Err(rars::Error::Cancelled);
    }
    Ok(json!({"written":true}))
}

/// ABI v1: caller passes a valid NUL-terminated UTF-8 JSON request and an optional
/// synchronous, no-throw control callback. Response is freed only with pw_rar_free.
/// No pointer or callback is retained after this call returns.
///
/// # Safety
/// `request` must address a readable NUL-terminated string until return. The
/// callback, when supplied, must be callable until return and must not unwind.
#[no_mangle]
pub unsafe extern "C" fn pw_rar_call(request: *const c_char, control: Control) -> *mut c_char {
    let response = std::panic::catch_unwind(|| {
        if request.is_null() {
            return json!({"ok":false,"error":"null request"});
        }
        let result = CStr::from_ptr(request)
            .to_str()
            .map_err(|_| "request is not UTF-8".to_string())
            .and_then(|text| {
                serde_json::from_str::<Request>(text).map_err(|_| "invalid request".into())
            })
            .and_then(|request| run(&request, control).map_err(|error| error.to_string()));
        match result {
            Ok(value) => json!({"ok":true,"value":value}),
            Err(error) => json!({"ok":false,"error":error}),
        }
    })
    .unwrap_or_else(|_| json!({"ok":false,"error":"native RAR panic; original not published"}));
    CString::new(response.to_string())
        .expect("JSON never contains raw NUL")
        .into_raw()
}
#[no_mangle]
/// # Safety
/// `response` must be null or an unfreed pointer returned by `pw_rar_call`.
pub unsafe extern "C" fn pw_rar_free(response: *mut c_char) {
    if !response.is_null() {
        drop(CString::from_raw(response));
    }
}
