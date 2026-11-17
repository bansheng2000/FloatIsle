//! Read-only DeepSeek Harness desktop checkpoints. Never opens the compressed conversation log
//! or activates a session: projection checkpoints may lag the live host by its write interval.

use std::{
    collections::{HashMap, HashSet},
    env, fs,
    io::Read,
    path::{Path, PathBuf},
    sync::{Mutex, PoisonError},
    time::SystemTime,
};

use serde_json::Value;

use super::{
    markers::AGENT_RUNNING_STALE_AFTER_MS,
    session_host::SessionHost,
    sessions::AgentSessionSummary,
    snapshot::system_time_to_unix_millis,
    title::{folder_title, shorten},
    window_focus::{self, SessionHints},
};

/// A checkpoint grows with its conversation, so the old limit of 512 KiB dropped long sessions
/// from the island without a trace. The cap now sits far above a normal checkpoint, and a file
/// past it is reported in `agent-sessions.log` instead of disappearing.
const MAX_CHECKPOINT_BYTES: u64 = 8 * 1024 * 1024;
/// Parsed checkpoints kept in memory; past this the least recently used entry makes room, so one
/// large conversation cannot empty the cache and force every checkpoint to be parsed again.
const CACHE_BYTES: u64 = 32 * 1024 * 1024;
const MAX_CHECKPOINTS: usize = 512;

struct CachedJson {
    stamp: (u64, Option<SystemTime>),
    value: Value,
    used: u64,
}

static CHECKPOINTS: Mutex<Option<HashMap<PathBuf, CachedJson>>> = Mutex::new(None);
static CACHE_CLOCK: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

fn next_use_tick() -> u64 {
    CACHE_CLOCK.fetch_add(1, std::sync::atomic::Ordering::Relaxed)
}

/// Drops the least recently used entries until `incoming` more bytes fit.
fn make_room(entries: &mut HashMap<PathBuf, CachedJson>, incoming: u64) {
    let used_bytes = |entries: &HashMap<PathBuf, CachedJson>| {
        entries.values().map(|entry| entry.stamp.0).sum::<u64>()
    };
    while entries.len() >= MAX_CHECKPOINTS || used_bytes(entries) + incoming > CACHE_BYTES {
        let oldest = entries
            .iter()
            .min_by_key(|(_, entry)| entry.used)
            .map(|(path, _)| path.clone());
        match oldest {
            Some(path) => {
                entries.remove(&path);
            }
            None => break,
        }
    }
}
pub(super) const DESKTOP_APPS: &[&str] = super::dsh_desktop::APPS;

/// Preserve the balance credential source priority; extra session locators do not select credentials.
pub(super) fn data_root(home: &Path) -> Option<PathBuf> {
    let isolated = env::var_os("APPDATA")
        .map(PathBuf::from)
        .map(|p| p.join("dsh-desktop/harness"));
    isolated.filter(|p| p.is_dir()).or_else(|| {
        let shared = home.join(".dsh");
        shared
            .join("storages/session_projcache/sessions")
            .is_dir()
            .then_some(shared)
    })
}

pub(super) fn read_json(path: &Path, maximum: u64) -> Option<Value> {
    read_json_checked(path, maximum).ok()
}

/// Why a checkpoint could not be read as JSON.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum ReadProblem {
    /// Missing, locked, or read failed.
    Open,
    /// Larger than the cap in force.
    TooLarge,
    /// Present and under the cap, but not JSON.
    Invalid,
}

impl ReadProblem {
    pub(super) fn label(self) -> &'static str {
        match self {
            Self::Open => "unreadable",
            Self::TooLarge => "too-large",
            Self::Invalid => "invalid-json",
        }
    }
}

