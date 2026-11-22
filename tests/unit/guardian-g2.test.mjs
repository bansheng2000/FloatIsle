// G2 crash-relaunch loop — source guards for the structural decisions that can
// only be proven end-to-end with a real process. The decision table itself is
// unit-tested in Rust (decide_after_exit); here we pin that the guardian *uses*
// it and that each of V2/V3/V4/V7 has a live code path.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

function read(path) {
  return readFileSync(new URL(`../../src-tauri/src/${path}`, import.meta.url), "utf8");
}

const guardian = read("system/guardian.rs");
const runBody = guardian.slice(guardian.indexOf("pub fn run("));

test("G2 V2: a genuine crash relaunches and the guardian keeps watching the new pid", () => {
  assert.match(guardian, /enum GuardianDecision/, "must model the decision explicitly");
  assert.match(runBody, /GuardianDecision::Relaunch/, "relaunch arm required in run()");
  assert.match(
    runBody,
    /let Some\(pid\) = relaunch_main\(\)/,
    "run() must actually launch a new main process",
  );
  assert.match(
    runBody,
    /args\.watch_pid = pid/,
    "guardian must keep watching the process it launched",
  );
  assert.match(guardian, /MANAGED_ENV/, "relaunched main must be flagged so it skips its own spawn");
});

test("G2 V3: opt-out marker stops the relaunch", () => {
  assert.match(
    guardian,
    /guard_optout::deliberate_exit_flag_present\(\)/,
    "guardian must read the opt-out flag after the watched exit",
  );
  const runBody = guardian.slice(guardian.indexOf("pub fn run("));
  const flagCheck = runBody.indexOf("deliberate_exit_flag_present()");
  const relaunch = runBody.indexOf("GuardianDecision::Relaunch");
  assert.ok(flagCheck > -1 && relaunch > -1 && flagCheck < relaunch, "check must precede relaunch in run()");
  assert.match(
    read("system/guard_optout.rs"),
    /pub\(crate\) fn deliberate_exit_flag_present\(\) -> bool/,
    "flag query must be exposed",
  );
});

test("G2 V7: a manual reopen prevents a duplicate launch", () => {
  assert.match(
    runBody,
    /other_island_processes\(args\.watch_pid\)/,
    "run() must enumerate other island processes before deciding",
  );
  assert.match(
    guardian,
    /running_process_ids\(&\["floatisle\.exe"\]\)/,
    "enumeration must look for island processes by name",
  );
  assert.match(
    guardian,
    /bridge_listening\(\)/,
    "must probe the bridge before deciding",
  );
  assert.match(guardian, /47821/, "bridge probe must target the real loopback port");
  assert.match(
    read("win32/guardian.rs"),
    /pub\(crate\) fn running_process_ids/,
    "process enumeration helper must exist in the guardian module",
  );
});

test("G2 V2 negative structure: decision purity is pinned by Rust tests", () => {
  assert.match(guardian, /fn decide_after_exit\(/, "decision must be a pure function");
  assert.match(guardian, /#\[cfg\(test\)\]/, "pure-function tests must live in the module");
  const clean = guardian.indexOf("a_clean_crash_relaunches");
  const guarded = guardian.indexOf("deliberate_exit_or_live_island_stops_relaunch");
  assert.ok(clean > -1 && guarded > -1, "both decision test cases must exist");
});
