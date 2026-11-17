use super::*;
use crate::agent_status::dsh_desktop;
use serde_json::json;

const ID: &str = "session-00000000-0000-4000-8000-000000000001";

fn fixture() -> Value {
    json!({"version":7,"record":{
        "identity":{"formatVersion":4,"createdAt":100,"cwd":"D:\\fictional-project","isSeeded":false},
        "rows":{
            "title":{"ver":1,"seq":12,"val":"Fictional session"},
            "sessionListMetadata":{"ver":1,"seq":12,"val":{"blank":false,"lastPromptAt":1000}},
            "turnBoundary":{"ver":2,"seq":12,"val":{"openTurnStartSeq":5,"lastTurn":1}},
            "turnOutline":{"ver":2,"seq":12,"val":{"turns":[{"turn":1,"seq":5,"prompt":"Example prompt","response":"Example response"}]}},
            "modelSelection":{"ver":2,"seq":12,"val":{"lastUsed":{"provider":"deepseek-official","model":"deepseek-v4","reasoningEffort":"high"}}},
            "userQuestions":{"ver":2,"seq":12,"val":{"questions":{"active":[]}}}
        }
    }})
}

#[test]
fn parses_known_checkpoint_and_does_not_guess_after_version_changes() {
    let mut value = fixture();
    let session = parse_checkpoint(&value, ID, 1100, 1200, true).unwrap();
    assert_eq!(session.provider, "dsh");
    assert_eq!(session.title.as_deref(), Some("Fictional session"));
    assert_eq!(session.phase, "running");
    assert_eq!(session.model.as_deref(), Some("deepseek-v4"));
    assert_eq!(session.owner_prompt_at, Some(1000));
    assert!(!session.closed);
    value["version"] = json!(99);
    assert!(parse_checkpoint(&value, ID, 1100, 1200, true).is_none());
    value = fixture();
    value["record"]["rows"]["turnBoundary"]["ver"] = json!(99);
    assert!(parse_checkpoint(&value, ID, 1100, 1200, true).is_none());
    assert!(parse_checkpoint(&fixture(), "../../example", 1100, 1200, true).is_none());
}

#[test]
fn differentiates_waiting_continued_done_stale_and_exited() {
    let mut value = fixture();
    value["record"]["rows"]["userQuestions"]["val"]["questions"]["active"] =
        json!([{"state":"open"}]);
    assert_eq!(
        parse_checkpoint(&value, ID, 1100, 9999999, true)
            .unwrap()
            .phase,
        "waiting"
    );
    value["record"]["rows"]["userQuestions"]["val"]["questions"]["active"][0]["state"] =
        json!("continued");
    assert_eq!(
        parse_checkpoint(&value, ID, 1100, 9999999, true)
            .unwrap()
            .phase,
        "stale"
    );
    value["record"]["rows"]["turnBoundary"]["val"]["openTurnStartSeq"] = Value::Null;
    assert_eq!(
        parse_checkpoint(&value, ID, 1100, 1200, true)
            .unwrap()
            .phase,
        "completed"
    );
    assert!(
        parse_checkpoint(&fixture(), ID, 1100, 1200, false)
            .unwrap()
            .closed
    );
    value["record"]["rows"]["sessionListMetadata"]["val"]["blank"] = json!(true);
    assert!(parse_checkpoint(&value, ID, 1100, 1200, true).is_none());
}

#[test]
fn merges_imported_sessions_by_id_and_retains_newest_owner() {
    let mut sessions = HashMap::new();
    let old = parse_checkpoint(&fixture(), ID, 1000, 1200, false).unwrap();
    let mut new = parse_checkpoint(&fixture(), ID, 1100, 1200, true).unwrap();
    new.host.as_mut().unwrap().name = "DSH Desktop (dataelement)".into();
    merge_session(&mut sessions, new);
    merge_session(&mut sessions, old);
    assert_eq!(sessions.len(), 1);
    assert_eq!(
        sessions[ID].host.as_ref().unwrap().name,
        "DSH Desktop (dataelement)"
    );
    assert!(!sessions[ID].closed);
}

fn temp_root(name: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("dsh-ck-{name}-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    let _ = fs::create_dir_all(dir.join("storages/session_projcache/sessions"));
    let _ = fs::create_dir_all(dir.join("sessions/shard-1"));
    dir
}

fn now_millis() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or_default()
}

