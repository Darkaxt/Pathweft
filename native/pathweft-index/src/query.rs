//! Query language (the subset of Everything's syntax Pathweft uses), matching,
//! sorting, paging, folder sizes and the caller-visibility rule.
//!
//! Locking: a store is read in chunks of `CHUNK` records under its read lock,
//! which is released between chunks, so an indexer waiting to apply a batch
//! never waits for a whole query.

use crate::protocol::{Hit, QueryRequest, SortKey, MAX_COUNT};
use crate::store::{Rec, Store, ATTR_DIRECTORY};
use std::cmp::Ordering;
use std::collections::{BinaryHeap, HashMap, HashSet};
use std::sync::atomic::{AtomicUsize, Ordering as AtomicOrdering};
use std::sync::{Arc, RwLock};

pub const CHUNK: usize = 65_536;

#[derive(Clone)]
pub struct StoreRef {
    pub slot: usize,
    pub store: Arc<RwLock<Store>>,
}

/// Decides what the caller may see. A directory's children are visible only
/// if the caller may list that directory.
pub trait Access: Sync {
    /// Identifies the caller for caches.
    fn principal(&self) -> &str;
    /// Whether the caller may list directories of store `slot` that carry
    /// security ID `sec`. `None` while unknown.
    fn listable(&self, slot: usize, sec: u32) -> Option<bool>;
}

/// A security ID the caller's access has not been evaluated for yet, with a
/// directory that carries it.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub struct Unknown {
    pub slot: usize,
    pub sec: u32,
    pub dir: u32,
}

#[derive(Debug)]
pub enum Outcome<T> {
    Done(T),
    NeedAccess(Vec<Unknown>),
}

#[derive(Debug, Clone, Default, PartialEq)]
pub struct QueryResult {
    pub total: u64,
    pub hits: Vec<Hit>,
}

// ---------------------------------------------------------------- parsing

#[derive(Clone, Copy, Default, Debug)]
struct Mods {
    case: bool,
    path: bool,
    ww: bool,
    wfn: bool,
    regex: bool,
    files: bool,
    folders: bool,
}

#[derive(Debug)]
pub enum Matcher {
    Sub(Vec<u8>),
    Glob(Vec<u8>),
    Exact(Vec<u8>),
    Word(Vec<u8>),
    Regex(regex::Regex),
}

#[derive(Debug)]
pub struct Text {
    pub on_path: bool,
    pub case: bool,
    pub m: Matcher,
    /// The term as written (after unquoting), for path resolution.
    pub raw: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SizeRange {
    pub lo: u64,
    pub hi: u64,
}

#[derive(Debug)]
pub enum Node {
    All,
    Never,
    And(Vec<Node>),
    Or(Vec<Node>),
    Not(Box<Node>),
    Files,
    Folders,
    Text(Text),
    Ext(Vec<String>),
    Size(SizeRange),
    Subtree { slot: usize, rec: u32 },
    Exact { slot: usize, rec: u32 },
    /// Modified (`created: false`) or created time in `lo..hi` (UTC FILETIME).
    Date { created: bool, lo: u64, hi: u64 },
    /// `parent:` before resolution: the folder's path.
    Parent(String),
    /// Direct children of an indexed folder.
    Children { slot: usize, rec: u32 },
}

const FUNCTIONS: &str = "path:, nopath:, case:, nocase:, ww:, noww:, wfn:, nowfn:, regex:, noregex:, file:, folder:, ext:, size:, dm:, dc:, parent:";

struct Parser {
    s: Vec<char>,
    i: usize,
    depth: usize,
    /// Today's local date (days since 1970-01-01), for relative dates.
    today: i64,
}

impl Parser {
    fn peek(&self) -> Option<char> {
        self.s.get(self.i).copied()
    }
    fn skip_ws(&mut self) {
        while matches!(self.peek(), Some(c) if c.is_whitespace()) {
            self.i += 1;
        }
    }

    fn parse_and(&mut self, m: Mods) -> Result<Node, String> {
        let mut items = Vec::new();
        loop {
            self.skip_ws();
            match self.peek() {
                None | Some('>') => break,
                _ => items.push(self.parse_or(m)?),
            }
        }
        Ok(match items.len() {
            0 => Node::All,
            1 => items.pop().unwrap(),
            _ => Node::And(items),
        })
    }

    fn parse_or(&mut self, m: Mods) -> Result<Node, String> {
        let mut items = vec![self.parse_unary(m)?];
        loop {
            let save = self.i;
            self.skip_ws();
            if self.peek() == Some('|') {
                self.i += 1;
                self.skip_ws();
                if matches!(self.peek(), None | Some('>')) {
                    break;
                }
                items.push(self.parse_unary(m)?);
            } else {
                self.i = save;
                break;
            }
        }
        Ok(if items.len() == 1 { items.pop().unwrap() } else { Node::Or(items) })
    }

    fn parse_unary(&mut self, m: Mods) -> Result<Node, String> {
        if self.peek() == Some('!') {
            self.i += 1;
            return Ok(Node::Not(Box::new(self.parse_unary(m)?)));
        }
        self.primary(m)
    }

    /// A function name: two or more ASCII letters followed by ':'.
    fn function_name(&self) -> Option<(String, usize)> {
        let mut j = self.i;
        while j < self.s.len() && self.s[j].is_ascii_alphabetic() {
            j += 1;
        }
        if j - self.i >= 2 && self.s.get(j) == Some(&':') {
            Some((self.s[self.i..j].iter().collect::<String>().to_ascii_lowercase(), j + 1))
        } else {
            None
        }
    }

    fn at_term_end(&self) -> bool {
        matches!(self.peek(), None | Some('|') | Some('>')) || matches!(self.peek(), Some(c) if c.is_whitespace())
    }

    fn primary(&mut self, mut m: Mods) -> Result<Node, String> {
        if self.peek() == Some('<') {
            self.i += 1;
            self.depth += 1;
            if self.depth > 64 {
                return Err("Search groups are nested too deeply.".into());
            }
            let node = self.parse_and(m)?;
            if self.peek() != Some('>') {
                return Err("Close the search quotes and groups.".into());
            }
            self.i += 1;
            self.depth -= 1;
            return Ok(node);
        }
        if let Some((name, after)) = self.function_name() {
            match name.as_str() {
                "case" => m.case = true,
                "nocase" => m.case = false,
                "path" => m.path = true,
                "nopath" => m.path = false,
                "ww" | "wholeword" | "wholewords" => m.ww = true,
                "noww" | "nowholeword" | "nowholewords" => m.ww = false,
                "wfn" | "wholefilename" => m.wfn = true,
                "nowfn" | "nowholefilename" => m.wfn = false,
                "regex" => m.regex = true,
                "noregex" => m.regex = false,
                "file" | "files" => m.files = true,
                "folder" | "folders" => m.folders = true,
                "ext" => {
                    self.i = after;
                    let arg = self.read_text(true)?;
                    let list = arg.split(';').map(|e| e.trim().trim_start_matches('.').to_lowercase()).collect();
                    return Ok(wrap(Node::Ext(list), m));
                }
                "size" => {
                    self.i = after;
                    let arg = self.read_comparison()?;
                    return Ok(wrap(Node::Size(parse_size(&arg)?), m));
                }
                "dm" | "datemodified" | "dc" | "datecreated" => {
                    self.i = after;
                    let arg = self.read_comparison()?;
                    let (lo, hi) = parse_date(&arg, self.today)?;
                    let created = name.starts_with("dc") || name == "datecreated";
                    return Ok(wrap(Node::Date { created, lo: local_midnight(lo), hi: local_midnight(hi) }, m));
                }
                "parent" => {
                    self.i = after;
                    let arg = self.read_text(true)?;
                    if arg.is_empty() {
                        return Err("parent: needs a folder, for example parent:\"C:\\Users\".".into());
                    }
                    return Ok(wrap(Node::Parent(arg), m));
                }
                other => {
                    return Err(format!(
                        "The index service does not support the search function '{other}:'. Supported: {FUNCTIONS}."
                    ))
                }
            }
            self.i = after;
            if self.at_term_end() {
                return Ok(wrap(Node::All, m));
            }
            return self.primary(m);
        }
        let text = self.read_text(true)?;
        build_term(text, m)
    }

