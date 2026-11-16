//! The island's click on a DSH session row, sent to the notifier plugin, which switches the
//! browser page showing that session (POST /jump on the plugin's port). Unlike `dsh_bridge`,
//! which only reads, the island writes exactly one thing here: the session id. Every attempt is
//! appended to `agent-jump.log` with the request line and the plugin's answer, because "clicking
//! did nothing" cannot be told apart from "the page never heard about it" without that record.

use std::{
    fs::{self, OpenOptions},
    io::Write,
    path::Path,
    time::{Duration, Instant},
};

use serde::Serialize;
use serde_json::Value;

use super::snapshot::current_unix_millis;

/// The plugin's jump endpoint, part of the bridge contract (see analysis/protocol-v1.md).
const JUMP_URL: &str = "http://127.0.0.1:47822/jump";
/// Which page shows which session, so a click on the session already in front says so instead of
/// queueing a jump the page then has to ignore.
const MOUNTED_URL: &str = "http://127.0.0.1:47822/_task-notify/mounted";
/// A loopback post answers at once; a click must not wait longer than this before falling back to
/// bringing the session's own window forward.
const REQUEST_TIMEOUT: Duration = Duration::from_millis(1200);
/// Where the attempts go, next to `agent-focus.log`.
pub(super) const LOG_FILE: &str = "agent-jump.log";
/// Past this the log is cut to its newer half.
const MAX_BYTES: u64 = 64 * 1024;
/// Enough of an answer to recognise it, small enough to keep every line on one row.
const BODY_KEEP: usize = 240;

/// What came of one click. `reason` is the branch taken, stable enough to grep in the log:
/// `already-open`, `delivered`, `queued`, `no-page`, `unreachable`, or the plugin's own error
/// (`unknown-session`, `bad-sid`) and `http-<status>` when the answer was not a JSON document.
#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct JumpOutcome {
    /// A page shows the session now, so the click needs nothing more.
    pub delivered: bool,
    pub reason: String,
    /// The HTTP status, absent when the plugin could not be reached at all.
    pub status: Option<u16>,
    /// The plugin's answer, cut short. For the log and for bug reports, not for the UI.
    pub body: String,
}

/// Asks the plugin to switch the browser page to `session_id`, logging the attempt.
pub async fn jump(session_id: &str, app_dir: Option<&Path>) -> JumpOutcome {
    let started = Instant::now();
    let client = reqwest::Client::builder()
        .timeout(REQUEST_TIMEOUT)
        .redirect(reqwest::redirect::Policy::none())
        .build();
    let client = match client {
        Ok(client) => client,
        Err(error) => {
            return finish(app_dir, session_id, started, JumpOutcome {
                reason: format!("client: {error}"),
                ..JumpOutcome::default()
            })
        }
    };

    if shows(client_mounts(&client).await, session_id) {
        return finish(app_dir, session_id, started, JumpOutcome {
            delivered: true,
            reason: "already-open".to_string(),
            ..JumpOutcome::default()
        });
    }

    let body = serde_json::json!({ "sid": session_id }).to_string();
    let response = client
        .post(JUMP_URL)
        .header(reqwest::header::CONTENT_TYPE, "application/json")
        .body(body)
        .send()
        .await;
    let outcome = match response {
        Ok(response) => {
            let status = response.status().as_u16();
            let answer = response.text().await.unwrap_or_default();
            let (delivered, reason) = classify(status, &answer);
            JumpOutcome {
                delivered,
                reason,
                status: Some(status),
                body: answer,
            }
        }
        Err(error) => JumpOutcome {
            reason: format!("unreachable: {error}"),
            ..JumpOutcome::default()
        },
    };
    finish(app_dir, session_id, started, outcome)
}

/// The mounted list as session ids, read only so `already-open` is not a guess. A plugin that
/// refuses the route is no reason to skip the jump: it is the older build talking.
async fn client_mounts(client: &reqwest::Client) -> Vec<String> {
    let Ok(response) = client.get(MOUNTED_URL).send().await else {
        return Vec::new();
    };
    if !response.status().is_success() {
        return Vec::new();
    }
    let Ok(text) = response.text().await else {
        return Vec::new();
    };
    let Ok(value) = serde_json::from_str::<Value>(&text) else {
        return Vec::new();
    };
    value
        .get("clients")
        .and_then(Value::as_array)
        .map(|clients| {
            clients
                .iter()
                .filter_map(|client| client.get("mountedSessionId").and_then(Value::as_str))
                .map(str::to_string)
                .collect()
        })
        .unwrap_or_default()
}

