//! Adaptive re-listing of folder roots (SPEC IDX-03).
//!
//! Change notifications do not report everything (a network share does not
//! forward changes made on the server itself), so every folder is also listed
//! again on its own schedule: after half the time since it last changed,
//! clamped between `min` and `max`. A folder that keeps changing is checked
//! every `min`; one that has been quiet for `2 * max` or longer, every `max`.

use std::cmp::Reverse;
use std::collections::{BinaryHeap, HashMap};
use std::time::{SystemTime, UNIX_EPOCH};

/// Seconds between 1601-01-01 (FILETIME) and 1970-01-01.
const FILETIME_EPOCH: u64 = 11_644_473_600;
/// Modified times before 2000 are placeholders (Tresorit reports 1970/1980).
const PLAUSIBLE_FROM: u64 = 946_684_800;

pub fn now() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map_or(0, |d| d.as_secs())
}

/// A FILETIME as Unix seconds, or 0 when it is not a believable change time.
pub fn change_time(filetime: u64, now: u64) -> u64 {
    let t = (filetime / 10_000_000).saturating_sub(FILETIME_EPOCH);
    if (PLAUSIBLE_FROM..=now).contains(&t) { t } else { 0 }
}

#[derive(Clone, Copy)]
struct Plan {
    /// Unix seconds of the last observed change, 0 if unknown.
    last: u64,
    next: u64,
}

pub struct Schedule {
    min: u64,
    max: u64,
    plans: HashMap<u32, Plan>,
    /// (due, key); entries whose `due` no longer matches the plan are stale.
    queue: BinaryHeap<Reverse<(u64, u32)>>,
}

impl Schedule {
    pub fn new(min_secs: u64, max_secs: u64) -> Schedule {
        let max = max_secs.max(60);
        Schedule { min: min_secs.clamp(60, max), max, plans: HashMap::new(), queue: BinaryHeap::new() }
    }

    fn interval(&self, last: u64, now: u64) -> u64 {
        if last == 0 || last > now {
            return self.max;
        }
        ((now - last) / 2).clamp(self.min, self.max)
    }

    fn plan(&mut self, key: u32, last: u64, next: u64) {
        self.plans.insert(key, Plan { last, next });
        self.queue.push(Reverse((next, key)));
    }

    /// A folder found by a scan; `last` is its change time (0: unknown).
    /// First checks are spread over the second half of the interval, so a
    /// fresh scan does not make every folder due at once.
    pub fn add(&mut self, key: u32, last: u64, now: u64) {
        let i = self.interval(last, now);
        let spread = (key as u64).wrapping_mul(2_654_435_761) % (i / 2 + 1);
        self.plan(key, last, now + i / 2 + spread);
    }

    /// The folder's contents changed at `now` (found by a check or a
    /// notification): check it again after `min`.
    pub fn changed(&mut self, key: u32, now: u64) {
        self.plan(key, now, now + self.min);
    }

    /// The folder was checked and nothing had changed.
    pub fn unchanged(&mut self, key: u32, now: u64) {
        let last = self.plans.get(&key).map_or(0, |p| p.last);
        self.plan(key, last, now + self.interval(last, now));
    }

    /// Check the folder as soon as possible (its entry in the parent changed).
    pub fn soon(&mut self, key: u32, now: u64) {
        let last = self.plans.get(&key).map_or(0, |p| p.last);
        self.plan(key, last, now);
    }

    pub fn forget(&mut self, key: u32) {
        self.plans.remove(&key);
    }

    fn drop_stale(&mut self) {
        while let Some(&Reverse((due, key))) = self.queue.peek() {
            if self.plans.get(&key).is_some_and(|p| p.next == due) {
                return;
            }
            self.queue.pop();
        }
    }