    /// Reads a function argument that may start with a comparison (`>`, `>=`,
    /// `<`, `<=`, `=`); a later `>` still closes a group.
    fn read_comparison(&mut self) -> Result<String, String> {
        let mut out = String::new();
        while let Some(c @ ('<' | '>' | '=')) = self.peek() {
            out.push(c);
            self.i += 1;
        }
        out.push_str(&self.read_text(true)?);
        Ok(out)
    }

    /// Reads a term; quotes may appear anywhere and `""` inside quotes is a
    /// literal quote. `stop_at_angle` ends the term at `<` and `>`.
    fn read_text(&mut self, stop_at_angle: bool) -> Result<String, String> {
        let mut out = String::new();
        let mut quoted = false;
        while let Some(c) = self.peek() {
            if quoted {
                if c == '"' {
                    if self.s.get(self.i + 1) == Some(&'"') {
                        out.push('"');
                        self.i += 2;
                        continue;
                    }
                    quoted = false;
                    self.i += 1;
                    continue;
                }
                out.push(c);
                self.i += 1;
                continue;
            }
            if c == '"' {
                quoted = true;
                self.i += 1;
                continue;
            }
            if c.is_whitespace() || c == '|' || (stop_at_angle && (c == '<' || c == '>')) {
                break;
            }
            out.push(c);
            self.i += 1;
        }
        if quoted {
            return Err("Close the search quotes and groups.".into());
        }
        Ok(out)
    }
}

fn wrap(node: Node, m: Mods) -> Node {
    let mut parts = Vec::new();
    if m.files {
        parts.push(Node::Files);
    }
    if m.folders {
        parts.push(Node::Folders);
    }
    if parts.is_empty() {
        return node;
    }
    if !matches!(node, Node::All) {
        parts.push(node);
    }
    if parts.len() == 1 {
        parts.pop().unwrap()
    } else {
        Node::And(parts)
    }
}

fn build_term(text: String, m: Mods) -> Result<Node, String> {
    if text.is_empty() {
        return Ok(wrap(Node::All, m));
    }
    // A backslash in a regex is an escape; only an escaped literal one (`\\`)
    // names a path separator.
    let on_path = m.path || if m.regex { text.contains(r"\\") } else { text.contains('\\') };
    let matcher = if m.regex {
        let re = regex::RegexBuilder::new(&text)
            .case_insensitive(!m.case)
            .build()
            .map_err(|e| format!("Invalid regular expression: {e}"))?;
        Matcher::Regex(re)
    } else {
        let needle = if m.case { text.clone() } else { text.to_lowercase() }.into_bytes();
        if text.contains('*') || text.contains('?') {
            Matcher::Glob(needle)
        } else if m.wfn {
            Matcher::Exact(needle)
        } else if m.ww {
            Matcher::Word(needle)
        } else {
            Matcher::Sub(needle)
        }
    };
    Ok(wrap(Node::Text(Text { on_path, case: m.case, m: matcher, raw: text }), m))
}

fn parse_size(arg: &str) -> Result<SizeRange, String> {
    let bad = || format!("Unsupported size '{arg}'. Use size:>10mb, size:<=1gb, size:1kb..2mb or size:empty.");
    let a = arg.trim().to_ascii_lowercase();
    if a == "empty" {
        return Ok(SizeRange { lo: 0, hi: 0 });
    }
    fn value(v: &str) -> Option<u64> {
        let v = v.trim();
        let split = v.find(|c: char| !(c.is_ascii_digit() || c == '.')).unwrap_or(v.len());
        let (num, unit) = v.split_at(split);
        let num: f64 = num.parse().ok()?;
        let mult: f64 = match unit.trim() {
            "" | "b" => 1.0,
            "k" | "kb" => 1024.0,
            "m" | "mb" => 1024.0 * 1024.0,
            "g" | "gb" => 1024.0 * 1024.0 * 1024.0,
            "t" | "tb" => 1024.0f64.powi(4),
            _ => return None,
        };
        Some((num * mult) as u64)
    }
    if let Some((lo, hi)) = a.split_once("..") {
        return Ok(SizeRange { lo: value(lo).ok_or_else(bad)?, hi: value(hi).ok_or_else(bad)? });
    }
    let (op, rest) = if let Some(r) = a.strip_prefix(">=") {
        (">=", r)
    } else if let Some(r) = a.strip_prefix("<=") {
        ("<=", r)
    } else if let Some(r) = a.strip_prefix('>') {
        (">", r)
    } else if let Some(r) = a.strip_prefix('<') {
        ("<", r)
    } else if let Some(r) = a.strip_prefix('=') {
        ("=", r)
    } else {
        ("=", a.as_str())
    };
    let v = value(rest).ok_or_else(bad)?;
    Ok(match op {
        ">=" => SizeRange { lo: v, hi: u64::MAX },
        ">" => SizeRange { lo: v.saturating_add(1), hi: u64::MAX },
        "<=" => SizeRange { lo: 0, hi: v },
        "<" => SizeRange { lo: 0, hi: v.saturating_sub(1) },
        _ => SizeRange { lo: v, hi: v },
    })
}

/// Compiles a request into a query tree (before path resolution).
pub fn compile(req: &QueryRequest) -> Result<Node, String> {
    let base = Mods { case: req.match_case, path: req.match_path, ww: req.whole_word, ..Default::default() };
    if req.regex {
        let q = req.query.trim();
        let q = if q.len() >= 6 && q[..6].eq_ignore_ascii_case("regex:") { &q[6..] } else { q };
        let q = q.strip_prefix('"').and_then(|x| x.strip_suffix('"')).map(|x| x.replace("\"\"", "\"")).unwrap_or(q.to_string());
        return build_term(q, Mods { regex: true, ..base });
    }
    let mut p = Parser { s: req.query.chars().collect(), i: 0, depth: 0, today: local_today() };
    let node = p.parse_and(base)?;
    if p.i < p.s.len() {
        return Err("Unbalanced Everything search groups.".into());
    }
    Ok(node)
}

// ---------------------------------------------------------------- dates

/// Days since 1970-01-01 of a proleptic Gregorian date.
fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = y.div_euclid(400);
    let yoe = y - era * 400;
    let mp = (m + 9) % 12;
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

fn civil_from_days(z: i64) -> (i64, i64, i64) {
    let z = z + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    (if m <= 2 { yoe + era * 400 + 1 } else { yoe + era * 400 }, m, d)
}

/// The local days `lo..hi` that one date word covers.
fn date_period(w: &str, today: i64) -> Option<(i64, i64)> {
    let (y, m, _) = civil_from_days(today);
    let monday = today - (today + 3).rem_euclid(7); // 1970-01-01 was a Thursday
    let month = |y: i64, m: i64| {
        let (ny, nm) = if m == 12 { (y + 1, 1) } else { (y, m + 1) };
        (days_from_civil(y, m, 1), days_from_civil(ny, nm, 1))
    };
    Some(match w {
        "today" => (today, today + 1),
        "yesterday" => (today - 1, today),
        "thisweek" => (monday, monday + 7),
        "lastweek" => (monday - 7, monday),
        "thismonth" => month(y, m),
        "lastmonth" => if m == 1 { month(y - 1, 12) } else { month(y, m - 1) },
        "thisyear" => (days_from_civil(y, 1, 1), days_from_civil(y + 1, 1, 1)),
        "lastyear" => (days_from_civil(y - 1, 1, 1), days_from_civil(y, 1, 1)),
        _ => {
            let parts: Vec<&str> = w.split(['-', '/']).collect();
            let num = |s: &str, len: std::ops::RangeInclusive<usize>| (len.contains(&s.len()) && s.bytes().all(|b| b.is_ascii_digit())).then(|| s.parse::<i64>().ok()).flatten();
            match parts.as_slice() {
                [ys] => {
                    let y = num(ys, 4..=4)?;
                    (days_from_civil(y, 1, 1), days_from_civil(y + 1, 1, 1))
                }
                [ys, ms] => {
                    let (y, m) = (num(ys, 4..=4)?, num(ms, 1..=2)?);
                    if !(1..=12).contains(&m) {
                        return None;
                    }
                    month(y, m)
                }
                [ys, ms, ds] => {
                    let (y, m, d) = (num(ys, 4..=4)?, num(ms, 1..=2)?, num(ds, 1..=2)?);
                    let day = days_from_civil(y, m, d);
                    if !(1..=12).contains(&m) || civil_from_days(day) != (y, m, d) {
                        return None;
                    }
                    (day, day + 1)
                }
                _ => return None,
            }
        }
    })
}

/// Parses a `dm:`/`dc:` argument into local days `lo..hi` (`i64::MIN`/`MAX`
/// for open ends): a date word, `a..b`, or `>`, `>=`, `<`, `<=`, `=` a date.
fn parse_date(arg: &str, today: i64) -> Result<(i64, i64), String> {
    let bad = || {
        format!(
            "Unsupported date '{arg}'. Use dm:today, yesterday, thisweek, lastweek, thismonth, lastmonth, thisyear, lastyear, \
             YYYY, YYYY-MM or YYYY-MM-DD, a range a..b, or >, >=, <, <= a date."
        )
    };
    let a = arg.trim().to_ascii_lowercase();
    let period = |w: &str| date_period(w.trim(), today).ok_or_else(bad);
    if let Some((x, y)) = a.split_once("..") {
        let (lo, _) = period(x)?;
        let (_, hi) = period(y)?;
        return Ok((lo, hi));
    }
    for (op, rest) in [(">=", a.strip_prefix(">=")), ("<=", a.strip_prefix("<=")), (">", a.strip_prefix('>')), ("<", a.strip_prefix('<')), ("=", a.strip_prefix('='))] {
        if let Some(r) = rest {
            let (lo, hi) = period(r)?;
            return Ok(match op {
                ">=" => (lo, i64::MAX),
                ">" => (hi, i64::MAX),
                "<=" => (i64::MIN, hi),
                "<" => (i64::MIN, lo),
                _ => (lo, hi),
            });
        }
    }
    period(&a)
}

/// UTC FILETIME of local midnight starting `day`; open ends map to 1 (so an
/// unknown time, 0, never matches) and `u64::MAX`.
fn local_midnight(day: i64) -> u64 {
    if day == i64::MIN {
        return 1;
    }
    if day == i64::MAX {
        return u64::MAX;
    }
    local_midnight_impl(day)
}

#[cfg(windows)]
fn local_midnight_impl(day: i64) -> u64 {
    use crate::ffi::{SystemTimeToFileTime, TzSpecificLocalTimeToSystemTime, FILETIME, SYSTEMTIME};
    let (y, m, d) = civil_from_days(day);
    let y = y.clamp(1601, 30_000);
    let local = SYSTEMTIME { year: y as u16, month: m as u16, day: d as u16, ..Default::default() };
    let mut utc = SYSTEMTIME::default();
    let mut ft = FILETIME::default();
    unsafe {
        if TzSpecificLocalTimeToSystemTime(std::ptr::null(), &local, &mut utc) == 0 || SystemTimeToFileTime(&utc, &mut ft) == 0 {
            return utc_midnight(day);
        }
    }
    ft.value()
}

#[cfg(not(windows))]
fn local_midnight_impl(day: i64) -> u64 {
    utc_midnight(day)
}

fn utc_midnight(day: i64) -> u64 {
    ((day + 134_774).max(0) as u64) * 86_400 * 10_000_000 // 1601-01-01 is day -134774
}

#[cfg(windows)]
fn local_today() -> i64 {
    let mut st = crate::ffi::SYSTEMTIME::default();
    unsafe { crate::ffi::GetLocalTime(&mut st) };
    days_from_civil(st.year as i64, st.month as i64, st.day as i64)
}

#[cfg(not(windows))]
fn local_today() -> i64 {
    (std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_secs()) / 86_400) as i64
}

