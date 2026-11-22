//! Crash-guard on/off state, readable from Rust before Tauri starts.
//!
//! The switch cannot live in the frontend's localStorage: the guardian is
//! spawned from `setup`, and a deciding guardian runs before Tauri at all. The
//! switch therefore lives in a tiny ASCII-named marker under `%LOCALAPPDATA%`.
//! A *missing* marker means ON, so existing users keep the guard after an
//! upgrade without anything being written. Only turning the guard off creates
//! the marker; turning it back on removes it.

use std::{
    env,
    ffi::OsStr,
    fs,
    path::{Path, PathBuf},
};

const DISABLED_FILE: &str = "floatisle-guard-disabled.flag";

/// Pure resolver: prefer LOCALAPPDATA, fall back to USERPROFILE.
fn state_path_from(
    local_app_data: Option<&OsStr>,
    user_profile: Option<&OsStr>,
) -> Option<PathBuf> {
    for value in [local_app_data, user_profile].into_iter().flatten() {
        let text = value.to_string_lossy();
        if !text.trim().is_empty() {
            return Some(Path::new(text.as_ref()).join(DISABLED_FILE));
        }
    }
    None
}

fn state_path() -> Option<PathBuf> {
    state_path_from(
        env::var_os("LOCALAPPDATA").as_deref(),
        env::var_os("USERPROFILE").as_deref(),
    )
}

/// Pure: a missing "disabled" marker means the guard is ON by default.
fn enabled_from_disabled(disabled_exists: bool) -> bool {
    !disabled_exists
}

/// Current switch, read in `setup` and again by the guardian before relaunching.
pub(crate) fn crash_guard_enabled() -> bool {
    let disabled = state_path().map(|path| path.exists()).unwrap_or(false);
    enabled_from_disabled(disabled)
}

fn write_disabled(path: &Path) {
    // Best effort: a failure here must never break the settings command.
    let _ = fs::write(path, "disabled\n");
}

fn remove_disabled(path: &Path) {
    if path.exists() {
        let _ = fs::remove_file(path);
    }
}

/// Business logic behind `set_crash_guard_enabled`. Turning the guard off also
/// immediately ends the guardian this main process spawned (V5/G3).
fn set_enabled(enabled: bool) -> Result<(), String> {
    let Some(path) = state_path() else {
        return Err(
            "Failed to resolve the user profile directory for the crash-guard switch.".to_string(),
        );
    };
    if enabled {
        remove_disabled(&path);
    } else {
        write_disabled(&path);
        crate::system::guardian::terminate_spawned_guardian();
    }
    Ok(())
}

/// Reads the crash-guard switch. Missing state file means enabled.
#[tauri::command]
pub fn get_crash_guard_enabled() -> Result<bool, String> {
    Ok(crash_guard_enabled())
}

/// Writes the crash-guard switch. Turning it off stops the live guardian too.
#[tauri::command]
pub fn set_crash_guard_enabled(enabled: bool) -> Result<(), String> {
    set_enabled(enabled)
}

#[cfg(test)]
mod tests {
    use super::{enabled_from_disabled, state_path_from, write_disabled, DISABLED_FILE};
    use std::{
        ffi::OsStr,
        path::PathBuf,
        time::{SystemTime, UNIX_EPOCH},
    };

    #[test]
    fn missing_marker_means_enabled() {
        assert!(enabled_from_disabled(false));
        assert!(!enabled_from_disabled(true));
    }

    #[test]
    fn state_path_prefers_local_app_data() {
        let path = state_path_from(
            Some(OsStr::new(r"C:\\Users\\LY\\AppData\\Local")),
            Some(OsStr::new(r"C:\\Users\\LY")),
        );
        assert_eq!(
            path,
            Some(PathBuf::from(r"C:\\Users\\LY\\AppData\\Local").join(DISABLED_FILE))
        );
    }

    #[test]
    fn state_path_falls_back_to_user_profile() {
        let path = state_path_from(None, Some(OsStr::new(r"C:\\Users\\LY")));
        assert_eq!(
            path,
            Some(PathBuf::from(r"C:\\Users\\LY").join(DISABLED_FILE))
        );
    }

    #[test]
    fn state_path_rejects_empty_values() {
        assert_eq!(
            state_path_from(Some(OsStr::new("  ")), Some(OsStr::new(""))),
            None
        );
        assert_eq!(state_path_from(None, None), None);
    }

    #[test]
    fn disabled_marker_write_is_best_effort() {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0);
        let path = std::env::temp_dir().join(format!("floatisle-guard-state-test-{nanos}.flag"));
        assert!(!path.exists());
        write_disabled(&path);
        assert!(path.exists());
        let _ = std::fs::remove_file(&path);
        assert!(!path.exists());
    }
}
