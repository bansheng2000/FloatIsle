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
    /// The 30-day totals, kept for the settings card.
    pub(crate) spend: Option<MagpieSpend>,
    /// The island's three spend figures. Today and 30 days come from the top-level `cost`;
    /// the 7-day figure is the sum of the seven daily buckets, because the gateway answers a
    /// non-today period with the 30-day total at the top level.
    pub(crate) spend_today: Option<f64>,
    pub(crate) spend_week: Option<f64>,
    pub(crate) spend_month: Option<f64>,
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

    let today = fetch(&client, &format!("{base}/v1/magpie/usage?period=today")).await;
    let week = fetch(&client, &format!("{base}/v1/magpie/usage?period=7d")).await;
    let month = fetch(&client, &format!("{base}/v1/magpie/usage?period=30d")).await;
    if let (Err(error), Err(_), Err(_)) = (&today, &week, &month) {
        usage.error = Some(error.clone());
        return usage;
    }
    usage.reachable = true;
    usage.error = today.as_ref().err().cloned().or_else(|| month.as_ref().err().cloned());
    usage.spend_today = today.as_deref().ok().and_then(parse_spend).map(|spend| spend.cost);
    usage.spend_week = week.as_deref().ok().and_then(week_cost);
    let monthly = month.as_deref().ok().and_then(parse_spend);
    usage.spend_month = monthly.as_ref().map(|spend| spend.cost);
    usage.spend = monthly;

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
        .map_err(|error| format!("Magpie 未运行（{url}）：{}", describe(&error)))?;
    if !response.status().is_success() {
        return Err(format!("Magpie 拒绝了 {url}：HTTP {}", response.status()));
    }
    response
        .text()
        .await
        .map_err(|error| format!("Magpie 的应答读不出来：{error}"))
}

/// reqwest keeps the cause chain out of `Display`, and the cause is what tells a refusal from a
/// timeout; the settings page names the reason from this text, so carry the whole chain along.
fn describe(error: &dyn std::error::Error) -> String {
    let mut detail = error.to_string();
    let mut source = error.source();
    while let Some(cause) = source {
        detail.push_str(": ");
        detail.push_str(&cause.to_string());
        source = cause.source();
    }
    detail
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

/// The seven daily buckets added up. The gateway's top-level `cost` is the 30-day total for
/// every non-today period, so a 7-day figure must be summed rather than read from the envelope.
fn parse_week_cost(body: &str) -> Option<f64> {
    let document: serde_json::Value = serde_json::from_str(body).ok()?;
    let series = document.get("series")?.as_array()?;
    if series.is_empty() {
        return None;
    }
    let mut total = 0.0;
    for bucket in series {
        let cost = bucket.get("cost").and_then(|value| value.as_f64())?;
        total += cost;
    }
    Some(total)
}

/// The one seat the 7-day row reads: the buckets, never the envelope. Its own function so a test
/// can break exactly this wiring and see the row go wrong, not just the parser.
fn week_cost(body: &str) -> Option<f64> {
    parse_week_cost(body)
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
