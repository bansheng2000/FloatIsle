//! The ledger read is the only thing standing between the card and a made-up number, so
//! these tests use a real SQLite file and never touch the machine's own ledger.

use std::{
    fs,
    path::PathBuf,
    sync::atomic::{AtomicUsize, Ordering},
};

use super::session_tokens_in;
use crate::win32::sqlite::Database;

static SERIAL: AtomicUsize = AtomicUsize::new(0);

fn ledger() -> PathBuf {
    let serial = SERIAL.fetch_add(1, Ordering::Relaxed);
    let directory = std::env::temp_dir().join(format!(
        "floatisle-ledger-{}-{serial}",
        std::process::id()
    ));
    let _ = fs::create_dir_all(&directory);
    let path = directory.join("usage.sqlite");
    let database = Database::open_for_tests(&path).unwrap_or_else(|error| panic!("{error}"));
    database
        .exec("create table calls (key text, session text, time integer, provider text, model text, kind text, live integer, input integer, cache_read integer, cache_write integer, output integer)")
        .unwrap_or_else(|error| panic!("{error}"));
    database
        .exec("insert into calls values ('k1','s-a',1,'p','m','turn',0,100,20,5,40), ('k2','s-a',2,'p','m','turn',0,10,0,0,7), ('k3','s-b',3,'p','m','turn',0,999,0,0,999)")
        .unwrap_or_else(|error| panic!("{error}"));
    path
}

#[test]
fn one_session_sums_exactly_its_own_rows() {
    let path = ledger();
    let tokens = session_tokens_in(&path, Some("s-a"));
    assert!(tokens.available, "{:?}", tokens.error);
    assert_eq!(tokens.input, 110);
    assert_eq!(tokens.cache_read, 20);
    assert_eq!(tokens.cache_write, 5);
    assert_eq!(tokens.output, 47);
    assert_eq!(tokens.calls, 2);
    assert_eq!(tokens.session.as_deref(), Some("s-a"));
    assert_eq!(tokens.source, path.to_string_lossy());
    assert!(tokens.error.is_none());
}

#[test]
fn without_a_session_the_card_is_told_to_wait_for_the_plugin() {
    let path = ledger();
    for missing in [None, Some(""), Some("   ")] {
        let tokens = session_tokens_in(&path, missing);
        assert!(!tokens.available);
        assert_eq!(tokens.error.as_deref(), Some("需要插件上报当前会话"));
        assert_eq!(tokens.calls, 0);
        assert_eq!(tokens.input, 0);
    }
}

#[test]
fn a_session_the_ledger_has_not_seen_is_unavailable_not_zero() {
    let path = ledger();
    let tokens = session_tokens_in(&path, Some("s-never"));
    assert!(!tokens.available);
    assert_eq!(tokens.error.as_deref(), Some("这个会话在账本里还没有记录"));
    assert_eq!(tokens.calls, 0);
}

#[test]
fn a_missing_ledger_file_never_reads_as_an_empty_tally() {
    let path = ledger().with_file_name("absent.sqlite");
    let tokens = session_tokens_in(&path, Some("s-a"));
    assert!(!tokens.available);
    let error = tokens.error.unwrap_or_default();
    assert!(error.starts_with("账本不存在"), "{error}");
    assert_eq!(tokens.calls, 0);
}

#[test]
fn a_broken_ledger_names_itself_even_before_a_session_is_known() {
    // A wrong path is the user's to fix; "wait for the plugin" would hide it behind a second cause.
    let path = ledger().with_file_name("absent-too.sqlite");
    let tokens = session_tokens_in(&path, None);
    assert!(!tokens.available);
    let error = tokens.error.unwrap_or_default();
    assert!(error.starts_with("账本不存在"), "{error}");
}
