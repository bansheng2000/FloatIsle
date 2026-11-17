import assert from "node:assert/strict";
import test from "node:test";
import { parseLastPage, readLastPage, writeLastPage, LAST_PAGE_STORAGE_KEY } from "../../src/windows/island/lastPage.ts";

function withWindow(windowValue, run) {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, "window");
  Object.defineProperty(globalThis, "window", { configurable: true, value: windowValue });
  try {
    run();
  } finally {
    if (descriptor) Object.defineProperty(globalThis, "window", descriptor);
    else delete globalThis.window;
  }
}

function memoryStorage(initial = []) {
  const values = new Map(initial);
  return {
    values,
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
  };
}

test("a known page round-trips through storage under the persisted key", () => {
  const storage = memoryStorage();
  withWindow({ localStorage: storage }, () => {
    assert.equal(readLastPage(), "todo");
    writeLastPage("agents");
    assert.equal(storage.values.get(LAST_PAGE_STORAGE_KEY), JSON.stringify("agents"));
    assert.equal(readLastPage(), "agents");
    writeLastPage("music");
    assert.equal(readLastPage(), "music");
  });
});

test("an illegal persisted page name falls back to the default page", () => {
  // A value that is not a page this build knows must never be opened.
  assert.equal(parseLastPage(JSON.stringify("not-a-page")), "todo");
  assert.equal(parseLastPage(JSON.stringify("")), "todo");
  assert.equal(parseLastPage(JSON.stringify(42)), "todo");
  assert.equal(parseLastPage(JSON.stringify(null)), "todo");
  assert.equal(parseLastPage(JSON.stringify(["music"])), "todo");
  assert.equal(parseLastPage("{not json"), "todo");
  assert.equal(parseLastPage(null), "todo");
});

test("blocked storage never throws and reads as the default", () => {
  const throwing = () => { throw new Error("QuotaExceededError"); };
  withWindow({ localStorage: { getItem: throwing, setItem: throwing } }, () => {
    assert.equal(readLastPage(), "todo");
    assert.doesNotThrow(() => writeLastPage("shelf"));
  });
});
