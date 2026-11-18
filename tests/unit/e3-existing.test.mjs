// E3 "already done" guarantees. A~C shipped these; this batch must not regress them.
// They are native invariants, so the guard reads the source instead of mocking a window.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = new URL("../../", import.meta.url);
const read = (p) => readFileSync(fileURLToPath(new URL(p, root)), "utf8");

const windowRs = read("src-tauri/src/win32/window.rs");
const focusRs = read("src-tauri/src/island/focus.rs");
const passthroughRs = read("src-tauri/src/island/passthrough.rs");
const appearanceStorage = read("src/features/appearance/storage.ts");

test("E3 focusing never synthesizes an Alt press", () => {
  // Bringing the island forward attaches to the foreground thread instead of
  // tapping VK_MENU, which would flash another window's Alt menu bar.
  assert.match(windowRs, /AttachThreadInput/);
  assert.doesNotMatch(windowRs, /keybd_event/);
  assert.doesNotMatch(windowRs, /VK_MENU/);
  assert.doesNotMatch(focusRs, /keybd_event|VK_MENU|send_key_down/);
});

test("E3 tuck wake zones are native hit tests, not DOM transparent blocks", () => {
  assert.match(passthroughRs, /set_ignore_cursor_events/);
  assert.match(passthroughRs, /point_in_notch/);
  assert.match(passthroughRs, /tucked_hot_zone_contains/);
  assert.match(passthroughRs, /Duration::from_millis\(12\)/);
});

test("E3 the one-time thin-line default migration is still guarded", () => {
  assert.match(appearanceStorage, /TUCK_SHAPE_DEFAULT_MIGRATION_KEY/);
  assert.match(appearanceStorage, /applyTuckShapeMigration/);
  assert.match(appearanceStorage, /getItem\(TUCK_SHAPE_DEFAULT_MIGRATION_KEY\)\s*!==\s*"1"/);
});
