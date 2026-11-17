import assert from "node:assert/strict";
import test from "node:test";
import { dshBalanceLabel } from "../../src/features/agent-status/dshUsage.ts";

const usage = (total, currency = "USD") => ({
  updatedAt: 0,
  available: true,
  wallets: [{ currency, total, granted: "0", toppedUp: "0" }],
  source: "dshApi",
});

test("a balance with four decimals is rounded to exactly two", () => {
  assert.equal(dshBalanceLabel(usage("8.2567")), "$8.26", "rounded, not truncated, to two places");
  assert.equal(dshBalanceLabel(usage("8.2500")), "$8.25");
});

test("a short balance is padded to exactly two decimals", () => {
  assert.equal(dshBalanceLabel(usage("8.5")), "$8.50");
  assert.equal(dshBalanceLabel(usage("8")), "$8.00");
});

test("currency symbol follows the wallet and a missing wallet has no label", () => {
  assert.equal(dshBalanceLabel(usage("12.345", "CNY")), "¥12.35");
  assert.equal(dshBalanceLabel({ ...usage("1"), wallets: [] }), "");
});
