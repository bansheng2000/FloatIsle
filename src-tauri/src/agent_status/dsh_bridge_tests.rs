use super::*;
use serde_json::json;

/// A real body from the installed plugin, captured from the running DSH before the
/// self-describing fields existed.
const OLD_BODY: &str = r#"{"ok":true,"running":[],"pending":[],"stats":{"enabled":true,"listening":true,"listenError":null,"eventsSent":203,"eventsFailed":2059,"lastEventAt":"2026-11-15T13:59:36.830Z","lastEventError":"connect ECONNREFUSED 127.0.0.1:47821","heartbeats":2203,"decisions":0,"decisionErrors":{"unknown-sid":1},"jumps":1,"approvals":0,"approvalEnds":0}}"#;

/// The same document as the current plugin source builds it.
fn new_body() -> String {
    json!({
        "ok": true,
        "running": ["session-a"],
        "pending": [],
        "recent": [{"kind": "stop"}, {"kind": "notify"}],
        "clients": 4,
        "clientList": [
            {"id": "20832-1", "since": "2026-11-15T14:01:00.000Z", "ws": "/w", "probe": ["harness"]},
            {"id": "20832-2", "since": "2026-11-15T14:01:00.000Z", "ws": "/w", "probe": []}
        ],
        "pid": 20832,
        "epoch": "e1",
        "stats": {"enabled": true, "listening": true, "eventsSent": 12, "eventsFailed": 0}
    })
    .to_string()
}

#[test]
fn parses_the_old_plugin_document_without_inventing_self_description() {
    let report = parse_status(OLD_BODY).unwrap();
    assert!(!report.self_describing);
    assert_eq!(report.clients, None);
    assert_eq!(report.pid, None);
    assert_eq!(report.events_sent, Some(203));
    assert_eq!(report.events_failed, Some(2059));
    assert_eq!(report.listening, Some(true));
    assert_eq!(
        report.last_event_error.as_deref(),
        Some("connect ECONNREFUSED 127.0.0.1:47821")
    );
    assert_eq!(
        classify(true, &Answer::Ours { status: 200, report, raw: String::new() }),
        DshBridgeState::PluginOutdated
    );
}

#[test]
fn parses_the_current_plugin_document_as_ok() {
    let report = parse_status(&new_body()).unwrap();
    assert!(report.self_describing);
    assert_eq!(report.clients, Some(4));
    assert_eq!(report.pid, Some(20832));
    assert_eq!(report.epoch.as_deref(), Some("e1"));
    assert_eq!(report.client_ids, ["20832-1", "20832-2"]);
    assert_eq!(report.running, ["session-a"]);
    assert_eq!(report.recent, 2);
    assert_eq!(
        classify(true, &Answer::Ours { status: 200, report, raw: String::new() }),
        DshBridgeState::Ok
    );
}

#[test]
fn a_hub_without_registered_clients_is_not_reported_as_ok() {
    // Measured on 2026-11-16 after a restart: the full field set, a pid, an epoch and a
    // heartbeat every ten seconds, but `clients: 0` and an empty `clientList` — the hub
    // listened while no DSH instance was attached, so not one event could arrive.
    let hub = json!({
        "ok": true,
        "running": [],
        "pending": [],
        "recent": [],
        "clients": 0,
        "clientList": [],
        "pid": 19680,
        "epoch": "mwie0bovhj0x",
        "stats": {"enabled": true, "listening": true, "eventsSent": 0, "eventsFailed": 0, "heartbeats": 17}
    })
    .to_string();
    let report = parse_status(&hub).unwrap();
    assert!(report.self_describing);
    assert_eq!(report.clients, Some(0));
    assert_eq!(
        classify(true, &Answer::Ours { status: 200, report, raw: String::new() }),
        DshBridgeState::NoClients
    );

    // An empty `running` list is not the same thing: nobody running a task is a normal bridge.
    let idle = json!({
        "ok": true,
        "running": [],
        "pending": [],
        "clients": 1,
        "clientList": [{"id": "19680-1", "since": "2026-11-16T06:00:00.000Z", "ws": "/w", "probe": ["harness"]}],
        "pid": 19680,
        "epoch": "mwie0bovhj0x",
        "stats": {"enabled": true, "listening": true, "eventsSent": 0, "eventsFailed": 0, "heartbeats": 17}
    })
    .to_string();
    let report = parse_status(&idle).unwrap();
    assert_eq!(
        classify(true, &Answer::Ours { status: 200, report, raw: String::new() }),
        DshBridgeState::Ok
    );
}