// ---------------------------------------------------------------- matching

#[inline]
fn fold_eq(a: u8, b_lower: u8) -> bool {
    a.to_ascii_lowercase() == b_lower
}

fn contains(hay: &[u8], needle: &[u8], fold: bool) -> bool {
    find_from(hay, needle, fold, 0).is_some()
}

fn find_from(hay: &[u8], needle: &[u8], fold: bool, from: usize) -> Option<usize> {
    if needle.is_empty() {
        return Some(from);
    }
    if hay.len() < needle.len() + from {
        return None;
    }
    let first = needle[0];
    let last_start = hay.len() - needle.len();
    let mut i = from;
    while i <= last_start {
        let rel = if fold && first.is_ascii_alphabetic() {
            memchr::memchr2(first, first.to_ascii_uppercase(), &hay[i..=last_start])
        } else {
            memchr::memchr(first, &hay[i..=last_start])
        }?;
        let at = i + rel;
        let window = &hay[at..at + needle.len()];
        let equal = if fold { window.iter().zip(needle).all(|(&a, &b)| fold_eq(a, b)) } else { window == needle };
        if equal {
            return Some(at);
        }
        i = at + 1;
    }
    None
}

fn is_word(b: u8) -> bool {
    b.is_ascii_alphanumeric() || b >= 0x80
}

fn word(hay: &[u8], needle: &[u8], fold: bool) -> bool {
    let mut from = 0;
    while let Some(at) = find_from(hay, needle, fold, from) {
        let before = at == 0 || !is_word(hay[at - 1]);
        let end = at + needle.len();
        let after = end >= hay.len() || !is_word(hay[end]);
        if before && after {
            return true;
        }
        from = at + 1;
    }
    false
}

#[inline]
fn utf8_len(b: u8) -> usize {
    match b {
        0x00..=0x7F => 1,
        0xC0..=0xDF => 2,
        0xE0..=0xEF => 3,
        0xF0..=0xF7 => 4,
        _ => 1,
    }
}

fn glob(p: &[u8], s: &[u8], fold: bool) -> bool {
    let (mut pi, mut si) = (0usize, 0usize);
    let mut star: Option<usize> = None;
    let mut mark = 0usize;
    let eq = |a: u8, b: u8| if fold { fold_eq(a, b) } else { a == b };
    while si < s.len() {
        if pi < p.len() && p[pi] == b'?' {
            si += utf8_len(s[si]);
            pi += 1;
            // Skip the continuation bytes of a multi-byte '?' in the pattern.
        } else if pi < p.len() && p[pi] == b'*' {
            star = Some(pi);
            pi += 1;
            mark = si;
        } else if pi < p.len() && eq(s[si], p[pi]) {
            pi += 1;
            si += 1;
        } else if let Some(st) = star {
            pi = st + 1;
            mark += utf8_len(s[mark]);
            si = mark;
        } else {
            return false;
        }
    }
    while pi < p.len() && p[pi] == b'*' {
        pi += 1;
    }
    pi == p.len() && si >= s.len()
}

