mod download;
mod elevated;
mod manifest;
mod netease;
mod record;
mod service;

use serde::Serialize;
use std::sync::atomic::{AtomicBool, Ordering};
use tauri::{Emitter, Manager};

pub(crate) const PROGRESS_EVENT: &str = "media-plugin-progress";
static BUSY: AtomicBool = AtomicBool::new(false);

#[derive(Debug, Serialize)]
pub(crate) struct PluginError {
    pub(super) kind: &'static str,
    detail: String,
}
impl PluginError {
    pub(super) fn new(kind: &'static str, detail: impl Into<String>) -> Self {
        Self {
            kind,
            detail: detail.into(),
        }
    }
    pub(super) fn network(error: reqwest::Error) -> Self {
        Self::new("Network", error.to_string())
    }
}
/// (E5-3) Map low-level failure text to a user-actionable reason.
/// Unknown cases return "Io" so pre-existing behavior is preserved.
/// This is text/kind heuristics only — no live drive-type or lock probing; real-machine
/// classification is unverified (see the E5 acceptance ledger).
pub(crate) fn classify_io_text(text: &str) -> &'static str {
    let text = text.to_lowercase();
    if text.contains("loader already exists") || text.contains("foreign loader") {
        "ForeignLoader"
    } else if text.contains("being used by another process")
        || text.contains("sharing violation")
        || text.contains("locked")
    {
        "FileInUse"
    } else if text.contains("network drive")
        || text.contains("mapped drive")
        || text.contains("network path")
        || text.contains("remote drive")
    {
        "NetworkDrive"
    } else if text.contains("focusd-tmp")
        || text.contains("temporary")
        || text.contains("temp file")
    {
        "TempInterrupted"
    } else if text.contains("access is denied") || text.contains("permission denied") {
        "SecuritySoftware"
    } else {
        "Io"
    }
}

/// (E5-3) Classify an IO error; falls back to text heuristics for kinds with no direct mapping.
fn classify_io_error(error: &std::io::Error) -> &'static str {
    match error.kind() {
        std::io::ErrorKind::PermissionDenied => "SecuritySoftware",
        std::io::ErrorKind::WouldBlock | std::io::ErrorKind::TimedOut => "FileInUse",
        _ => classify_io_text(&error.to_string()),
    }
}

impl From<std::io::Error> for PluginError {
    fn from(error: std::io::Error) -> Self {
        let detail = error.to_string();
        let kind = classify_io_error(&error);
        Self::new(kind, detail)
    }
}
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Progress {
    stage: &'static str,
    detail: Option<String>,
}
fn progress(app: &tauri::AppHandle, stage: &'static str) {
    let _ = app.emit(
        PROGRESS_EVENT,
        Progress {
            stage,
            detail: None,
        },
    );
}
struct Operation;
impl Operation {
    fn acquire() -> Result<Self, PluginError> {
        BUSY.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
            .map(|_| Self)
            .map_err(|_| PluginError::new("Busy", "Another plugin operation is running"))
    }
}
impl Drop for Operation {
    fn drop(&mut self) {
        BUSY.store(false, Ordering::SeqCst);
    }
}

#[tauri::command]
pub(crate) async fn netease_plugin_status() -> Result<netease::NeteasePluginStatus, PluginError> {
    tauri::async_runtime::spawn_blocking(netease::status)
        .await
        .map_err(|e| PluginError::new("Io", e.to_string()))
}
async fn operate(
    app: tauri::AppHandle,
    remove: bool,
) -> Result<service::OperationResult, PluginError> {
    let guard = Operation::acquire()?;
    let data = app
        .path()
        .app_data_dir()
        .map_err(|e| PluginError::new("Io", e.to_string()))?;
    // The whole operation (including file IO and UAC waiting) runs off the IPC thread.
    tauri::async_runtime::spawn_blocking(move || {
        let _guard = guard;
        let result = if remove {
            service::uninstall(&app, &data)
        } else {
            tauri::async_runtime::block_on(service::install(&app, &data))
        };
        progress(&app, if result.is_ok() { "done" } else { "error" });
        result
    })
    .await
    .map_err(|e| PluginError::new("Io", e.to_string()))?
}
#[tauri::command]
pub(crate) async fn install_netease_plugin(
    app: tauri::AppHandle,
) -> Result<service::OperationResult, PluginError> {
    operate(app, false).await
}
#[tauri::command]
pub(crate) async fn uninstall_netease_plugin(
    app: tauri::AppHandle,
) -> Result<service::OperationResult, PluginError> {
    operate(app, true).await
}

/// 结束网易云（`relaunch` 时随后重新打开），供安装后重启、或卸载前关闭使用。只在用户点击后调用。
#[tauri::command]
pub(crate) async fn close_netease(relaunch: bool) -> Result<(), PluginError> {
    tauri::async_runtime::spawn_blocking(move || netease::close(relaunch))
        .await
        .map_err(|e| PluginError::new("Io", e.to_string()))?
}

pub(crate) fn handle_startup() {
    elevated::handle_startup();
}
pub(crate) fn diagnostic_state(directory: &std::path::Path) -> &'static str {
    netease::plugin_state(directory).0
}
macro_rules! tauri_commands {
    ($next:ident [$($done:tt)*] $($rest:tt)*) => {
        $next!([$($done)*
            crate::media_plugins::netease_plugin_status,
            crate::media_plugins::install_netease_plugin,
            crate::media_plugins::uninstall_netease_plugin,
            crate::media_plugins::close_netease,
        ] $($rest)*)
    };
}
pub(crate) use tauri_commands;
#[cfg(test)]
mod tests;
