import { test } from "node:test";
import assert from "node:assert/strict";

const {
  MAGPIE_MISSING,
  magpieFailureReason,
  magpieMissingLabel,
  nonzeroBalances,
  quotaDetail,
  quotaSummary,
  quotaTiers,
  spendTiers,
  windowPercent,
  windowShortName,
  sessionTokenDetail,
  sessionTokenSummary,
  spendDetail,
  spendLabel,
  tokenCount,
} = await import("../../src/features/agent-status/dshSpend.ts");

const tokens = {
  available: true, session: "s2", input: 111860, cacheRead: 896, cacheWrite: 0, output: 12028,
  calls: 17, source: "usage.sqlite", error: null,
};

const usage = {
  base: "http://127.0.0.1:3425", reachable: true, error: null,
  spend: { cost: 11.6782, period: "30d", calls: 7644, errors: 24, unpriced: 0 },
  spendToday: 7.9466, spendWeek: 14.1033, spendMonth: 14.1001,
  windows: [
    { name: "5 hours", kind: "plan", remaining: 82, used: 18, resetsAt: "2026-11-16T09:41:40Z" },
    { name: "Month", kind: "plan", remaining: 88, used: 12, resetsAt: "2026-12-15T01:46:57Z" },
  ],
  balances: ["¥0.00"],
  readAt: "2026-11-16T09:04:00Z",
};

test("token counts keep their grouping and never carry money", () => {
  assert.equal(tokenCount(111860), "111,860");
  assert.equal(sessionTokenSummary(tokens), "111,860 in · 12,028 out");
  assert.equal(sessionTokenDetail(tokens), "cache 896 read · 0 write · 17 calls");
  assert.equal(sessionTokenSummary({ ...tokens, available: false }), null);
  assert.ok(!sessionTokenSummary(tokens).includes("$"));
});

test("spend carries its own period, and a missing gateway says so", () => {
  assert.equal(spendLabel(usage), "近 30 天 · $11.68");
  assert.equal(spendDetail(usage), "7,644 次调用");
  assert.equal(spendDetail({ ...usage, spend: { ...usage.spend, unpriced: 3 } }), "7,644 次调用 · 有 3 次调用未计价");
  assert.equal(magpieMissingLabel({ ...usage, reachable: false }), MAGPIE_MISSING);
  assert.equal(magpieMissingLabel(null), MAGPIE_MISSING);
  assert.equal(spendLabel({ ...usage, reachable: false, spend: null }), null);
});

test("quota repeats each window and the gateway's balance string verbatim", () => {
  assert.equal(quotaSummary(usage), "剩余 5 hours 82% · 剩余 Month 88% · ¥0.00");
  assert.ok(quotaDetail(usage).startsWith("重置 5 hours · "));
  assert.equal(quotaSummary({ ...usage, windows: [], balances: [] }), null);
});

test("the island's spend row keeps today, 7 days and 30 days apart", () => {
  assert.equal(spendTiers(usage, { today: "今天", week: "7天", month: "30天" }), "今天 $7.95 · 7天 $14.10 · 30天 $14.10");
  // A missing bucket stays a dash: the 30-day figure must never fill in for today.
  assert.equal(spendTiers({ ...usage, spendWeek: null }, { today: "今天", week: "7天", month: "30天" }),
    "今天 $7.95 · 7天 — · 30天 $14.10");
  assert.equal(spendTiers({ ...usage, spendToday: null, spendWeek: null, spendMonth: null }, { today: "今天", week: "7天", month: "30天" }), null);
  assert.equal(spendTiers(null, { today: "today", week: "7d", month: "30d" }), null);
});

test("the island's quota row reads the field the mode asks for", () => {
  const windows = [
    { name: "5 hours", kind: "plan", remaining: 79, used: 20, resetsAt: null },
    { name: "7 days", kind: "plan", remaining: 87, used: 13, resetsAt: null },
    { name: "Month", kind: "plan", remaining: 86, used: 12, resetsAt: null },
  ];
  const withWindows = { ...usage, windows, balances: [] };
  assert.equal(quotaTiers(withWindows, "remaining", "月"), "5h 79% · 7d 87% · 月 86%");
  // Used comes straight from `used`, not from 100 minus remaining: 20, not 21.
  assert.equal(quotaTiers(withWindows, "used", "月"), "5h 20% · 7d 13% · 月 12%");
  // Only a window that lacks the wanted field is turned around, and it says so with ≈.
  const half = { ...usage, windows: [{ ...windows[0], used: null }], balances: [] };
  assert.equal(quotaTiers(half, "used", "月"), "5h ≈21%");
  assert.deepEqual(windowPercent({ name: "5 hours", kind: "plan", remaining: 79, used: null, resetsAt: null }, "used"),
    { value: 21, derived: true });
  assert.equal(quotaTiers({ ...usage, windows: [], balances: [] }, "remaining", "月"), null);
});

test("a zero balance stays out of the line, a real one comes along", () => {
  assert.deepEqual(nonzeroBalances(["¥0.00"]), []);
  assert.deepEqual(nonzeroBalances(["¥0.00", "¥12.30"]), ["¥12.30"]);
  assert.equal(quotaTiers({ ...usage, balances: ["¥0.00", "¥12.30"] }, "remaining", "月"),
    "5h 82% · 月 88% · ¥12.30");
});

test("window names shorten to one line", () => {
  assert.equal(windowShortName("5 hours", "月"), "5h");
  assert.equal(windowShortName("7 days", "月"), "7d");
  assert.equal(windowShortName("Month", "月"), "月");
  assert.equal(windowShortName("Month", "mo"), "mo");
  assert.equal(windowShortName("2 weeks", "月"), "2 weeks");
});

test("a gateway that did not answer says why", () => {
  assert.equal(magpieFailureReason("error sending request: Connection refused (os error 10061)"), "refused");
  assert.equal(magpieFailureReason("operation timed out"), "timeout");
  assert.equal(magpieFailureReason("unexpected end of file"), "silent");
  assert.equal(magpieFailureReason(null), "silent");
});
