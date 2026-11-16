//! Spend and quota, read from the Magpie gateway when one is running.
//!
//! This is a separate source from the token ledger on purpose: Magpie reports a global
//! rolling period, so its numbers must never be shown as "this session". When Magpie is
//! absent the two rows say so and the session row keeps working.

use std::time::Duration;

use serde::Serialize;

const DEFAULT_BASE: &str = "http://127.0.0.1:3425";
const BASE_ENV: &str = "FLOATISLE_MAGPIE_BASE";
const TIMEOUT: Duration = Duration::from_secs(2);

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct MagpieSpend {
    pub(crate) cost: f64,
    pub(crate) period: String,
    pub(crate) calls: i64,
    pub(crate) errors: i64,
    pub(crate) unpriced: i64,
}

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct MagpieWindow {
    pub(crate) name: String,
    pub(crate) kind: Option<String>,
    pub(crate) remaining: Option<f64>,
    pub(crate) used: Option<f64>,
    pub(crate) resets_at: Option<String>,
}

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct MagpieUsage {
    pub(crate) base: String,
    pub(crate) reachable: bool,
    pub(crate) error: Option<String>,
    pub(crate) spend: Option<MagpieSpend>,
    pub(crate) windows: Vec<MagpieWindow>,
    pub(crate) balances: Vec<String>,
    pub(crate) read_at: Option<String>,
}

pub(crate) fn base_url() -> String {
    std::env::var(BASE_ENV)
        .ok()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
        .unwrap_or_else(|| DEFAULT_BASE.to_string())
        .trim_end_matches('/')
        .to_string()
}

#[tauri::command]
pub(crate) async fn probe_magpie_usage() -> MagpieUsage {
    read_at(&base_url()).await
}

/// Reads both documents of one gateway; a half-answer is still reported with its error.
pub(crate) async fn read_at(base: &str) -> MagpieUsage {
    let mut usage = MagpieUsage {
        base: base.to_string(),
        ..MagpieUsage::default()
    };
    let Ok(client) = reqwest::Client::builder()
        .timeout(TIMEOUT)
        .redirect(reqwest::redirect::Policy::none())
        .build()
    else {
        usage.error = Some("HTTP client unavailable".to_string());
        return usage;
    };

    match fetch(&client, &format!("{base}/v1/magpie/usage")).await {
        Ok(document) => {
            usage.reachable = true;
            usage.spend = parse_spend(&document);
        }
        Err(error) => {
            usage.error = Some(error);
            return usage;
        }
    }

    match fetch(&client, &format!("{base}/v1/magpie/quotas")).await {
        Ok(document) => {
            let (windows, balances, read_at) = parse_quotas(&document);
            usage.windows = windows;
            usage.balances = balances;
            usage.read_at = read_at;
        }
        Err(error) => usage.error = Some(error),
    }
    usage
}

async fn fetch(client: &reqwest::Client, url: &str) -> Result<String, String> {
    let response = client
        .get(url)
        .send()
        .await
        .map_err(|error| format!("Magpie 未运行（{url}）：{error}"))?;
    if !response.status().is_success() {
        return Err(format!("Magpie 拒绝了 {url}：HTTP {}", response.status()));
    }
    response
        .text()
        .await
        .map_err(|error| format!("Magpie 的应答读不出来：{error}"))
}

fn number(value: Option<&serde_json::Value>) -> Option<f64> {
    value.and_then(|value| value.as_f64())
}

fn text(value: Option<&serde_json::Value>) -> Option<String> {
    value
        .and_then(|value| value.as_str())
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_string)
}

fn parse_spend(body: &str) -> Option<MagpieSpend> {
    let document: serde_json::Value = serde_json::from_str(body).ok()?;
    let spend = document.get("spend").unwrap_or(&document);
    let cost = number(spend.get("cost"))?;
    Some(MagpieSpend {
        cost,
        period: text(spend.get("period")).unwrap_or_else(|| "30d".to_string()),
        calls: number(spend.get("calls")).unwrap_or_default() as i64,
        errors: number(spend.get("errors")).unwrap_or_default() as i64,
        unpriced: number(spend.get("unpriced")).unwrap_or_default() as i64,
    })
}

fn parse_quotas(body: &str) -> (Vec<MagpieWindow>, Vec<String>, Option<String>) {
    let Ok(document) = serde_json::from_str::<serde_json::Value>(body) else {
        return (Vec::new(), Vec::new(), None);
    };
    let mut read_at = text(document.get("readAt")).or_else(|| text(document.get("ts")));
    let mut windows = Vec::new();
    let mut balances = Vec::new();
    // The gateway answers a list envelope: {"object":"list","data":[provider…]}.
    let Some(providers) = document.get("data").and_then(|value| value.as_array()) else {
        return (windows, balances, read_at);
    };
    for provider in providers {
        let provider_name = text(provider.get("provider")).unwrap_or_default();
        let kind = text(provider.get("kind"));
        if read_at.is_none() {
            read_at = text(provider.get("readAt"));
        }
        if let Some(balance) = text(provider.get("balance")) {
            // The gateway's own string, never reformatted: a zero balance is information.
            balances.push(balance);
        }
        let Some(list) = provider.get("windows").and_then(|value| value.as_array()) else {
            continue;
        };
        for window in list {
            let name = text(window.get("name")).unwrap_or_else(|| provider_name.clone());
            windows.push(MagpieWindow {
                name,
                kind: kind.clone(),
                remaining: number(window.get("remaining")),
                used: number(window.get("used")),
                resets_at: text(window.get("resetsAt")),
            });
        }
    }
    (windows, balances, read_at)
}

#[cfg(test)]
#[path = "magpie_usage_tests.rs"]
mod magpie_usage_tests;
