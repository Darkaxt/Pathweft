//! NTFS file record (MFT) and USN record parsing. Pure byte parsing; the
//! service does the I/O.

pub const ROOT_RECORD: u64 = 5;
pub const EXTEND_RECORD: u64 = 11;
pub const FIRST_USER_RECORD: u64 = 16;
pub const FRN_MASK: u64 = 0x0000_FFFF_FFFF_FFFF;

const ATTR_STANDARD_INFORMATION: u32 = 0x10;
const ATTR_ATTRIBUTE_LIST: u32 = 0x20;
const ATTR_FILE_NAME: u32 = 0x30;
const ATTR_DATA: u32 = 0x80;
const ATTR_END: u32 = 0xFFFF_FFFF;

const NAMESPACE_DOS: u8 = 2;

#[inline]
fn u16_at(b: &[u8], o: usize) -> Option<u16> {
    b.get(o..o + 2).map(|s| u16::from_le_bytes([s[0], s[1]]))
}
#[inline]
fn u32_at(b: &[u8], o: usize) -> Option<u32> {
    b.get(o..o + 4).map(|s| u32::from_le_bytes([s[0], s[1], s[2], s[3]]))
}
#[inline]
fn u64_at(b: &[u8], o: usize) -> Option<u64> {
    b.get(o..o + 8).map(|s| u64::from_le_bytes(s.try_into().unwrap()))
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub struct StdInfo {
    pub ctime: u64,
    pub mtime: u64,
    pub attrs: u32,
    pub sec: u32,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FileName {
    pub parent: u64,
    pub namespace: u8,
    pub name: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Run {
    /// Logical cluster number, `None` for a sparse run.
    pub lcn: Option<i64>,
    pub clusters: u64,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum AttrList {
    Resident(Vec<u8>),
    NonResident { runs: Vec<Run>, size: u64 },
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct RecordInfo {
    /// Base record number (0 for a base record).
    pub base: u64,
    pub in_use: bool,
    pub directory: bool,
    pub std: Option<StdInfo>,
    pub names: Vec<FileName>,
    /// Size of the unnamed data stream when its first extent is in this record.
    pub data_size: Option<u64>,
    /// Runs of the unnamed data stream and its lowest VCN (used for `$MFT`).
    pub data_runs: Option<(u64, Vec<Run>)>,
    pub attr_list: Option<AttrList>,
}

impl RecordInfo {
    /// The primary (non-DOS) name.
    pub fn primary_name(&self) -> Option<&FileName> {
        self.names.iter().find(|n| n.namespace != NAMESPACE_DOS)
    }

    /// Merges an extension record's attributes into this base record.
    pub fn merge(&mut self, ext: RecordInfo) {
        if self.std.is_none() {
            self.std = ext.std;
        }
        self.names.extend(ext.names);
        if self.data_size.is_none() {
            self.data_size = ext.data_size;
        }
    }
}

/// Applies the update sequence array in place. Sectors whose tail does not
/// carry the sequence number are taken as already fixed up (records returned
/// by `FSCTL_GET_NTFS_FILE_RECORD`). False if the header is malformed.
pub fn apply_fixups(buf: &mut [u8]) -> bool {
    let (Some(usa_off), Some(usa_count)) = (u16_at(buf, 4), u16_at(buf, 6)) else { return false };
    let (usa_off, usa_count) = (usa_off as usize, usa_count as usize);
    if usa_count < 2 || usa_off + usa_count * 2 > buf.len() {
        return false;
    }
    let stride = buf.len() / (usa_count - 1);
    if stride < 512 || stride % 2 != 0 {
        return false;
    }
    let usn = [buf[usa_off], buf[usa_off + 1]];
    for i in 1..usa_count {
        let tail = i * stride - 2;
        if tail + 2 > buf.len() {
            return false;
        }
        if buf[tail..tail + 2] == usn {
            buf[tail] = buf[usa_off + 2 * i];
            buf[tail + 1] = buf[usa_off + 2 * i + 1];
        }
    }
    true
}

/// Decodes an NTFS mapping-pairs (data runs) array.
pub fn parse_runs(data: &[u8]) -> Vec<Run> {
    let mut runs = Vec::new();
    let mut i = 0;
    let mut lcn: i64 = 0;
    while i < data.len() {
        let header = data[i];
        if header == 0 {
            break;
        }
        let len_size = (header & 0x0F) as usize;
        let off_size = (header >> 4) as usize;
        i += 1;
        if len_size == 0 || len_size > 8 || off_size > 8 || i + len_size + off_size > data.len() {
            break;
        }
        let mut clusters: u64 = 0;
        for k in 0..len_size {
            clusters |= (data[i + k] as u64) << (8 * k);
        }
        i += len_size;
        if off_size == 0 {
            runs.push(Run { lcn: None, clusters });
            continue;
        }
        let mut delta: i64 = 0;
        for k in 0..off_size {
            delta |= (data[i + k] as i64) << (8 * k);
        }
        // Sign-extend.
        let shift = 64 - 8 * off_size as u32;
        if shift > 0 && shift < 64 {
            delta = (delta << shift) >> shift;
        }
        i += off_size;
        lcn += delta;
        runs.push(Run { lcn: Some(lcn), clusters });
    }
    runs
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct AttrListEntry {
    pub attr_type: u32,
    pub name_len: u8,
    pub lowest_vcn: u64,
    pub record: u64,
}

pub fn parse_attr_list(data: &[u8]) -> Vec<AttrListEntry> {
    let mut out = Vec::new();
    let mut o = 0;
    while o + 26 <= data.len() {
        let attr_type = u32_at(data, o).unwrap();
        let len = u16_at(data, o + 4).unwrap() as usize;
        if len < 26 || attr_type == ATTR_END {
            break;
        }
        out.push(AttrListEntry {
            attr_type,
            name_len: data[o + 6],
            lowest_vcn: u64_at(data, o + 8).unwrap(),
            record: u64_at(data, o + 16).unwrap() & FRN_MASK,
        });
        o += len;
    }
    out
}

/// Extension records holding this base record's names or first data extent.
pub fn extension_records(list: &[AttrListEntry], base: u64) -> Vec<u64> {
    let mut out: Vec<u64> = list
        .iter()
        .filter(|e| e.record != base)
        .filter(|e| e.attr_type == ATTR_FILE_NAME || (e.attr_type == ATTR_DATA && e.name_len == 0 && e.lowest_vcn == 0))
        .map(|e| e.record)
        .collect();
    out.sort_unstable();
    out.dedup();
    out
}

/// Extension records holding further `$MFT` data extents.
pub fn data_extension_records(list: &[AttrListEntry], base: u64) -> Vec<u64> {
    let mut out: Vec<u64> = list
        .iter()
        .filter(|e| e.record != base && e.attr_type == ATTR_DATA && e.name_len == 0)
        .map(|e| e.record)
        .collect();
    out.sort_unstable();
    out.dedup();
    out
}

fn resident_value(attr: &[u8]) -> Option<&[u8]> {
    let vlen = u32_at(attr, 16)? as usize;
    let voff = u16_at(attr, 20)? as usize;
    attr.get(voff..voff + vlen)
}

/// Parses a fixed-up FILE record. `None` if it is not a FILE record.
pub fn parse_record(buf: &[u8]) -> Option<RecordInfo> {
    if buf.len() < 48 || &buf[0..4] != b"FILE" {
        return None;
    }
    let first_attr = u16_at(buf, 20)? as usize;
    let flags = u16_at(buf, 22)?;
    let used = (u32_at(buf, 24)? as usize).min(buf.len());
    let mut info = RecordInfo {
        base: u64_at(buf, 32)? & FRN_MASK,
        in_use: flags & 1 != 0,
        directory: flags & 2 != 0,
        ..Default::default()
    };
    let mut o = first_attr;
    while o + 16 <= used {
        let ty = u32_at(buf, o)?;
        if ty == ATTR_END {
            break;
        }
        let len = u32_at(buf, o + 4)? as usize;
        if len < 16 || o + len > used {
            break;
        }
        let attr = &buf[o..o + len];
        let non_resident = attr[8] != 0;
        let name_len = attr[9];
        match ty {
            ATTR_STANDARD_INFORMATION if !non_resident => {
                if let Some(v) = resident_value(attr) {
                    if v.len() >= 36 {
                        info.std = Some(StdInfo {
                            ctime: u64_at(v, 0)?,
                            mtime: u64_at(v, 8)?,
                            attrs: u32_at(v, 32)?,
                            sec: if v.len() >= 56 { u32_at(v, 52)? } else { 0 },
                        });
                    }
                }
            }
            ATTR_FILE_NAME if !non_resident => {
                if let Some(v) = resident_value(attr) {
                    if v.len() >= 66 {
                        let n = v[64] as usize;
                        if let Some(raw) = v.get(66..66 + 2 * n) {
                            let units: Vec<u16> = raw.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
                            info.names.push(FileName {
                                parent: u64_at(v, 0)? & FRN_MASK,
                                namespace: v[65],
                                name: String::from_utf16_lossy(&units),
                            });
                        }
                    }
                }
            }
            ATTR_DATA if name_len == 0 => {
                if non_resident {
                    let lowest_vcn = u64_at(attr, 16)?;
                    if lowest_vcn == 0 {
                        info.data_size = Some(u64_at(attr, 48)?);
                    }
                    let runs_off = u16_at(attr, 32)? as usize;
                    if let Some(runs) = attr.get(runs_off..) {
                        info.data_runs = Some((lowest_vcn, parse_runs(runs)));
                    }
                } else {
                    info.data_size = Some(u32_at(attr, 16)? as u64);
                }
            }
            ATTR_ATTRIBUTE_LIST => {
                if non_resident {
                    let runs_off = u16_at(attr, 32)? as usize;
                    let size = u64_at(attr, 48)?;
                    info.attr_list = Some(AttrList::NonResident { runs: parse_runs(attr.get(runs_off..)?), size });
                } else if let Some(v) = resident_value(attr) {
                    info.attr_list = Some(AttrList::Resident(v.to_vec()));
                }
            }
            _ => {}
        }
        o += len;
    }
    Some(info)
}

/// The fields of a USN_RECORD_V2 the service uses.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct UsnRecord {
    pub frn: u64,
    pub parent: u64,
    pub usn: i64,
    pub reason: u32,
}

/// Parses the output of FSCTL_READ_USN_JOURNAL: the next USN, then records.
pub fn parse_usn_buffer(buf: &[u8]) -> Option<(i64, Vec<UsnRecord>)> {
    let next = u64_at(buf, 0)? as i64;
    let mut out = Vec::new();
    let mut o = 8;
    while o + 60 <= buf.len() {
        let len = u32_at(buf, o)? as usize;
        if len < 60 || o + len > buf.len() {
            break;
        }
        let major = u16_at(buf, o + 4)?;
        if major == 2 {
            out.push(UsnRecord {
                frn: u64_at(buf, o + 8)? & FRN_MASK,
                parent: u64_at(buf, o + 16)? & FRN_MASK,
                usn: u64_at(buf, o + 24)? as i64,
                reason: u32_at(buf, o + 40)?,
            });
        }
        o += (len + 7) & !7;
    }
    Some((next, out))
}

#[cfg(test)]
pub mod tests {
    use super::*;

    /// Builds a 1024-byte FILE record with fixups applied for writing to disk.
    pub fn build_record(flags: u16, base: u64, attrs: &[Vec<u8>]) -> Vec<u8> {
        let mut b = vec![0u8; 1024];
        b[0..4].copy_from_slice(b"FILE");
        b[4..6].copy_from_slice(&48u16.to_le_bytes()); // usa offset
        b[6..8].copy_from_slice(&3u16.to_le_bytes()); // usa count: 1 + 2 sectors
        b[20..22].copy_from_slice(&56u16.to_le_bytes());
        b[22..24].copy_from_slice(&flags.to_le_bytes());
        b[32..40].copy_from_slice(&base.to_le_bytes());
        let mut o = 56;
        for a in attrs {
            b[o..o + a.len()].copy_from_slice(a);
            o += a.len();
        }
        b[o..o + 4].copy_from_slice(&ATTR_END.to_le_bytes());
        o += 8;
        b[24..28].copy_from_slice(&(o as u32).to_le_bytes());
        b[28..32].copy_from_slice(&1024u32.to_le_bytes());
        // Protect sector tails with the sequence number 0x0101.
        b[48..50].copy_from_slice(&[1, 1]);
        for i in 1..3usize {
            let tail = i * 512 - 2;
            b[48 + 2 * i] = b[tail];
            b[48 + 2 * i + 1] = b[tail + 1];
            b[tail] = 1;
            b[tail + 1] = 1;
        }
        b
    }

    pub fn resident(ty: u32, name_len: u8, value: &[u8]) -> Vec<u8> {
        let voff = 24usize;
        let len = (voff + value.len() + 7) & !7;
        let mut a = vec![0u8; len];
        a[0..4].copy_from_slice(&ty.to_le_bytes());
        a[4..8].copy_from_slice(&(len as u32).to_le_bytes());
        a[9] = name_len;
        a[16..20].copy_from_slice(&(value.len() as u32).to_le_bytes());
        a[20..22].copy_from_slice(&(voff as u16).to_le_bytes());
        a[voff..voff + value.len()].copy_from_slice(value);
        a
    }

    pub fn std_info(ctime: u64, mtime: u64, attrs: u32, sec: u32) -> Vec<u8> {
        let mut v = vec![0u8; 72];
        v[0..8].copy_from_slice(&ctime.to_le_bytes());
        v[8..16].copy_from_slice(&mtime.to_le_bytes());
        v[32..36].copy_from_slice(&attrs.to_le_bytes());
        v[52..56].copy_from_slice(&sec.to_le_bytes());
        resident(ATTR_STANDARD_INFORMATION, 0, &v)
    }

    pub fn file_name(parent: u64, ns: u8, name: &str) -> Vec<u8> {
        let units: Vec<u16> = name.encode_utf16().collect();
        let mut v = vec![0u8; 66 + 2 * units.len()];
        v[0..8].copy_from_slice(&(parent | (3u64 << 48)).to_le_bytes());
        v[64] = units.len() as u8;
        v[65] = ns;
        for (i, u) in units.iter().enumerate() {
            v[66 + 2 * i..68 + 2 * i].copy_from_slice(&u.to_le_bytes());
        }
        resident(ATTR_FILE_NAME, 0, &v)
    }

    pub fn nonresident_data(size: u64, runs: &[u8]) -> Vec<u8> {
        let runs_off = 64usize;
        let len = (runs_off + runs.len() + 7) & !7;
        let mut a = vec![0u8; len];
        a[0..4].copy_from_slice(&ATTR_DATA.to_le_bytes());
        a[4..8].copy_from_slice(&(len as u32).to_le_bytes());
        a[8] = 1;
        a[32..34].copy_from_slice(&(runs_off as u16).to_le_bytes());
        a[48..56].copy_from_slice(&size.to_le_bytes());
        a[runs_off..runs_off + runs.len()].copy_from_slice(runs);
        a
    }

    #[test]
    fn parses_a_file_record_with_dos_and_win32_names() {
        let mut rec = build_record(
            1,
            0,
            &[
                std_info(111, 222, 0x20, 0x105),
                file_name(5, 2, "LONGNA~1.TXT"),
                file_name(5, 1, "Long name ä.txt"),
                nonresident_data(123_456, &[0x11, 0x10, 0x20, 0x00]),
            ],
        );
        assert!(apply_fixups(&mut rec));
        let info = parse_record(&rec).unwrap();
        assert!(info.in_use);
        assert!(!info.directory);
        assert_eq!(info.std, Some(StdInfo { ctime: 111, mtime: 222, attrs: 0x20, sec: 0x105 }));
        let name = info.primary_name().unwrap();
        assert_eq!(name.name, "Long name ä.txt");
        assert_eq!(name.parent, 5);
        assert_eq!(info.data_size, Some(123_456));
        assert_eq!(info.data_runs.unwrap().1, vec![Run { lcn: Some(0x20), clusters: 0x10 }]);
    }

    #[test]
    fn resident_data_and_directories() {
        let mut rec = build_record(3, 0, &[std_info(1, 2, 0, 0), file_name(5, 3, "Folder"), resident(ATTR_DATA, 0, b"abc")]);
        assert!(apply_fixups(&mut rec));
        let info = parse_record(&rec).unwrap();
        assert!(info.directory);
        assert_eq!(info.data_size, Some(3));
        // Already fixed up: a second application changes nothing.
        let copy = rec.clone();
        assert!(apply_fixups(&mut rec));
        assert_eq!(copy, rec);
    }

    #[test]
    fn runs_decode_negative_offsets_and_sparse() {
        // 0x21: 1-byte length, 2-byte offset. Second run goes backwards.
        let runs = parse_runs(&[0x21, 0x08, 0x00, 0x10, 0x11, 0x04, 0xF0, 0x01, 0x05, 0x00]);
        assert_eq!(
            runs,
            vec![
                Run { lcn: Some(0x1000), clusters: 8 },
                Run { lcn: Some(0x1000 - 0x10), clusters: 4 },
                Run { lcn: None, clusters: 5 },
            ]
        );
    }

    #[test]
    fn attribute_list_extensions() {
        let mut data = Vec::new();
        for (ty, rec, vcn) in [(0x10u32, 40u64, 0u64), (0x30, 41, 0), (0x80, 42, 0), (0x80, 43, 100)] {
            let mut e = vec![0u8; 32];
            e[0..4].copy_from_slice(&ty.to_le_bytes());
            e[4..6].copy_from_slice(&32u16.to_le_bytes());
            e[8..16].copy_from_slice(&vcn.to_le_bytes());
            e[16..24].copy_from_slice(&(rec | (1 << 48)).to_le_bytes());
            data.extend(e);
        }
        let list = parse_attr_list(&data);
        assert_eq!(list.len(), 4);
        assert_eq!(extension_records(&list, 40), vec![41, 42]);
        assert_eq!(data_extension_records(&list, 40), vec![42, 43]);
    }

    #[test]
    fn usn_buffer() {
        let mut buf = 77u64.to_le_bytes().to_vec();
        let mut r = vec![0u8; 64];
        r[0..4].copy_from_slice(&64u32.to_le_bytes());
        r[4..6].copy_from_slice(&2u16.to_le_bytes());
        r[8..16].copy_from_slice(&(1234u64 | (9 << 48)).to_le_bytes());
        r[16..24].copy_from_slice(&5u64.to_le_bytes());
        r[24..32].copy_from_slice(&70u64.to_le_bytes());
        r[40..44].copy_from_slice(&0x100u32.to_le_bytes());
        buf.extend(r);
        let (next, records) = parse_usn_buffer(&buf).unwrap();
        assert_eq!(next, 77);
        assert_eq!(records, vec![UsnRecord { frn: 1234, parent: 5, usn: 70, reason: 0x100 }]);
    }
}
