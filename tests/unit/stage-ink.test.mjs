import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { inkOn } from "../../src/utils/color.ts";

const root = fileURLToPath(new URL("../../src/", import.meta.url));
const stageCss = readFileSync(`${root}windows/island/IslandApp.css`, "utf8");
const agentCss = readFileSync(`${root}features/agent-status/AgentSessionsPanel.css`, "utf8");

test("island text ink is light on a dark background and dark on a light one", () => {
  // The stage derives its ink from the actual island background brightness, not the theme.
  const light = "241, 240, 246";
  const dark = "24, 24, 30";
  assert.equal(inkOn("#101014"), light);
  assert.equal(inkOn("#2F2F37"), light);
  assert.equal(inkOn("#ffffff"), dark);
  assert.equal(inkOn("#f8f4e9"), dark);
});

test("the stage root owns the four text tokens and derives them from the island ink", () => {
  // If the ink wiring is reverted — hard-coded text colour back in tokens.css, or the tokens
  // dropped from the stage — the island stops following its background and this goes red.
  for (const alpha of ["0.96", "0.68", "0.54", "0.42"]) {
    assert.ok(
      stageCss.includes(`rgba(var(--island-ink-rgb), ${alpha})`),
      `stage must define a text token at alpha ${alpha} from --island-ink-rgb`,
    );
  }
  assert.ok(stageCss.includes("--island-ink-rgb:"), "stage must declare the island ink variable");
  // The panel renders in an isolated QA page with no .stage, so it keeps a local fallback.
  // That fallback must derive from the same island ink variable — never a hard-coded colour —
  // or the panel would stop following the background on that page. Reverting to a literal
  // colour, or dropping the derivation, turns this red.
  assert.ok(
    /--agent-ink:\s*var\(--island-ink-rgb/.test(agentCss)
      && /--text-1:\s*rgba\(var\(--agent-ink\)/.test(agentCss),
    "the agent panel's fallback ink must derive from --island-ink-rgb, not a hard-coded colour",
  );
});
