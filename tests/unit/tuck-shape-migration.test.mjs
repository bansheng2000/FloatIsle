import assert from "node:assert/strict";
import test from "node:test";
import {
  applyTuckShapeMigration,
  COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY,
  loadSettings,
  STORAGE_KEY,
  TUCK_SHAPE_DEFAULT_MIGRATION_KEY,
} from "../../src/features/appearance/storage.ts";
import { DEFAULT_SETTINGS } from "../../src/features/appearance/settings.ts";

function withStorage(entries, run) {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, "window");
  const values = new Map(entries);
  const localStorage = {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
  };
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: { localStorage, crypto: globalThis.crypto },
  });
  try {
    run(values);
  } finally {
    if (descriptor) Object.defineProperty(globalThis, "window", descriptor);
    else delete globalThis.window;
  }
}

test("a brand-new install sets the tuck-shape migration marker and defaults to a thread", () => {
  withStorage([], (values) => {
    assert.equal(loadSettings().tuckShape, "thread");
    assert.equal(values.get(TUCK_SHAPE_DEFAULT_MIGRATION_KEY), "1");
  });
});

test("first upgrade forces the thread shape and records the marker so it happens once", () => {
  withStorage(
    [
      [STORAGE_KEY, JSON.stringify({ tuckShape: "edge" })],
      [COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY, "1"],
    ],
    (values) => {
      const loaded = loadSettings();
      assert.equal(loaded.tuckShape, "thread");
      assert.equal(values.get(TUCK_SHAPE_DEFAULT_MIGRATION_KEY), "1");
      assert.equal(JSON.parse(values.get(STORAGE_KEY)).tuckShape, "thread");
    },
  );
});

test("a shape the user picked survives a restart and is never overwritten again", () => {
  withStorage(
    [
      [STORAGE_KEY, JSON.stringify({ tuckShape: "hidden" })],
      [COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY, "1"],
      [TUCK_SHAPE_DEFAULT_MIGRATION_KEY, "1"],
    ],
    (values) => {
      assert.equal(loadSettings().tuckShape, "hidden");
      assert.equal(JSON.parse(values.get(STORAGE_KEY)).tuckShape, "hidden");
    },
  );
});

test("the migration helper is a no-op once done and forces a thread only on the first pass", () => {
  const chosen = { ...DEFAULT_SETTINGS, tuckShape: "edge" };
  assert.deepEqual(applyTuckShapeMigration(chosen, true), chosen);
  assert.equal(applyTuckShapeMigration(chosen, false).tuckShape, "thread");
});
