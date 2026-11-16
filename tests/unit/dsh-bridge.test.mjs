import assert from "node:assert/strict";
import test from "node:test";
import { dshBridgeDiagnostics } from "../../src/features/agent-status/dshBridge.ts";

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

const receiver = (overrides = {}) => ({
  port: 47821, listening: true, error: null, presences: 42, events: 3, rejected: 0,
  presence: { clientId: "20832-1", visible: true, focused: true, mountedSessionId: "s2", reportedAt: "2026-11-16T07:10:00.000Z" },
  lastPresenceAtMs: Date.parse("2026-11-16T07:10:00.000Z"), lastEventAtMs: null, lastEventType: "task", ...overrides,
});

test("the receiver's own numbers and the mounted session go into the pasted diagnostics", () => {
  const listening = dshBridgeDiagnostics(status(), receiver());
  assert.ok(listening.includes("receiver: port=47821 listening=true presences=42 events=3 rejected=0 error=none"), listening);
  assert.ok(listening.includes("mounted: session=s2 visible=true focused=true"), listening);
  // A listener that never bound reports its reason, and claims no session.
  const down = dshBridgeDiagnostics(status(), receiver({ listening: false, error: "47821 in use", presence: null, lastPresenceAtMs: null }));
  assert.ok(down.includes("listening=false presences=42 events=3 rejected=0 error=47821 in use"), down);
  assert.ok(down.includes("mounted: session=none"), down);
  // When the receiver has not been read at all, say so instead of printing empties.
  const unread = dshBridgeDiagnostics(status());
  assert.ok(unread.includes("receiver: not read") && unread.includes("mounted: not read"), unread);
});

test("a bridge with no registered client reads as its own state, not as OK", () => {
  const empty = dshBridgeDiagnostics(status({ state: "noClients", report: report({ clients: 0, clientIds: [], running: [] }) }));
  assert.ok(empty.includes("state: noClients"), empty);
  assert.ok(empty.includes("clients=0"), empty);
  // Nothing invented for the session count: an empty running list is a legitimate zero.
  assert.ok(empty.includes("running=0"), empty);
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
