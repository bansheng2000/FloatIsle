import assert from "node:assert/strict";
import test from "node:test";

import {
  selectableIds,
  toggleSelected,
  deletableSelectedIds,
  allNonFavoritesSelected,
} from "../../src/features/clipboard/clipboardSelection.ts";
import { CLIPBOARD_VISIBLE_ROWS } from "../../src/features/appearance/settingsLimits.ts";
import { normalizeSettings, DEFAULT_SETTINGS } from "../../src/features/appearance/settings.ts";

const items = [
  { id: "a", kind: "text", favorite: false, preview: "a" },
  { id: "b", kind: "text", favorite: true, preview: "b" },
  { id: "c", kind: "image", favorite: false, preview: "c" },
];

test("E2 select all excludes favourites", () => {
  assert.deepEqual(selectableIds(items), ["a", "c"]);
});

test("E2 toggle does not mutate the input set", () => {
  const original = new Set(["a"]);
  const next = toggleSelected(original, "c");
  assert.deepEqual([...original], ["a"]);
  assert.deepEqual([...next], ["a", "c"]);
  assert.deepEqual([...toggleSelected(next, "a")], ["c"]);
});

test("E2 batch delete never targets a favourite even if selected", () => {
  const selected = new Set(["a", "b", "c"]);
  assert.deepEqual(deletableSelectedIds(selected, items), ["a", "c"]);
});

test("E2 select-all state is true only when every non-favourite is selected", () => {
  assert.equal(allNonFavoritesSelected(new Set(["a", "c"]), items), true);
  assert.equal(allNonFavoritesSelected(new Set(["a"]), items), false);
  // No selectable items at all is not "all selected".
  assert.equal(allNonFavoritesSelected(new Set(), [{ id: "x", favorite: true }]), false);
});

test("E2 clipboard visible rows default to 4 and clamp to 3-8", () => {
  assert.equal(CLIPBOARD_VISIBLE_ROWS.default, 4);
  assert.equal(DEFAULT_SETTINGS.clipboardVisibleRows, 4);
  assert.equal(normalizeSettings({ clipboardVisibleRows: 1 }).clipboardVisibleRows, 3);
  assert.equal(normalizeSettings({ clipboardVisibleRows: 20 }).clipboardVisibleRows, 8);
  assert.equal(normalizeSettings({ clipboardVisibleRows: 6 }).clipboardVisibleRows, 6);
});
