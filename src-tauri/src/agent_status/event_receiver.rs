//! The island's end of the plugin bridge: a listener on `127.0.0.1:47821` where
//! dsh-task-notify posts `presence` (which session its window shows) and `event` (turn
//! started, settled, approval asked...). Until this existed the plugin's posts were refused,
//! which is the `eventsFailed` count the connection card shows; the mounted session it reports
//! is also the only way to know what "this session" means for the token card.

use std::{
    io::{BufRead, BufReader, Write},
    net::{TcpListener, TcpStream},
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex, MutexGuard, OnceLock, PoisonError,
    },
    thread,
    time::{Duration, SystemTime, UNIX_EPOCH},
};

use serde::{Deserialize, Serialize};
use serde_json::Value;

/// Where the plugin posts; the protocol document fixes this port.
pub(crate) const PORT: u16 = 47_821;
/// A post is small and arrives at once; a client that stalls is dropped.
const READ_TIMEOUT: Duration = Duration::from_secs(5);
const MAX_BODY_BYTES: usize = 64 * 1024;
const MAX_HEADERS: usize = 32;
const MAX_LINE_BYTES: usize = 4 * 1024;
/// A client that stops reporting is forgotten at the same age the hub forgets it, so a
/// crashed page (or a leftover test post) never keeps naming a session that is gone.
const PRESENCE_TTL_MS: u64 = 10 * 60 * 1000;

/// What the plugin's window is showing. Field names are the plugin's (`mountedSessionId`),
/// with the older `sessionId` still accepted because deployed copies send that.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Presence {
    pub client_id: Option<String>,
    pub visible: Option<bool>,
    pub focused: Option<bool>,
    pub mounted_session_id: Option<String>,
    pub reported_at: Option<String>,
}

/// What the connection card shows about this listener.
#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ReceiverStatus {
    pub port: u16,
    pub listening: bool,
    pub error: Option<String>,
    pub presence: Option<Presence>,
    pub presences: u64,
    pub last_presence_at_ms: Option<u64>,
    pub events: u64,
    pub rejected: u64,
    pub last_event_at_ms: Option<u64>,
    pub last_event_type: Option<String>,
}

#[derive(Debug, Default)]
struct State {
    presence: Option<Presence>,
    presences: u64,
    last_presence_at_ms: Option<u64>,
    events: u64,
    rejected: u64,
    last_event_at_ms: Option<u64>,
    last_event_type: Option<String>,
}

struct Shared {
    port: u16,
    listening: AtomicBool,
    stopping: AtomicBool,
    state: Mutex<State>,
}

impl Shared {
    fn new(port: u16) -> Self {
        Self {
            port,
            listening: AtomicBool::new(true),
            stopping: AtomicBool::new(false),
            state: Mutex::new(State::default()),
        }
    }

    fn state(&self) -> MutexGuard<'_, State> {
        self.state.lock().unwrap_or_else(PoisonError::into_inner)
    }

    fn status(&self, error: Option<String>) -> ReceiverStatus {
        let state = self.state();
        let mut status = ReceiverStatus {
            port: self.port,
            listening: self.listening.load(Ordering::Relaxed),
            error,
            presence: state.presence.clone(),
            presences: state.presences,
            last_presence_at_ms: state.last_presence_at_ms,
            events: state.events,
            rejected: state.rejected,
            last_event_at_ms: state.last_event_at_ms,
            last_event_type: state.last_event_type.clone(),
        };
        if presence_expired(status.last_presence_at_ms, now_ms()) {
            status.presence = None;
            status.last_presence_at_ms = None;
        }
        if status.presence.is_none() {
            status.last_presence_at_ms = None;
        }
        status
    }

    fn reject(&self) {
        self.state().rejected += 1;
    }

    /// One request: `presence` and `event` from the plugin, `status` for a readback.
    fn handle(&self, request: Request) -> Result<(u16, String), (u16, &'static str)> {
        // The plugin talks under `/_task-notify`, older builds post to the bare paths.
        let bare = request.path.as_str();
        let path = bare
            .strip_prefix("/_task-notify")
            .filter(|rest| rest.starts_with('/'))
            .unwrap_or(bare);
        match (request.method.as_str(), path) {
            ("POST", "/presence") => {
                let presence = presence_from(&request.body)?;
                {
                    let mut state = self.state();
                    state.presence = Some(presence);
                    state.presences += 1;
                    state.last_presence_at_ms = now_ms();
                }
                Ok((200, "{\"ok\":true}".to_string()))
            }
            ("POST", "/event") => {
                let (kind, presence) = event_from(&request.body)?;
                {
                    let mut state = self.state();
                    state.events += 1;
                    state.last_event_at_ms = now_ms();
                    state.last_event_type = Some(kind);
                    // The plugin sends presence through `/event` too, but only when it
                    // changes; the periodic copy goes to the hub, not here.
                    if let Some(presence) = presence {
                        state.presence = Some(presence);
                        state.presences += 1;
                        state.last_presence_at_ms = now_ms();
                    }
                }
                Ok((200, "{\"ok\":true}".to_string()))
            }
            ("GET", "/status") => {
                let body = serde_json::to_string(&self.status(None))
                    .unwrap_or_else(|_| "{\"ok\":false}".to_string());
                Ok((200, body))
            }
            ("GET", "/") => Ok((200, "{\"ok\":true,\"service\":\"floatisle\"}".to_string())),
            (_, "/presence" | "/event") => Err((405, "Only POST is accepted here.")),
            _ => Err((404, "Unknown path.")),
        }
    }
}

