import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_SETTINGS,
  normalizeSettings,
  applyAppearanceSnapshot,
  COLOR_SETTING_KEYS,
} from "../../src/features/appearance/settings.ts";
import {
  applyE1Migrations,
  COLOR_FIELDS,
} from "../../src/features/appearance/storage.ts";
import {
  MAX_PRESETS,
  canAddPreset,
  addPreset,
  applyPreset,
  makeColorSnapshot,
  makeAppearanceSnapshot,
} from "../../src/features/appearance/presets/presetLogic.ts";
import { resolveColorScheme } from "../../src/features/appearance/colorScheme.ts";

function preset(kind, overrides = {}) {
  return { id: `${kind}-${Math.random()}`, name: "p", createdAt: 0, kind, snapshot: {}, ...overrides };
}

test("E1 defaults keep the long-standing dark classic look", () => {
  assert.equal(DEFAULT_SETTINGS.colorScheme, "dark");
  assert.equal(DEFAULT_SETTINGS.texture, "classic");
  assert.equal(DEFAULT_SETTINGS.islandFontSize, 14);
  assert.equal(DEFAULT_SETTINGS.collapsedWidth, 0);
  assert.equal(DEFAULT_SETTINGS.collapsedHeight, 100);
  assert.equal(DEFAULT_SETTINGS.pageTextColor, null);
});

test("E1 appearance fields are clamped to their allowed ranges", () => {
  assert.equal(normalizeSettings({ colorScheme: "dark" }).colorScheme, "dark");
  assert.equal(normalizeSettings({ colorScheme: "light" }).colorScheme, "light");
  assert.equal(normalizeSettings({ colorScheme: "system" }).colorScheme, "system");
  assert.equal(normalizeSettings({ colorScheme: "neon" }).colorScheme, "dark");
  assert.equal(normalizeSettings({ texture: "frosted" }).texture, "frosted");
  assert.equal(normalizeSettings({ texture: "liquidGlass" }).texture, "liquidGlass");
  assert.equal(normalizeSettings({ texture: "metal" }).texture, "classic");
  assert.equal(normalizeSettings({ islandFontSize: 99 }).islandFontSize, 20);
  assert.equal(normalizeSettings({ islandFontSize: 1 }).islandFontSize, 12);
  assert.equal(normalizeSettings({ islandFontSize: 16 }).islandFontSize, 16);
  assert.equal(normalizeSettings({ collapsedWidth: 999 }).collapsedWidth, 360);
  assert.equal(normalizeSettings({ collapsedWidth: 100 }).collapsedWidth, 280);
  assert.equal(normalizeSettings({ collapsedWidth: 320 }).collapsedWidth, 320);
  assert.equal(normalizeSettings({ collapsedWidth: 0 }).collapsedWidth, 0);
  assert.equal(normalizeSettings({ collapsedHeight: 999 }).collapsedHeight, 120);
  assert.equal(normalizeSettings({ collapsedHeight: 10 }).collapsedHeight, 80);
  assert.equal(normalizeSettings({ collapsedHeight: 110 }).collapsedHeight, 110);
  assert.equal(normalizeSettings({ pageTextColor: "#abcdef" }).pageTextColor, "#abcdef");
  assert.equal(normalizeSettings({ pageTextColor: "not-a-color" }).pageTextColor, null);
});

test("E1 colour refresh only touches users who never changed a colour, and offers a restore", () => {
  const neverChanged = applyE1Migrations(DEFAULT_SETTINGS, { colorRefreshDone: false, sortDone: true });
  assert.equal(neverChanged.offerColorRestore, true);
  for (const field of COLOR_FIELDS) {
    assert.equal(neverChanged.settings[field], DEFAULT_SETTINGS[field]);
  }

  const recoloured = { ...DEFAULT_SETTINGS, islandBackgroundColor: "#123456" };
  const kept = applyE1Migrations(recoloured, { colorRefreshDone: false, sortDone: true });
  assert.equal(kept.offerColorRestore, false);
  assert.equal(kept.settings.islandBackgroundColor, "#123456");

  const done = applyE1Migrations(DEFAULT_SETTINGS, { colorRefreshDone: true, sortDone: true });
  assert.equal(done.offerColorRestore, false);
});

test("E1 session sort flips from the old default once and never overrides a real choice", () => {
  const oldDefault = { ...DEFAULT_SETTINGS, agentSessionSort: "status" };
  const flipped = applyE1Migrations(oldDefault, { colorRefreshDone: true, sortDone: false });
  assert.equal(flipped.settings.agentSessionSort, "recent");

  const chosen = { ...DEFAULT_SETTINGS, agentSessionSort: "firstSeen" };
  const kept = applyE1Migrations(chosen, { colorRefreshDone: true, sortDone: false });
  assert.equal(kept.settings.agentSessionSort, "firstSeen");

  const done = applyE1Migrations(oldDefault, { colorRefreshDone: true, sortDone: true });
  assert.equal(done.settings.agentSessionSort, "status");
});

test("E1 presets are capped at eight per kind in the shared list", () => {
  assert.equal(MAX_PRESETS, 8);
  let list = [];
  for (let i = 0; i < MAX_PRESETS; i += 1) list = addPreset(list, preset("colors"));
  assert.equal(list.filter((p) => p.kind === "colors").length, 8);
  assert.equal(canAddPreset(list, "colors"), false);
  const overflow = addPreset(list, preset("colors"));
  assert.equal(overflow.filter((p) => p.kind === "colors").length, 8);
  // The other kind still has room because the lists are counted independently.
  assert.equal(canAddPreset(list, "appearance"), true);
  list = addPreset(list, preset("appearance"));
  assert.equal(list.filter((p) => p.kind === "appearance").length, 1);
});

test("E1 colour presets touch only palette fields; appearance presets touch the rest", () => {
  const settings = { ...DEFAULT_SETTINGS, islandBackgroundColor: "#010203", islandFontSize: 18 };
  const colorSnap = makeColorSnapshot(settings);
  assert.deepEqual(Object.keys(colorSnap).sort(), [...COLOR_SETTING_KEYS].sort());
  const appliedColor = applyPreset(settings, preset("colors", { snapshot: colorSnap }));
  assert.equal(appliedColor.islandBackgroundColor, "#010203");
  assert.equal(appliedColor.islandFontSize, 18);

  const appearanceSnap = makeAppearanceSnapshot(settings);
  assert.equal(appearanceSnap.islandFontSize, 18);
  assert.equal(appearanceSnap.colorScheme, "dark");
  const appliedAppearance = applyPreset(settings, preset("appearance", { snapshot: appearanceSnap }));
  assert.equal(appliedAppearance.islandFontSize, 18);

  // A partial snapshot only overwrites the keys it carries.
  const partial = applyAppearanceSnapshot(settings, { texture: "frosted" });
  assert.equal(partial.texture, "frosted");
  assert.equal(partial.islandFontSize, 18);
});

test("E1 system palette resolves to a concrete set", () => {
  assert.equal(resolveColorScheme("dark", true), "dark");
  assert.equal(resolveColorScheme("light", false), "light");
  assert.equal(resolveColorScheme("system", true), "light");
  assert.equal(resolveColorScheme("system", false), "dark");
});