fn shows(mounts: Vec<String>, session_id: &str) -> bool {
    mounts.iter().any(|mounted| mounted == session_id)
}

/// Whether the answer means the page switched. Kept apart from the request so the branches can be
/// tested without a port: the plugin's document is the only input.
fn classify(status: u16, body: &str) -> (bool, String) {
    let Ok(answer) = serde_json::from_str::<Value>(body) else {
        return (false, format!("http-{status}"));
    };
    if answer.get("ok").and_then(Value::as_bool) != Some(true) {
        let error = answer
            .get("error")
            .and_then(Value::as_str)
            .unwrap_or("refused");
        return (false, error.to_string());
    }
    if answer
        .get("deliveredTo")
        .is_some_and(|target| !target.is_null())
    {
        return (true, "delivered".to_string());
    }
    // Accepted, but no page took it: the plugin keeps it for the next poll (0.13.0) or has no
    // page at all. Either way the island's click has more to do.
    if answer.get("queued").and_then(Value::as_bool) == Some(true) {
        return (false, "queued".to_string());
    }
    (false, "no-page".to_string())
}

/// Appends the attempt to the log, best effort, then hands the outcome back.
fn finish(app_dir: Option<&Path>, session_id: &str, started: Instant, outcome: JumpOutcome) -> JumpOutcome {
    if let Some(app_dir) = app_dir {
        let status = outcome
            .status
            .map_or_else(|| "-".to_string(), |status| status.to_string());
        let line = format!(
            "{} {} | POST /jump sid={} | status={} | {} | {} in {} ms\n",
            current_unix_millis(),
            trim(&outcome.reason),
            session_id,
            status,
            trim(&outcome.body),
            if outcome.delivered {
                "delivered"
            } else {
                "not-delivered"
            },
            started.elapsed().as_millis(),
        );
        append(app_dir, &line);
    }
    outcome
}

/// One line at a time, cut to fit: a log with a runaway body would push the useful attempts out.
fn trim(text: &str) -> String {
    let one_line: String = text.chars().map(|c| if c.is_control() { ' ' } else { c }).collect();
    let mut cut: String = one_line.chars().take(BODY_KEEP).collect();
    if one_line.chars().count() > BODY_KEEP {
        cut.push('…');
    }
    cut.trim().to_string()
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
    fn a_switch_needs_a_page_named() {
        assert_eq!(classify(200, r#"{"ok":true,"seq":4,"deliveredTo":"30468-1"}"#), (true, "delivered".to_string()));
        // Accepted but nobody polled for it: the island still has to act.
        assert_eq!(classify(200, r#"{"ok":true,"queued":true,"deliveredTo":null}"#), (false, "queued".to_string()));
        assert_eq!(classify(200, r#"{"ok":true}"#), (false, "no-page".to_string()));
        assert_eq!(classify(200, r#"{"ok":true,"deliveredTo":null}"#), (false, "no-page".to_string()));
    }

    #[test]
    fn a_refusal_keeps_the_plugins_reason() {
        assert_eq!(classify(200, r#"{"ok":false,"error":"unknown-session"}"#), (false, "unknown-session".to_string()));
        assert_eq!(classify(200, r#"{"ok":false}"#), (false, "refused".to_string()));
        // Not a document at all: say how, do not pretend it worked.
        assert_eq!(classify(404, "Unknown path."), (false, "http-404".to_string()));
        assert_eq!(classify(302, ""), (false, "http-302".to_string()));
    }

    #[test]
    fn only_the_mounted_session_counts_as_open() {
        let mounts = vec!["session-a".to_string(), "session-b".to_string()];
        assert!(shows(mounts.clone(), "session-b"));
        assert!(!shows(mounts, "session-c"));
        assert!(!shows(Vec::new(), "session-a"));
    }

    #[test]
    fn a_log_row_stays_on_one_line() {
        let cut = trim("{\"ok\":false,\"error\":\"unknown-session\",\n\"x\":1}");
        assert!(!cut.contains('\n'));
        assert!(cut.contains("unknown-session"));
        let long = trim(&"a".repeat(BODY_KEEP + 50));
        assert_eq!(long.chars().count(), BODY_KEEP + 1);
        assert!(long.ends_with('…'));
    }
}