fn match_bytes(m: &Matcher, hay: &[u8], fold: bool) -> bool {
    match m {
        Matcher::Sub(n) => contains(hay, n, fold),
        Matcher::Exact(n) => hay.len() == n.len() && hay.iter().zip(n).all(|(&a, &b)| if fold { fold_eq(a, b) } else { a == b }),
        Matcher::Word(n) => word(hay, n, fold),
        Matcher::Glob(p) => glob(p, hay, fold),
        Matcher::Regex(_) => false,
    }
}

fn text_matches(t: &Text, s: &str) -> bool {
    if let Matcher::Regex(re) = &t.m {
        return re.is_match(s);
    }
    if t.case {
        match_bytes(&t.m, s.as_bytes(), false)
    } else if s.is_ascii() {
        match_bytes(&t.m, s.as_bytes(), true)
    } else {
        match_bytes(&t.m, s.to_lowercase().as_bytes(), false)
    }
}

fn extension(name: &str) -> &str {
    match name.rfind('.') {
        Some(i) => &name[i + 1..],
        None => "",
    }
}

struct Cand<'a> {
    st: &'a Store,
    slot: usize,
    r: u32,
    rec: &'a Rec,
    name: &'a str,
    path: Option<String>,
}

impl<'a> Cand<'a> {
    fn path(&mut self) -> &str {
        if self.path.is_none() {
            let mut p = String::with_capacity(96);
            if !self.st.full_path(self.r, &mut p) {
                p.clear();
            }
            self.path = Some(p);
        }
        self.path.as_deref().unwrap()
    }
}

fn eval(n: &Node, c: &mut Cand) -> bool {
    match n {
        Node::All => true,
        Node::Never => false,
        Node::And(items) => items.iter().all(|x| eval(x, c)),
        Node::Or(items) => items.iter().any(|x| eval(x, c)),
        Node::Not(x) => !eval(x, c),
        Node::Files => !c.rec.is_dir(),
        Node::Folders => c.rec.is_dir(),
        Node::Ext(list) => {
            let e = extension(c.name);
            list.iter().any(|x| x.len() == e.len() && (x.as_bytes().eq_ignore_ascii_case(e.as_bytes()) || *x == e.to_lowercase()))
        }
        Node::Size(range) => !c.rec.is_dir() && c.rec.size >= range.lo && c.rec.size <= range.hi,
        Node::Subtree { slot, rec } => c.slot == *slot && c.st.is_below(c.r, *rec),
        Node::Exact { slot, rec } => c.slot == *slot && c.r == *rec,
        Node::Date { created, lo, hi } => {
            let t = if *created { c.rec.ctime } else { c.rec.mtime };
            t != 0 && t >= *lo && t < *hi
        }
        Node::Children { slot, rec } => c.slot == *slot && c.rec.parent == *rec && c.r != *rec,
        Node::Parent(_) => false, // replaced during resolution
        Node::Text(t) => {
            if t.on_path {
                let p = c.path();
                text_matches(t, p)
            } else {
                text_matches(t, c.name)
            }
        }
    }
}

// ---------------------------------------------------------------- resolution

fn fold_path(p: &str) -> String {
    p.trim_end_matches('\\').to_lowercase()
}

/// Resolves exact paths to records. Paths are compared case-insensitively,
/// without trailing separators.
pub fn resolve_paths(stores: &[StoreRef], paths: &[String], dirs_only: bool) -> Vec<Option<(usize, u32)>> {
    let mut out = vec![None; paths.len()];
    let targets: Vec<String> = paths.iter().map(|p| fold_path(p)).collect();
    for sr in stores {
        let (root_name, len) = {
            let st = sr.store.read().unwrap();
            (fold_path(st.name(st.root)), st.recs.len())
        };
        let mine: Vec<usize> = (0..targets.len())
            .filter(|&i| out[i].is_none())
            .filter(|&i| targets[i] == root_name || targets[i].starts_with(&format!("{root_name}\\")))
            .collect();
        if mine.is_empty() {
            continue;
        }
        let mut wanted: HashMap<String, Vec<usize>> = HashMap::new();
        let mut lengths = [false; 1024];
        for &i in &mine {
            if targets[i] == root_name {
                out[i] = Some((sr.slot, sr.store.read().unwrap().root));
                continue;
            }
            let last = targets[i].rsplit('\\').next().unwrap_or("").to_string();
            if last.len() < lengths.len() {
                lengths[last.len()] = true;
            }
            wanted.entry(last).or_default().push(i);
        }
        if wanted.is_empty() {
            continue;
        }
        let mut path = String::new();
        let mut start = 0;
        while start < len {
            let st = sr.store.read().unwrap();
            let end = (start + CHUNK).min(st.recs.len());
            for r in start as u32..end as u32 {
                let rec = &st.recs[r as usize];
                if !rec.live() || (dirs_only && !rec.is_dir()) {
                    continue;
                }
                let name = st.name(r);
                let folded = if name.is_ascii() {
                    if name.len() >= lengths.len() || !lengths[name.len()] {
                        continue;
                    }
                    name.to_ascii_lowercase()
                } else {
                    name.to_lowercase()
                };
                let Some(list) = wanted.get(&folded) else { continue };
                if !st.full_path(r, &mut path) {
                    continue;
                }
                let full = fold_path(&path);
                for &i in list {
                    if out[i].is_none() && targets[i] == full {
                        out[i] = Some((sr.slot, r));
                    }
                }
            }
            start = end;
        }
    }
    out
}

/// Replaces path terms that name a folder by drive-letter prefix (`path:"D:\x\"`)
/// or exactly (`path:wfn:"D:\x"`) with record checks.
fn resolve(node: &mut Node, stores: &[StoreRef]) {
    resolve_parents(node, stores);
    let mut wanted: Vec<(String, bool)> = Vec::new();
    collect(node, &mut wanted);
    if wanted.is_empty() {
        return;
    }
    let mut prefix: Vec<String> = Vec::new();
    let mut exact: Vec<String> = Vec::new();
    for (p, is_prefix) in &wanted {
        if *is_prefix { prefix.push(p.clone()) } else { exact.push(p.clone()) }
    }
    let rp = resolve_paths(stores, &prefix, true);
    let re = resolve_paths(stores, &exact, false);
    let map: HashMap<(String, bool), Option<(usize, u32)>> = prefix
        .iter()
        .zip(rp)
        .map(|(p, r)| ((fold_path(p), true), r))
        .chain(exact.iter().zip(re).map(|(p, r)| ((fold_path(p), false), r)))
        .collect();
    replace(node, &map);
}

