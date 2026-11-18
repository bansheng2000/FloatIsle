import assert from "node:assert/strict";
import test from "node:test";

import { DEFAULT_SETTINGS, normalizeSettings } from "../../src/features/appearance/settings.ts";
import { TUCK_PREVIEW_MS } from "../../src/features/appearance/settingsLimits.ts";
import { isNoTuckBlocked, noTuckEdge } from "../../src/windows/island/noTuckConditions.ts";
import { idlePlan } from "../../src/windows/island/idleCollapse.ts";
import { isReservedSystemShortcut, shortcutKey } from "../../src/windows/settings/shortcutConflicts.ts";
import { parseTuckPreview } from "../../src/windows/settings/storage.ts";

const blockedSettings = {
  autoTuck: true,
  tuckDelaySeconds: 30,
  autoCollapseSeconds: 30,
};

test("E3 three 'don't tuck while' switches default off, including on legacy saves", () => {
  assert.equal(DEFAULT_SETTINGS.noTuckWhileAgent, false);
  assert.equal(DEFAULT_SETTINGS.noTuckWhileLyrics, false);
  assert.equal(DEFAULT_SETTINGS.noTuckWhileTimer, false);
  // A save from before E3 has none of the fields; normalize must keep them off.
  const normalized = normalizeSettings({});
  assert.equal(normalized.noTuckWhileAgent, false);
  assert.equal(normalized.noTuckWhileLyrics, false);
  assert.equal(normalized.noTuckWhileTimer, false);
  // Persisted true values survive normalization.
  assert.equal(normalizeSettings({ noTuckWhileTimer: true }).noTuckWhileTimer, true);
});

test("E3 a switch only blocks when its own condition is live", () => {
  const allOn = { noTuckWhileAgent: true, noTuckWhileLyrics: true, noTuckWhileTimer: true };
  assert.equal(isNoTuckBlocked(allOn, { agentActive: false, lyricsShown: false, timerRunning: false }), false);
  assert.equal(isNoTuckBlocked(allOn, { agentActive: true, lyricsShown: false, timerRunning: false }), true);
  assert.equal(isNoTuckBlocked(allOn, { agentActive: false, lyricsShown: true, timerRunning: false }), true);
  assert.equal(isNoTuckBlocked(allOn, { agentActive: false, lyricsShown: false, timerRunning: true }), true);
  // A condition without its switch enabled does not block.
  assert.equal(
    isNoTuckBlocked(
      { noTuckWhileAgent: true, noTuckWhileLyrics: false, noTuckWhileTimer: false },
      { agentActive: false, lyricsShown: true, timerRunning: true },
    ),
    false,
  );
});

test("E3 lyrics condition follows the shown slot, not merely a playing song", () => {
  // The wiring feeds `collapsedItem === "lyrics"` as lyricsShown; a song playing with
  // another collapsed panel selected must not satisfy the lyrics switch.
  const switches = { noTuckWhileAgent: false, noTuckWhileLyrics: true, noTuckWhileTimer: false };
  assert.equal(isNoTuckBlocked(switches, { agentActive: true, lyricsShown: false, timerRunning: false }), false);
  assert.equal(isNoTuckBlocked(switches, { agentActive: false, lyricsShown: true, timerRunning: false }), true);
});

test("E3 a live condition cancels the collapsed island's tuck plan", () => {
  assert.deepEqual(idlePlan("collapsed", blockedSettings, false, false, false), { seconds: 30, action: "tuck" });
  assert.equal(idlePlan("collapsed", blockedSettings, false, false, true), null);
  // The open island's collapse plan is untouched by the tuck block.
  assert.deepEqual(idlePlan("expanded", blockedSettings, false, false, true), { seconds: 30, action: "collapse" });
});

test("E3 the 'call the island out' edge fires only when a block first becomes active", () => {
  assert.equal(noTuckEdge(true, false), true);
  assert.equal(noTuckEdge(true, true), false);
  assert.equal(noTuckEdge(false, true), false);
  assert.equal(noTuckEdge(false, false), false);
});

test("E3 tuck preview lasts 2.5 seconds and refuses stale or malformed requests", () => {
  assert.equal(TUCK_PREVIEW_MS, 2500);
  const fresh = JSON.stringify({ shape: "thread", requestedAt: 1000 });
  assert.equal(parseTuckPreview(fresh, 1500), "thread");
  assert.equal(parseTuckPreview(fresh, 2500), null); // older than the 1s acceptance window
  assert.equal(parseTuckPreview(JSON.stringify({ shape: "bogus", requestedAt: 1000 }), 1000), null);
  assert.equal(parseTuckPreview(JSON.stringify({ shape: "edge" }), 1000), null);
  assert.equal(parseTuckPreview("not json", 1000), null);
  assert.equal(parseTuckPreview(null, 1000), null);
});

test("E3 Windows shell combinations are reserved and cannot be recorded", () => {
  for (const combo of ["Alt+Tab", "Win+L", "Ctrl+Alt+Del", "Alt+F4", "Win+R", "Win+D", "Ctrl+Shift+Esc"]) {
    assert.equal(isReservedSystemShortcut(combo), true, `${combo} must be reserved`);
  }
  // Case, spacing and modifier order do not let a reserved combo slip through.
  assert.equal(isReservedSystemShortcut("aLt + tAb"), true);
  assert.equal(isReservedSystemShortcut("Alt+Ctrl+Del"), true);
  assert.equal(isReservedSystemShortcut("META+l"), true);
  // Ordinary shortcuts remain recordable.
  assert.equal(isReservedSystemShortcut("Ctrl+Shift+W"), false);
  assert.equal(isReservedSystemShortcut("Ctrl+Alt+T"), false);
  assert.equal(shortcutKey("CTRL+shift+w"), "ctrl+shift+w");
});
