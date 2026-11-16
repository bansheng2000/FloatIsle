//! Payload shapes copied from a live Magpie (0.1.4224) so the parser is checked against
//! what the gateway really answers, not against a guess.

use super::{parse_quotas, parse_spend};

const USAGE: &str = r#"{"period":"30d","since":"2026-10-18T00:00:00+08:00","calls":7428,"errors":24,"retried":4,"canceled":11,"input":23815287,"output":4701986,"cache_read":1005431850,"cache_write":0,"reasoning":1995531,"cost":11.190596045999968,"unpriced":3,"bucket":"day","series":[]}"#;

const QUOTAS: &str = r#"{"data":[{"provider":"opencode-go","name":"OpenCode Go","kind":"plan","windows":[{"name":"5 hours","used":14,"remaining":86,"resetsAt":"2026-11-16T09:41:40Z"},{"name":"7 days","used":10,"remaining":90,"resetsAt":"2026-11-23T00:00:00Z"},{"name":"Month","used":11,"remaining":89,"resetsAt":"2026-12-15T01:46:57Z"}],"readAt":"2026-11-16T16:33:06.4359429+08:00","last":true},{"provider":"zhipu","name":"Zhipu GLM","kind":"balance","windows":[],"balance":"¥0.00","readAt":"2026-11-16T16:33:06.3435676+08:00"}],"object":"list"}"#;

#[test]
fn the_spend_document_keeps_its_own_period_and_cost() {
    let spend = parse_spend(USAGE).unwrap_or_else(|| panic!("the live usage document did not parse"));
    assert_eq!(spend.period, "30d");
    assert_eq!(spend.calls, 7428);
    assert_eq!(spend.errors, 24);
    assert_eq!(spend.unpriced, 3);
    assert!((spend.cost - 11.190596045999968).abs() < f64::EPSILON);
}

#[test]
fn the_quota_document_yields_every_window_and_the_balance_string() {
    let (windows, balances, read_at) = parse_quotas(QUOTAS);
    assert_eq!(windows.len(), 3);
    assert_eq!(windows[0].name, "5 hours");
    assert_eq!(windows[0].remaining, Some(86.0));
    assert_eq!(windows[0].used, Some(14.0));
    assert_eq!(windows[0].resets_at.as_deref(), Some("2026-11-16T09:41:40Z"));
    assert_eq!(windows[2].name, "Month");
    assert_eq!(balances, vec!["¥0.00".to_string()]);
    assert_eq!(read_at.as_deref(), Some("2026-11-16T16:33:06.4359429+08:00"));
}

#[test]
fn a_document_that_is_not_the_gateway_answers_nothing_instead_of_zeroes() {
    assert!(parse_spend("<html>404</html>").is_none());
    assert!(parse_spend(r#"{"object":"list"}"#).is_none());
    let (windows, balances, read_at) = parse_quotas("not json");
    assert!(windows.is_empty());
    assert!(balances.is_empty());
    assert!(read_at.is_none());
}
