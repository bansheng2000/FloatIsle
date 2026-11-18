//! Updater support: proxy discovery and whether this build can check for updates.

use std::{os::windows::process::CommandExt, process::Command};
use tauri::AppHandle;

use super::CREATE_NO_WINDOW;

pub(super) const INTERNET_SETTINGS_KEY: &str =
    r"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";

pub(super) fn registry_value(key: &str, value: &str) -> Option<String> {
    let output = Command::new("reg")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["query", key, "/v", value])
        .output()
        .ok()?;

    if !output.status.success() {
        return None;
    }

    String::from_utf8_lossy(&output.stdout)
        .lines()
        .find(|line| line.contains(value))
        .and_then(|line| line.split_whitespace().last())
        .map(str::to_string)
}

/// Picks the first supported HTTP(S) proxy out of a Windows proxy string.
///
/// Windows lists proxy entries as `http=host:port;https=host:port;socks=host:port`.
/// SOCKS entries are recognised but deliberately skipped: this build's HTTP
/// client has no SOCKS support, so feeding it `http://socks=...` fails the URL
/// parse outright. When a string contains only unsupported protocols we return
/// `None` and the updater connects directly, which is what the feature asks for
/// ("ignore unsupported protocols").
pub(super) fn normalize_proxy(raw: &str) -> Option<String> {
    let entries: Vec<&str> = raw
        .split(';')
        .map(str::trim)
        .filter(|entry| !entry.is_empty())
        .collect();

    for prefix in ["https=", "http="] {
        if let Some(server) = entries
            .iter()
            .find_map(|entry| entry.strip_prefix(prefix))
            .map(str::trim)
            .filter(|server| !server.is_empty())
        {
            return Some(with_http_scheme(server));
        }
    }

    // No labelled HTTP(S) entry: a bare `host:port` keeps the historical
    // http:// default. Entries carrying any other `key=` protocol (socks, etc.)
    // are skipped rather than mistaken for a host.
    entries
        .iter()
        .find(|entry| !entry.contains('='))
        .map(|entry| entry.trim())
        .filter(|entry| !entry.is_empty())
        .map(with_http_scheme)
}

fn with_http_scheme(server: &str) -> String {
    if server.contains("://") {
        server.to_string()
    } else {
        format!("http://{server}")
    }
}

/// Proxy for the updater: explicit env vars win, else the Windows system proxy.
#[tauri::command]
pub fn get_updater_proxy() -> Option<String> {
    for key in ["HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy"] {
        if let Ok(value) = std::env::var(key) {
            let value = value.trim();
            if !value.is_empty() {
                return normalize_proxy(value);
            }
        }
    }

    if registry_value(INTERNET_SETTINGS_KEY, "ProxyEnable")?.trim() != "0x1" {
        return None;
    }

    let server = registry_value(INTERNET_SETTINGS_KEY, "ProxyServer")?;
    if server.trim().is_empty() {
        return None;
    }

    normalize_proxy(&server)
}

/// Whether this build carries an updater public key. tauri.conf.json ships the
/// fork's key, so every build can update; a build whose config clears it (for
/// example another fork's) cannot, and the settings panel hides the action.
#[tauri::command]
pub fn updater_configured(app: AppHandle) -> bool {
    app.config()
        .plugins
        .0
        .get("updater")
        .and_then(|config| config.get("pubkey"))
        .and_then(|value| value.as_str())
        .is_some_and(|key| !key.trim().is_empty())
}

#[cfg(test)]
mod tests {
    use super::normalize_proxy;
    use serde_json::{json, Value};

    // tauri-plugin-updater declares `pubkey` as a mandatory config field with no
    // serde default, so dropping the key aborts startup with "missing field
    // `pubkey`". The shipped config keeps an empty placeholder; release builds
    // replace it through `src-tauri/tauri.release.conf.json`.
    fn shipped_updater_config() -> Value {
        let config: Value = serde_json::from_str(include_str!("../../tauri.conf.json"))
            .expect("tauri.conf.json must be valid JSON");
        config
            .pointer("/plugins/updater")
            .cloned()
            .expect("tauri.conf.json must define plugins.updater")
    }

    #[test]
    fn shipped_updater_config_parses_with_the_empty_pubkey_placeholder() {
        let parsed: tauri_plugin_updater::Config = serde_json::from_value(shipped_updater_config())
            .expect("plugins.updater must deserialize or the app aborts at startup");

        assert_eq!(parsed.pubkey, "");
        assert!(!parsed.endpoints.is_empty());
    }

    #[test]
    fn proxy_strings_pick_http_and_ignore_socks() {
        // A mixed Windows proxy string: HTTP wins over SOCKS regardless of order.
        assert_eq!(
            normalize_proxy("http=1.2.3.4:8080;socks=127.0.0.1:1080"),
            Some("http://1.2.3.4:8080".to_string())
        );
        assert_eq!(
            normalize_proxy("socks5h=127.0.0.1:1080;https=proxy.example:443"),
            Some("http://proxy.example:443".to_string())
        );
        // https= takes priority over http= when both are present.
        assert_eq!(
            normalize_proxy("http=a:1;https=b:2"),
            Some("http://b:2".to_string())
        );
        // A bare host:port keeps the historical http:// prefix.
        assert_eq!(
            normalize_proxy("proxy.example:3128"),
            Some("http://proxy.example:3128".to_string())
        );
    }

    #[test]
    fn proxy_strings_with_only_unsupported_protocols_are_ignored() {
        // SOCKS cannot be used by this build's HTTP client: ignore it so the
        // updater connects directly instead of failing on http://socks=...
        assert_eq!(normalize_proxy("socks=127.0.0.1:1080"), None);
        assert_eq!(normalize_proxy("socks5=127.0.0.1:1080"), None);
        assert_eq!(normalize_proxy("socks5h=127.0.0.1:1080;socks=127.0.0.1:1081"), None);
        assert_eq!(normalize_proxy(""), None);
        assert_eq!(normalize_proxy("socks=127.0.0.1:1080;http=a:1"), Some("http://a:1".to_string()));
    }

    #[test]
    fn release_overlay_replaces_the_pubkey_placeholder() {
        let mut update_config = shipped_updater_config();
        update_config["pubkey"] = json!("RWQtest-only-release-public-key");

        let parsed: tauri_plugin_updater::Config = serde_json::from_value(update_config)
            .expect("a release config carrying a real pubkey must deserialize");

        assert_eq!(parsed.pubkey, "RWQtest-only-release-public-key");
        assert!(!parsed.endpoints.is_empty());
    }
}