pub(super) fn read_json_checked(path: &Path, maximum: u64) -> Result<Value, ReadProblem> {
    let file = fs::File::open(path).map_err(|_| ReadProblem::Open)?;
    let metadata = file.metadata().map_err(|_| ReadProblem::Open)?;
    if metadata.len() > maximum {
        return Err(ReadProblem::TooLarge);
    }
    let stamp = (metadata.len(), metadata.modified().ok());
    let mut cache = CHECKPOINTS.lock().unwrap_or_else(PoisonError::into_inner);
    let entries = cache.get_or_insert_with(HashMap::new);
    if let Some(entry) = entries.get_mut(path).filter(|entry| entry.stamp == stamp) {
        entry.used = next_use_tick();
        return Ok(entry.value.clone());
    }
    let mut text = String::new();
    file.take(maximum + 1)
        .read_to_string(&mut text)
        .map_err(|_| ReadProblem::Open)?;
    if text.len() as u64 > maximum {
        return Err(ReadProblem::TooLarge);
    }
    let value: Value = serde_json::from_str(&text).map_err(|_| ReadProblem::Invalid)?;
    make_room(entries, stamp.0);
    entries.insert(
        path.to_path_buf(),
        CachedJson {
            stamp,
            value: value.clone(),
            used: next_use_tick(),
        },
    );
    Ok(value)
}

/// Bytes of parsed checkpoints currently held, and their paths, oldest use first.
#[cfg(test)]
pub(super) fn cache_state() -> (u64, Vec<(PathBuf, u64)>) {
    let mut cache = CHECKPOINTS.lock().unwrap_or_else(PoisonError::into_inner);
    let entries = cache.get_or_insert_with(HashMap::new);
    let bytes = entries.values().map(|entry| entry.stamp.0).sum();
    let mut used: Vec<(PathBuf, u64)> = entries
        .iter()
        .map(|(path, entry)| (path.clone(), entry.used))
        .collect();
    used.sort_by_key(|(_, tick)| *tick);
    (bytes, used)
}

pub(super) fn is_session_id(id: &str) -> bool {
    id.strip_prefix("session-").is_some_and(|id| {
        id.len() == 36
            && id.bytes().enumerate().all(|(i, c)| {
                if [8, 13, 18, 23].contains(&i) {
                    c == b'-'
                } else {
                    c.is_ascii_hexdigit()
                }
            })
    })
}

/// Unknown domain generations and changed projection versions are skipped, not guessed.
fn row<'a>(rows: &'a Value, key: &str, version: u64) -> Option<&'a Value> {
    let row = &rows[key];
    (row["ver"].as_u64() == Some(version)).then_some(&row["val"])
}

fn text(value: &Value, maximum: usize) -> Option<String> {
    value
        .as_str()
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .and_then(|s| shorten(s, maximum))
}