/// A parsed request: only what the bridge needs.
#[derive(Debug, PartialEq, Eq)]
struct Request {
    method: String,
    path: String,
    body: Vec<u8>,
}

fn read_request(reader: &mut impl BufRead) -> Result<Request, (u16, &'static str)> {
    let line = read_line(reader)?;
    let mut parts = line.split_whitespace();
    let (Some(method), Some(target), Some(version)) = (parts.next(), parts.next(), parts.next())
    else {
        return Err((400, "Malformed request line."));
    };
    if !version.starts_with("HTTP/1.") {
        return Err((505, "Only HTTP/1.x is supported."));
    }
    let path = target.split('?').next().unwrap_or_default().to_string();

    let mut length = 0usize;
    for _ in 0..MAX_HEADERS {
        let header = read_line(reader)?;
        if header.is_empty() {
            break;
        }
        if let Some((name, value)) = header.split_once(':') {
            if name.trim().eq_ignore_ascii_case("content-length") {
                length = value.trim().parse().map_err(|_| (400, "Bad Content-Length."))?;
            }
        }
    }
    if length > MAX_BODY_BYTES {
        return Err((413, "Body is too large."));
    }
    let mut body = vec![0u8; length];
    reader
        .read_exact(&mut body)
        .map_err(|_| (400, "Body is shorter than Content-Length."))?;
    Ok(Request {
        method: method.to_ascii_uppercase(),
        path,
        body,
    })
}

fn read_line(reader: &mut impl BufRead) -> Result<String, (u16, &'static str)> {
    let mut line = String::new();
    let read = reader
        .read_line(&mut line)
        .map_err(|_| (400, "Could not read the request."))?;
    if read == 0 {
        return Err((400, "Connection closed before the request arrived."));
    }
    if line.len() > MAX_LINE_BYTES {
        return Err((431, "Request line is too long."));
    }
    Ok(line.trim_end_matches(['\r', '\n']).to_string())
}

/// The plugin's presence body. New field names win; the deployed `sessionId` is the fallback,
/// and `reported_at` accepts a string timestamp or a number.
///
/// `mountedSessionId` is read strictly: the key must be there, but `null` is a valid answer
/// ("this page has no session open"). A body that omits the key entirely is a broken report,
/// not a page without a session, so it is refused instead of silently clearing the mounted id.
fn presence_from(body: &[u8]) -> Result<Presence, (u16, &'static str)> {
    let value: Value = serde_json::from_slice(body).map_err(|_| (400, "Body is not JSON."))?;
    let object = value
        .get("presence")
        .and_then(Value::as_object)
        .or_else(|| value.as_object())
        .ok_or((400, "Presence is not an object."))?;
    let text = |key: &str| {
        object
            .get(key)
            .and_then(Value::as_str)
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string)
    };
    let flag = |key: &str| object.get(key).and_then(Value::as_bool);
    let stamp = |key: &str| match object.get(key) {
        Some(Value::String(value)) if !value.trim().is_empty() => Some(value.clone()),
        Some(Value::Number(value)) => Some(value.to_string()),
        _ => None,
    };
    let mounts = object.contains_key("mountedSessionId") || object.contains_key("sessionId");
    if !mounts {
        return Err((
            400,
            "Presence omits mountedSessionId (null is allowed, a missing key is not).",
        ));
    }
    let presence = Presence {
        client_id: text("clientId").or_else(|| text("client")),
        visible: flag("visible"),
        focused: flag("focused"),
        mounted_session_id: text("mountedSessionId").or_else(|| text("sessionId")),
        reported_at: text("reportedAt").or_else(|| stamp("ts")),
    };
    if presence == Presence::default() {
        return Err((400, "Presence names none of the fields the bridge knows."));
    }
    Ok(presence)
}

