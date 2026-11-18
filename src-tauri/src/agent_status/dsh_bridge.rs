//! What Settings → Connection shows about the DSH notifier bridge: whether DSH runs,
//! whether the plugin answers on its status port, and what the plugin says about itself.
//!
//! Read-only on purpose. This module never sends an event, never answers an approval, and
//! never opens the event port 47821; the bridge's receiving end is a separate piece of work.
//! Everything here is read from three places: a process snapshot, one loopback GET, and the
//! plugin's own package.json on disk.

use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::{HashMap, HashSet},
    fs,
    path::{Path, PathBuf},
    time::{Duration, SystemTime},
};

use crate::win32::{process, process_times, process_tree};

/// The plugin's status endpoint, part of the bridge contract (see analysis/protocol-v1.md).
const STATUS_URL: &str = "http://127.0.0.1:47822/status";
/// Which page has which session open, one entry per page. Plugin 0.13.0 answers this with GET;
/// older builds refuse it (405), and the card then says so instead of claiming nothing is open.
const MOUNTED_URL: &str = "http://127.0.0.1:47822/_task-notify/mounted";
/// A loopback request answers in microseconds; waiting longer than this is worse for a
/// settings card than saying plainly that nothing answered.
const PROBE_TIMEOUT: Duration = Duration::from_secs(2);
/// Enough for a whole status document, small enough to paste into a chat or an issue.
const RAW_LIMIT: usize = 4096;
const PLUGIN_PACKAGE: &str = "dsh-task-notify";
/// The DSH desktop executable. Electron starts one main process and a tree of renderer,
/// GPU and utility processes with the same image name; only the main one hosts the plugin.
const DSH_EXE: &str = "DeepSeek Harness.exe";
/// 100 ns ticks between the Windows epoch (1601-01-01) and the Unix epoch.
const FILETIME_UNIX_EPOCH_MS: i64 = 11_644_473_600_000;

/// The four states the card can show, plus the one case that must not be folded into them:
/// something else holding the status port answers with a valid HTTP response that is not
/// the plugin's document, which points at a completely different fix.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum DshBridgeState {
    /// No DSH process and nothing answering.
    DshNotRunning,
    /// DSH runs, but nothing answers on the status port.
    PluginSilent,
    /// Something answers on the status port, but it is not the plugin.
    PortTaken,
    /// The plugin answers, but without `clients` / `pid` / `epoch`: the running copy is old.
    PluginOutdated,
    /// The plugin answers with the self-describing fields but nothing registered with it:
    /// a hub that listens and beats, yet no DSH instance is attached, so no event arrives.
    NoClients,
    /// The plugin answers with the self-describing fields and at least one registered client.
    Ok,
}

#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DshProcessSummary {
    /// Pids of the top-level DSH processes (normally one); child renderer/GPU processes are
    /// counted separately because naming them would read as several DSH installations.
    pub main_pids: Vec<u32>,
    pub child_count: usize,
    /// Unix milliseconds, when the first main process started.
    pub started_at: Option<i64>,
    pub exe: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DshPluginInfo {
    /// Version in the package.json found on disk, which is the installed copy, not the one
    /// a running DSH has loaded.
    pub version: Option<String>,
    /// Profile directory the manifest was found under.
    pub profile: Option<String>,
}

/// The part of `/status` this card renders, all optional: an old plugin simply lacks the
/// self-describing fields.
#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DshBridgeReport {
    pub pid: Option<u32>,
    pub epoch: Option<String>,
    pub clients: Option<usize>,
    pub client_ids: Vec<String>,
    pub running: Vec<String>,
    pub recent: usize,
    pub last_event_at: Option<String>,
    pub events_sent: Option<u64>,
    pub events_failed: Option<u64>,
    pub last_event_error: Option<String>,
    /// Whether the plugin bound its reverse port; `false` means approvals cannot come back.
    pub listening: Option<bool>,
    /// True when the plugin reports `clients`, `pid` and `epoch`, which only versions after
    /// the self-description change do.
    pub self_describing: bool,
}

/// One page's report from `/_task-notify/mounted`. `age_ms` is the hub's own freshness
/// (milliseconds since it received the report) and is what the card uses; `received_at` and
/// `reported_at` are timestamps whose serialisation differs between hub versions (numbers in
/// one, ISO strings in another) and are only there to fall back on.
#[derive(Clone, Debug, Default, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct DshMount {
    pub client_id: Option<String>,
    pub mounted_session_id: Option<String>,
    pub visible: Option<bool>,
    pub focused: Option<bool>,
    pub stale: Option<bool>,
    #[serde(default, deserialize_with = "timestamp")]
    pub reported_at: Option<String>,
    #[serde(default, deserialize_with = "timestamp")]
    pub received_at: Option<String>,
    pub age_ms: Option<u64>,
}