pub(super) fn parse_checkpoint(
    value: &Value,
    id: &str,
    updated_at: i64,
    now: i64,
    open: bool,
) -> Option<AgentSessionSummary> {
    if !is_session_id(id) || !matches!(value["version"].as_u64(), Some(3..=7)) {
        return None;
    }
    let record = &value["record"];
    if !matches!(record["identity"]["formatVersion"].as_u64(), Some(3 | 4)) {
        return None;
    }
    let rows = &record["rows"];
    let metadata = row(rows, "sessionListMetadata", 1)?;
    if metadata["blank"].as_bool()? {
        return None;
    }
    let boundary = row(rows, "turnBoundary", 2)?;
    let in_turn = boundary["openTurnStartSeq"].as_u64().is_some();
    // Continued timed questions no longer block the agent. Permissions are host-only transient
    // state and cannot be reconstructed from this cache (see docs/deepseek-harness.md).
    let question = row(rows, "userQuestions", 2)
        .and_then(|v| v["questions"]["active"].as_array())
        .is_some_and(|questions| questions.iter().any(|q| q["state"] == "open"));
    let phase = match (open, question, in_turn) {
        (false, _, _) => "completed",
        (_, true, _) => "waiting",
        (_, _, true) if now.saturating_sub(updated_at) > AGENT_RUNNING_STALE_AFTER_MS => "stale",
        (_, _, true) => "running",
        _ => "completed",
    };
    let cwd = record["identity"]["cwd"]
        .as_str()
        .filter(|s| !s.is_empty())
        .map(str::to_string);
    let outline = row(rows, "turnOutline", 2)
        .and_then(|v| v["turns"].as_array())
        .and_then(|v| v.last());
    let title = row(rows, "title", 1)
        .and_then(|v| text(v, 80))
        .or_else(|| row(rows, "titleInput", 1).and_then(|v| text(&v["first"]["text"], 80)))
        .or_else(|| cwd.as_deref().and_then(folder_title));
    let selection = row(rows, "modelSelection", 2).map(|v| &v["lastUsed"]);
    Some(AgentSessionSummary {
        provider: "dsh",
        session_id: id.to_string(),
        cwd,
        title,
        model: selection.and_then(|v| text(&v["model"], 80)),
        effort: selection.and_then(|v| text(&v["reasoningEffort"], 40)),
        phase,
        last_message: outline.and_then(|v| text(&v["response"], 200)),
        reply_marker: None,
        reply_asks: false,
        updated_at,
        host: Some(SessionHost {
            name: "DeepSeek Harness".to_string(),
            exe: None,
            kind: "app",
        }),
        turn_started_at: metadata["lastPromptAt"].as_i64(),
        owner_prompt_at: metadata["lastPromptAt"].as_i64(),
        last_prompt: outline.and_then(|v| text(&v["prompt"], 80)),
        current_tool: None,
        permission_mode: None,
        subagents: 0,
        background_tasks: 0,
        compacting: false,
        todos: None,
        closed: !open,
        remote_host: None,
        context_tokens: [4, 5]
            .into_iter()
            .find_map(|ver| row(rows, "contextPressure", ver))
            .and_then(|v| v["surfaceTokens"].as_u64()),
    })
}

/// The shard folder names encode the working directory Claude-style: `--D-~6587~6863-Bot--` is
/// `D:\文档\Bot`. Anything unexpected decodes to nothing and the row falls back to its short id.
fn cwd_from_shard(name: &str) -> Option<String> {
    let inner = name.strip_prefix("--")?.strip_suffix("--")?;
    let mut chars = inner.chars();
    let drive = chars.next().filter(char::is_ascii_alphabetic)?;
    let mut cwd = String::from(drive);
    cwd.push(':');
    let rest: Vec<char> = chars.collect();
    let mut index = 0;
    while index < rest.len() {
        match rest[index] {
            '-' => {
                cwd.push('\\');
                index += 1;
            }
            '~' => {
                let hex: String = rest.iter().skip(index + 1).take(4).collect();
                let code = u32::from_str_radix(&hex, 16).ok()?;
                cwd.push(char::from_u32(code)?);
                index += 5;
            }
            other => {
                cwd.push(other);
                index += 1;
            }
        }
    }
    Some(cwd)
}

