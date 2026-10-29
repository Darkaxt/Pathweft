//! Disposable interoperability inputs, never used by the application.
use rars::{ArchiveVersion, Builder};
use std::{fs, path::PathBuf};
fn main() -> rars::Result<()> {
    let root = PathBuf::from(std::env::args().nth(1).expect("fixture root"));
    assert!(root.starts_with(r"D:\Temp\pathweft-rars-native"));
    fs::create_dir_all(&root)?;
    let payload: Vec<u8> = (0..1_000_000)
        .map(|n| ((n * 71) ^ (n >> 8)) as u8)
        .collect();
    fs::write(root.join("large-original.bin"), &payload)?;
    for (name, version, encrypted) in [
        ("solid-rar4", ArchiveVersion::Rar40, false),
        ("solid-rar5", ArchiveVersion::Rar50, false),
        ("encrypted-rar4", ArchiveVersion::Rar40, true),
        ("encrypted-rar5", ArchiveVersion::Rar50, true),
    ] {
        let time = Some(if version.family() == rars::ArchiveFamily::Rar50Plus {
            1_700_000_000
        } else {
            0x576e_b1aa
        });
        let mut builder = Builder::new(version)
            .compression_level(Some(3))
            .solid(true)
            .comment(Some(b"Pathweft archive comment".to_vec()));
        if encrypted {
            builder = builder
                .password(Some(b"fixture-password".to_vec()))
                .header_encryption(true);
        }
        builder.add_bytes(b"nested/large.bin".to_vec(), payload.clone(), time, None)?;
        builder.set_dos_attributes(b"nested/large.bin", 0x21)?;
        if version.family() == rars::ArchiveFamily::Rar50Plus {
            builder.set_file_comment(b"nested/large.bin", Some(b"member comment".to_vec()))?;
        }
        builder.add_bytes(
            b"second.txt".to_vec(),
            b"retained secondary content".to_vec(),
            time,
            None,
        )?;
        builder.add_directory(b"empty".to_vec(), time, None)?;
        builder.write_to_path(&root.join(format!("{name}.rar")), None)?;
    }
    // Request a dictionary shape that requires RAR7's version-1 wire encoding;
    // a default "RAR7" target can otherwise legitimately emit RAR5-compatible v0.
    use rars::rar50::{ArchiveEntry, Rar50Writer, ServiceEntry, WriterOptions};
    let mut features = rars::FeatureSet::default();
    features.solid = true;
    let entries = vec![
        ArchiveEntry::new(
            b"nested/large.bin".to_vec(),
            rars::EntrySource::from_bytes(payload.clone()),
        )
        .with_mtime(Some(1_700_000_000))
        .with_attributes(0x21)
        .with_service(ServiceEntry::new(
            b"CMT".to_vec(),
            b"member comment".to_vec(),
        )),
        ArchiveEntry::new(
            b"second.txt".to_vec(),
            rars::EntrySource::from_bytes(b"retained secondary content".to_vec()),
        )
        .with_mtime(Some(1_700_000_000))
        .with_attributes(0x20),
        ArchiveEntry::new(
            b"empty".to_vec(),
            rars::EntrySource::from_bytes(Vec::<u8>::new()),
        )
        .with_directory(true)
        .with_mtime(Some(1_700_000_000))
        .with_attributes(0x10),
    ];
    let bytes = Rar50Writer::new(
        WriterOptions::new(ArchiveVersion::Rar70, features)
            .with_dictionary_size(192 * 1024)
            .with_compression_level(3),
    )
    .entries(entries)
    .archive_comment(Some(b"Pathweft archive comment"))
    .finish()?;
    fs::write(root.join("solid-rar7.rar"), bytes)?;
    if let rars::Archive::Rar50Plus(parsed) =
        rars::ArchiveReader::read_path(root.join("solid-rar7.rar"))?
    {
        assert!(parsed.files().any(|file| file
            .decoded_compression_info()
            .is_ok_and(|info| info.algorithm_version == 1)));
    } else {
        panic!("RAR7 fixture family mismatch");
    }
    let unsupported = root.parent().unwrap().join("unsupported-fixtures");
    fs::create_dir_all(&unsupported)?;
    let mut legacy = Builder::new(ArchiveVersion::Rar29).compression_level(Some(3));
    legacy.add_bytes(
        b"original.txt".to_vec(),
        b"legacy comment fixture".to_vec(),
        Some(0x576e_b1aa),
        None,
    )?;
    legacy.set_file_comment(b"original.txt", Some(b"legacy member comment".to_vec()))?;
    legacy.write_to_path(&unsupported.join("legacy-member-comment.rar"), None)?;
    Ok(())
}
