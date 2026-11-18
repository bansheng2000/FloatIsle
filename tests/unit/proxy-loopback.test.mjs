// Loopback HTTP clients must bypass the system WinINET proxy. On this machine
// ProxyEnable=1 / 127.0.0.1:7890 makes reqwest send 127.0.0.1:47822 through the
// proxy, which answers 502. These guards read the Rust source so each one goes
// red when a .no_proxy() call on a loopback client is removed.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = new URL("../../", import.meta.url);
const read = (p) => readFileSync(fileURLToPath(new URL(p, root)), "utf8");

const jump = read("src-tauri/src/agent_status/jump_request.rs");
const bridge = read("src-tauri/src/agent_status/dsh_bridge.rs");

// A client is a builder chain; take the slice up to .build() so a .no_proxy()
// added far away in the file cannot make an unrelated client look protected.
function clientChain(source, anchor) {
  const start = source.indexOf(anchor);
  assert.ok(start >= 0, `client anchor not found: ${anchor}`);
  const end = source.indexOf(".build()", start);
  assert.ok(end > start, `.build() not found after anchor: ${anchor}`);
  return source.slice(start, end);
}

test("jump POST client bypasses the system proxy", () => {
  const chain = clientChain(jump, "let client = reqwest::Client::builder()");
  assert.match(chain, /\.no_proxy\(\)/);
  assert.match(jump, /const JUMP_URL: &str = "http:\/\/127\.0\.0\.1:47822\/jump";/);
});

test("dsh_bridge status probe client bypasses the system proxy", () => {
  const chain = clientChain(bridge, "let Ok(client) = reqwest::Client::builder()");
  assert.match(chain, /\.no_proxy\(\)/);
  assert.match(bridge, /const STATUS_URL: &str = "http:\/\/127\.0\.0\.1:47822\/status";/);
});

test("dsh_bridge mounts read client bypasses the system proxy", () => {
  // The second builder is the mounts reader; skip past the probe client.
  const probe = bridge.indexOf("let Ok(client) = reqwest::Client::builder()");
  const anchor = bridge.indexOf("let client = reqwest::Client::builder()", probe);
  const chain = clientChain(bridge.slice(anchor), "let client = reqwest::Client::builder()");
  assert.match(chain, /\.no_proxy\(\)/);
  assert.match(bridge, /const MOUNTED_URL: &str = "http:\/\/127\.0\.0\.1:47822\/_task-notify\/mounted";/);
});

test("every loopback 47822 client has a no_proxy call", () => {
  // Three builders target 127.0.0.1:47822. Guard the exact count so a fourth
  // loopback client cannot be added without this guard noticing.
  const all = `${jump}\n${bridge}`;
  const builders = all.match(/reqwest::Client::builder\(\)/g) ?? [];
  assert.equal(builders.length, 3, "three loopback clients expected");
  const noProxies = all.match(/\.no_proxy\(\)/g) ?? [];
  assert.equal(noProxies.length, 3, "each loopback client must call .no_proxy()");
});
