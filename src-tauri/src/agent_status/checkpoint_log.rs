//! `agent-sessions.log` in the app data folder: one line whenever a session checkpoint cannot be
//! read, so a session that is missing from the island can be traced on another computer. It holds
//! paths and sizes, never conversation text. The same problem on the same path is noted at most
//! once every five minutes, because the list is read once a second.

use std::{
    collections::HashMap,
    fs::{self, OpenOptions},
    io::Write,
    path::Path,
    sync::{Mutex, PoisonError},
};

pub(super) const FILE_NAME: &str = "agent-sessions.log";
/// Past this the file is cut to its newer half.
const MAX_BYTES: u64 = 64 * 1024;
const THROTTLE_MS: i64 = 5 * 60 * 1000;

static SEEN: Mutex<Option<HashMap<(String, String), i64>>> = Mutex::new(None);

/// Appends one line for `path`, unless that path already reported this problem recently.
pub(super) fn note(app_dir: Option<&Path>, at: i64, path: &Path, problem: &str, bytes: u64) {
    let Some(app_dir) = app_dir else { return };
    let key = (path.to_string_lossy().into_owned(), problem.to_string());
    {
        let mut seen = SEEN.lock().unwrap_or_else(PoisonError::into_inner);
        let entries = seen.get_or_insert_with(HashMap::new);
        if entries
            .get(&key)
            .is_some_and(|last| at.saturating_sub(*last) < THROTTLE_MS)
        {
            return;
        }
        entries.insert(key, at);
    }
    trim(app_dir);
    let line = format!("{at} {problem} | {bytes} bytes | {}\n", path.display());
    if let Ok(mut file) = OpenOptions::new()
        .create(true)
        .append(true)
        .open(app_dir.join(FILE_NAME))
    {
        let _ = file.write_all(line.as_bytes());
    }
}

fn trim(app_dir: &Path) {
    let path = app_dir.join(FILE_NAME);
    if fs::metadata(&path).is_ok_and(|metadata| metadata.len() > MAX_BYTES) {
        if let Ok(text) = fs::read_to_string(&path) {
            let half = text.len() / 2;
            let cut = text[half..]
                .find('\n')
                .map_or(text.len(), |at| half + at + 1);
            let _ = fs::write(&path, &text[cut..]);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("cklog-{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        let _ = fs::create_dir_all(&dir);
        dir
    }

    #[test]
    fn a_note_names_the_path_the_problem_and_the_size() {
        let dir = temp_dir("note");
        note(Some(&dir), 5000, &dir.join("a.json"), "too-large", 552_540);
        let text = fs::read_to_string(dir.join(FILE_NAME)).expect("log");
        assert!(text.contains("too-large | 552540 bytes | "), "{text}");
        assert!(text.contains("a.json"), "{text}");
        assert_eq!(text.lines().count(), 1);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_same_path_reports_once_in_the_throttle_window() {
        let dir = temp_dir("throttle");
        let path = dir.join("b.json");
        note(Some(&dir), 10_000, &path, "unreadable", 0);
        note(Some(&dir), 10_000 + 60_000, &path, "unreadable", 0);
        let text = fs::read_to_string(dir.join(FILE_NAME)).expect("log");
        assert_eq!(text.lines().count(), 1, "{text}");
        note(Some(&dir), 10_000 + THROTTLE_MS, &path, "unreadable", 0);
        let text = fs::read_to_string(dir.join(FILE_NAME)).expect("log");
        assert_eq!(text.lines().count(), 2, "{text}");
        let _ = fs::remove_dir_all(&dir);
    }
}
