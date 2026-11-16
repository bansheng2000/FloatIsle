import { test } from "node:test";
import assert from "node:assert/strict";

const {
  MAGPIE_MISSING,
  magpieMissingLabel,
  quotaDetail,
  quotaSummary,
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
  assert.equal(quotaSummary(usage), "5 hours 82% · Month 88% · ¥0.00");
  assert.ok(quotaDetail(usage).startsWith("重置 5 hours · "));
  assert.equal(quotaSummary({ ...usage, windows: [], balances: [] }), null);
});