    /// Up to `limit` folders due at `now`. Each stays planned at its old time
    /// until the caller reports the check (`changed`, `unchanged`, `forget`).
    pub fn due(&mut self, now: u64, limit: usize) -> Vec<u32> {
        let mut out = Vec::new();
        while out.len() < limit {
            self.drop_stale();
            match self.queue.peek() {
                Some(&Reverse((due, key))) if due <= now => {
                    self.queue.pop();
                    out.push(key);
                }
                _ => break,
            }
        }
        out
    }

    /// When the next folder is due.
    pub fn next_due(&mut self) -> Option<u64> {
        self.drop_stale();
        self.queue.peek().map(|&Reverse((due, _))| due)
    }

    pub fn len(&self) -> usize {
        self.plans.len()
    }

    /// Folders currently checked at least every `secs`.
    pub fn checked_within(&self, secs: u64, now: u64) -> usize {
        self.plans.values().filter(|p| self.interval(p.last, now) <= secs).count()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const MIN: u64 = 15 * 60;
    const MAX: u64 = 6 * 3600;
    const T: u64 = 2_000_000_000;

    #[test]
    fn interval_is_half_the_quiet_time_within_bounds() {
        let s = Schedule::new(MIN, MAX);
        assert_eq!(s.interval(T - 60, T), MIN);
        assert_eq!(s.interval(T - 4 * 3600, T), 2 * 3600);
        assert_eq!(s.interval(T - 30 * 86400, T), MAX);
        assert_eq!(s.interval(0, T), MAX);
    }

    #[test]
    fn a_changing_folder_is_checked_often_and_backs_off_when_quiet() {
        let mut s = Schedule::new(MIN, MAX);
        s.add(7, 0, T);
        assert!(s.due(T + MAX / 2 - 1, 10).is_empty());
        let mut t = T + MAX;
        assert_eq!(s.due(t, 10), vec![7]);
        s.changed(7, t);
        assert_eq!(s.next_due(), Some(t + MIN));
        // Quiet afterwards: 15, 15, 22.5, 33.75 ... minutes, never above MAX.
        let mut gaps = Vec::new();
        for _ in 0..12 {
            let due = s.next_due().unwrap();
            assert_eq!(s.due(due, 10), vec![7]);
            s.unchanged(7, due);
            gaps.push(s.next_due().unwrap() - due);
            t = due;
        }
        assert_eq!(gaps[0], MIN);
        assert!(gaps.windows(2).all(|w| w[1] >= w[0]));
        assert_eq!(*gaps.last().unwrap(), MAX);
        let _ = t;
    }

    #[test]
    fn recent_modified_time_starts_hot_and_unknown_starts_at_max() {
        let mut s = Schedule::new(MIN, MAX);
        s.add(1, T - 600, T); // changed 10 minutes ago
        s.add(2, 0, T); // 1970 placeholder
        assert!(s.next_due().unwrap() <= T + MIN);
        assert_eq!(s.due(T + MIN, 10), vec![1]);
        assert_eq!(s.checked_within(3600, T), 1);
    }

    #[test]
    fn soon_forget_and_limits() {
        let mut s = Schedule::new(MIN, MAX);
        for k in 0..5 {
            s.add(k, 0, T);
        }
        s.soon(3, T);
        assert_eq!(s.due(T, 10), vec![3]);
        s.forget(4);
        let due = s.due(T + MAX, 2);
        assert_eq!(due.len(), 2);
        let rest = s.due(T + MAX, 10);
        assert_eq!(rest.len(), 1, "3 and 4 are no longer queued");
        assert_eq!(s.len(), 4);
    }

    #[test]
    fn filetime_conversion_rejects_placeholders() {
        let ft = |unix: u64| (unix + FILETIME_EPOCH) * 10_000_000;
        assert_eq!(change_time(ft(T - 5), T), T - 5);
        assert_eq!(change_time(ft(0), T), 0);
        assert_eq!(change_time(ft(315_532_800), T), 0); // 1980
        assert_eq!(change_time(ft(T + 100), T), 0); // future
        assert_eq!(change_time(0, T), 0);
    }
}