/// A real-shaped checkpoint padded to exactly `size` bytes.
fn checkpoint_bytes(size: usize) -> Vec<u8> {
    let mut value = fixture();
    value["record"]["rows"]["title"]["val"] = json!("");
    let base = serde_json::to_vec(&value).expect("json").len();
    value["record"]["rows"]["title"]["val"] = json!("x".repeat(size - base));
    let bytes = serde_json::to_vec(&value).expect("json");
    assert_eq!(bytes.len(), size);
    bytes
}

fn plant(root: &std::path::Path, id: &str, bytes: &[u8]) {
    fs::write(
        root.join("storages/session_projcache/sessions")
            .join(format!("{id}.json")),
        bytes,
    )
    .expect("checkpoint");
    let source = root.join("sessions/shard-1").join(id);
    fs::create_dir_all(&source).expect("shard");
    fs::write(source.join("session.v4.jsonl.zstd"), b"zstd").expect("source");
}

fn listed_ids(root: &std::path::Path, app_dir: Option<&std::path::Path>) -> Vec<String> {
    listed_ids_with(root, app_dir, true)
}

fn listed_ids_with(
    root: &std::path::Path,
    app_dir: Option<&std::path::Path>,
    fallback: bool,
) -> Vec<String> {
    list_from_root(root, now_millis(), 2 * 60 * 60 * 1000, None, app_dir, fallback)
        .into_iter()
        .map(|session| session.session_id)
        .collect()
}

