//! Token totals for the session the island is currently showing.
//!
//! The DSS bot ledger (`~/.dsh/bot/usage.sqlite`) records one row per model call and has
//! no money column, so this module only ever reports tokens; turning them into currency
//! would be inventing a price.

use std::path::PathBuf;

use serde::Serialize;

use crate::system::windows_home_dir;
use crate::win32::sqlite::Database;

/// Where the ledger lives, unless a run points somewhere else on purpose.
const LEDGER_ENV: &str = "FLOATISLE_DSH_LEDGER";

const COLUMNS: &str = "coalesce(sum(input),0), coalesce(sum(cache_read),0), coalesce(sum(cache_write),0), coalesce(sum(output),0), count(*)";

/// One session's token use. `available` is false whenever the numbers are not known —
/// the interface then shows "not provided" instead of a zero that looks measured.
#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct SessionTokens {
    pub(crate) available: bool,
    pub(crate) session: Option<String>,
    pub(crate) input: i64,
    pub(crate) cache_read: i64,
    pub(crate) cache_write: i64,
    pub(crate) output: i64,
    pub(crate) calls: i64,
    pub(crate) source: String,
    pub(crate) error: Option<String>,
}

pub(crate) fn ledger_path() -> Result<PathBuf, String> {
    if let Some(value) = std::env::var_os(LEDGER_ENV).filter(|value| !value.is_empty()) {
        return Ok(PathBuf::from(value));
    }
    let home = windows_home_dir()?;
    Ok(home.join(".dsh").join("bot").join("usage.sqlite"))
}

/// Reads the totals of one session from the ledger the machine actually uses.
pub(crate) fn session_tokens(session: Option<&str>) -> SessionTokens {
    match ledger_path() {
        Ok(path) => session_tokens_in(&path, session),
        Err(error) => SessionTokens {
            error: Some(error),
            ..SessionTokens::default()
        },
    }
}

/// Same read, against an explicit file; tests and the negative case both use this.
pub(crate) fn session_tokens_in(path: &std::path::Path, session: Option<&str>) -> SessionTokens {
    let source = path.to_string_lossy().to_string();
    let Some(session) = session.map(str::trim).filter(|session| !session.is_empty()) else {
        return SessionTokens {
            source,
            error: Some("需要插件上报当前会话".to_string()),
            ..SessionTokens::default()
        };
    };
    match read(path, session) {
        Ok(totals) => totals,
        Err(error) => SessionTokens {
            session: Some(session.to_string()),
            source,
            error: Some(error),
            ..SessionTokens::default()
        },
    }
}

fn read(path: &std::path::Path, session: &str) -> Result<SessionTokens, String> {
    if !path.exists() {
        return Err(format!("账本不存在：{}", path.to_string_lossy()));
    }
    let database = Database::open_read_only(path).map_err(|error| format!("账本打不开：{error}"))?;
    let statement = format!("select {COLUMNS} from calls where session = ?1");
    let rows = database
        .query_ints(&statement, &[session])
        .map_err(|error| format!("账本读不了：{error}"))?;
    let Some(row) = rows.first() else {
        return Err("账本没有返回合计行".to_string());
    };
    let value = |index: usize| row.get(index).copied().unwrap_or_default();
    let totals = SessionTokens {
        available: value(4) > 0,
        session: Some(session.to_string()),
        input: value(0),
        cache_read: value(1),
        cache_write: value(2),
        output: value(3),
        calls: value(4),
        source: path.to_string_lossy().to_string(),
        error: None,
    };
    if !totals.available {
        return Ok(SessionTokens {
            error: Some("这个会话在账本里还没有记录".to_string()),
            ..totals
        });
    }
    Ok(totals)
}

/// The command the card calls; it reads the session from the receiver's last presence.
#[tauri::command]
pub(crate) fn dsh_session_tokens() -> SessionTokens {
    let mounted = crate::agent_status::event_receiver::status().presence;
    session_tokens(mounted.as_ref().and_then(|presence| presence.mounted_session_id.as_deref()))
}

#[cfg(test)]
#[path = "usage_ledger_tests.rs"]
mod usage_ledger_tests;
