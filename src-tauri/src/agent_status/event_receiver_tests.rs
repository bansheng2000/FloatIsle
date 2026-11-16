//! The listener is tested over a real socket: the plugin's posts are plain HTTP, so a fake
//! client posting the same bodies proves the parse, the counters and the status readback.

use std::{
    io::{Read, Write},
    net::TcpStream,
    time::Duration,
};

use super::{Receiver, PORT};

fn connect(port: u16) -> std::io::Result<TcpStream> {
    let stream = TcpStream::connect(("127.0.0.1", port))?;
    stream.set_read_timeout(Some(Duration::from_secs(2)))?;
    stream.set_write_timeout(Some(Duration::from_secs(2)))?;
    Ok(stream)
}

fn request(port: u16, method: &str, path: &str, body: &str) -> Option<(u16, String)> {
    let stream = connect(port).ok()?;
    let mut stream = stream;
    let message = format!(
        "{method} {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
        body.len()
    );
    stream.write_all(message.as_bytes()).ok()?;
    let mut response = String::new();
    stream.read_to_string(&mut response).ok()?;
    let status = response
        .split_whitespace()
        .nth(1)
        .and_then(|code| code.parse().ok())?;
    let body = response
        .split_once("\r\n\r\n")
        .map(|(_, body)| body.to_string())?;
    Some((status, body))
}

fn post(port: u16, path: &str, body: &str) -> (u16, String) {
    match request(port, "POST", path, body) {
        Some(answer) => answer,
        None => panic!("the listener at {port} did not answer {path}"),
    }
}

fn listener() -> Receiver {
    match Receiver::bind(0) {
        Ok(receiver) => receiver,
        Err(error) => panic!("could not bind a test listener: {error}"),
    }
}

#[test]
fn a_fresh_listener_reports_its_ephemeral_port_and_an_empty_state() {
    let receiver = listener();
    let status = receiver.status();
    assert!(status.listening);
    assert_ne!(status.port, 0);
    assert_eq!(status.presence, None);
    assert_eq!(status.presences, 0);
    assert_eq!(status.events, 0);
    assert_eq!(status.rejected, 0);
}

#[test]
fn a_presence_event_lands_in_the_same_slot_as_a_presence_post() {
    let receiver = listener();
    let body = r#"{"type":"presence","clientId":"page-e2e","visible":true,"focused":true,"mountedSessionId":null,"ts":"2026-11-16T08:03:00.000Z"}"#;
    let (code, answer) = post(receiver.status().port, "/_task-notify/event", body);
    assert_eq!(code, 200);
    assert_eq!(answer, "{\"ok\":true}");

    let status = receiver.status();
    let presence = status.presence.clone().unwrap_or_default();
    assert_eq!(presence.client_id.as_deref(), Some("page-e2e"));
    assert_eq!(presence.mounted_session_id, None);
    assert_eq!(presence.reported_at.as_deref(), Some("2026-11-16T08:03:00.000Z"));
    assert_eq!(status.events, 1);
    assert_eq!(status.presences, 1);
    assert_eq!(status.last_event_type.as_deref(), Some("presence"));

    // A presence event without the mounted key is the one shape the bridge refuses.
    let (code, answer) = post(
        receiver.status().port,
        "/_task-notify/event",
        r#"{"type":"presence","clientId":"page-e2e","visible":true}"#,
    );
    assert_eq!(code, 400);
    assert!(answer.contains("mountedSessionId"));
    assert_eq!(receiver.status().presence, status.presence);
}

#[test]
fn a_presence_post_is_read_into_the_mounted_session() {
    let receiver = listener();
    let body = r#"{"clientId":"19680-1","visible":true,"focused":false,"mountedSessionId":"session-abc","reportedAt":"2026-11-16T07:20:00Z"}"#;
    let (status, answer) = post(receiver.status().port, "/presence", body);
    assert_eq!(status, 200);
    assert_eq!(answer, "{\"ok\":true}");

    let status = receiver.status();
    let presence = status.presence.unwrap_or_default();
    assert_eq!(presence.client_id.as_deref(), Some("19680-1"));
    assert_eq!(presence.visible, Some(true));
    assert_eq!(presence.focused, Some(false));
    assert_eq!(presence.mounted_session_id.as_deref(), Some("session-abc"));
    assert_eq!(presence.reported_at.as_deref(), Some("2026-11-16T07:20:00Z"));
    assert_eq!(status.presences, 1);
    assert!(status.last_presence_at_ms.is_some());
}

#[test]
fn the_deployed_session_id_field_is_still_read_as_the_mounted_session() {
    let receiver = listener();
    let body = r#"{"sessionId":"session-old","visible":true,"lastInputAt":1794723800000}"#;
    let (status, _) = post(receiver.status().port, "/presence", body);
    assert_eq!(status, 200);

    let status = receiver.status();
    let presence = status.presence.unwrap_or_default();
    assert_eq!(presence.mounted_session_id.as_deref(), Some("session-old"));
    assert_eq!(presence.client_id, None);
    assert_eq!(presence.reported_at, None);
}

