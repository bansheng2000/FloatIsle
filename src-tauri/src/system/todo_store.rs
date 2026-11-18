//! The todo list's file of record: one JSON document in the user's Documents
//! folder, with rotating backups, an atomic replace and a lock so two running
//! islands never write over each other.
//!
//! The webview keeps its `localStorage` copy as the running cache; this file is
//! what survives a cleared cache, so a broken file is never silently replaced:
//! it is renamed aside and the newest backup is restored instead.

use serde::Serialize;
use std::{
    fs::{self, File, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    sync::Mutex,
    time::{SystemTime, UNIX_EPOCH},
};

use super::windows_home_dir;
use crate::win32::known_folder;

const DATA_DIR_NAME: &str = "FloatIsle";
/// (E5-4) The pre-rebrand folder name; kept so existing users' todos are not orphaned.
const LEGACY_DATA_DIR_NAME: &str = "fly-island";
const DOCUMENT_NAME: &str = "todos.json";
const LOCK_NAME: &str = "todos.lock";
const BACKUP_DIR: &str = "backups";
const EXPORT_DIR: &str = "exports";
const BACKUP_PREFIX: &str = "todos-";
const KEEP_BACKUPS: usize = 10;

/// Held for the lifetime of the process once a todo file has been loaded, so a
/// second copy of the island (a dev build next to the installed one) can read
/// the list but never overwrite it.
static LOCK: Mutex<Option<File>> = Mutex::new(None);

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TodoLoad {
    /// `ok`, `missing` (no file yet) or `corrupt` (the file was unreadable).
    pub state: &'static str,
    pub writable: bool,
    pub directory: String,
    pub file_path: String,
    pub document: Option<serde_json::Value>,
    pub updated_at: Option<String>,
    /// The renamed copy of the unreadable file, when `state` is `corrupt`.
    pub corrupt_path: Option<String>,
    pub restored_from: Option<String>,
    pub backup_count: u32,
    pub detail: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TodoSave {
    pub directory: String,
    pub file_path: String,
    pub bytes: usize,
    pub backup_path: Option<String>,
    pub backup_count: u32,
}

/// The todo folder: the user's chosen one, else `<Documents>\FloatIsle`.
/// (E5-4) Pick the folder under `documents`: keep using the legacy folder if it
/// already exists, otherwise the current brand folder. Pure so it can be unit-tested
/// with a temp directory instead of touching the user's real Documents.
fn choose_data_folder(documents: &Path) -> PathBuf {
    let legacy = documents.join(LEGACY_DATA_DIR_NAME);
    if legacy.is_dir() {
        legacy
    } else {
        documents.join(DATA_DIR_NAME)
    }
}

fn resolve(directory: Option<String>) -> Result<PathBuf, String> {
    if let Some(directory) = directory
        .as_deref()
        .map(str::trim)
        .filter(|text| !text.is_empty())
    {
        return Ok(PathBuf::from(directory));
    }

    let documents = known_folder::documents()
        .or_else(|| windows_home_dir().ok().map(|home| home.join("Documents")));

    documents
        .map(|documents| choose_data_folder(&documents))
        .ok_or_else(|| "Failed to resolve the Documents folder.".to_string())
}

fn document_path(directory: &Path) -> PathBuf {
    directory.join(DOCUMENT_NAME)
}

fn backup_dir(directory: &Path) -> PathBuf {
    directory.join(BACKUP_DIR)
}

/// `yyyyMMdd-HHmmss` in local-less UTC, which is enough to order backups.
fn stamp(now: SystemTime) -> String {
    let seconds = now
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| elapsed.as_secs())
        .unwrap_or_default();
    let days = i64::try_from(seconds / 86_400).unwrap_or_default();
    let rest = seconds % 86_400;
    let (year, month, day) = civil_from_days(days);
    format!(
        "{year:04}{month:02}{day:02}-{:02}{:02}{:02}",
        rest / 3_600,
        rest % 3_600 / 60,
        rest % 60
    )
}

/// Howard Hinnant's `civil_from_days`: days since 1970-01-01 to a calendar date.
fn civil_from_days(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = u32::try_from(doy - (153 * mp + 2) / 5 + 1).unwrap_or(1);
    let m = u32::try_from(if mp < 10 { mp + 3 } else { mp - 9 }).unwrap_or(1);
    (if m <= 2 { y + 1 } else { y }, m, d)
}

fn backup_names(directory: &Path) -> Vec<PathBuf> {
    let Ok(entries) = fs::read_dir(backup_dir(directory)) else {
        return Vec::new();
    };

    let mut names: Vec<PathBuf> = entries
        .flatten()
        .map(|entry| entry.path())
        .filter(|path| {
            path.extension()
                .is_some_and(|extension| extension == "json")
                && path
                    .file_name()
                    .and_then(|name| name.to_str())
                    .is_some_and(|name| name.starts_with(BACKUP_PREFIX))
        })
        .collect();
    names.sort();
    names
}

fn newest_backup(directory: &Path) -> Option<PathBuf> {
    backup_names(directory).pop()
}

/// Copies the current document into `backups/` and keeps the newest ten.
fn backup_current(directory: &Path) -> Option<String> {
    let document = document_path(directory);
    if !document.is_file() {
        return None;
    }

    let target_dir = backup_dir(directory);
    fs::create_dir_all(&target_dir).ok()?;
    let target = target_dir.join(format!("{BACKUP_PREFIX}{}.json", stamp(SystemTime::now())));
    fs::copy(&document, &target).ok()?;

    let mut names = backup_names(directory);
    while names.len() > KEEP_BACKUPS {
        if let Some(oldest) = names.first() {
            let _ = fs::remove_file(oldest);
        }
        names.remove(0);
    }

    Some(target.to_string_lossy().to_string())
}

/// `tauri-plugin-single-instance` is the first guard: a second launch exits before it gets here.
/// This file lock is the second guard, for the cases that plugin cannot cover — a second copy
/// from another install path, a stale window after an update, or a build without the plugin.
/// The lock is the open handle, not the file's existence, so a window that was killed outright
/// releases it when the process dies; the leftover empty file does not keep anyone out.
fn try_lock(directory: &Path) -> bool {
    let Ok(mut held) = LOCK.lock() else {
        return false;
    };
    if held.is_some() {
        return true;
    }

    use std::os::windows::fs::OpenOptionsExt;

    let lock_path = directory.join(LOCK_NAME);
    // A second process cannot open the file when sharing is denied, which is how
    // it learns it must stay read-only.
    let opened = OpenOptions::new()
        .create(true)
        .truncate(false)
        .write(true)
        .share_mode(0)
        .open(&lock_path);

    match opened {
        Ok(file) => {
            *held = Some(file);
            true
        }
        Err(_) => false,
    }
}

/// Writes the document through a temporary file and an atomic replace, then
/// reads it back so a silent short write cannot pass as success.
fn write_document(
    directory: &Path,
    content: &str,
    backup: Option<String>,
) -> Result<TodoSave, String> {
    fs::create_dir_all(directory)
        .map_err(|error| format!("Failed to create {}: {error}", directory.display()))?;

    let document = document_path(directory);
    let temporary = directory.join(format!("{DOCUMENT_NAME}.tmp"));

    let written = (|| -> std::io::Result<()> {
        let mut file = File::create(&temporary)?;
        file.write_all(content.as_bytes())?;
        file.sync_all()?;
        Ok(())
    })();
    written.map_err(|error| format!("Failed to write {}: {error}", temporary.display()))?;

    fs::rename(&temporary, &document).map_err(|error| {
        let _ = fs::remove_file(&temporary);
        format!("Failed to replace {}: {error}", document.display())
    })?;

    let read_back = fs::read_to_string(&document)
        .map_err(|error| format!("Failed to read back {}: {error}", document.display()))?;
    if read_back != content {
        return Err(format!(
            "{} did not read back as written ({} of {} bytes).",
            document.display(),
            read_back.len(),
            content.len()
        ));
    }

    Ok(TodoSave {
        directory: directory.to_string_lossy().to_string(),
        file_path: document.to_string_lossy().to_string(),
        bytes: read_back.len(),
        backup_path: backup,
        backup_count: u32::try_from(backup_names(directory).len()).unwrap_or(0),
    })
}

#[tauri::command]
pub fn get_todo_data_directory(directory: Option<String>) -> Result<String, String> {
    resolve(directory).map(|path| path.to_string_lossy().to_string())
}

/// The export folder under the same root, so a Markdown export does not land in
/// a second, differently named place.
#[tauri::command]
pub fn get_todo_export_directory(directory: Option<String>) -> Result<String, String> {
    let directory = resolve(directory)?.join(EXPORT_DIR);
    Ok(directory.to_string_lossy().to_string())
}

#[tauri::command]
pub fn load_todo_document(directory: Option<String>) -> Result<TodoLoad, String> {
    let directory = resolve(directory)?;
    fs::create_dir_all(&directory)
        .map_err(|error| format!("Failed to create {}: {error}", directory.display()))?;
    let writable = try_lock(&directory);

    let document = document_path(&directory);
    let directory_text = directory.to_string_lossy().to_string();
    let file_text = document.to_string_lossy().to_string();
    let backups = u32::try_from(backup_names(&directory).len()).unwrap_or(0);

    let unreadable = |detail: String| TodoLoad {
        state: "corrupt",
        writable,
        directory: directory_text.clone(),
        file_path: file_text.clone(),
        document: None,
        updated_at: None,
        corrupt_path: None,
        restored_from: None,
        backup_count: backups,
        detail: Some(detail),
    };

    if !document.is_file() {
        return Ok(TodoLoad {
            state: "missing",
            writable,
            directory: directory_text,
            file_path: file_text,
            document: None,
            updated_at: None,
            corrupt_path: None,
            restored_from: None,
            backup_count: backups,
            detail: None,
        });
    }

    let text = match fs::read_to_string(&document) {
        Ok(text) => text,
        Err(error) => {
            return Ok(unreadable(format!(
                "Failed to read {}: {error}",
                document.display()
            )))
        }
    };

    if let Some(load) = parse_document(&text, &directory, writable) {
        return Ok(load);
    }

    // Broken: keep the evidence, then try the newest backup.
    let corrupt = directory.join(format!("todos.corrupt-{}.json", stamp(SystemTime::now())));
    let _ = fs::rename(&document, &corrupt);
    let corrupt_text = Some(corrupt.to_string_lossy().to_string());

    let Some(backup) = newest_backup(&directory) else {
        return Ok(TodoLoad {
            corrupt_path: corrupt_text,
            ..unreadable("The todo file was unreadable and no backup existed.".to_string())
        });
    };

    match fs::read_to_string(&backup) {
        Ok(backup_text) => {
            let mut load =
                parse_document(&backup_text, &directory, writable).unwrap_or_else(|| {
                    unreadable(format!(
                        "The backup {} was unreadable too.",
                        backup.display()
                    ))
                });
            load.state = "corrupt";
            load.corrupt_path = corrupt_text;
            load.restored_from = Some(backup.to_string_lossy().to_string());
            if load.document.is_some() {
                let _ = fs::copy(&backup, &document);
            }
            Ok(load)
        }
        Err(error) => Ok(TodoLoad {
            corrupt_path: corrupt_text,
            ..unreadable(format!(
                "Failed to read the backup {}: {error}",
                backup.display()
            ))
        }),
    }
}

fn parse_document(text: &str, directory: &Path, writable: bool) -> Option<TodoLoad> {
    let document: serde_json::Value = serde_json::from_str(text).ok()?;
    let updated_at = document
        .get("updatedAt")
        .and_then(serde_json::Value::as_str)
        .map(str::to_string);

    Some(TodoLoad {
        state: "ok",
        writable,
        directory: directory.to_string_lossy().to_string(),
        file_path: document_path(directory).to_string_lossy().to_string(),
        document: Some(document),
        updated_at,
        corrupt_path: None,
        restored_from: None,
        backup_count: u32::try_from(backup_names(directory).len()).unwrap_or(0),
        detail: None,
    })
}

#[tauri::command]
pub fn save_todo_document(directory: Option<String>, content: String) -> Result<TodoSave, String> {
    let directory = resolve(directory)?;
    fs::create_dir_all(&directory)
        .map_err(|error| format!("Failed to create {}: {error}", directory.display()))?;

    if !try_lock(&directory) {
        return Err(
            "Another FloatIsle window owns the todo file; this one is read-only.".to_string(),
        );
    }

    serde_json::from_str::<serde_json::Value>(&content)
        .map_err(|error| format!("Refusing to write a todo file that is not JSON: {error}"))?;

    let backup = backup_current(&directory);
    write_document(&directory, &content, backup)
}

/// The one-off copy of the old `localStorage` payload, kept next to the backups
/// so a migration can be undone by hand.
#[tauri::command]
pub fn backup_todo_local_storage(
    directory: Option<String>,
    content: String,
) -> Result<String, String> {
    let directory = resolve(directory)?;
    let target_dir = backup_dir(&directory);
    fs::create_dir_all(&target_dir)
        .map_err(|error| format!("Failed to create {}: {error}", target_dir.display()))?;

    let target = target_dir.join(format!("localstorage-{}.json", stamp(SystemTime::now())));
    fs::write(&target, content)
        .map_err(|error| format!("Failed to write {}: {error}", target.display()))?;
    Ok(target.to_string_lossy().to_string())
}

/// Checks a folder the user picked by writing and removing a probe file, so the
/// todo file never starts out unwritable.
#[tauri::command]
pub fn validate_todo_data_directory(directory: String) -> Result<String, String> {
    let directory = directory.trim();
    if directory.is_empty() {
        return Err("The folder path is empty.".to_string());
    }

    let path = PathBuf::from(directory);
    if !path.is_absolute() {
        return Err(
            "Choose an absolute folder path, for example D:\\Documents\\FloatIsle.".to_string(),
        );
    }

    fs::create_dir_all(&path)
        .map_err(|error| format!("Failed to create {}: {error}", path.display()))?;

    let probe = path.join(".floatisle-write-test");
    fs::write(&probe, b"ok")
        .map_err(|error| format!("Cannot write in {}: {error}", path.display()))?;
    let _ = fs::remove_file(&probe);

    Ok(path.to_string_lossy().to_string())
}

#[tauri::command]
pub fn open_todo_data_directory(directory: Option<String>) -> Result<(), String> {
    let directory = resolve(directory)?;
    fs::create_dir_all(&directory)
        .map_err(|error| format!("Failed to create {}: {error}", directory.display()))?;
    tauri_plugin_opener::reveal_item_in_dir(&directory).map_err(|error| error.to_string())
}

/// Removes the todo document from a folder the user is leaving, once the new
/// folder has been written. Only the fixed file name is ever removed.
#[tauri::command]
pub fn remove_todo_document(directory: Option<String>) -> Result<bool, String> {
    let directory = resolve(directory)?;
    let document = document_path(&directory);
    if !document.is_file() {
        return Ok(false);
    }

    fs::remove_file(&document)
        .map_err(|error| format!("Failed to remove {}: {error}", document.display()))?;
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::{civil_from_days, choose_data_folder, resolve, stamp, DATA_DIR_NAME, LEGACY_DATA_DIR_NAME};
    use std::time::{Duration, UNIX_EPOCH};

    #[test]
    fn e5_legacy_folder_is_reused_only_when_present() {
        let root = std::env::temp_dir().join(format!(
            "floatisle-todo-folder-{}",
            std::process::id()
        ));
        let legacy = root.join(LEGACY_DATA_DIR_NAME);
        let current = root.join(DATA_DIR_NAME);
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        // No legacy folder yet -> current brand folder.
        assert_eq!(choose_data_folder(&root), current);
        // Legacy folder appears -> keep using it so existing todos stay visible.
        std::fs::create_dir_all(&legacy).unwrap();
        assert_eq!(choose_data_folder(&root), legacy);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn the_default_folder_sits_under_the_documents_folder() {
        let directory = resolve(None).expect("default folder");
        assert!(directory.is_absolute(), "{directory:?}");
        assert!(directory.ends_with("FloatIsle"), "{directory:?}");
    }

    #[test]
    fn a_chosen_folder_wins_over_the_default() {
        let directory = resolve(Some("  D:\\work\\todos  ".to_string())).expect("chosen folder");
        assert_eq!(directory, std::path::Path::new("D:\\work\\todos"));
    }

    #[test]
    fn a_blank_chosen_folder_falls_back_to_the_default() {
        let chosen = resolve(Some("   ".to_string())).expect("default folder");
        let default = resolve(None).expect("default folder");
        assert_eq!(chosen, default);
    }

    #[test]
    fn stamps_order_like_time() {
        let earlier = stamp(UNIX_EPOCH + Duration::from_secs(1_700_000_000));
        let later = stamp(UNIX_EPOCH + Duration::from_secs(1_700_000_061));
        assert!(earlier < later, "{earlier} vs {later}");
        assert_eq!(earlier.len(), 15, "{earlier}");
    }

    #[test]
    fn civil_dates_match_known_days() {
        assert_eq!(civil_from_days(0), (1970, 1, 1));
        assert_eq!(civil_from_days(19_723), (2024, 1, 1));
    }

    #[test]
    fn a_second_window_cannot_open_the_lock_file() {
        use std::os::windows::fs::OpenOptionsExt;

        let directory = std::env::temp_dir().join(format!("floatisle-lock-{}", std::process::id()));
        std::fs::create_dir_all(&directory).expect("temp folder");
        let path = directory.join(super::LOCK_NAME);
        let open = || {
            std::fs::OpenOptions::new()
                .create(true)
                .truncate(false)
                .write(true)
                .share_mode(0)
                .open(&path)
        };

        let first = open().expect("first window takes the lock");
        // Sharing is denied, which is how a second window learns it must stay read-only.
        assert!(open().is_err(), "the lock file was shared twice");
        drop(first);
        assert!(open().is_ok(), "the lock stays usable after it is released");
        let _ = std::fs::remove_dir_all(&directory);
    }

    /// A window that is killed outright cannot release anything itself; the handle must go with
    /// the process, or every later launch would be stuck read-only on a file it cannot remove.
    #[test]
    fn a_killed_window_leaves_no_permanent_lock() {
        use std::os::windows::fs::OpenOptionsExt;
        use std::time::{Duration, Instant};

        let directory = std::env::temp_dir().join(format!("floatisle-kill-{}", std::process::id()));
        std::fs::create_dir_all(&directory).expect("temp folder");
        let path = directory.join(super::LOCK_NAME);
        let open = || {
            std::fs::OpenOptions::new()
                .create(true)
                .truncate(false)
                .write(true)
                .share_mode(0)
                .open(&path)
        };

        // Stands in for another FloatIsle: it holds the file with sharing denied.
        let script = format!(
            "$f=[IO.File]::Open('{}','OpenOrCreate','ReadWrite','None'); Start-Sleep -Seconds 30",
            path.display()
        );
        let mut child = std::process::Command::new("powershell")
            .args(["-NoProfile", "-Command", &script])
            .spawn()
            .expect("powershell");

        let mut held = false;
        let deadline = Instant::now() + Duration::from_secs(20);
        while Instant::now() < deadline {
            if open().is_err() {
                held = true;
                break;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        assert!(held, "the stand-in never took the lock");

        // `/F` is the same hard kill a user would do from the task manager.
        let killed = std::process::Command::new("taskkill")
            .args(["/F", "/PID", &child.id().to_string()])
            .status()
            .expect("taskkill");
        assert!(killed.success(), "taskkill refused");
        // /F already ended it; reaping keeps the test from leaving the child behind.
        let _ = child.wait();

        let mut acquired = false;
        let deadline = Instant::now() + Duration::from_secs(20);
        while Instant::now() < deadline {
            if open().is_ok() {
                acquired = true;
                break;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        assert!(acquired, "the file stayed locked after its owner was killed");
        assert!(path.exists(), "the lock file should still be on disk");
        let _ = std::fs::remove_dir_all(&directory);
    }
}