/// Conversations whose projection checkpoint is gone still have their source log on disk. Only the
/// log's mtime, size and folder name are used: no decompression, so listing stays inside the same
/// budget as reading checkpoints. Such rows are marked completed until a checkpoint shows up again.
fn list_from_logs(
    root: &Path,
    listed: &HashSet<String>,
    now: i64,
    window_ms: i64,
    exe: Option<&str>,
) -> Vec<AgentSessionSummary> {
    let mut newest: HashMap<String, (i64, Option<String>)> = HashMap::new();
    for shard in fs::read_dir(root.join("sessions")).into_iter().flatten().flatten() {
        let cwd = cwd_from_shard(&shard.file_name().to_string_lossy());
        for entry in fs::read_dir(shard.path()).into_iter().flatten().flatten() {
            let Some(id) = entry.file_name().to_str().map(str::to_string) else {
                continue;
            };
            if !is_session_id(&id) || listed.contains(&id) {
                continue;
            }
            let mut modified: Option<i64> = None;
            for version in [3, 4] {
                let path = entry.path().join(format!("session.v{version}.jsonl.zstd"));
                let at = fs::metadata(&path)
                    .ok()
                    .and_then(|meta| meta.modified().ok())
                    .and_then(system_time_to_unix_millis);
                if let Some(at) = at {
                    if modified.is_none_or(|old| at > old) {
                        modified = Some(at);
                    }
                }
            }
            let Some(at) = modified.filter(|at| now.saturating_sub(*at) <= window_ms) else {
                continue;
            };
            let newer = newest.get(&id).is_none_or(|(old, _)| at > *old);
            if newer {
                newest.insert(id, (at, cwd.clone()));
            }
        }
    }
    let mut rows: Vec<_> = newest.into_iter().collect();
    rows.sort_by_key(|(_, (at, _))| std::cmp::Reverse(*at));
    rows.into_iter()
        .take(MAX_CHECKPOINTS)
        .map(|(id, (updated_at, cwd))| AgentSessionSummary {
            provider: "dsh",
            title: cwd.as_deref().and_then(folder_title).or_else(|| shorten(&id, 20)),
            cwd,
            session_id: id,
            model: None,
            effort: None,
            phase: "completed",
            last_message: None,
            reply_marker: None,
            reply_asks: false,
            updated_at,
            host: Some(SessionHost {
                name: "DeepSeek Harness".to_string(),
                exe: exe.map(str::to_string),
                kind: "app",
            }),
            turn_started_at: None,
            owner_prompt_at: None,
            last_prompt: None,
            current_tool: None,
            permission_mode: None,
            subagents: 0,
            background_tasks: 0,
            compacting: false,
            todos: None,
            closed: exe.is_none(),
            remote_host: None,
            context_tokens: None,
        })
        .collect()
}

pub(super) fn list_from_root(
    root: &Path,
    now: i64,
    window_ms: i64,
    exe: Option<&str>,
    app_dir: Option<&Path>,
    fallback: bool,
) -> Vec<AgentSessionSummary> {
    let dir = root.join("storages/session_projcache/sessions");
    let mut paths: Vec<_> = fs::read_dir(dir)
        .into_iter()
        .flatten()
        .flatten()
        .filter_map(|entry| {
            let modified = entry
                .metadata()
                .ok()?
                .modified()
                .ok()
                .and_then(system_time_to_unix_millis)?;
            (now.saturating_sub(modified) <= window_ms).then_some((modified, entry.path()))
        })
        .collect();
    paths.sort_by_key(|(modified, _)| std::cmp::Reverse(*modified));
    let mut sessions: Vec<AgentSessionSummary> = paths
        .into_iter()
        .take(MAX_CHECKPOINTS)
        .filter_map(|(modified, path)| {
            if path.extension()? != "json" {
                return None;
            }
            let id = path.file_stem()?.to_str()?;
            let value = match read_json_checked(&path, MAX_CHECKPOINT_BYTES) {
                Ok(value) => value,
                Err(problem) => {
                    let bytes = fs::metadata(&path).map(|m| m.len()).unwrap_or(0);
                    super::checkpoint_log::note(app_dir, now, &path, problem.label(), bytes);
                    return None;
                }
            };
            let mut summary = parse_checkpoint(&value, id, modified, now, exe.is_some())?;
            // Deleted logs may leave a checkpoint. Only sessions with a corresponding source file
            // are listed. Checking existence does not read or decompress the conversation body.
            let exists = fs::read_dir(root.join("sessions"))
                .into_iter()
                .flatten()
                .flatten()
                .any(|shard| {
                    [3, 4].iter().any(|version| {
                        shard
                            .path()
                            .join(id)
                            .join(format!("session.v{version}.jsonl.zstd"))
                            .is_file()
                    })
                });
            if !exists {
                return None;
            }
            if let Some(host) = &mut summary.host {
                host.exe = exe.map(str::to_string);
            }
            Some(summary)
        })
        .collect();
    if fallback {
        let listed: HashSet<String> = sessions
            .iter()
            .map(|session| session.session_id.clone())
            .collect();
        sessions.extend(list_from_logs(root, &listed, now, window_ms, exe));
    }
    sessions.sort_by_key(|session| std::cmp::Reverse(session.updated_at));
    sessions
}

