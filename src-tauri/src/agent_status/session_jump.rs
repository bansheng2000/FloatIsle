//! `POST /jump` on the event port: something outside the island asks it to show a session, and
//! this module turns that into what the answer says. Only the webview knows the list, so the
//! request is handed over as a Tauri event and the post waits for the webview's report; a jump
//! that nothing answers is reported as unanswered rather than as a success.
//!
//! Every arrival, the row it hit and what came of it are appended to `agent-jump-in.log`, which is
//! the record for "I pressed it and nothing happened": time, session id, source, the row matched.

use std::{
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    sync::{Condvar, Mutex, OnceLock, PoisonError},
    time::{Duration, Instant},
};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, Manager};

use super::snapshot::current_unix_millis;
use crate::island::WINDOW_LABEL;

/// The event the webview listens for. Part of the island's contract (contracts.md).
pub(crate) const EVENT: &str = "island-session-jump";
const LOG_FILE: &str = "agent-jump-in.log";
const MAX_BYTES: u64 = 64 * 1024;
/// Long enough for the island to open, switch to the Agent page and scroll a row into view; short
/// enough that whoever waits on the socket is not left hanging.
const ANSWER_TIMEOUT: Duration = Duration::from_millis(1200);

/// What the webview says it did, and the row it says it hit.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub(crate) struct Report {
    pub(crate) found: bool,
    pub(crate) scrolled: bool,
    /// The webview's word for the branch taken (`ok`, `not-listed`, `no-agent-page`, ...).
    pub(crate) reason: String,
    /// The `data-session-key` of the row, when one was hit.
    pub(crate) row: Option<String>,
}

/// The answer to the post, and what the settings card and the log show.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Answer {
    pub(crate) found: bool,
    pub(crate) scrolled: bool,
    pub(crate) reason: String,
    pub(crate) row: Option<String>,
    pub(crate) ms: u64,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Ask<'a> {
    request_id: u64,
    sid: &'a str,
}

#[derive(Debug, Default)]
struct Slot {
    next: u64,
    waiting: u64,
    report: Option<Report>,
}

static SLOT: OnceLock<(Mutex<Slot>, Condvar)> = OnceLock::new();
static APP: OnceLock<AppHandle> = OnceLock::new();
static APP_DIR: OnceLock<Option<PathBuf>> = OnceLock::new();

fn slot() -> &'static (Mutex<Slot>, Condvar) {
    SLOT.get_or_init(|| {
        (
            Mutex::new(Slot {
                next: 1,
                waiting: 0,
                report: None,
            }),
            Condvar::new(),
        )
    })
}

/// Remembers where to send the event and where to write the log. Called once from `setup`.
pub(crate) fn init(app: &AppHandle) {
    let _ = APP.set(app.clone());
    let _ = APP_DIR.set(app.path().app_data_dir().ok());
}

/// Asks the webview to show `sid` and waits for its report. Nothing listening (a headless test, a
/// window that has not loaded) is an answer of its own, not an error.
pub(crate) fn ask(sid: &str, from: &str) -> Answer {
    let started = Instant::now();
    let Some(app) = APP.get() else {
        return Answer {
            reason: "no-window".to_string(),
            ..Answer::default()
        };
    };
    let (lock, ready) = slot();
    let request_id = {
        let mut slot = lock.lock().unwrap_or_else(PoisonError::into_inner);
        slot.next += 1;
        slot.report = None;
        slot.waiting = slot.next;
        slot.next
    };
    if app
        .emit_to(WINDOW_LABEL, EVENT, Ask { request_id, sid })
        .is_err()
    {
        let mut slot = lock.lock().unwrap_or_else(PoisonError::into_inner);
        slot.waiting = 0;
        return Answer {
            reason: "no-window".to_string(),
            ..Answer::default()
        };
    }
    let deadline = Instant::now() + ANSWER_TIMEOUT;
    let mut slot = lock.lock().unwrap_or_else(PoisonError::into_inner);
    let report = loop {
        if let Some(report) = slot.report.take() {
            break Some(report);
        }
        let left = deadline.saturating_duration_since(Instant::now());
        if left.is_zero() {
            break None;
        }
        let (guard, timeout) = ready
            .wait_timeout(slot, left)
            .unwrap_or_else(PoisonError::into_inner);
        slot = guard;
        if timeout.timed_out() && slot.report.is_none() {
            break None;
        }
    };
    slot.waiting = 0;
    let answer = match report {
        Some(report) => Answer {
            found: report.found,
            scrolled: report.scrolled,
            reason: report.reason,
            row: report.row,
            ms: started.elapsed().as_millis() as u64,
        },
        None => Answer {
            // The island was told and did not say what happened: neither a hit nor a miss.
            found: false,
            reason: "no-answer".to_string(),
            ms: started.elapsed().as_millis() as u64,
            ..Answer::default()
        },
    };
    log(from, sid, &answer);
    answer
}

