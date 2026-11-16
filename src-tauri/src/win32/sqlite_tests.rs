//! The system SQLite is real, so these tests build a real database in a temp folder and
//! read it back through the same read-only path production uses.

use std::{
    fs,
    path::{Path, PathBuf},
    sync::atomic::{AtomicUsize, Ordering},
};

static SERIAL: AtomicUsize = AtomicUsize::new(0);

use super::Database;

fn scratch(name: &str) -> PathBuf {
    let stamp = std::time::Instant::now();
    let _ = stamp;
    let serial = SERIAL.fetch_add(1, Ordering::Relaxed);
    let directory = std::env::temp_dir().join(format!(
        "floatisle-sqlite-{}-{serial}-{name}",
        std::process::id()
    ));
    let _ = fs::create_dir_all(&directory);
    directory.join("ledger.sqlite")
}

fn build(path: &Path) {
    eprintln!("building {:?}", path);
    let database = Database::open_for_tests(path).unwrap_or_else(|error| panic!("{error}"));
    database
        .exec("create table calls (session text, input integer, cache_read integer, cache_write integer, output integer)")
        .unwrap_or_else(|error| panic!("{error}"));
    database
        .exec("insert into calls values ('a', 10, 2, 1, 3), ('a', 5, 0, 0, 7), ('b', 100, 0, 0, 200)")
        .unwrap_or_else(|error| panic!("{error}"));
}

#[test]
fn a_read_only_open_sums_the_rows_of_one_session() {
    let path = scratch("sums");
    build(&path);

    let database = Database::open_read_only(&path).unwrap_or_else(|error| panic!("{error}"));
    let rows = database
        .query_ints(
            "select coalesce(sum(input),0), coalesce(sum(cache_read),0), coalesce(sum(cache_write),0), coalesce(sum(output),0), count(*) from calls where session = ?1",
            &["a"],
        )
        .unwrap_or_else(|error| panic!("{error}"));

    assert_eq!(rows, vec![vec![15, 2, 1, 10, 2]]);
}

#[test]
fn a_read_only_open_sees_rows_written_into_the_write_ahead_log() {
    let path = scratch("wal");
    {
        let database = Database::open_for_tests(&path).unwrap_or_else(|error| panic!("{error}"));
        database
            .exec("pragma journal_mode = wal")
            .unwrap_or_else(|error| panic!("{error}"));
        database
            .exec("create table calls (session text, input integer)")
            .unwrap_or_else(|error| panic!("{error}"));
        database
            .exec("insert into calls values ('wal-row', 42)")
            .unwrap_or_else(|error| panic!("{error}"));
        // The row lives only in the -wal file until a checkpoint, which is exactly the
        // shape the DSS ledger has; a reader that ignored the log would see nothing.
        assert!(path.with_extension("sqlite-wal").exists() || sidecar(&path, "-wal").exists());
    }

    let database = Database::open_read_only(&path).unwrap_or_else(|error| panic!("{error}"));
    let rows = database
        .query_ints(
            "select coalesce(sum(input),0) from calls where session = ?1",
            &["wal-row"],
        )
        .unwrap_or_else(|error| panic!("{error}"));
    assert_eq!(rows, vec![vec![42]]);
}

#[test]
fn a_session_without_rows_sums_to_zero_but_still_answers() {
    let path = scratch("empty");
    build(&path);
    let database = Database::open_read_only(&path).unwrap_or_else(|error| panic!("{error}"));
    let rows = database
        .query_ints(
            "select coalesce(sum(input),0), count(*) from calls where session = ?1",
            &["nobody"],
        )
        .unwrap_or_else(|error| panic!("{error}"));
    assert_eq!(rows, vec![vec![0, 0]]);
}

#[test]
fn a_missing_file_is_an_error_not_an_empty_ledger() {
    let path = scratch("missing").with_file_name("absent.sqlite");
    assert!(Database::open_read_only(&path).is_err());
}

fn sidecar(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path.as_os_str().to_os_string();
    name.push(suffix);
    PathBuf::from(name)
}
