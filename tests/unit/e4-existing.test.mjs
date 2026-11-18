// E4 "already done" guarantees plus the new mirror/signature invariant. These
// are native/config/source invariants, so the guards read source instead of
// mocking a window. Each one must go red when its source fact is broken.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = new URL("../../", import.meta.url);
const read = (p) => readFileSync(fileURLToPath(new URL(p, root)), "utf8");

const attention = read("src/features/updater/updateAttention.ts");
const schedule = read("src/features/updater/updateSchedule.ts");
const controller = read("src/features/updater/updaterController.ts");
const channel = read("src-tauri/src/system/update_channel.rs");
const config = read("src-tauri/tauri.conf.json");
const proxy = read("src-tauri/src/system/updater.rs");
const releaseScript = read("scripts/release/make-latest-json.mjs");
const collapsedBar = read("src/windows/island/IslandCollapsedBar.tsx");

test("E4 the collapsed bar brand follows the product identity", () => {
  assert.match(collapsedBar, /className="island__brand">\{t\.settings\.project\.appName\}/);
});

test("E4 the attention dot stays latched until install or dismissal", () => {
  assert.match(attention, /dismissed\s*\|\|\s*stage\s*===\s*"done"/);
  assert.match(attention, /return found/);
  // The scheduled check must not blink the latched flag off on a "latest" result.
  assert.match(controller, /if \(!state\.found\) setState\(\{ stage: "latest" \}\)/);
});

test("E4 checks retry after five minutes and sweep every six hours", () => {
  assert.match(schedule, /CHECK_RETRY_MS\s*=\s*5\s*\*\s*60_000/);
  assert.match(schedule, /PERIODIC_CHECK_MS\s*=\s*6\s*\*\s*60\s*\*\s*60_000/);
  assert.match(schedule, /lastCheckFailed\s*\?\s*CHECK_RETRY_MS\s*:\s*PERIODIC_CHECK_MS/);
});

test("E4 the check layer keeps three mirrors and a twenty-second timeout", () => {
  assert.match(config, /github\.com/);
  assert.match(config, /ghfast\.top/);
  assert.match(config, /gh-proxy\.com/);
  assert.match(channel, /CHECK_TIMEOUT:\s*Duration\s*=\s*Duration::from_secs\(20\)/);
});

test("E4 the update package is still signature-verified", () => {
  assert.match(config, /"pubkey"/);
  assert.match(releaseScript, /\.sig/);
  // The download helper only rewrites the URL; it never clears or replaces the signature.
  assert.match(channel, /update\.download_url\s*=\s*candidate/);
  assert.doesNotMatch(channel, /update\.signature\s*=/);
  assert.match(channel, /Update signature verification failed/);
  assert.match(channel, /if is_signature_error\(&error\)/);
});

test("E4 SOCKS entries are recognised but ignored, not fed to the HTTP client", () => {
  assert.match(proxy, /SOCKS entries are recognised but deliberately skipped/);
  assert.match(proxy, /socks5h?=/);
  assert.match(proxy, /normalize_proxy\("socks=/);
});