#[test]
fn lists_a_checkpoint_just_under_the_old_cap() {
    let root = temp_root("under");
    let id = "session-00000000-0000-4000-8000-00000000c498";
    plant(&root, id, &checkpoint_bytes(498_333));
    assert_eq!(listed_ids(&root, None), vec![id.to_string()]);
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn lists_a_checkpoint_past_the_old_cap() {
    let root = temp_root("past");
    let id = "session-00000000-0000-4000-8000-00000000c552";
    plant(&root, id, &checkpoint_bytes(552_540));
    assert_eq!(
        listed_ids(&root, None),
        vec![id.to_string()],
        "a 552,540-byte checkpoint is what the 512 KiB cap used to drop"
    );
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn drops_a_checkpoint_past_the_new_cap_and_notes_it_in_the_log() {
    let root = temp_root("over");
    let log_dir = temp_root("over-log");
    let id = "session-00000000-0000-4000-8000-00000000c8a1";
    let size = 8 * 1024 * 1024 + 2048;
    plant(&root, id, &checkpoint_bytes(size));
    assert!(
        listed_ids_with(&root, Some(&log_dir), false).is_empty(),
        "without source-log recovery a checkpoint past the cap lists nothing"
    );
    assert_eq!(
        listed_ids(&root, Some(&log_dir)),
        vec![id.to_string()],
        "the source log behind it still brings the session back"
    );
    let text = fs::read_to_string(log_dir.join("agent-sessions.log")).expect("log");
    assert!(text.contains("too-large"), "{text}");
    assert!(text.contains(&format!("{size} bytes")), "{text}");
    assert!(text.contains(&format!("{id}.json")), "{text}");
    let _ = fs::remove_dir_all(&root);
    let _ = fs::remove_dir_all(&log_dir);
}

#[test]
fn notes_an_unparsable_checkpoint_and_still_lists_the_others() {
    let root = temp_root("broken");
    let log_dir = temp_root("broken-log");
    let good = "session-00000000-0000-4000-8000-0000000abcde";
    let bad = "session-00000000-0000-4000-8000-0000000fffff";
    plant(&root, good, &checkpoint_bytes(120_000));
    plant(&root, bad, b"{ not json");
    assert_eq!(
        listed_ids_with(&root, Some(&log_dir), false),
        vec![good.to_string()]
    );
    let text = fs::read_to_string(log_dir.join("agent-sessions.log")).expect("log");
    assert!(text.contains("invalid-json"), "{text}");
    assert!(text.contains("10 bytes"), "{text}");
    let _ = fs::remove_dir_all(&root);
    let _ = fs::remove_dir_all(&log_dir);
}

#[test]
fn the_parse_cache_stays_within_its_budget_and_drops_the_least_recently_used() {
    let mut entries: HashMap<PathBuf, CachedJson> = HashMap::new();
    let eight_mib = 8 * 1024 * 1024;
    for index in 0..4u64 {
        entries.insert(
            PathBuf::from(format!("c:/cache/{index}.json")),
            CachedJson {
                stamp: (eight_mib, None),
                value: json!({}),
                used: index,
            },
        );
    }
    assert_eq!(entries.len(), 4);
    make_room(&mut entries, eight_mib);
    let bytes: u64 = entries.values().map(|entry| entry.stamp.0).sum();
    assert!(
        bytes + eight_mib <= CACHE_BYTES,
        "room was not made: {bytes} + {eight_mib} > {CACHE_BYTES}"
    );
    assert!(
        !entries.contains_key(&PathBuf::from("c:/cache/0.json")),
        "the least recently used entry must go first"
    );
    assert!(entries.contains_key(&PathBuf::from("c:/cache/3.json")));

    make_room(&mut entries, 1);
    assert_eq!(entries.len(), 3, "a small entry needs no eviction");
    assert!(cache_state().0 <= CACHE_BYTES);
}

#[test]
fn a_shard_folder_name_decodes_to_the_working_directory() {
    assert_eq!(
        cwd_from_shard("--D-~6587~6863-Bot--").as_deref(),
        Some("D:\\文档\\Bot")
    );
    assert_eq!(
        cwd_from_shard("--C-Users-LY-.dsh-bot-workspace--").as_deref(),
        Some("C:\\Users\\LY\\.dsh\\bot\\workspace")
    );
    assert_eq!(cwd_from_shard("shard-1"), None);
}

#[test]
fn a_session_without_a_checkpoint_comes_back_from_its_source_log() {
    let root = temp_root("recover");
    let id = "session-00000000-0000-4000-8000-0000000beef1";
    let shard = root.join("sessions/--D-~6587~6863-Bot--").join(id);
    fs::create_dir_all(&shard).expect("shard");
    fs::write(shard.join("session.v4.jsonl.zstd"), b"zstd").expect("source");
    assert_eq!(
        listed_ids_with(&root, None, false),
        Vec::<String>::new(),
        "with recovery off a missing checkpoint lists nothing"
    );
    let sessions = list_from_root(&root, now_millis(), 2 * 60 * 60 * 1000, None, None, true);
    assert_eq!(sessions.len(), 1);
    assert_eq!(sessions[0].session_id, id);
    assert_eq!(sessions[0].cwd.as_deref(), Some("D:\\文档\\Bot"));
    assert_eq!(sessions[0].title.as_deref(), Some("Bot"));
    assert_eq!(sessions[0].phase, "completed");
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn the_same_session_in_two_shards_is_listed_once_with_the_newer_log() {
    let root = temp_root("dup");
    let id = "session-00000000-0000-4000-8000-0000000d0d01";
    for (shard, seconds) in [("--D-~6587~6863-Bot--", 60u64), ("--D-~6587~6863-Bot-lead--", 10)] {
        let dir = root.join("sessions").join(shard).join(id);
        fs::create_dir_all(&dir).expect("shard");
        let path = dir.join("session.v4.jsonl.zstd");
        fs::write(&path, b"zstd").expect("source");
        let stamp = std::time::SystemTime::now() - std::time::Duration::from_secs(seconds);
        fs::File::options()
            .write(true)
            .open(&path)
            .and_then(|file| file.set_modified(stamp))
            .expect("touch");
    }
    let sessions = list_from_root(&root, now_millis(), 2 * 60 * 60 * 1000, None, None, true);
    assert_eq!(sessions.len(), 1, "one row per session id");
    assert_eq!(sessions[0].title.as_deref(), Some("lead"), "the newer log wins");
    let _ = fs::remove_dir_all(&root);
}

/// Not part of the suite: `cargo test --release -- --ignored --nocapture bench_source_scan`
/// reports what scanning every shard for checkpoint-less sessions costs on this machine.
#[test]
#[ignore]
fn bench_source_scan() {
    let home = crate::system::windows_home_dir().expect("home");
    let root = dsh_desktop::roots(&home, std::env::var_os("APPDATA").map(std::path::PathBuf::from))
        .into_iter()
        .map(|(root, _)| root)
        .find(|root| root.join("sessions").is_dir())
        .expect("a harness root with sessions");
    let now = now_millis();
    let mut runs: Vec<f64> = Vec::new();
    let mut found = 0usize;
    for _ in 0..20 {
        let start = std::time::Instant::now();
        let rows = list_from_logs(&root, &HashSet::new(), now, 2 * 60 * 60 * 1000, None);
        runs.push(start.elapsed().as_secs_f64() * 1000.0);
        found = rows.len();
    }
    runs.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
    println!(
        "source-log scan: rows={found} min={:.2}ms median={:.2}ms max={:.2}ms",
        runs[0],
        runs[runs.len() / 2],
        runs[runs.len() - 1]
    );
}
