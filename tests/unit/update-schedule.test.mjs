import assert from "node:assert/strict";
import test from "node:test";
import {
  CHECK_RETRY_MS,
  PERIODIC_CHECK_MS,
  isCheckDue,
  nextCheckDelayMs,
} from "../../src/features/updater/updateSchedule.ts";

test("a failed check retries after five minutes, a healthy one after six hours", () => {
  assert.equal(CHECK_RETRY_MS, 5 * 60_000);
  assert.equal(PERIODIC_CHECK_MS, 6 * 60 * 60_000);
  assert.equal(nextCheckDelayMs(true), CHECK_RETRY_MS);
  assert.equal(nextCheckDelayMs(false), PERIODIC_CHECK_MS);
});

test("the next check is due only after the right delay has elapsed", () => {
  const now = 1_000_000_000;
  assert.equal(isCheckDue(now + 4 * 60_000, now, true), false, "retry too early");
  assert.equal(isCheckDue(now + 5 * 60_000, now, true), true, "retry exactly at five minutes");
  assert.equal(isCheckDue(now + 6 * 60 * 60_000 - 1, now, false), false, "periodic too early");
  assert.equal(isCheckDue(now + 6 * 60 * 60_000, now, false), true, "periodic at six hours");
  assert.equal(isCheckDue(now + 5 * 60_000, now, false), false, "a healthy check does not use the retry delay");
});