/// A timestamp that may arrive as an ISO string or as epoch milliseconds, kept as text so the
/// interface shows what the hub said rather than a reformatted guess.
fn timestamp<'de, D>(deserializer: D) -> Result<Option<String>, D::Error>
where
    D: serde::Deserializer<'de>,
{
    #[derive(Deserialize)]
    #[serde(untagged)]
    enum Raw {
        Text(String),
        Integer(i64),
        Fraction(f64),
    }
    Ok(Option::<Raw>::deserialize(deserializer)?.map(|raw| match raw {
        Raw::Text(text) => text,
        Raw::Integer(millis) => millis.to_string(),
        Raw::Fraction(millis) => (millis as i64).to_string(),
    }))
}

/// Wall-clock milliseconds, for turning a hub timestamp into an age.
fn mount_now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|elapsed| elapsed.as_millis() as i64)
        .unwrap_or(0)
}

/// Fills in `age_ms` from `received_at` when the hub is new enough to answer but old enough
/// not to compute the age for us. A missing stamp leaves the age unknown, which the card says.
fn fill_age(now_ms: i64, mount: &mut DshMount) {
    if mount.age_ms.is_some() {
        return;
    }
    let Some(received) = mount.received_at.as_deref() else {
        return;
    };
    let stamp = received
        .parse::<i64>()
        .ok()
        .or_else(|| super::sessions::parse_timestamp_ms(received));
    if let Some(stamp) = stamp {
        mount.age_ms = Some(now_ms.saturating_sub(stamp).max(0) as u64);
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DshBridgeStatus {
    pub state: DshBridgeState,
    pub processes: DshProcessSummary,
    pub plugin: DshPluginInfo,
    /// `host:port` the card probed, for the diagnostics block.
    pub endpoint: String,
    pub reachable: bool,
    pub http_status: Option<u16>,
    /// Why the port did not answer, verbatim from the HTTP client.
    pub error: Option<String>,
    pub report: DshBridgeReport,
    /// The pages that reported a mounted session, or `None` when the plugin refused the read.
    pub mounts: Option<Vec<DshMount>>,
    /// Why the mounted read failed, verbatim, when `mounts` is `None`.
    pub mounts_error: Option<String>,
    /// Body of `/status` (or of whatever answered), truncated for pasting.
    pub raw: Option<String>,
}

/// One answer from the status port, kept apart from the network so the state rules below
/// can be tested without a server.
#[derive(Debug)]
enum Answer {
    /// Nothing answered; the string is the client's own error text.
    Silent(String),
    /// Something answered but not with the plugin's document.
    Foreign { status: u16, raw: String },
    /// The plugin answered.
    Ours { status: u16, report: DshBridgeReport, raw: String },
}

/// Main-process ids among `ids`, i.e. the ones whose parent is not another DSH process.
fn main_processes(ids: &[u32], parents: &HashMap<u32, u32>) -> Vec<u32> {
    let dsh: HashSet<u32> = ids.iter().copied().collect();
    let mut main: Vec<u32> = ids
        .iter()
        .copied()
        .filter(|pid| parents.get(pid).is_none_or(|parent| !dsh.contains(parent)))
        .collect();
    main.sort_unstable();
    main
}

fn processes() -> DshProcessSummary {
    let ids = process_tree::process_ids_named(DSH_EXE);
    if ids.is_empty() {
        return DshProcessSummary::default();
    }
    let main_pids = main_processes(&ids, &process_tree::parent_process_ids());
    let child_count = ids.len().saturating_sub(main_pids.len());
    let first = main_pids.first().copied();
    DshProcessSummary {
        started_at: first
            .and_then(process_times::start_time)
            .map(filetime_to_unix_ms),
        exe: first.and_then(process::image_path),
        main_pids,
        child_count,
    }
}

fn filetime_to_unix_ms(ticks: u64) -> i64 {
    (ticks / 10_000) as i64 - FILETIME_UNIX_EPOCH_MS
}

/// The newest installed copy of the plugin, from the manifests DSH keeps per profile.
fn plugin_info(home: &Path) -> DshPluginInfo {
    let mut best: Option<(SystemTime, PathBuf, String)> = None;
    for root in [".dsh", ".dsh-beta"] {
        let base = home.join(root);
        let mut candidates = vec![base
            .join("node_modules")
            .join(PLUGIN_PACKAGE)
            .join("package.json")];
        if let Ok(entries) = fs::read_dir(base.join("profiles")) {
            for entry in entries.flatten() {
                candidates.push(
                    entry
                        .path()
                        .join("node_modules")
                        .join(PLUGIN_PACKAGE)
                        .join("package.json"),
                );
            }
        }
        for path in candidates {
            let Some((written, version)) = read_manifest_version(&path) else {
                continue;
            };
            if best
                .as_ref()
                .is_none_or(|(newest, _, _)| written > *newest)
            {
                best = Some((written, path, version));
            }
        }
    }
    let Some((_, path, version)) = best else {
        return DshPluginInfo::default();
    };
    DshPluginInfo {
        version: Some(version),
        profile: profile_of(&path),
    }
}

fn read_manifest_version(path: &Path) -> Option<(SystemTime, String)> {
    let metadata = fs::metadata(path).ok()?;
    // A manifest is a few KB; anything this large is not one.
    if metadata.len() > 256 * 1024 {
        return None;
    }
    let text = fs::read_to_string(path).ok()?;
    let value: Value = serde_json::from_str(&text).ok()?;
    let version = value.get("version").and_then(Value::as_str)?;
    Some((metadata.modified().ok()?, version.to_string()))
}

/// `<home>/.dsh/profiles/<name>/node_modules/...` names the profile `<name>`.
fn profile_of(manifest: &Path) -> Option<String> {
    let parts: Vec<_> = manifest
        .components()
        .map(|part| part.as_os_str().to_string_lossy().to_string())
        .collect();
    parts
        .iter()
        .position(|part| part == "profiles")
        .and_then(|index| parts.get(index + 1))
        .cloned()
}

/// Reads the plugin's document out of a status body. `None` means the body is not the
/// plugin's, which the caller reports as the port being taken by something else.
fn parse_status(body: &str) -> Option<DshBridgeReport> {
    let value: Value = serde_json::from_str(body).ok()?;
    if value.get("ok").and_then(Value::as_bool) != Some(true) {
        return None;
    }
    value.get("running")?.as_array()?;
    let stats = value.get("stats")?.as_object()?;
    let number = |key: &str| stats.get(key).and_then(Value::as_u64);
    let text = |key: &str| {
        stats
            .get(key)
            .and_then(Value::as_str)
            .map(str::to_string)
    };
    let clients = value
        .get("clients")
        .and_then(Value::as_u64)
        .map(|count| count as usize);
    let pid = value
        .get("pid")
        .and_then(Value::as_u64)
        .map(|pid| pid as u32);
    let epoch = text_of(&value, "epoch");
    Some(DshBridgeReport {
        self_describing: clients.is_some() && pid.is_some() && epoch.is_some(),
        clients,
        pid,
        epoch,
        client_ids: non_empty_strings(value.get("clientList"), "id"),
        running: non_empty_strings(value.get("running"), ""),
        recent: value
            .get("recent")
            .and_then(Value::as_array)
            .map_or(0, Vec::len),
        last_event_at: text("lastEventAt"),
        events_sent: number("eventsSent"),
        events_failed: number("eventsFailed"),
        last_event_error: text("lastEventError"),
        listening: stats.get("listening").and_then(Value::as_bool),
    })
}

/// Strings at `key` in every object of `list`, or in `list` itself when `key` is empty.
fn non_empty_strings(list: Option<&Value>, key: &str) -> Vec<String> {
    let Some(items) = list.and_then(Value::as_array) else {
        return Vec::new();
    };
    items
        .iter()
        .filter_map(|item| {
            let value = if key.is_empty() { item } else { item.get(key)? };
            value
                .as_str()
                .filter(|text| !text.is_empty())
                .map(str::to_string)
        })
        .collect()
}

fn text_of(value: &Value, key: &str) -> Option<String> {
    value
        .get(key)
        .and_then(Value::as_str)
        .filter(|text| !text.is_empty())
        .map(str::to_string)
}

/// The state rules, kept free of I/O so every branch has a fixture test. A plugin that
/// answers is never reported as "DSH not running": process enumeration can fail while the
/// plugin is demonstrably alive.
fn classify(has_process: bool, answer: &Answer) -> DshBridgeState {
    match answer {
        Answer::Ours { report, .. } => {
            if !report.self_describing {
                DshBridgeState::PluginOutdated
            } else if report.clients == Some(0) {
                // A hub that answers with a full field set but has nothing attached is not a
                // healthy bridge: it beats and listens, and not one event can arrive. An empty
                // `running` list is different — that only means nobody is running a task now.
                DshBridgeState::NoClients
            } else {
                DshBridgeState::Ok
            }
        }
        Answer::Foreign { .. } => DshBridgeState::PortTaken,
        Answer::Silent(_) => {
            if has_process {
                DshBridgeState::PluginSilent
            } else {
                DshBridgeState::DshNotRunning
            }
        }
    }
}

fn truncated(body: &str) -> String {
    if body.len() <= RAW_LIMIT {
        return body.to_string();
    }
    let mut end = RAW_LIMIT;
    while !body.is_char_boundary(end) {
        end -= 1;
    }
    format!("{}…", &body[..end])
}

async fn probe() -> Answer {
    let Ok(client) = reqwest::Client::builder()
        .timeout(PROBE_TIMEOUT)
        // Loopback only (127.0.0.1:47822): keep the status probe off any system proxy.
        .no_proxy()
        .redirect(reqwest::redirect::Policy::none())
        .build()
    else {
        return Answer::Silent("HTTP client unavailable".to_string());
    };
    let Ok(response) = client.get(STATUS_URL).send().await else {
        return Answer::Silent("no answer on 47822".to_string());
    };
    let status = response.status().as_u16();
    let body = response.text().await.unwrap_or_default();
    match parse_status(&body) {
        Some(report) => Answer::Ours {
            status,
            report,
            raw: truncated(&body),
        },
        None => Answer::Foreign {
            status,
            raw: truncated(&body),
        },
    }
}

fn interpret(
    answer: Answer,
    processes: DshProcessSummary,
    plugin: DshPluginInfo,
) -> DshBridgeStatus {
    let has_process = !processes.main_pids.is_empty();
    let state = classify(has_process, &answer);
    let (reachable, http_status, error, report, raw) = match answer {
        Answer::Silent(message) => (
            false,
            None,
            Some(message),
            DshBridgeReport::default(),
            None,
        ),
        Answer::Foreign { status, raw } => (
            true,
            Some(status),
            None,
            DshBridgeReport::default(),
            Some(raw),
        ),
        Answer::Ours {
            status,
            report,
            raw,
        } => (true, Some(status), None, report, Some(raw)),
    };
    DshBridgeStatus {
        state,
        processes,
        plugin,
        endpoint: "127.0.0.1:47822".to_string(),
        reachable,
        http_status,
        error,
        report,
        mounts: None,
        mounts_error: None,
        raw,
    }
}

/// Reads the per-page mounts. A refusal is reported, never turned into an empty list.
async fn read_mounts() -> Result<Vec<DshMount>, String> {
    let client = reqwest::Client::builder()
        .timeout(PROBE_TIMEOUT)
        // Loopback only (127.0.0.1:47822): keep the mounts read off any system proxy.
        .no_proxy()
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .map_err(|error| error.to_string())?;
    let response = client
        .get(MOUNTED_URL)
        .send()
        .await
        .map_err(|error| error.to_string())?;
    let status = response.status();
    let body = response.text().await.unwrap_or_default();
    if !status.is_success() {
        return Err(format!("HTTP {status}"));
    }
    parse_mounts(&body).ok_or_else(|| "the answer is not a mounts document".to_string())
}

/// `{ok, ts, count, clients: [...]}`; anything else means the plugin did not answer this route.
fn parse_mounts(body: &str) -> Option<Vec<DshMount>> {
    let value: Value = serde_json::from_str(body).ok()?;
    if value.get("ok").and_then(Value::as_bool) != Some(true) {
        return None;
    }
    let clients = value.get("clients")?.as_array()?;
    let now_ms = mount_now_ms();
    let mut mounts = Vec::with_capacity(clients.len());
    for client in clients {
        // One page the shape of which we do not understand makes the whole answer unusable:
        // showing the pages we happened to parse would look like a complete list.
        let mut mount = serde_json::from_value::<DshMount>(client.clone()).ok()?;
        fill_age(now_ms, &mut mount);
        mounts.push(mount);
    }
    Some(mounts)
}

/// Everything the connection card shows: DSH's processes, the plugin's manifest, and one
/// read of its status endpoint. Never fails: an unreachable or foreign endpoint is a state,
/// not an error the UI has to distinguish from a broken command.
#[tauri::command]
pub async fn probe_dsh_bridge() -> Result<DshBridgeStatus, String> {
    let plugin = crate::system::windows_home_dir()
        .map(|home| plugin_info(&home))
        .unwrap_or_default();
    // The process snapshot is one Toolhelp enumeration, fast enough to stay on this task;
    // the only slow part here is the port, which is awaited.
    let processes = processes();
    let answer = probe().await;
    // Only ask for mounts when the hub described itself; an old or foreign port has no such route.
    let (mounts, mounts_error) = match &answer {
        Answer::Ours { report, .. } if report.self_describing => match read_mounts().await {
            Ok(mounts) => (Some(mounts), None),
            Err(error) => (None, Some(error)),
        },
        _ => (None, None),
    };
    let mut status = interpret(answer, processes, plugin);
    status.mounts = mounts;
    status.mounts_error = mounts_error;
    Ok(status)
}

#[cfg(test)]
#[path = "dsh_bridge_tests.rs"]
mod tests;