#[test]
fn a_numbered_ts_is_kept_as_the_reported_time() {
    let receiver = listener();
    let body = r#"{"sessionId":"session-ts","ts":1794723800000}"#;
    let (status, _) = post(receiver.status().port, "/presence", body);
    assert_eq!(status, 200);
    assert_eq!(
        receiver.status().presence.unwrap_or_default().reported_at,
        Some("1794723800000".to_string())
    );
}

#[test]
fn an_event_post_is_counted_with_its_type() {
    let receiver = listener();
    let body = r#"{"type":"turn/start","sid":"session-abc","ws":"D:/work","turn":"t1"}"#;
    let (status, _) = post(receiver.status().port, "/event", body);
    assert_eq!(status, 200);

    let status = receiver.status();
    assert_eq!(status.events, 1);
    assert_eq!(status.last_event_type.as_deref(), Some("turn/start"));
    assert!(status.last_event_at_ms.is_some());
    assert_eq!(status.rejected, 0);
}

#[test]
fn the_status_path_answers_with_the_same_state_as_the_command() {
    let receiver = listener();
    let port = receiver.status().port;
    post(port, "/presence", r#"{"sessionId":"session-abc"}"#);
    let (status, body) = request(port, "GET", "/status", "").unwrap_or_else(|| {
        panic!("no answer from /status");
    });
    assert_eq!(status, 200);
    assert!(body.contains("\"listening\":true"));
    assert!(body.contains("session-abc"));
    assert!(body.contains("\"presences\":1"));
}

#[test]
fn an_unknown_path_is_refused_and_counted() {
    let receiver = listener();
    let port = receiver.status().port;
    let (status, _) = request(port, "GET", "/nope", "").unwrap_or_else(|| {
        panic!("no answer from /nope");
    });
    assert_eq!(status, 404);
    assert_eq!(receiver.status().rejected, 1);
}

#[test]
fn a_body_that_misnames_the_fields_is_refused_rather_than_read_as_empty() {
    // The negative the card's readback needs: guessing a field name must never look like a
    // silent success that leaves the mounted session empty.
    let receiver = listener();
    let port = receiver.status().port;
    let (status, _) = post(port, "/presence", r#"{"mounted_session_id":"session-abc"}"#);
    assert_eq!(status, 400);
    let state = receiver.status();
    assert_eq!(state.presence, None);
    assert_eq!(state.presences, 0);
    assert_eq!(state.rejected, 1);
}

#[test]
fn a_stopped_listener_reports_that_it_is_no_longer_listening() {
    let receiver = listener();
    let port = receiver.status().port;
    receiver.stop();
    let mut attempts = 0;
    while receiver.status().listening && attempts < 100 {
        std::thread::sleep(Duration::from_millis(20));
        attempts += 1;
    }
    assert!(!receiver.status().listening);
    assert!(connect(port).is_err());
}

#[test]
fn the_app_port_is_the_one_the_plugin_posts_to() {
    assert_eq!(PORT, 47_821);
}

#[test]
fn a_missing_mounted_session_key_is_refused_but_null_is_accepted() {
    let receiver = listener();
    let port = receiver.status().port;

    // No `mountedSessionId` key at all: a broken report, not "no session open".
    let (status, _) = post(port, "/presence", r#"{"clientId":"19680-1","visible":true}"#);
    assert_eq!(status, 400);
    assert_eq!(receiver.status().presence, None);
    assert_eq!(receiver.status().rejected, 1);

    // The key is there and says null: that is a page with nothing mounted.
    let body = r#"{"clientId":"19680-1","visible":true,"focused":false,"mountedSessionId":null,"reportedAt":"2026-11-16T07:30:00Z"}"#;
    let (status, _) = post(port, "/presence", body);
    assert_eq!(status, 200);
    let state = receiver.status();
    let presence = state.presence.unwrap_or_default();
    assert_eq!(presence.client_id.as_deref(), Some("19680-1"));
    assert_eq!(presence.mounted_session_id, None);
    assert_eq!(presence.visible, Some(true));
    assert_eq!(state.presences, 1);
}

#[test]
fn the_plugin_path_prefix_reaches_the_same_handlers() {
    let receiver = listener();
    let port = receiver.status().port;
    let body = r#"{"clientId":"19680-2","visible":true,"focused":true,"mountedSessionId":"session-abc","reportedAt":"2026-11-16T07:31:00Z"}"#;
    let (status, _) = post(port, "/_task-notify/presence", body);
    assert_eq!(status, 200);
    let (status, _) = post(port, "/_task-notify/event", r#"{"type":"heartbeat","running":["session-abc"],"ts":1794723800000}"#);
    assert_eq!(status, 200);

    let state = receiver.status();
    assert_eq!(
        state.presence.unwrap_or_default().mounted_session_id.as_deref(),
        Some("session-abc")
    );
    assert_eq!(state.events, 1);
    assert_eq!(state.last_event_type.as_deref(), Some("heartbeat"));
}