/// Replaces `parent:` folders with record checks (`Never` if not indexed).
fn resolve_parents(node: &mut Node, stores: &[StoreRef]) {
    fn gather(n: &Node, out: &mut Vec<String>) {
        match n {
            Node::And(v) | Node::Or(v) => v.iter().for_each(|x| gather(x, out)),
            Node::Not(x) => gather(x, out),
            Node::Parent(p) => out.push(p.clone()),
            _ => {}
        }
    }
    fn put(n: &mut Node, map: &HashMap<String, Option<(usize, u32)>>) {
        match n {
            Node::And(v) | Node::Or(v) => v.iter_mut().for_each(|x| put(x, map)),
            Node::Not(x) => put(x, map),
            Node::Parent(p) => {
                *n = match map.get(&fold_path(p)) {
                    Some(Some((slot, rec))) => Node::Children { slot: *slot, rec: *rec },
                    _ => Node::Never,
                }
            }
            _ => {}
        }
    }
    let mut paths = Vec::new();
    gather(node, &mut paths);
    if paths.is_empty() {
        return;
    }
    let found = resolve_paths(stores, &paths, true);
    let map: HashMap<String, Option<(usize, u32)>> = paths.iter().map(|p| fold_path(p)).zip(found).collect();
    put(node, &map);
}

fn path_term(t: &Text) -> Option<(String, bool)> {
    if !t.on_path || t.case {
        return None;
    }
    let raw = &t.raw;
    let b = raw.as_bytes();
    let drive = b.len() >= 3 && b[0].is_ascii_alphabetic() && b[1] == b':' && b[2] == b'\\';
    if !drive || raw.contains('*') || raw.contains('?') {
        return None;
    }
    match t.m {
        Matcher::Sub(_) if raw.ends_with('\\') => Some((raw.trim_end_matches('\\').to_string(), true)),
        Matcher::Exact(_) => Some((raw.clone(), false)),
        _ => None,
    }
}

fn collect(node: &Node, out: &mut Vec<(String, bool)>) {
    match node {
        Node::And(v) | Node::Or(v) => v.iter().for_each(|x| collect(x, out)),
        Node::Not(x) => collect(x, out),
        Node::Text(t) => {
            if let Some(p) = path_term(t) {
                out.push(p);
            }
        }
        _ => {}
    }
}

fn replace(node: &mut Node, map: &HashMap<(String, bool), Option<(usize, u32)>>) {
    match node {
        Node::And(v) | Node::Or(v) => v.iter_mut().for_each(|x| replace(x, map)),
        Node::Not(x) => replace(x, map),
        Node::Text(t) => {
            if let Some((p, is_prefix)) = path_term(t) {
                if let Some(found) = map.get(&(fold_path(&p), is_prefix)) {
                    *node = match (found, is_prefix) {
                        (Some((slot, rec)), true) => Node::Subtree { slot: *slot, rec: *rec },
                        (Some((slot, rec)), false) => Node::Exact { slot: *slot, rec: *rec },
                        // No such indexed folder or entry: nothing can match.
                        (None, _) => Node::Never,
                    };
                }
            }
        }
        _ => {}
    }
}

// ---------------------------------------------------------------- execution

struct Top {
    num: u64,
    text: String,
    slot: u32,
    r: u32,
    desc: bool,
}

impl Top {
    fn key_cmp(&self, num: u64, text: &str, slot: u32, r: u32) -> Ordering {
        let c = (self.num, self.text.as_str(), self.slot, self.r).cmp(&(num, text, slot, r));
        if self.desc { c.reverse() } else { c }
    }
}
impl PartialEq for Top {
    fn eq(&self, o: &Self) -> bool {
        self.cmp(o) == Ordering::Equal
    }
}
impl Eq for Top {}
impl PartialOrd for Top {
    fn partial_cmp(&self, o: &Self) -> Option<Ordering> {
        Some(self.cmp(o))
    }
}
impl Ord for Top {
    /// "Greater" is worse, so the max-heap keeps the worst kept entry on top.
    fn cmp(&self, o: &Self) -> Ordering {
        self.key_cmp(o.num, &o.text, o.slot, o.r)
    }
}

struct Worker {
    total: u64,
    heap: BinaryHeap<Top>,
    unknown: HashMap<(usize, u32), u32>,
}

fn fold_into(out: &mut String, s: &str) {
    out.clear();
    if s.is_ascii() {
        out.push_str(s);
        out.make_ascii_lowercase();
    } else {
        out.push_str(&s.to_lowercase());
    }
}

fn worker_threads(jobs: usize) -> usize {
    let cores = std::thread::available_parallelism().map(|n| n.get()).unwrap_or(4);
    cores.min(8).min(jobs).max(1)
}

/// Runs a query. Every match is returned with `visible` telling whether the
/// caller may list its parent folder; `visible_only` drops the others before
/// counting and paging. Security IDs not yet evaluated for the caller make the
/// outcome `NeedAccess`; evaluate them and run the query again.
pub fn query(stores: &[StoreRef], req: &QueryRequest, access: &dyn Access) -> Result<Outcome<QueryResult>, String> {
    let mut node = compile(req)?;
    resolve(&mut node, stores);
    if matches!(node, Node::Never) {
        return Ok(Outcome::Done(QueryResult::default()));
    }
    let count = req.count.min(MAX_COUNT) as usize;
    let keep = req.offset as usize + count;
    let desc = req.descending;
    let sort = req.sort;
    let visible_only = req.visible_only;

    let mut jobs: Vec<(usize, usize)> = Vec::new();
    for (i, sr) in stores.iter().enumerate() {
        let len = sr.store.read().unwrap().recs.len();
        let mut start = 0;
        while start < len {
            jobs.push((i, start));
            start += CHUNK;
        }
    }
    let next = AtomicUsize::new(0);
    let threads = worker_threads(jobs.len());
    let node = &node;
    let jobs = &jobs;
    let next = &next;
    let workers: Vec<Worker> = std::thread::scope(|scope| {
        let handles: Vec<_> = (0..threads)
            .map(|_| {
                scope.spawn(move || {
                    let mut w = Worker { total: 0, heap: BinaryHeap::new(), unknown: HashMap::new() };
                    let mut scratch = String::new();
                    loop {
                        let j = next.fetch_add(1, AtomicOrdering::Relaxed);
                        let Some(&(si, start)) = jobs.get(j) else { break };
                        let sr = &stores[si];
                        let st = sr.store.read().unwrap();
                        let end = (start + CHUNK).min(st.recs.len());
                        for r in start as u32..end as u32 {
                            let rec = &st.recs[r as usize];
                            if !rec.live() || r == st.root {
                                continue;
                            }
                            let mut c = Cand { st: &st, slot: sr.slot, r, rec, name: st.name(r), path: None };
                            if !eval(node, &mut c) {
                                continue;
                            }
                            if visible_only {
                                let psec = st.recs.get(rec.parent as usize).map(|p| p.sec).unwrap_or(0);
                                match access.listable(sr.slot, psec) {
                                    Some(true) => {}
                                    Some(false) => continue,
                                    None => {
                                        w.unknown.entry((sr.slot, psec)).or_insert(rec.parent);
                                        continue;
                                    }
                                }
                            }
                            w.total += 1;
                            if keep == 0 {
                                continue;
                            }
                            let (num, text): (u64, &str) = match sort {
                                SortKey::Name => {
                                    fold_into(&mut scratch, c.name);
                                    (0, scratch.as_str())
                                }
                                SortKey::Path => {
                                    let p = c.path().to_string();
                                    fold_into(&mut scratch, &p);
                                    (0, scratch.as_str())
                                }
                                SortKey::Extension => {
                                    let mut s = String::new();
                                    fold_into(&mut s, extension(c.name));
                                    s.push('\u{0}');
                                    let mut n = String::new();
                                    fold_into(&mut n, c.name);
                                    s.push_str(&n);
                                    scratch = s;
                                    (0, scratch.as_str())
                                }
                                SortKey::Size => (rec.size, ""),
                                SortKey::Created => (rec.ctime, ""),
                                SortKey::Modified => (rec.mtime, ""),
                            };
                            let slot = sr.slot as u32;
                            if w.heap.len() < keep {
                                w.heap.push(Top { num, text: text.to_string(), slot, r, desc });
                            } else if w.heap.peek().unwrap().key_cmp(num, text, slot, r) == Ordering::Greater {
                                w.heap.pop();
                                w.heap.push(Top { num, text: text.to_string(), slot, r, desc });
                            }
                        }
                    }
                    w
                })
            })
            .collect();
        handles.into_iter().map(|h| h.join().unwrap()).collect()
    });

    let mut unknown: HashMap<(usize, u32), u32> = HashMap::new();
    let mut total = 0;
    let mut all: Vec<Top> = Vec::new();
    for w in workers {
        total += w.total;
        for (k, v) in w.unknown {
            unknown.entry(k).or_insert(v);
        }
        all.extend(w.heap.into_vec());
    }
    if !unknown.is_empty() {
        return Ok(Outcome::NeedAccess(unknown.into_iter().map(|((slot, sec), dir)| Unknown { slot, sec, dir }).collect()));
    }
    all.sort();
    let page: Vec<&Top> = all.iter().skip(req.offset as usize).take(count).collect();

    // Build the page under one short read lock per store.
    let by_slot: HashMap<usize, &StoreRef> = stores.iter().map(|s| (s.slot, s)).collect();
    let mut hits: Vec<Option<Hit>> = vec![None; page.len()];
    let mut path = String::new();
    let mut unknown: HashMap<(usize, u32), u32> = HashMap::new();
    for slot in page.iter().map(|t| t.slot as usize).collect::<HashSet<_>>() {
        let Some(sr) = by_slot.get(&slot) else { continue };
        let st = sr.store.read().unwrap();
        for (i, t) in page.iter().enumerate().filter(|(_, t)| t.slot as usize == slot) {
            let Some(rec) = st.get(t.r) else { continue };
            if !st.full_path(t.r, &mut path) {
                continue;
            }
            let visible = if t.r == st.root {
                true
            } else {
                let psec = st.recs.get(rec.parent as usize).map(|p| p.sec).unwrap_or(0);
                access.listable(slot, psec).unwrap_or_else(|| {
                    unknown.entry((slot, psec)).or_insert(rec.parent);
                    false
                })
            };
            hits[i] = Some(Hit {
                visible,
                path: path.clone(),
                folder: rec.is_dir(),
                size: rec.size as i64,
                modified: rec.mtime,
                created: rec.ctime,
                attributes: rec.attrs | if rec.is_dir() { ATTR_DIRECTORY } else { 0 },
            });
        }
    }
    if !unknown.is_empty() {
        return Ok(Outcome::NeedAccess(unknown.into_iter().map(|((slot, sec), dir)| Unknown { slot, sec, dir }).collect()));
    }
    Ok(Outcome::Done(QueryResult { total, hits: hits.into_iter().flatten().collect() }))
}