#[test]
fn a_body_that_is_not_the_plugins_is_a_taken_port_not_a_silent_plugin() {
    assert!(parse_status("<html>hello</html>").is_none());
    assert!(parse_status(r#"{"ok":false}"#).is_none());
    // `ok` alone is not enough: the plugin always reports its running list.
    assert!(parse_status(r#"{"ok":true}"#).is_none());
    let answer = Answer::Foreign { status: 200, raw: "<html>".to_string() };
    assert_eq!(classify(true, &answer), DshBridgeState::PortTaken);
    assert_eq!(classify(false, &answer), DshBridgeState::PortTaken);
}

#[test]
fn silence_is_blamed_on_the_process_only_when_there_is_none() {
    let silent = Answer::Silent("no answer on 47822".to_string());
    assert_eq!(classify(true, &silent), DshBridgeState::PluginSilent);
    assert_eq!(classify(false, &silent), DshBridgeState::DshNotRunning);
}

#[test]
fn main_processes_skips_children_of_other_dsh_processes() {
    let parents: HashMap<u32, u32> = [(11, 10), (12, 10), (20, 4), (21, 20)]
        .into_iter()
        .collect();
    assert_eq!(main_processes(&[10, 11, 12, 20, 21], &parents), [10, 20]);
    // A parent that is gone (pid absent from the snapshot) makes its child a root.
    assert_eq!(main_processes(&[31, 32], &HashMap::new()), [31, 32]);
    assert!(main_processes(&[], &HashMap::new()).is_empty());
}

#[test]
fn counts_children_against_the_main_process() {
    let processes = DshProcessSummary {
        main_pids: vec![20832],
        child_count: 8,
        started_at: Some(1_763_190_000_000),
        exe: None,
    };
    assert_eq!(processes.main_pids.len(), 1);
    assert_eq!(processes.child_count, 8);
    assert_eq!(filetime_to_unix_ms(11_644_473_600_000 * 10_000), 0);
}

#[test]
fn truncation_keeps_utf8_whole_and_marks_the_cut() {
    let body = "あ".repeat(RAW_LIMIT);
    let cut = truncated(&body);
    assert!(cut.ends_with('…'));
    assert!(cut.len() <= RAW_LIMIT + '…'.len_utf8());
    assert_eq!(truncated("short"), "short");
}

#[test]
fn reads_the_newest_installed_manifest_and_its_profile() {
    let home = std::env::temp_dir().join(format!("floatisle-bridge-{}", std::process::id()));
    let mut written = Vec::new();
    for (profile, version) in [("desktop", "0.11.0"), ("work", "0.12.0")] {
        let dir = home
            .join(".dsh")
            .join("profiles")
            .join(profile)
            .join("node_modules")
            .join(PLUGIN_PACKAGE);
        fs::create_dir_all(&dir).unwrap();
        let path = dir.join("package.json");
        fs::write(&path, json!({"name": PLUGIN_PACKAGE, "version": version}).to_string()).unwrap();
        written.push((profile, path));
    }
    // Make the second profile the newest without touching its contents.
    let newest = written[1].1.clone();
    let later = SystemTime::now() + Duration::from_secs(60);
    fs::File::options()
        .write(true)
        .open(&newest)
        .unwrap()
        .set_modified(later)
        .unwrap();

    let info = plugin_info(&home);
    assert_eq!(info.version.as_deref(), Some("0.12.0"));
    assert_eq!(info.profile.as_deref(), Some("work"));

    // A manifest that is not JSON, or has no version, is ignored rather than guessed.
    fs::write(&newest, "not json").unwrap();
    assert_eq!(plugin_info(&home).version.as_deref(), Some("0.11.0"));
    assert!(plugin_info(&home.join("absent")).version.is_none());

    fs::remove_dir_all(&home).ok();
}

#[test]
fn a_mounts_document_lists_every_page_separately() {
    let body = r#"{"ok":true,"ts":"2026-11-16T07:45:00.000Z","count":2,"clients":[
        {"clientId":"19680-1","mountedSessionId":"session-a","visible":true,"focused":true,"reportedAt":"2026-11-16T07:44:59.000Z","receivedAt":"2026-11-16T07:45:00.000Z","ageMs":1200,"reports":3,"stale":false},
        {"clientId":"19680-2","mountedSessionId":null,"visible":false,"focused":false,"reportedAt":"2026-11-16T07:40:00.000Z","receivedAt":"2026-11-16T07:45:00.000Z","ageMs":300000,"reports":7,"stale":true}
    ]}"#;
    let mounts = parse_mounts(body).expect("a mounts document");
    assert_eq!(mounts.len(), 2, "pages are never merged");
    assert_eq!(mounts[0].client_id.as_deref(), Some("19680-1"));
    assert_eq!(mounts[0].mounted_session_id.as_deref(), Some("session-a"));
    assert_eq!(mounts[0].received_at.as_deref(), Some("2026-11-16T07:45:00.000Z"));
    assert_eq!(mounts[0].age_ms, Some(1_200), "freshness comes from the hub's ageMs");
    assert_eq!(mounts[0].stale, Some(false));
    assert_eq!(mounts[1].mounted_session_id, None, "a page with nothing mounted keeps its entry");
    assert_eq!(mounts[1].stale, Some(true));
}

#[test]
fn a_mounts_refusal_is_never_read_as_an_empty_list() {
    for body in [
        r#"{"ok":false,"error":"internal","message":"POST only"}"#,
        r#"{"ok":true}"#,
        "<html>405</html>",
        "",
    ] {
        assert_eq!(parse_mounts(body), None, "refused body: {body}");
    }
}

#[test]
fn a_numeric_received_at_gives_the_age_when_the_hub_omits_it() {
    let mut mount = serde_json::from_value::<DshMount>(json!({
        "clientId": "20832-1",
        "mountedSessionId": "s2",
        "visible": true,
        "focused": true,
        "reportedAt": 1_700_000_000_000i64,
        "receivedAt": 1_700_000_010_000i64,
        "stale": false
    }))
    .expect("a numeric timestamp is still a timestamp");
    assert_eq!(mount.received_at.as_deref(), Some("1700000010000"));
    fill_age(1_700_000_012_000, &mut mount);
    assert_eq!(mount.age_ms, Some(2_000), "the hub's own arrival time is the freshness");
}

#[test]
fn an_iso_received_at_also_gives_the_age() {
    let received = "2026-11-16T09:00:00.000Z";
    let now = super::super::sessions::parse_timestamp_ms("2026-11-16T09:00:05.000Z").unwrap();
    let mut mount = DshMount { received_at: Some(received.to_string()), ..DshMount::default() };
    fill_age(now, &mut mount);
    assert_eq!(mount.age_ms, Some(5_000));
}

#[test]
fn one_unreadable_page_makes_the_whole_answer_unusable() {
    let body = json!({
        "ok": true,
        "clients": [
            {"clientId": "a", "mountedSessionId": "s1"},
            {"clientId": "b", "mountedSessionId": {"nested": true}}
        ]
    })
    .to_string();
    assert!(parse_mounts(&body).is_none(), "a page we cannot read must not look like an absent page");
}
