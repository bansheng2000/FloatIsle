import assert from "node:assert/strict";
import test from "node:test";
import { updateNeedsAttention } from "../../src/features/updater/updateAttention.ts";

// Once an update is found the dot stays on through every transient stage — a
// re-check, a download failure, or "latest" must not blink it off. It clears
// only after install (done) or when the user dismisses it.
const pendingStages = ["available", "downloading", "ready", "checking", "latest", "error"];

test("a found update keeps the attention dot through every non-terminal stage", () => {
  for (const stage of pendingStages) {
    assert.equal(updateNeedsAttention(true, stage, false), true, stage);
  }
});

test("the dot clears after install or on dismissal even if a version was found", () => {
  assert.equal(updateNeedsAttention(true, "done", false), false);
  assert.equal(updateNeedsAttention(true, "ready", true), false);
});

test("a check that never found anything never shows the dot", () => {
  for (const stage of pendingStages) {
    assert.equal(updateNeedsAttention(false, stage, false), false, stage);
  }
  assert.equal(updateNeedsAttention(false, "error", false), false, "a failed check without a found update");
});
