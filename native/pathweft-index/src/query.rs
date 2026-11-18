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
use std::sync::{Arc, Mutex, RwLock};

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
}

const FUNCTIONS: &str = "path:, nopath:, case:, nocase:, ww:, noww:, wfn:, nowfn:, regex:, noregex:, file:, folder:, ext:, size:";

struct Parser {
    s: Vec<char>,
    i: usize,
    depth: usize,
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
                    let arg = self.read_text(false)?;
                    return Ok(wrap(Node::Size(parse_size(&arg)?), m));
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
    let mut p = Parser { s: req.query.chars().collect(), i: 0, depth: 0 };
    let node = p.parse_and(base)?;
    if p.i < p.s.len() {
        return Err("Unbalanced Everything search groups.".into());
    }
    Ok(node)
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

/// Runs a query. Entries whose parent directory's security ID is not yet
/// evaluated for the caller make the outcome `NeedAccess`; evaluate them and
/// run the query again.
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
                            let psec = st.recs.get(rec.parent as usize).map(|p| p.sec).unwrap_or(0);
                            match access.listable(sr.slot, psec) {
                                Some(true) => {}
                                Some(false) => continue,
                                None => {
                                    w.unknown.entry((sr.slot, psec)).or_insert(rec.parent);
                                    continue;
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
    let mut folders: HashMap<usize, Vec<(usize, u32)>> = HashMap::new();
    let mut path = String::new();
    for slot in page.iter().map(|t| t.slot as usize).collect::<HashSet<_>>() {
        let Some(sr) = by_slot.get(&slot) else { continue };
        let st = sr.store.read().unwrap();
        for (i, t) in page.iter().enumerate().filter(|(_, t)| t.slot as usize == slot) {
            let Some(rec) = st.get(t.r) else { continue };
            if !st.full_path(t.r, &mut path) {
                continue;
            }
            if rec.is_dir() {
                folders.entry(slot).or_default().push((i, t.r));
            }
            hits[i] = Some(Hit {
                path: path.clone(),
                folder: rec.is_dir(),
                size: rec.size as i64,
                modified: rec.mtime,
                created: rec.ctime,
                attributes: rec.attrs | if rec.is_dir() { ATTR_DIRECTORY } else { 0 },
            });
        }
    }
    // Folder sizes count only what the caller can list.
    for (slot, list) in folders {
        let sr = by_slot[&slot];
        let targets: Vec<u32> = list.iter().map(|&(_, r)| r).collect();
        match visible_sizes(sr, &targets, access, None)? {
            Outcome::Done(sizes) => {
                for ((i, _), size) in list.iter().zip(sizes) {
                    if let Some(h) = hits[*i].as_mut() {
                        h.size = size.map(|s| s as i64).unwrap_or(-1);
                    }
                }
            }
            Outcome::NeedAccess(u) => return Ok(Outcome::NeedAccess(u)),
        }
    }
    Ok(Outcome::Done(QueryResult { total, hits: hits.into_iter().flatten().collect() }))
}

// ---------------------------------------------------------------- folder sizes

/// A directory the caller may not list, with its child directories.
#[derive(Clone, Debug)]
pub struct Restricted {
    pub dir: u32,
    pub children: Vec<u32>,
}

/// Caches restricted-directory lists per store, caller and folder generation.
#[derive(Default)]
pub struct RestrictedCache {
    map: Mutex<HashMap<(usize, String), (u64, Arc<Vec<Restricted>>)>>,
}

impl RestrictedCache {
    pub fn clear(&self) {
        self.map.lock().unwrap().clear();
    }
}

fn restricted(sr: &StoreRef, access: &dyn Access, cache: Option<&RestrictedCache>) -> Outcome<Arc<Vec<Restricted>>> {
    let gen = sr.store.read().unwrap().dir_gen;
    let key = (sr.slot, access.principal().to_string());
    if let Some(c) = cache {
        if let Some((g, list)) = c.map.lock().unwrap().get(&key) {
            if *g == gen {
                return Outcome::Done(list.clone());
            }
        }
    }
    let len = sr.store.read().unwrap().recs.len();
    let mut denied: Vec<u32> = Vec::new();
    let mut unknown: HashMap<u32, u32> = HashMap::new();
    let mut start = 0;
    while start < len {
        let st = sr.store.read().unwrap();
        let end = (start + CHUNK).min(st.recs.len());
        for r in start as u32..end as u32 {
            let rec = &st.recs[r as usize];
            if !rec.live() || !rec.is_dir() {
                continue;
            }
            match access.listable(sr.slot, rec.sec) {
                Some(true) => {}
                Some(false) => denied.push(r),
                None => {
                    unknown.entry(rec.sec).or_insert(r);
                }
            }
        }
        start = end;
    }
    if !unknown.is_empty() {
        return Outcome::NeedAccess(unknown.into_iter().map(|(sec, dir)| Unknown { slot: sr.slot, sec, dir }).collect());
    }
    let mut children: HashMap<u32, Vec<u32>> = denied.iter().map(|&d| (d, Vec::new())).collect();
    if !denied.is_empty() {
        let mut start = 0;
        while start < len {
            let st = sr.store.read().unwrap();
            let end = (start + CHUNK).min(st.recs.len());
            for r in start as u32..end as u32 {
                let rec = &st.recs[r as usize];
                if rec.live() && rec.is_dir() {
                    if let Some(list) = children.get_mut(&rec.parent) {
                        list.push(r);
                    }
                }
            }
            start = end;
        }
    }
    let list: Arc<Vec<Restricted>> =
        Arc::new(denied.into_iter().map(|dir| Restricted { dir, children: children.remove(&dir).unwrap_or_default() }).collect());
    if let Some(c) = cache {
        c.map.lock().unwrap().insert(key, (gen, list.clone()));
    }
    Outcome::Done(list)
}

/// Sizes of the folders `targets` in one store counting only files whose
/// parent directory the caller may list. `None` for a target that is not a
/// live folder or is itself not visible.
pub fn visible_sizes(
    sr: &StoreRef,
    targets: &[u32],
    access: &dyn Access,
    cache: Option<&RestrictedCache>,
) -> Result<Outcome<Vec<Option<u64>>>, String> {
    let list = match restricted(sr, access, cache) {
        Outcome::Done(l) => l,
        Outcome::NeedAccess(u) => return Ok(Outcome::NeedAccess(u)),
    };
    let st = sr.store.read().unwrap();
    let index: HashMap<u32, usize> = targets.iter().enumerate().map(|(i, &t)| (t, i)).collect();
    let mut adjust = vec![0u64; targets.len()];
    for d in list.iter() {
        let Some(rec) = st.get(d.dir) else { continue };
        let child_sum: u64 = d.children.iter().filter_map(|&c| st.get(c)).filter(|c| c.parent == d.dir).map(|c| c.size).sum();
        let direct = rec.size.saturating_sub(child_sum);
        if direct == 0 {
            continue;
        }
        let mut cur = d.dir;
        for _ in 0..1024 {
            if let Some(&i) = index.get(&cur) {
                adjust[i] += direct;
            }
            let Some(c) = st.get(cur) else { break };
            if cur == st.root {
                break;
            }
            cur = c.parent;
        }
    }
    let mut out = Vec::with_capacity(targets.len());
    let mut unknown = Vec::new();
    for (i, &t) in targets.iter().enumerate() {
        let Some(rec) = st.get(t) else {
            out.push(None);
            continue;
        };
        if !rec.is_dir() {
            out.push(None);
            continue;
        }
        let visible = if t == st.root {
            Some(true)
        } else {
            let psec = st.recs.get(rec.parent as usize).map(|p| p.sec).unwrap_or(0);
            let v = access.listable(sr.slot, psec);
            if v.is_none() {
                unknown.push(Unknown { slot: sr.slot, sec: psec, dir: rec.parent });
            }
            v
        };
        out.push(match visible {
            Some(true) => Some(rec.size.saturating_sub(adjust[i])),
            _ => None,
        });
    }
    if !unknown.is_empty() {
        return Ok(Outcome::NeedAccess(unknown));
    }
    Ok(Outcome::Done(out))
}

/// Recursive sizes of exact folder paths, as the caller may see them.
pub fn folder_sizes(
    stores: &[StoreRef],
    paths: &[String],
    access: &dyn Access,
    cache: Option<&RestrictedCache>,
) -> Result<Outcome<Vec<Option<u64>>>, String> {
    let found = resolve_paths(stores, paths, true);
    let mut out = vec![None; paths.len()];
    let mut per_slot: HashMap<usize, Vec<(usize, u32)>> = HashMap::new();
    for (i, f) in found.iter().enumerate() {
        if let Some((slot, rec)) = f {
            per_slot.entry(*slot).or_default().push((i, *rec));
        }
    }
    let mut unknown = Vec::new();
    for (slot, list) in per_slot {
        let Some(sr) = stores.iter().find(|s| s.slot == slot) else { continue };
        let targets: Vec<u32> = list.iter().map(|&(_, r)| r).collect();
        match visible_sizes(sr, &targets, access, cache)? {
            Outcome::Done(sizes) => {
                for ((i, _), s) in list.iter().zip(sizes) {
                    out[*i] = s;
                }
            }
            Outcome::NeedAccess(u) => unknown.extend(u),
        }
    }
    if !unknown.is_empty() {
        return Ok(Outcome::NeedAccess(unknown));
    }
    Ok(Outcome::Done(out))
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
        assert_eq!(run(&s, "size:10..60"), vec![r"C:\Projects\Other\readme.md", r"C:\Projects\Pathweft\notes.TXT"]);
        assert_eq!(run(&s, "ww:search"), vec![r"C:\Projects\Pathweft\Search"]);
        assert_eq!(run(&s, "wfn:readme.md"), vec![r"C:\Projects\Other\readme.md"]);
        assert_eq!(run(&s, r#"regex:"^note.*\.(exe|txt)$""#),vec![r"C:\Projects\Pathweft\notes.TXT", r"C:\Windows\notepad.exe"]);
        assert_eq!(run(&s, "a|"), run(&s, "a"));
        assert!(compile(&QueryRequest { query: "dm:today".into(), ..Default::default() }).unwrap_err().contains("dm:"));
        assert!(compile(&QueryRequest { query: "<abc".into(), ..Default::default() }).is_err());
        assert!(compile(&QueryRequest { query: "\"abc".into(), ..Default::default() }).is_err());
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
        let req = QueryRequest { query: "^n.*e\\.exe$".into(), count: 10, regex: true, ..Default::default() };
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
        let Outcome::Done(sizes) = folder_sizes(&s, &paths, &open(), None).unwrap() else { panic!() };
        assert_eq!(sizes, vec![Some(560), Some(550), None, Some(4561), None]);
    }

    #[test]
    fn visibility_hides_unlistable_children_and_their_sizes() {
        let s = sample();
        {
            let mut st = s[0].store.write().unwrap();
            // C:\Projects\Pathweft gets security ID 2, which the caller cannot list.
            let mut x = e(20, "Pathweft", 0, true, 11);
            x.sec = 2;
            st.set(21, Some(&x));
        }
        let fake = Fake { denied: vec![2], known: true };
        let req = QueryRequest { query: "*.cs".into(), count: 10, ..Default::default() };
        let Outcome::Done(r) = query(&s, &req, &fake).unwrap() else { panic!() };
        // PaneSearch.cs (parent Pathweft) is hidden; Search\EverythingSearchClient.cs stays visible:
        // its parent Search is listable.
        assert_eq!(r.total, 1);
        assert_eq!(r.hits[0].path, r"C:\Projects\Pathweft\Search\EverythingSearchClient.cs");
        let paths = vec![r"C:\Projects".to_string(), r"C:\Projects\Pathweft".into()];
        let Outcome::Done(sizes) = folder_sizes(&s, &paths, &fake, None).unwrap() else { panic!() };
        // Pathweft's direct files (200 + 50) are not counted.
        assert_eq!(sizes, vec![Some(310), Some(300)]);
        // Unknown security IDs are reported with a sample directory.
        {
            let mut st = s[0].store.write().unwrap();
            let mut x = e(20, "Other", 0, true, 13);
            x.sec = 99;
            st.set(23, Some(&x));
        }
        let fake = Fake { denied: vec![], known: false };
        let req = QueryRequest { query: "readme".into(), count: 10, ..Default::default() };
        match query(&s, &req, &fake).unwrap() {
            Outcome::NeedAccess(u) => assert_eq!(u, vec![Unknown { slot: 0, sec: 99, dir: 23 }]),
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
