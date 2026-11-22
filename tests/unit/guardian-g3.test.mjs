// G3 crash-guard switch — source guards for the Rust-readable persistence,
// the two IPC commands, the off-kills-live-guardian path and the frontend
// toggle. The pure decision (missing file = enabled) is unit-tested in Rust.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

function readTauri(path) {
  return readFileSync(new URL(`../../src-tauri/src/${path}`, import.meta.url), "utf8");
}
function readSrc(path) {
  return readFileSync(new URL(`../../src/${path}`, import.meta.url), "utf8");
}

const guardian = readTauri("system/guardian.rs");
const config = readTauri("system/guardian_config.rs");
const sysMod = readTauri("system/mod.rs");
const api = readSrc("windows/settings/api.ts");
const behavior = readSrc("features/appearance/BehaviorSettingsSection.tsx");
const behaviorI18n = readSrc("i18n/sections/settings/behavior.ts");

test("G3 V5: a guard switched off at startup spawns no guardian", () => {
  const spawnBody = guardian.slice(guardian.indexOf("pub(crate) fn spawn_for_current_process"));
  assert.match(
    spawnBody,
    /if !super::guardian_config::crash_guard_enabled\(\)/,
    "spawn must read the switch and bail when off",
  );
});

test("G3 V5: a guard switched off at runtime stops the live guardian", () => {
  assert.match(
    config,
    /crate::system::guardian::terminate_spawned_guardian\(\)/,
    "turning off must end the spawned guardian",
  );
  assert.match(
    guardian,
    /pub\(crate\) fn terminate_spawned_guardian\(\)/,
    "terminate helper must exist",
  );
  assert.match(guardian, /SPAWNED_GUARDIAN_PID/, "spawned pid must be tracked");
  assert.match(
    readTauri("win32/guardian.rs"),
    /pub\(crate\) fn terminate\(pid: u32\)/,
    "safe terminate wrapper required",
  );
  assert.match(readTauri("win32/guardian.rs"), /TerminateProcess/, "must actually terminate");
});

test("G3: switch survives a relaunch cycle and both IPC commands are registered", () => {
  const runBody = guardian.slice(guardian.indexOf("pub fn run("));
  // The run() body uses the crate:: path; spawn_for_current_process uses the
  // super:: path. Match the crate:: form so removing the run-loop recheck is red
  // even though the spawn-time check still exists further down the file.
  assert.match(
    runBody,
    /crate::system::guardian_config::crash_guard_enabled\(\)/,
    "guardian must re-check the switch after a watched exit",
  );
  assert.match(config, /#\[tauri::command\]\s*pub fn get_crash_guard_enabled/, "get command required");
  assert.match(config, /#\[tauri::command\]\s*pub fn set_crash_guard_enabled/, "set command required");
  assert.match(sysMod, /guardian_config::get_crash_guard_enabled/, "get must be registered");
  assert.match(sysMod, /guardian_config::set_crash_guard_enabled/, "set must be registered");
});

test("G3: frontend honors the switch with a toggle and zh/en text", () => {
  assert.match(api, /get_crash_guard_enabled/, "frontend must read the switch");
  assert.match(api, /set_crash_guard_enabled/, "frontend must write the switch");
  assert.match(behavior, /crashGuardEnabled/, "settings page must pass the switch");
  assert.match(behavior, /t\.settings\.behavior\.crashGuard\b/, "toggle title must be localized");
  assert.match(behaviorI18n, /crashGuard:/, "zh/en section must define the title");
  assert.match(behaviorI18n, /crashGuardHint:/, "zh/en section must define the hint");
  // Only the zhCN/en string literals count; the type declares `crashGuard: string`.
  assert.equal((behaviorI18n.match(/crashGuard: "/g) || []).length, 2, "title text in zhCN and en");
  assert.equal(
    (behaviorI18n.match(/crashGuardHint: "[^"]+"/g) || []).length,
    2,
    "non-empty hint text in zhCN and en",
  );
});
