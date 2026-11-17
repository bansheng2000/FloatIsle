import assert from "node:assert/strict";
import test from "node:test";
import { appendSessions, archiveMusicSource, restoreMusicSource } from "../../src/features/media/musicSourceArchive.ts";
import { loadArchivedMusicSources, MUSIC_SOURCE_ARCHIVE_STORAGE_KEY, saveArchivedMusicSources } from "../../src/features/media/storage.ts";

const priority = [
  { id: "qq", label: "QQ 音乐" },
  { id: "netease", label: "网易云音乐" },
];

function withStorage(initial = [], run) {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, "window");
  const values = new Map(initial);
  const storage = {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
  };
  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: { localStorage: storage, dispatchEvent: () => {} },
  });
  try {
    run(values);
  } finally {
    if (descriptor) Object.defineProperty(globalThis, "window", descriptor);
    else delete globalThis.window;
  }
}

test("archiving removes a player from the order and keeps its label in the archive", () => {
  const result = archiveMusicSource(priority, [], "qq");
  assert.deepEqual(result.priority, [{ id: "netease", label: "网易云音乐" }]);
  assert.deepEqual(result.archived, [{ id: "qq", label: "QQ 音乐" }]);
  // Unknown ids change nothing.
  assert.equal(archiveMusicSource(priority, [], "spotify").priority, priority);
});

test("restoring puts a player back at the end of the order", () => {
  const archived = [{ id: "qq", label: "QQ 音乐" }];
  const result = restoreMusicSource([{ id: "netease", label: "网易云音乐" }], archived, "qq");
  assert.deepEqual(result.priority, [
    { id: "netease", label: "网易云音乐" },
    { id: "qq", label: "QQ 音乐" },
  ]);
  assert.deepEqual(result.archived, []);
});

test("an archived player is not re-added when its session reappears", () => {
  const sessions = [{ id: "qq", label: "QQ 音乐" }, { id: "spotify", label: "Spotify" }];
  const next = appendSessions([], [{ id: "qq", label: "QQ 音乐" }], sessions);
  assert.deepEqual(next, [{ id: "spotify", label: "Spotify" }]);
});

test("the archive list round-trips through storage and rejects bad data", () => {
  withStorage([], (values) => {
    assert.deepEqual(loadArchivedMusicSources(), []);
    saveArchivedMusicSources([{ id: "qq", label: "QQ 音乐" }]);
    assert.equal(values.get(MUSIC_SOURCE_ARCHIVE_STORAGE_KEY), JSON.stringify([{ id: "qq", label: "QQ 音乐" }]));
    assert.deepEqual(loadArchivedMusicSources(), [{ id: "qq", label: "QQ 音乐" }]);
  });
  withStorage([[MUSIC_SOURCE_ARCHIVE_STORAGE_KEY, "not json"]], () => {
    assert.deepEqual(loadArchivedMusicSources(), []);
  });
  withStorage([[MUSIC_SOURCE_ARCHIVE_STORAGE_KEY, JSON.stringify([{ id: 12 }, { id: "a", label: "A" }, { id: "a", label: "dup" }])]], () => {
    assert.deepEqual(loadArchivedMusicSources(), [{ id: "a", label: "A" }]);
  });
});