/// The webview's report for `request_id`; false when that jump is no longer waiting (the post has
/// already been answered, which is what a late report from a slow island looks like).
pub(crate) fn report(request_id: u64, report: Report) -> bool {
    let (lock, ready) = slot();
    let mut slot = lock.lock().unwrap_or_else(PoisonError::into_inner);
    if slot.waiting != request_id {
        return false;
    }
    slot.report = Some(report);
    ready.notify_all();
    true
}

fn log(from: &str, sid: &str, answer: &Answer) {
    let Some(Some(app_dir)) = APP_DIR.get() else {
        return;
    };
    let line = format!(
        "{} from={from} sid={sid} | row={} found={} scrolled={} | {} | {} ms\n",
        current_unix_millis(),
        answer.row.as_deref().unwrap_or("-"),
        yes_no(answer.found),
        yes_no(answer.scrolled),
        answer.reason,
        answer.ms,
    );
    append(app_dir, &line);
}

fn yes_no(value: bool) -> &'static str {
    if value {
        "yes"
    } else {
        "no"
    }
}

fn append(app_dir: &Path, line: &str) {
    let path = app_dir.join(LOG_FILE);
    if fs::metadata(&path).is_ok_and(|metadata| metadata.len() > MAX_BYTES) {
        if let Ok(text) = fs::read_to_string(&path) {
            let half = text.len() / 2;
            let start = text[half..].find('\n').map_or(text.len(), |at| half + at + 1);
            let _ = fs::write(&path, &text[start..]);
        }
    }
    if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(&path) {
        let _ = file.write_all(line.as_bytes());
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_report_reaches_only_the_jump_it_names() {
        let (lock, _) = slot();
        {
            let mut slot = lock.lock().unwrap_or_else(PoisonError::into_inner);
            slot.waiting = 7;
        }
        let late = Report {
            found: true,
            scrolled: true,
            reason: "ok".to_string(),
            row: Some("dsh:session-1".to_string()),
        };
        // A report for another (already answered) jump is refused, not applied to this one.
        assert!(!report(6, late.clone()));
        assert!(report(7, late));
        let mut slot = lock.lock().unwrap_or_else(PoisonError::into_inner);
        assert!(slot.report.is_some());
        slot.report = None;
        slot.waiting = 0;
    }

    #[test]
    fn an_unanswered_jump_is_not_reported_as_a_hit() {
        let default = Answer::default();
        assert!(!default.found);
        assert!(!default.scrolled);
        assert_eq!(default.reason, "");
    }

    #[test]
    fn the_answer_carries_the_row_it_hit() {
        let answer = Answer {
            found: true,
            scrolled: true,
            reason: "ok".to_string(),
            row: Some("dsh:session-1".to_string()),
            ms: 12,
        };
        let json = serde_json::to_string(&answer).unwrap_or_default();
        assert!(json.contains("\"found\":true"));
        assert!(json.contains("\"scrolled\":true"));
        assert!(json.contains("\"row\":\"dsh:session-1\""));
    }
}