fn merge_session(
    sessions: &mut HashMap<String, AgentSessionSummary>,
    session: AgentSessionSummary,
) {
    if sessions
        .get(&session.session_id)
        .is_none_or(|old| session.updated_at > old.updated_at)
    {
        sessions.insert(session.session_id.clone(), session);
    }
}

pub(super) fn list_sessions(
    home: &Path,
    now: i64,
    window_ms: i64,
    app_dir: Option<&Path>,
) -> Vec<AgentSessionSummary> {
    list_sessions_with(home, now, window_ms, app_dir, true)
}

/// `fallback: false` skips source-log recovery, which is what the tests use to prove the recovery
/// is the thing that finds a session whose projection checkpoint has gone.
pub(super) fn list_sessions_with(
    home: &Path,
    now: i64,
    window_ms: i64,
    app_dir: Option<&Path>,
    fallback: bool,
) -> Vec<AgentSessionSummary> {
    let mut sessions: HashMap<String, AgentSessionSummary> = HashMap::new();
    for (root, owner) in super::dsh_desktop::roots(home, env::var_os("APPDATA").map(PathBuf::from))
    {
        let exe = super::dsh_desktop::exe_for(owner);
        let name = exe
            .as_deref()
            .map(super::dsh_desktop::name)
            .unwrap_or(owner);
        for mut session in list_from_root(&root, now, window_ms, exe.as_deref(), app_dir, fallback) {
            if let Some(host) = &mut session.host {
                host.name = name.to_string();
            }
            merge_session(&mut sessions, session);
        }
    }
    let mut sessions: Vec<_> = sessions.into_values().collect();
    sessions.sort_by_key(|session| std::cmp::Reverse(session.updated_at));
    sessions
}

pub(super) fn focus_desktop(id: Option<&str>) -> bool {
    let owner = crate::system::windows_home_dir().ok().and_then(|home| {
        list_sessions(
            &home,
            super::snapshot::current_unix_millis(),
            i64::MAX,
            None,
        )
            .into_iter()
            .find(|session| Some(session.session_id.as_str()) == id)
            .and_then(|session| session.host)
            .and_then(|host| host.exe)
    });
    let windows = super::dsh_desktop::windows();
    let target = super::dsh_desktop::preferred_window(&windows, owner.as_deref())
        .and_then(|index| windows.get(index));
    target.is_some_and(|(_, pid)| {
        window_focus::focus_session_window(&SessionHints::default(), &[*pid], None).is_some()
    })
}

/// Do not mark every desktop conversation as read merely because the app is foreground.
pub(super) fn session_in_front(home: &Path, id: &str) -> bool {
    super::dsh_desktop::roots(home, env::var_os("APPDATA").map(PathBuf::from))
        .into_iter()
        .any(|(root, owner)| {
            let front =
                crate::win32::process::window_process_id(crate::win32::window::foreground_window());
            let front_exe = crate::win32::process::image_path(front);
            if !front_exe
                .as_deref()
                .is_some_and(|exe| super::dsh_desktop::owns(owner, exe))
            {
                return false;
            }
            ["desktop", "web"].iter().any(|profile| {
                read_json(
                    &root
                        .join("profiles")
                        .join(profile)
                        .join("desktop-storage.json"),
                    MAX_CHECKPOINT_BYTES,
                )
                .and_then(|v| {
                    v["dsh.sessions.current"]
                        .as_str()
                        .and_then(|s| serde_json::from_str::<Value>(s).ok())
                })
                .is_some_and(|current| current["sessionId"] == id)
            })
        })
}

#[cfg(test)]
#[path = "dsh_sessions_tests.rs"]
mod tests;
