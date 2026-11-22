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
const guardian = read("src-tauri/src/system/guardian.rs");

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
  const endCall = "clipboard_history::init(app.handle())";
  const endAt = lib.indexOf(endCall, start);
  assert.ok(endAt >= 0, "setup must init clipboard history");
  // G1 inserted the guardian spawn between the clear and the tray build, so a
  // fixed 400-char window no longer reaches build_tray. Slice by a boundary past
  // the tray build instead; the ordering assertion below is unchanged.
  const head = lib.slice(start, endAt + endCall.length);
  assert.match(head, /guard_optout::clear_deliberate_exit\(\);/);
  assert.ok(
    head.indexOf("clear_deliberate_exit") < head.indexOf("build_tray"),
    "marker must be cleared before the tray is ready"
  );
});

test("OPTOUT in-product guardian reads the native marker and retires instead of relaunching", () => {
  // The external PowerShell watchdog retires in G4-3, so the in-product guardian
  // is the authority. Assert against what the product code actually uses:
  // guard_optout.rs writes floatisle-guard-optout.flag with "deliberate-exit "
  // and guardian.rs reads it before deciding to relaunch.
  assert.match(mod, /const FLAG_FILE: &str = "floatisle-guard-optout\.flag";/);
  assert.ok(
    mod.includes('format!("deliberate-exit {seconds}\\n")'),
    "marker body must carry the product-internal deliberate-exit reason"
  );
  assert.match(
    mod,
    /pub\(crate\) fn deliberate_exit_flag_present\(\) -> bool/,
    "Rust must expose marker presence to the guardian"
  );

  const runStart = guardian.indexOf("pub fn run(mut args: GuardianArgs)");
  const runEnd = guardian.indexOf("pub(crate) fn spawn_for_current_process");
  assert.ok(runStart >= 0 && runEnd > runStart, "guardian run loop must exist");
  const run = guardian.slice(runStart, runEnd);
  const reading = run.indexOf("deliberate_exit_flag_present()");
  const relaunching = run.indexOf("relaunch_main()");
  assert.ok(reading >= 0, "the run loop must actually read the opt-out marker");
  assert.ok(
    reading < relaunching,
    "the marker check must precede any relaunch (skip before launch)"
  );

  const decideStart = guardian.indexOf("pub(crate) fn decide_after_exit(");
  const decide = guardian.slice(decideStart, guardian.indexOf("pub fn parse_args"));
  assert.match(decide, /if optout \|\|/, "a present marker must force the Retire branch");
});

test("OPTOUT crash exit paths do NOT write the marker", () => {
  // lib.rs close-requested hides the window, and the build-failure exit is an
  // abnormal path — neither may call mark_deliberate_exit.
  assert.match(lib, /api\.prevent_close\(\)/);
  assert.match(lib, /window\.hide\(\)/);
  const abnormal = lib.slice(lib.indexOf("std::process::exit(1)") - 120, lib.indexOf("std::process::exit(1)") + 60);
  assert.doesNotMatch(abnormal, /mark_deliberate_exit/);
});