/// The plugin's event body: `type` (or `kind`) and the session it belongs to.
fn event_from(body: &[u8]) -> Result<(String, Option<Presence>), (u16, &'static str)> {
    let value: Value = serde_json::from_slice(body).map_err(|_| (400, "Body is not JSON."))?;
    let kind = value
        .get("type")
        .or_else(|| value.get("kind"))
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|kind| !kind.is_empty())
        .ok_or((400, "Event has no type."))?;
    let presence = if kind == "presence" {
        Some(presence_from(body)?)
    } else {
        None
    };
    Ok((kind.to_string(), presence))
}

/// True when the last report is older than the hub's own forget window.
fn presence_expired(last_ms: Option<u64>, now: Option<u64>) -> bool {
    let (Some(last), Some(now)) = (last_ms, now) else {
        return false;
    };
    now.saturating_sub(last) > PRESENCE_TTL_MS
}

fn now_ms() -> Option<u64> {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .ok()
        .map(|elapsed| elapsed.as_millis() as u64)
}

fn write_response(stream: &mut TcpStream, status: u16, body: &str) -> std::io::Result<()> {
    let reason = match status {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        431 => "Request Header Fields Too Large",
        505 => "HTTP Version Not Supported",
        _ => "Error",
    };
    write!(
        stream,
        "HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
        body.len()
    )?;
    stream.flush()
}

/// One listener in the app: bound once at startup, read through the `dsh_receiver_status` command.
pub(crate) struct Receiver {
    shared: Arc<Shared>,
}

impl Receiver {
    /// Binds `port` (`0` picks a free one, which the tests use) and serves on a thread.
    fn bind(port: u16) -> Result<Self, String> {
        let listener = TcpListener::bind(("127.0.0.1", port))
            .map_err(|error| format!("127.0.0.1:{port}: {error}"))?;
        let port = listener
            .local_addr()
            .map_err(|error| format!("127.0.0.1:{port}: {error}"))?
            .port();
        let shared = Arc::new(Shared::new(port));
        let serving = Arc::clone(&shared);
        thread::Builder::new()
            .name("dsh-event-receiver".to_string())
            .spawn(move || serve(listener, &serving))
            .map_err(|error| format!("could not start the listener thread: {error}"))?;
        Ok(Self { shared })
    }

    fn status(&self) -> ReceiverStatus {
        self.shared.status(None)
    }

    /// Stops accepting (tests only; the app keeps one listener for its lifetime).
    #[cfg(test)]
    fn stop(&self) {
        self.shared.stopping.store(true, Ordering::Relaxed);
        let _ = TcpStream::connect(("127.0.0.1", self.shared.port));
    }
}

fn serve(listener: TcpListener, shared: &Arc<Shared>) {
    for stream in listener.incoming() {
        if shared.stopping.load(Ordering::Relaxed) {
            break;
        }
        if let Ok(stream) = stream {
            handle_connection(stream, shared);
        }
    }
    shared.listening.store(false, Ordering::Relaxed);
}

fn handle_connection(stream: TcpStream, shared: &Shared) {
    let Ok(mut writer) = stream.try_clone() else {
        shared.reject();
        return;
    };
    let _ = stream.set_read_timeout(Some(READ_TIMEOUT));
    let mut reader = BufReader::new(stream);
    let answered = match read_request(&mut reader) {
        Ok(request) => shared.handle(request),
        Err(error) => Err(error),
    };
    let (status, body) = match answered {
        Ok(answer) => answer,
        Err((status, message)) => {
            shared.reject();
            (
                status,
                format!("{{\"ok\":false,\"reason\":\"{message}\"}}"),
            )
        }
    };
    let _ = write_response(&mut writer, status, &body);
}

static RECEIVER: OnceLock<Receiver> = OnceLock::new();
static START_FAILURE: OnceLock<String> = OnceLock::new();

/// Starts the listener once. A taken port is kept as the reported error, so the card can say
/// why nothing is arriving instead of looking healthy.
pub(crate) fn start() {
    if RECEIVER.get().is_some() {
        return;
    }
    match Receiver::bind(PORT) {
        Ok(receiver) => {
            let _ = RECEIVER.set(receiver);
        }
        Err(error) => {
            let _ = START_FAILURE.set(error);
        }
    }
}

/// What the connection card shows: whether the listener runs, the last presence the plugin
/// posted, and the event counters.
pub(crate) fn status() -> ReceiverStatus {
    match RECEIVER.get() {
        Some(receiver) => receiver.status(),
        None => ReceiverStatus {
            port: PORT,
            listening: false,
            error: START_FAILURE.get().cloned(),
            ..ReceiverStatus::default()
        },
    }
}

#[tauri::command]
pub fn dsh_receiver_status() -> Result<ReceiverStatus, String> {
    Ok(status())
}

#[cfg(test)]
#[path = "event_receiver_tests.rs"]
mod tests;