// ---------------------------------------------------------------- folder sizes

/// Recursive sizes of exact folder paths, counting everything indexed (the
/// administrator view, for disk analysis). `None` for a path that is not an
/// indexed folder.
pub fn folder_sizes(stores: &[StoreRef], paths: &[String]) -> Vec<Option<u64>> {
    resolve_paths(stores, paths, true)
        .into_iter()
        .map(|f| {
            let (slot, r) = f?;
            let sr = stores.iter().find(|s| s.slot == slot)?;
            let st = sr.store.read().unwrap();
            let rec = st.get(r)?;
            rec.is_dir().then_some(rec.size)
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::store::Entry;

    struct Fake {
        denied: Vec<u32>,
        known: bool,
    }
    impl Access for Fake {
        fn principal(&self) -> &str {
            "S-1-5-21-test"
        }
        fn listable(&self, _slot: usize, sec: u32) -> Option<bool> {
            if !self.known && sec == 99 {
                return None;
            }
            Some(!self.denied.contains(&sec))
        }
    }
    fn open() -> Fake {
        Fake { denied: vec![], known: true }
    }

    fn e(parent: u32, name: &str, size: u64, dir: bool, mtime: u64) -> Entry {
        Entry { parent, name: name.into(), size, mtime, ctime: mtime / 2, attrs: if dir { 0x10 } else { 0x20 }, sec: 1 }
    }

    /// C:\Projects\Pathweft\{Search\EverythingSearchClient.cs, PaneSearch.cs, notes.TXT},
    /// C:\Projects\Other\readme.md, C:\Windows\notepad.exe, C:\Data Ä\Ölfile.txt
    fn sample() -> Vec<StoreRef> {
        let mut s = Store::new(5, "C:", 64);
        s.set(20, Some(&e(5, "Projects", 0, true, 10)));
        s.set(21, Some(&e(20, "Pathweft", 0, true, 11)));
        s.set(22, Some(&e(21, "Search", 0, true, 12)));
        s.set(30, Some(&e(22, "EverythingSearchClient.cs", 300, false, 30)));
        s.set(31, Some(&e(21, "PaneSearch.cs", 200, false, 31)));
        s.set(32, Some(&e(21, "notes.TXT", 50, false, 32)));
        s.set(23, Some(&e(20, "Other", 0, true, 13)));
        s.set(33, Some(&e(23, "readme.md", 10, false, 33)));
        s.set(24, Some(&e(5, "Windows", 0, true, 14)));
        s.set(34, Some(&e(24, "notepad.exe", 4000, false, 34)));
        s.set(25, Some(&e(5, "Data Ä", 0, true, 15)));
        s.set(35, Some(&e(25, "Ölfile.txt", 1, false, 35)));
        vec![StoreRef { slot: 0, store: Arc::new(RwLock::new(s)) }]
    }

    fn run(stores: &[StoreRef], q: &str) -> Vec<String> {
        run_req(stores, QueryRequest { query: q.into(), count: 100, sort: SortKey::Path, ..Default::default() })
    }
    fn run_req(stores: &[StoreRef], req: QueryRequest) -> Vec<String> {
        match query(stores, &req, &open()).unwrap() {
            Outcome::Done(r) => r.hits.into_iter().map(|h| h.path).collect(),
            Outcome::NeedAccess(_) => panic!("need access"),
        }
    }

    #[test]
    fn substrings_wildcards_and_case() {
        let s = sample();
        assert_eq!(run(&s, "search"), vec![
            r"C:\Projects\Pathweft\PaneSearch.cs",
            r"C:\Projects\Pathweft\Search",
            r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs",
        ]);
        assert_eq!(run(&s, "*.cs").len(), 2);
        assert_eq!(run(&s, "*.txt").len(), 2);
        assert_eq!(run(&s, "case:*.txt"), vec![r"C:\Data Ä\Ölfile.txt"]);
        assert_eq!(run(&s, "öl"), vec![r"C:\Data Ä\Ölfile.txt"]);
        assert_eq!(run(&s, "pane?earch.cs"), vec![r"C:\Projects\Pathweft\PaneSearch.cs"]);
        assert_eq!(run(&s, "*Ä*"), vec![r"C:\Data Ä"]);
    }

    #[test]
    fn operators_groups_and_functions() {
        let s = sample();
        assert_eq!(run(&s, "file: <*.md | *.exe>"), vec![r"C:\Projects\Other\readme.md", r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, "*.cs !pane"), vec![r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs"]);
        assert_eq!(run(&s, "folder: p"), vec![r"C:\Projects", r"C:\Projects\Pathweft"]);
        assert_eq!(run(&s, "ext:md;exe"), vec![r"C:\Projects\Other\readme.md", r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, "size:>1kb"), vec![r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, "<size:>1kb>"), vec![r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, "size:10..60"), vec![r"C:\Projects\Other\readme.md", r"C:\Projects\Pathweft\notes.TXT"]);
        assert_eq!(run(&s, "ww:search"), vec![r"C:\Projects\Pathweft\Search"]);
        assert_eq!(run(&s, "wfn:readme.md"), vec![r"C:\Projects\Other\readme.md"]);
        assert_eq!(run(&s, r#"regex:"^note.*\.(exe|txt)$""#),vec![r"C:\Projects\Pathweft\notes.TXT", r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, "a|"), run(&s, "a"));
        assert!(compile(&QueryRequest { query: "dupe:".into(), ..Default::default() }).unwrap_err().contains("dupe:"));
        assert!(compile(&QueryRequest { query: "dm:someday".into(), ..Default::default() }).unwrap_err().contains("someday"));
        assert!(compile(&QueryRequest { query: "<abc".into(), ..Default::default() }).is_err());
        assert!(compile(&QueryRequest { query: "\"abc".into(), ..Default::default() }).is_err());
    }

    #[test]
    fn date_words_ranges_and_operators() {
        let d = |y, m, dd| days_from_civil(y, m, dd);
        let today = d(2026, 11, 19); // a Thursday
        for day in [d(1970, 1, 1), d(2000, 2, 29), today, d(2100, 3, 1)] {
            let (y, m, dd) = civil_from_days(day);
            assert_eq!(days_from_civil(y, m, dd), day);
        }
        assert_eq!(d(1970, 1, 1), 0);
        let p = |a: &str| parse_date(a, today);
        assert_eq!(p("today"), Ok((today, today + 1)));
        assert_eq!(p("Yesterday"), Ok((today - 1, today)));
        assert_eq!(p("thisweek"), Ok((d(2026, 11, 16), d(2026, 11, 23))), "weeks start on Monday");
        assert_eq!(p("lastweek"), Ok((d(2026, 11, 9), d(2026, 11, 16))));
        assert_eq!(p("thismonth"), Ok((d(2026, 11, 1), d(2026, 12, 1))));
        assert_eq!(p("lastmonth"), Ok((d(2026, 10, 1), d(2026, 11, 1))));
        assert_eq!(parse_date("lastmonth", d(2026, 1, 5)), Ok((d(2025, 12, 1), d(2026, 1, 1))));
        assert_eq!(parse_date("thismonth", d(2026, 12, 31)), Ok((d(2026, 12, 1), d(2027, 1, 1))));
        assert_eq!(p("thisyear"), Ok((d(2026, 1, 1), d(2027, 1, 1))));
        assert_eq!(p("lastyear"), Ok((d(2025, 1, 1), d(2026, 1, 1))));
        assert_eq!(p("2024"), Ok((d(2024, 1, 1), d(2025, 1, 1))));
        assert_eq!(p("2024-02"), Ok((d(2024, 2, 1), d(2024, 3, 1))));
        assert_eq!(p("2024-2-29"), Ok((d(2024, 2, 29), d(2024, 3, 1))));
        assert_eq!(p("2024/02/29"), p("2024-02-29"));
        assert_eq!(p("2024-01-10..2024-01-12"), Ok((d(2024, 1, 10), d(2024, 1, 13))), "both ends inclusive");
        assert_eq!(p("2023..lastmonth"), Ok((d(2023, 1, 1), d(2026, 11, 1))));
        assert_eq!(p(">2026-11-01"), Ok((d(2026, 11, 2), i64::MAX)));
        assert_eq!(p(">=2026-11"), Ok((d(2026, 11, 1), i64::MAX)));
        assert_eq!(p("<2026"), Ok((i64::MIN, d(2026, 1, 1))));
        assert_eq!(p("<=yesterday"), Ok((i64::MIN, today)));
        assert_eq!(p("=today"), p("today"));
        for bad in ["", "someday", "2025-02-29", "2026-13", "2026-00-10", "26-01-01", "2026-1-1-1", ">", "..2026", "2026-11-19x"] {
            assert!(p(bad).is_err(), "{bad}");
        }
        assert_eq!(local_midnight(i64::MIN), 1);
        assert_eq!(local_midnight(i64::MAX), u64::MAX);
        assert!(local_midnight(d(2026, 11, 20)) > local_midnight(d(2026, 11, 19)));
    }

    #[test]
    fn date_filters_on_modified_and_created_times() {
        let d = |y, m, dd| days_from_civil(y, m, dd);
        let at = |day: i64, hours: u64| local_midnight(day) + hours * 3600 * 10_000_000;
        let mut s = Store::new(5, "C:", 64);
        let mut f = |r: u32, name: &str, mtime: u64, ctime: u64| {
            s.set(r, Some(&Entry { parent: 5, name: name.into(), size: 1, mtime, ctime, attrs: 0x20, sec: 1 }));
        };
        f(20, "early.txt", at(d(2026, 11, 18), 0), at(d(2024, 3, 1), 12));
        f(21, "late.txt", at(d(2026, 11, 18), 23), at(d(2026, 11, 18), 1));
        f(22, "next.txt", at(d(2026, 11, 19), 0), at(d(2026, 11, 19), 0));
        f(23, "unknown.txt", 0, 0);
        let s = vec![StoreRef { slot: 0, store: Arc::new(RwLock::new(s)) }];
        assert_eq!(run(&s, "dm:2026-11-18"), vec![r"C:\early.txt", r"C:\late.txt"]);
        assert_eq!(run(&s, "datemodified:>=2026-11-19"), vec![r"C:\next.txt"]);
        assert_eq!(run(&s, "dm:<2026-11-19"), vec![r"C:\early.txt", r"C:\late.txt"], "unknown times never match");
        assert_eq!(run(&s, "dm:2026-11-18..2026-11-19 !late"), vec![r"C:\early.txt", r"C:\next.txt"]);
        assert_eq!(run(&s, "dc:2024"), vec![r"C:\early.txt"]);
        assert_eq!(run(&s, "datecreated:2026-11"), vec![r"C:\late.txt", r"C:\next.txt"]);
        assert_eq!(run(&s, "<dc:2024 | dm:2026-11-19> *.txt"), vec![r"C:\early.txt", r"C:\next.txt"]);
        assert_eq!(run(&s, "!dm:2026"), vec![r"C:\unknown.txt"]);
    }

    #[test]
    fn parent_matches_direct_children_only() {
        let s = sample();
        assert_eq!(run(&s, r"parent:C:\Projects"), vec![r"C:\Projects\Other", r"C:\Projects\Pathweft"]);
        assert_eq!(run(&s, r#"parent:"c:\projects\pathweft\" *.cs"#), vec![r"C:\Projects\Pathweft\PaneSearch.cs"]);
        assert_eq!(run(&s, r"folder: parent:C:\"), vec![r"C:\Data Ä", r"C:\Projects", r"C:\Windows"]);
        assert_eq!(run(&s, r"parent:C:"), run(&s, r"parent:C:\"));
        assert_eq!(run(&s, r"parent:C:\Missing"), Vec::<String>::new());
        assert_eq!(run(&s, r"parent:C:\Projects\Pathweft\notes.TXT"), Vec::<String>::new(), "a file has no children");
        assert_eq!(run(&s, r"<parent:C:\Windows | parent:C:\Projects\Other> file:"), vec![r"C:\Projects\Other\readme.md", r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, r"file: *.cs !parent:C:\Projects\Pathweft"), vec![r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs"]);
        assert!(compile(&QueryRequest { query: "parent:".into(), ..Default::default() }).is_err());
    }

    #[test]
    fn pathweft_compositions() {
        let s = sample();
        // IndexedQuery.Compose(query, folder) with Path, Case and WholeWord modifiers.
        assert_eq!(run(&s, r#"<*.cs> path:"C:\Projects\Pathweft\""#).len(), 2);
        assert_eq!(run(&s, r#"<*.cs> path:"c:\projects\pathweft\search\""#), vec![r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs"]);
        assert_eq!(run(&s, r#"<readme> path:"C:\Projects\Pathweft\""#), Vec::<String>::new());
        assert_eq!(run(&s, r#"path:<other\readme>"#), vec![r"C:\Projects\Other\readme.md"]);
        assert_eq!(run(&s, r#"case:ww:path:<Search>"#), vec![
            r"C:\Projects\Pathweft\Search",
            r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs",
        ]);
        assert_eq!(run(&s, r#"regex:"^Pane.*""#), vec![r"C:\Projects\Pathweft\PaneSearch.cs"]);
        // EverythingSearchClient.FolderExpression.
        assert_eq!(run(&s, r#"folder: <path:wfn:"C:\Projects\Pathweft" | path:wfn:"C:\Windows">"#), vec![
            r"C:\Projects\Pathweft",
            r"C:\Windows",
        ]);
        // MCP cleanup: file: path:"root\" pattern
        assert_eq!(run(&s, r#"file: path:"C:\Projects\" *.md"#), vec![r"C:\Projects\Other\readme.md"]);
        // Request switches.
        let req = QueryRequest { query: "pathweft".into(), count: 10, match_path: true, sort: SortKey::Path, ..Default::default() };
        assert_eq!(run_req(&s, req).len(), 5);
        let req = QueryRequest { query: "regex:\"^n.*e$\"".into(), count: 10, regex: true, ..Default::default() };
        assert_eq!(run_req(&s, req), vec![r"C:\Windows\notepad.exe"]);
        let req = QueryRequest { query: "^n.*d\\.exe$".into(), count: 10, regex: true, ..Default::default() };
        assert_eq!(run_req(&s, req), vec![r"C:\Windows\notepad.exe"]);
    }

    #[test]
    fn sorting_paging_and_totals() {
        let s = sample();
        let req = |sort, descending, offset, count| QueryRequest { query: "file:".into(), sort, descending, offset, count, ..Default::default() };
        let names = |r: QueryRequest| -> Vec<String> {
            run_req(&s, r).into_iter().map(|p| p.rsplit('\\').next().unwrap().to_string()).collect()
        };
        assert_eq!(names(req(SortKey::Size, true, 0, 2)), vec!["notepad.exe", "EverythingSearchClient.cs"]);
        assert_eq!(names(req(SortKey::Size, false, 0, 2)), vec!["Ölfile.txt", "readme.md"]);
        assert_eq!(names(req(SortKey::Name, false, 0, 3)), vec!["EverythingSearchClient.cs", "notepad.exe", "notes.TXT"]);
        assert_eq!(names(req(SortKey::Name, false, 2, 2)), vec!["notes.TXT", "PaneSearch.cs"]);
        assert_eq!(names(req(SortKey::Modified, true, 0, 1)), vec!["Ölfile.txt"]);
        assert_eq!(names(req(SortKey::Extension, false, 0, 2)), vec!["EverythingSearchClient.cs", "PaneSearch.cs"]);
        match query(&s, &req(SortKey::Name, false, 0, 0), &open()).unwrap() {
            Outcome::Done(r) => {
                assert_eq!(r.total, 6);
                assert!(r.hits.is_empty());
            }
            _ => panic!(),
        }
    }

    #[test]
    fn hits_carry_metadata_and_folder_sizes() {
        let s = sample();
        let req = QueryRequest { query: r#"path:wfn:"C:\Projects""#.into(), count: 5, ..Default::default() };
        let Outcome::Done(r) = query(&s, &req, &open()).unwrap() else { panic!() };
        assert_eq!(r.hits.len(), 1);
        let h = &r.hits[0];
        assert!(h.folder);
        assert_eq!(h.size, 560);
        assert_eq!(h.modified, 10);
        assert_eq!(h.created, 5);
        assert_eq!(h.attributes & 0x10, 0x10);
        let paths = vec![r"C:\Projects".to_string(), r"c:\projects\pathweft\".to_string(), r"C:\Nope".into(), r"C:\".into(), r"C:\Windows\notepad.exe".into()];
        let sizes = folder_sizes(&s, &paths);
        assert_eq!(sizes, vec![Some(560), Some(550), None, Some(4561), None]);
        assert!(h.visible);
    }

    #[test]
    fn visibility_flags_unlistable_children_and_sizes_stay_full() {
        let s = sample();
        {
            let mut st = s[0].store.write().unwrap();
            // C:\Projects\Pathweft gets security ID 2, which the caller cannot list.
            let mut x = e(20, "Pathweft", 0, true, 11);
            x.sec = 2;
            st.set(21, Some(&x));
        }
        let fake = Fake { denied: vec![2], known: true };
        // Default: both matches; PaneSearch.cs (parent Pathweft) is flagged not visible,
        // Search\EverythingSearchClient.cs is visible (its parent Search is listable).
        let req = QueryRequest { query: "*.cs".into(), count: 10, sort: SortKey::Path, ..Default::default() };
        let Outcome::Done(r) = query(&s, &req, &fake).unwrap() else { panic!() };
        assert_eq!(r.total, 2);
        let flags: Vec<(&str, bool)> = r.hits.iter().map(|h| (h.path.as_str(), h.visible)).collect();
        assert_eq!(
            flags,
            vec![(r"C:\Projects\Pathweft\PaneSearch.cs", false), (r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs", true)]
        );
        // visible_only filters before counting.
        let req = QueryRequest { visible_only: true, ..req };
        let Outcome::Done(r) = query(&s, &req, &fake).unwrap() else { panic!() };
        assert_eq!(r.total, 1);
        assert_eq!(r.hits[0].path, r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs");
        // Folder sizes are the full (administrator) view.
        let paths = vec![r"C:\Projects".to_string(), r"C:\Projects\Pathweft".into()];
        assert_eq!(folder_sizes(&s, &paths), vec![Some(560), Some(550)]);
        // Unknown security IDs are reported with a sample directory.
        {
            let mut st = s[0].store.write().unwrap();
            let mut x = e(20, "Other", 0, true, 13);
            x.sec = 99;
            st.set(23, Some(&x));
        }
        let fake = Fake { denied: vec![], known: false };
        let req = QueryRequest { query: "readme".into(), count: 10, ..Default::default() };
        let other = s[0].store.read().unwrap().index_of(23).unwrap();
        match query(&s, &req, &fake).unwrap() {
            Outcome::NeedAccess(u) => assert_eq!(u, vec![Unknown { slot: 0, sec: 99, dir: other }]),
            _ => panic!("expected NeedAccess"),
        }
    }

    #[test]
    fn glob_and_find_edge_cases() {
        assert!(glob(b"*", b"", true));
        assert!(glob(b"a*b*c", b"axxbyyc", true));
        assert!(!glob(b"a*b*c", b"axxbyy", true));
        assert!(glob(b"?", "ä".as_bytes(), false));
        assert!(!glob(b"??", "ä".as_bytes(), false));
        assert!(contains(b"HelloWorld", b"oworld", true));
        assert!(!contains(b"Hello", b"hellox", true));
        assert!(word(b"my search.cs", b"search", true));
        assert!(!word(b"PaneSearch.cs", b"search", true));
    }
}
