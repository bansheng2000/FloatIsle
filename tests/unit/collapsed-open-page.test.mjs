import assert from "node:assert/strict";
import test from "node:test";
import { collapsedClickTarget, collapsedOpenPage } from "../../src/windows/island/collapsedOpenPage.ts";

test("the middle text reopens the last page", () => {
  assert.equal(collapsedClickTarget("text", "clipboard", false), "clipboard");
  assert.equal(collapsedClickTarget("text", "music", false), "music");
  // A running agent named in the text takes over the click.
  assert.equal(collapsedClickTarget("text", "clipboard", true), "agents");
});

test("the four zones go to their own pages", () => {
  assert.equal(collapsedClickTarget("agent", "todo", false), "agents");
  assert.equal(collapsedClickTarget("music", "todo", false), "music");
  // The waveform wins over an agent named in the text.
  assert.equal(collapsedClickTarget("music", "todo", true), "music");
  assert.equal(collapsedClickTarget("shelf", "music", true), "shelf");
});

test("the text wrapper keeps its agent/last-page behaviour", () => {
  assert.equal(collapsedOpenPage("lyrics", false, "clipboard"), "clipboard");
  assert.equal(collapsedOpenPage("agent", true, "todo"), "agents");
  assert.equal(collapsedOpenPage("agent", false, "shelf"), "shelf");
  assert.equal(collapsedOpenPage("todo", false, "translation"), "translation");
  assert.equal(collapsedOpenPage(null, false, "qq"), "qq");
});
