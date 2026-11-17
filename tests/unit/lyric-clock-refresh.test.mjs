import assert from "node:assert/strict";
import test from "node:test";
import { lyricClockRefreshKey } from "../../src/features/media/lyrics/lyricClockRefresh.ts";
import { lineIndexAt, playbackPosition } from "../../src/features/media/lyrics/lrc.ts";

const lines = [
  { timeMs: 0, text: "first" },
  { timeMs: 30_000, text: "second" },
  { timeMs: 60_000, text: "third" },
];

test("lines arriving on the collapsed flip refresh the clock even if the player never updated", () => {
  // The player's updatedAt stays fixed across expand -> collapse; only line availability flips.
  const expanded = lyricClockRefreshKey(false, 1_000, "playing", 0);
  const collapsed = lyricClockRefreshKey(true, 1_000, "playing", 0);
  assert.notEqual(collapsed, expanded);

  // Other boundaries still work on their own.
  assert.notEqual(lyricClockRefreshKey(true, 2_000, "playing", 0), collapsed);
  assert.notEqual(lyricClockRefreshKey(true, 1_000, "paused", 0), collapsed);
  assert.notEqual(lyricClockRefreshKey(true, 1_000, "playing", 500), collapsed);
});

test("at the collapsed flip a refreshed clock picks the line being sung now", () => {
  // updatedAt is fixed at t=0. The island was expanded for 40 s with no lines; on collapse the
  // real clock is t=40_000, so the second line is playing. A clock left at the pre-expansion
  // instant (t=0) would still show the first line — the stale-line symptom.
  const state = { positionMs: 0, durationMs: 180_000, updatedAt: 0, playbackStatus: "playing" };
  const staleNow = 0;
  const refreshedNow = 40_000;

  const stale = lineIndexAt(lines, playbackPosition(state, staleNow) + 200);
  const refreshed = lineIndexAt(lines, playbackPosition(state, refreshedNow) + 200);
  assert.equal(stale, 0);
  assert.equal(refreshed, 1);
  assert.equal(lines[refreshed].text, "second");
});
