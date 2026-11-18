// Watchdog opt-out guarantees. The marker is a native file and the guard is a
// PowerShell script, so these guards read source instead of mocking a window.
// Each one must go red when its source fact is broken.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = new URL("../../", import.meta.url);
const read = (p) => readFileSync(fileURLToPath(new URL(p, root)), "utf8");

const mod = read("src-tauri/src/system/guard_optout.rs");
const sysMod = read("src-tauri/src/system/mod.rs");
const lib = read("src-tauri/src/lib.rs");
const tray = read("src-tauri/src/tray.rs");
const settings = read("src-tauri/src/settings_window/mod.rs");
const guard = readFileSync("C:\\Users\\LY\\floatisle-guard.ps1", "utf8");

test("OPTOUT marker path is ASCII-only and prefers LOCALAPPDATA", () => {
  assert.match(mod, /const FLAG_FILE: &str = "floatisle-guard-optout\.flag";/);
  assert.match(mod, /env::var_os\("LOCALAPPDATA"\)/);
  assert.match(mod, /env::var_os\("USERPROFILE"\)/);
});

test("OPTOUT module is wired into the system module", () => {
  assert.match(sysMod, /pub\(crate\) mod guard_optout;/);
});

test("OPTOUT tray Quit writes the marker before exit", () => {
  assert.match(tray, /"quit" => \{/);
  const quitBody = tray.slice(tray.indexOf('"quit" => {'));
  assert.match(quitBody.slice(0, 260), /guard_optout::mark_deliberate_exit\(\);/);
  assert.match(quitBody.slice(0, 260), /app\.exit\(0\)/);
  assert.ok(
    quitBody.indexOf("mark_deliberate_exit") < quitBody.indexOf("app.exit(0)"),
    "marker must be written before the process exits"
  );
});

test("OPTOUT About-page quit_island writes the marker before exit", () => {
  const start = settings.indexOf("quit_island");
  assert.ok(start >= 0);
  const body = settings.slice(start, start + 300);
  assert.match(body, /guard_optout::mark_deliberate_exit\(\);/);
  assert.match(body, /app\.exit\(0\)/);
});

test("OPTOUT early startup clears the marker", () => {
  const start = lib.indexOf(".setup(|app| {");
  assert.ok(start >= 0);
  const head = lib.slice(start, start + 400);
  assert.match(head, /guard_optout::clear_deliberate_exit\(\);/);
  assert.ok(
    head.indexOf("clear_deliberate_exit") < head.indexOf("build_tray"),
    "marker must be cleared before the tray is ready"
  );
});

test("OPTOUT guard checks the marker and skips, else relaunches", () => {
  assert.match(guard, /floatisle-guard-optout\.flag/);
  assert.match(guard, /OPTOUT_SKIP/);
  assert.match(guard, /Test-Path -LiteralPath \$flagPath/, "must actually test for marker presence");
  const skip = guard.indexOf("OPTOUT_SKIP");
  const relaunch = guard.indexOf("MethodName Create");
  assert.ok(skip >= 0 && relaunch >= 0 && skip < relaunch, "skip check must precede the WMI Create launch");
  assert.match(guard, /\$env:LOCALAPPDATA/);
  assert.match(guard, /\$env:USERPROFILE/);
});

test("OPTOUT crash exit paths do NOT write the marker", () => {
  // lib.rs close-requested hides the window, and the build-failure exit is an
  // abnormal path — neither may call mark_deliberate_exit.
  assert.match(lib, /api\.prevent_close\(\)/);
  assert.match(lib, /window\.hide\(\)/);
  const abnormal = lib.slice(lib.indexOf("std::process::exit(1)") - 120, lib.indexOf("std::process::exit(1)") + 60);
  assert.doesNotMatch(abnormal, /mark_deliberate_exit/);
});
