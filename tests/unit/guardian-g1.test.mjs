// G1 guardian skeleton — source guards for the two structural invariants that
// cannot be observed without a real process: the guardian branches before Tauri
// (V1) and therefore cannot collide with the single-instance mutex (V6).
// Pure argument parsing is exercised by Rust #[cfg(test)] in guardian.rs.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

function read(path) {
  return readFileSync(new URL(`../../src-tauri/src/${path}`, import.meta.url), "utf8");
}

test("G1 V1: main() branches to guardian before calling floatisle_lib::run()", () => {
  const main = read("main.rs");
  assert.match(main, /floatisle_lib::guardian_args\(&args\)/, "main must parse guardian args");
  assert.match(main, /floatisle_lib::run_guardian\(guardian\)/, "main must dispatch to the guardian path");
  const dispatch = main.indexOf("floatisle_lib::run_guardian(guardian)");
  const normalRun = main.indexOf("floatisle_lib::run()");
  assert.ok(
    dispatch > -1 && normalRun > -1 && dispatch < normalRun,
    `guardian dispatch must come before the normal run() call (dispatch=${dispatch}, run=${normalRun})`,
  );
});

test("G1 V1: guardian path never enters the Tauri builder", () => {
  const module = read("system/guardian.rs");
  const win = read("win32/guardian.rs");
  const combined = `${module}\n${win}`;
  for (const forbidden of ["tauri::Builder", "generate_context", "tauri_plugin_single_instance", "build_tray"]) {
    assert.ok(!combined.includes(forbidden), `guardian path must not touch ${forbidden}`);
  }
  assert.match(win, /WaitForSingleObject/, "guardian must block on the watched process");
});

test("G1 V6: normal run path still installs the single-instance plugin", () => {
  const lib = read("lib.rs");
  assert.match(lib, /tauri_plugin_single_instance::init/, "normal startup must keep single-instance mutex");
});

test("G1: setup spawns exactly one hidden guardian, best effort", () => {
  const lib = read("lib.rs");
  const guardian = read("system/guardian.rs");
  assert.match(lib, /system::guardian::spawn_for_current_process\(\)/, "setup must spawn guardian");
  assert.match(guardian, /CREATE_NO_WINDOW/, "spawn must hide the console window");
  assert.match(guardian, /--watch-pid=/, "spawn must pass the parent pid");
  assert.match(guardian, /Stdio::null\(\)/, "spawn must detach stdio");
});

test("G1: guardian arguments are parsed by a pure function with defaults rejected", () => {
  const guardian = read("system/guardian.rs");
  assert.match(guardian, /pub fn parse_args\(args: &\[String\]\)/, "parse_args must be pure and public");
  assert.match(guardian, /arg == "--guardian"/, "guardian switch required");
  assert.match(guardian, /strip_prefix\("--watch-pid="\)/, "watch pid must be parsed");
  assert.match(guardian, /watch_pid == 0/, "pid 0 must be rejected");
});
