//! Payload shapes copied from a live Magpie (0.1.4224) so the parser is checked against
//! what the gateway really answers, not against a guess.

use super::{describe, parse_quotas, parse_spend, parse_week_cost};

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

/// The gateway answers any period except today with the 30-day totals at the top level, so the
/// seven-day figure has to be summed from the daily buckets; a bucket without a cost leaves the
/// whole week unknown rather than a total that silently misses a day.
const WEEK: &str = r#"{"period":"7d","cost":14.1,"bucket":"day","series":[{"label":"2026-11-10","cost":1.5},{"label":"2026-11-11","cost":2.25},{"label":"2026-11-12","cost":0.75},{"label":"2026-11-13","cost":3.0},{"label":"2026-11-14","cost":4.1},{"label":"2026-11-15","cost":1.2},{"label":"2026-11-16","cost":1.3}]}"#;

#[test]
fn the_seven_day_figure_is_the_sum_of_its_daily_buckets() {
    let week = parse_week_cost(WEEK).unwrap_or_else(|| panic!("the live 7d document did not parse"));
    assert!((week - 14.1).abs() < 1e-9, "summed cost was {week}");
}

#[test]
fn a_week_missing_a_daily_cost_stays_unknown() {
    let partial = r#"{"period":"7d","series":[{"label":"2026-11-15","cost":1.2},{"label":"2026-11-16"}]}"#;
    assert!(parse_week_cost(partial).is_none());
    assert!(parse_week_cost(r#"{"period":"7d","series":[]}"#).is_none());
    assert!(parse_week_cost("not json").is_none());
}

struct Cause(&'static str, Option<Box<Cause>>);

impl std::fmt::Display for Cause {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.write_str(self.0)
    }
}

impl std::fmt::Debug for Cause {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        formatter.write_str(self.0)
    }
}

impl std::error::Error for Cause {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        self.1.as_deref().map(|cause| cause as &(dyn std::error::Error + 'static))
    }
}

/// The settings page tells a refusal from a timeout by the words in this message, and reqwest's
/// own `Display` drops the cause that carries them.
#[test]
fn the_reason_keeps_the_cause_chain_that_names_a_refusal() {
    let error = Cause("error sending request for url (http://127.0.0.1:3999)", Some(Box::new(Cause("tcp connect error: Connection refused (os error 10061)", None))));
    let detail = describe(&error);
    assert_eq!(detail, "error sending request for url (http://127.0.0.1:3999): tcp connect error: Connection refused (os error 10061)");
}
