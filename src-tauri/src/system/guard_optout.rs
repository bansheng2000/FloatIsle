//! Watchdog opt-out flag (FloatIsle standalone guard).
//!
//! The Task Scheduler watchdog relaunches the island whenever it is not running.
//! A user who deliberately quits from the tray or the About page does not want it
//! to come back, so those two exits write a small marker file. On the next normal
//! startup the marker is removed, which resumes the watchdog. Crashes and forced
//! kills (for example a DSH session teardown) never reach this code, so no marker
//! is written and the watchdog still restores the island.
//!
//! The marker lives in `%LOCALAPPDATA%\floatisle-guard-optout.flag` (ASCII-only
//! path) so the PowerShell watchdog can test it without the encoding problems a
//! Chinese path causes under Windows PowerShell 5.1.

use std::{
    env,
    ffi::OsStr,
    fs,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

const FLAG_FILE: &str = "floatisle-guard-optout.flag";

/// Pure resolver: prefer LOCALAPPDATA, fall back to USERPROFILE.
fn flag_path_from(local_app_data: Option<&OsStr>, user_profile: Option<&OsStr>) -> Option<PathBuf> {
    for value in [local_app_data, user_profile].into_iter().flatten() {
        let text = value.to_string_lossy();
        if !text.trim().is_empty() {
            return Some(Path::new(text.as_ref()).join(FLAG_FILE));
        }
    }
    None
}

fn flag_path() -> Option<PathBuf> {
    flag_path_from(
        env::var_os("LOCALAPPDATA").as_deref(),
        env::var_os("USERPROFILE").as_deref(),
    )
}

fn write_flag(path: &Path) {
    let seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    // Best effort only: a failure here must never block a deliberate exit.
    let _ = fs::write(path, format!("deliberate-exit {seconds}\n"));
}

fn remove_flag(path: &Path) {
    if path.exists() {
        // Best effort cleanup; a stale marker is harmless once the app is running.
        let _ = fs::remove_file(path);
    }
}

/// Called from the tray Quit and the About-page quit command.
pub(crate) fn mark_deliberate_exit() {
    if let Some(path) = flag_path() {
        write_flag(&path);
    }
}

/// Called early in `setup`: a successful launch means the previous session is
/// over, so any leftover marker must stop suppressing the watchdog.
pub(crate) fn clear_deliberate_exit() {
    if let Some(path) = flag_path() {
        remove_flag(&path);
    }
}

#[cfg(test)]
mod tests {
    use super::{flag_path_from, remove_flag, write_flag, FLAG_FILE};
    use std::time::{SystemTime, UNIX_EPOCH};
    use std::{
        ffi::OsStr,
        path::PathBuf,
        sync::atomic::{AtomicU64, Ordering},
    };

    static COUNTER: AtomicU64 = AtomicU64::new(0);

    fn temp_flag_path() -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0);
        let n = COUNTER.fetch_add(1, Ordering::Relaxed);
        std::env::temp_dir().join(format!("floatisle-optout-test-{nanos}-{n}.flag"))
    }

    #[test]
    fn flag_path_prefers_local_app_data() {
        let path = flag_path_from(
            Some(OsStr::new(r"C:\Users\LY\AppData\Local")),
            Some(OsStr::new(r"C:\Users\LY")),
        );
        assert_eq!(
            path,
            Some(PathBuf::from(r"C:\Users\LY\AppData\Local").join(FLAG_FILE)),
            "must use LOCALAPPDATA"
        );
    }

    #[test]
    fn flag_path_falls_back_to_user_profile() {
        let path = flag_path_from(None, Some(OsStr::new(r"C:\Users\LY")));
        assert_eq!(
            path,
            Some(PathBuf::from(r"C:\Users\LY").join(FLAG_FILE)),
            "must use USERPROFILE fallback"
        );
    }

    #[test]
    fn flag_path_rejects_empty_values() {
        assert_eq!(
            flag_path_from(Some(OsStr::new("  ")), Some(OsStr::new(""))),
            None
        );
        assert_eq!(flag_path_from(None, None), None);
    }

    #[test]
    fn write_then_remove_flag_roundtrip() {
        let path = temp_flag_path();
        assert!(!path.exists());
        write_flag(&path);
        assert!(path.exists(), "write_flag must create the marker");
        let body = std::fs::read_to_string(&path).unwrap_or_default();
        assert!(
            body.starts_with("deliberate-exit "),
            "marker must carry an ASCII reason, got {body:?}"
        );
        remove_flag(&path);
        assert!(!path.exists(), "remove_flag must delete the marker");
    }
}
