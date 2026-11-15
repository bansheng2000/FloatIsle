import assert from "node:assert/strict";
import test from "node:test";
import { dshBridgeDiagnostics, eventChannelGap } from "../../src/features/agent-status/dshBridge.ts";

const processes = { mainPids: [20832], childCount: 8, startedAt: 1_763_190_000_000, exe: "E:\\dsh.exe" };
const plugin = { version: "0.12.0", profile: "desktop" };
const report = (overrides = {}) => ({
  pid: 20832, epoch: "e1", clients: 4, clientIds: ["20832-1", "20832-2"], running: ["s1", "s2"], recent: 3,
  lastEventAt: "2026-11-15T13:59:36.830Z", eventsSent: 203, eventsFailed: 2059,
  lastEventError: null, listening: true, selfDescribing: true, ...overrides,
});
const status = (overrides = {}) => ({
  state: "ok", processes, plugin, endpoint: "127.0.0.1:47822", reachable: true, httpStatus: 200, error: null,
  report: report(), raw: '{"ok":true}', ...overrides,
});

test("the island's own missing event receiver is read out of the plugin's last error", () => {
  assert.equal(eventChannelGap(report({ lastEventError: "connect ECONNREFUSED 127.0.0.1:47821" })), true);
  // The line disappears by itself once the receiver exists, because the error goes away.
  assert.equal(eventChannelGap(report()), false);
  assert.equal(eventChannelGap(report({ lastEventError: "ECONNREFUSED 127.0.0.1:47822" })), false);
});

test("diagnostics carry both numbers and bodies, and never invent values", () => {
  const healthy = dshBridgeDiagnostics(status());
  for (const line of ["state: ok", "main=20832", "children=8", "plugin: 0.12.0 (desktop)", "clients=4 pid=20832 epoch=e1", "running=2", "recentCount=3", 'raw: {"ok":true}']) {
    assert.ok(healthy.includes(line), line);
  }
  // The person pasting this into a chat should not have to convert milliseconds to a clock time.
  assert.ok(healthy.includes(`startedAt=${processes.startedAt} (${new Date(processes.startedAt).toISOString()})`), healthy);
  // An old plugin has no clients, pid or epoch; the text says so instead of printing numbers.
  const old = dshBridgeDiagnostics(status({ state: "pluginOutdated", report: report({ pid: null, epoch: null, clients: null, selfDescribing: false }), raw: null }));
  assert.ok(old.includes("not self-describing (old plugin)"));
  assert.ok(old.includes("raw: (no body)"));
  // Nothing is left as JavaScript's undefined in something meant to be pasted.
  const bare = dshBridgeDiagnostics(status({ plugins: undefined, processes: { mainPids: [], childCount: 0, startedAt: null, exe: null }, plugin: { version: null, profile: null }, error: "no answer on 47822", raw: null }));
  assert.ok(!bare.includes("undefined"), bare);
  assert.ok(bare.includes("main=none") && bare.includes("startedAt=unknown") && bare.includes("error: no answer on 47822"));
});
