import { readString, removeItem, writeJson, writeString } from "../../lib/storage.ts";
import {
  DAILY_NOTE_STORAGE_KEY,
  ACTIVE_TODO_STORAGE_KEY,
  SCHEDULED_TODOS_STORAGE_KEY,
  TODO_ARCHIVE_STORAGE_KEY,
  TODO_DATE_STORAGE_KEY,
  TODOS_STORAGE_KEY,
  TODO_DURATIONS_STORAGE_KEY,
  TODO_TIMER_STORAGE_KEY,
  loadActiveTodoId,
  loadCurrentTodoDate,
  loadDailyNote,
  loadScheduledTodos,
  loadTodoArchives,
  loadTodoDataDirectory,
  loadTodoDurations,
  saveTodoDataDirectory,
  loadTodoTimer,
  loadTodos,
  subscribeTodoStored,
} from "./storage.ts";
import {
  backupTodoLocalStorage,
  loadTodoDocument,
  removeTodoDocument,
  saveTodoDocument,
  validateTodoDataDirectory,
  type TodoDocument,
} from "./api.ts";

const SCHEMA_VERSION = 1;
const WRITE_DELAY_MS = 500;
/** Kept so every later save still says when the file was first created from the cache. */
const MIGRATED_AT_KEY = "focusd-island-todo-migrated-at";

export type TodoFileStatus =
  | { kind: "synced"; directory: string; filePath: string; writable: boolean; backupCount: number }
  | { kind: "read-only"; directory: string; filePath: string; reason: string }
  | {
      kind: "corrupt";
      directory: string;
      filePath: string;
      corruptPath: string | null;
      restoredFrom: string | null;
      reason: string;
    }
  | { kind: "migrated"; directory: string; filePath: string; backupPath: string | null }
  | { kind: "error"; directory: string; reason: string };

let status: TodoFileStatus | null = null;
let timer: ReturnType<typeof setTimeout> | null = null;
let booted = false;
const listeners = new Set<(next: TodoFileStatus | null) => void>();

function publish(next: TodoFileStatus) {
  status = next;
  listeners.forEach((listener) => listener(status));
}

export function currentTodoFileStatus() {
  return status;
}

export function subscribeTodoFileStatus(listener: (next: TodoFileStatus | null) => void) {
  listeners.add(listener);
  listener(status);
  return () => {
    listeners.delete(listener);
  };
}

/** Keys this build owns; anything else in the file belongs to someone else and is written back. */
const OWNED_KEYS = new Set([
  "schemaVersion",
  "updatedAt",
  "todos",
  "scheduled",
  "archives",
  "timer",
  "activeTodoId",
  "currentDate",
  "dailyNote",
  "durations",
  "migratedAt",
]);

/** Keys this build owns on a single todo or scheduled entry. */
const OWNED_ENTRY_KEYS = new Set(["id", "title", "completed", "createdAt", "kind", "date"]);

let spareTopLevel: Record<string, unknown> = {};
const spareEntries = new Map<string, Record<string, unknown>>();

function spareOf(source: unknown, owned: Set<string>) {
  const spare: Record<string, unknown> = {};
  if (source && typeof source === "object" && !Array.isArray(source)) {
    for (const [key, value] of Object.entries(source as Record<string, unknown>)) {
      if (!owned.has(key)) spare[key] = value;
    }
  }
  return spare;
}

/**
 * Remembers what this build does not understand, so the next save writes it back unchanged
 * instead of deleting another tool's additions.
 */
function rememberSpare(document: TodoDocument | null) {
  spareTopLevel = spareOf(document, OWNED_KEYS);
  spareEntries.clear();
  const lists = [document?.todos, document?.scheduled];
  for (const list of lists) {
    if (!Array.isArray(list)) continue;
    for (const entry of list) {
      const id = (entry as { id?: unknown } | null)?.id;
      if (typeof id !== "string" || !id) continue;
      const spare = spareOf(entry, OWNED_ENTRY_KEYS);
      if (Object.keys(spare).length > 0) spareEntries.set(id, spare);
    }
  }
}

function withSpare<T extends { id: string }>(entries: T[]): T[] {
  return entries.map((entry) => {
    const spare = spareEntries.get(entry.id);
    return spare ? { ...spare, ...entry } : entry;
  });
}

/** The file's contents, rebuilt from the running copy in localStorage. */
function composeDocument(migratedAt?: string): string {
  const stamp = migratedAt ?? readString(MIGRATED_AT_KEY);
  return JSON.stringify(
    {
      ...spareTopLevel,
      schemaVersion: SCHEMA_VERSION,
      updatedAt: new Date().toISOString(),
      todos: withSpare(loadTodos()),
      scheduled: withSpare(loadScheduledTodos()),
      archives: loadTodoArchives(),
      timer: loadTodoTimer(),
      activeTodoId: loadActiveTodoId(),
      currentDate: loadCurrentTodoDate(),
      dailyNote: loadDailyNote(),
      durations: loadTodoDurations(),
      ...(stamp ? { migratedAt: stamp } : {}),
    },
    null,
    2,
  );
}

/** The raw localStorage payload, kept as-is so a migration can be undone by hand. */
function rawLocalStorage(): string {
  return JSON.stringify({
    [TODOS_STORAGE_KEY]: readString(TODOS_STORAGE_KEY),
    [SCHEDULED_TODOS_STORAGE_KEY]: readString(SCHEDULED_TODOS_STORAGE_KEY),
    [TODO_ARCHIVE_STORAGE_KEY]: readString(TODO_ARCHIVE_STORAGE_KEY),
    [TODO_TIMER_STORAGE_KEY]: readString(TODO_TIMER_STORAGE_KEY),
    [TODO_DURATIONS_STORAGE_KEY]: readString(TODO_DURATIONS_STORAGE_KEY),
    [TODO_DATE_STORAGE_KEY]: readString(TODO_DATE_STORAGE_KEY),
    [ACTIVE_TODO_STORAGE_KEY]: readString(ACTIVE_TODO_STORAGE_KEY),
    [DAILY_NOTE_STORAGE_KEY]: readString(DAILY_NOTE_STORAGE_KEY),
  });
}

function hasLocalTodos() {
  return (
    loadTodos().length > 0 ||
    loadScheduledTodos().length > 0 ||
    loadTodoArchives().length > 0 ||
    loadDailyNote().length > 0
  );
}

/**
 * Writes the file's contents back into localStorage, which is the running copy
 * every hook reads. It uses the raw writers on purpose: this is a load, and it
 * must not schedule another file write.
 */
function applyDocument(document: TodoDocument) {
  const put = (key: string, value: unknown) => {
    if (value !== undefined && value !== null) writeString(key, JSON.stringify(value));
  };

  if (typeof document.currentDate === "string") writeString(TODO_DATE_STORAGE_KEY, document.currentDate);
  if (typeof document.dailyNote === "string") writeString(DAILY_NOTE_STORAGE_KEY, document.dailyNote);
  if (typeof document.activeTodoId === "string") writeString(ACTIVE_TODO_STORAGE_KEY, document.activeTodoId);
  else if (document.activeTodoId === null) removeItem(ACTIVE_TODO_STORAGE_KEY);

  put(TODOS_STORAGE_KEY, document.todos);
  put(SCHEDULED_TODOS_STORAGE_KEY, document.scheduled);
  put(TODO_ARCHIVE_STORAGE_KEY, document.archives);
  put(TODO_DURATIONS_STORAGE_KEY, document.durations);
  if (document.timer !== undefined && document.timer !== null) writeJson(TODO_TIMER_STORAGE_KEY, document.timer);
}

async function writeNow() {
  if (timer) {
    clearTimeout(timer);
    timer = null;
  }

  const directory = loadTodoDataDirectory();
  try {
    const saved = await saveTodoDocument(directory, composeDocument());
    publish({
      kind: "synced",
      directory: saved.directory,
      filePath: saved.filePath,
      writable: true,
      backupCount: saved.backupCount,
    });
  } catch (error) {
    // A read-only mirror (a second window owns the file) is not a data loss, so
    // it is reported apart from a real write failure.
    const reason = error instanceof Error ? error.message : String(error);
    if (status?.kind === "read-only") return;
    publish({ kind: "error", directory: status?.directory ?? directory, reason });
  }
}

/** Mirrors the todo list to its file shortly after any change, coalescing bursts. */
export function scheduleTodoFileWrite() {
  if (timer) clearTimeout(timer);
  timer = setTimeout(() => {
    timer = null;
    void writeNow();
  }, WRITE_DELAY_MS);
}

/**
 * Runs once before the first render: reads the file, brings localStorage up to
 * date with it, or migrates the old cache into a file the first time.
 */
export async function bootTodoFile() {
  if (booted) return status;
  booted = true;

  const directory = loadTodoDataDirectory();
  let load;
  try {
    load = await loadTodoDocument(directory);
  } catch (error) {
    publish({ kind: "error", directory, reason: error instanceof Error ? error.message : String(error) });
    return status;
  }

  if (!load.writable) {
    publish({
      kind: "read-only",
      directory: load.directory,
      filePath: load.filePath,
      reason: "Another FloatIsle window owns the todo file.",
    });
  }

  if (load.state === "corrupt") {
    publish({
      kind: "corrupt",
      directory: load.directory,
      filePath: load.filePath,
      corruptPath: load.corruptPath,
      restoredFrom: load.restoredFrom,
      reason: load.detail ?? "The todo file was unreadable.",
    });
  }

  if (load.document) {
    rememberSpare(load.document);
    applyDocument(load.document);
  } else if (load.state === "missing" && hasLocalTodos()) {
    const migratedAt = new Date().toISOString();
    writeString(MIGRATED_AT_KEY, migratedAt);
    try {
      const backupPath = await backupTodoLocalStorage(directory, rawLocalStorage());
      const saved = await saveTodoDocument(directory, composeDocument(migratedAt));
      publish({ kind: "migrated", directory: saved.directory, filePath: saved.filePath, backupPath });
    } catch (error) {
      publish({ kind: "error", directory, reason: error instanceof Error ? error.message : String(error) });
    }
  }

  if (!status) {
    publish({
      kind: "synced",
      directory: load.directory,
      filePath: load.filePath,
      writable: load.writable,
      backupCount: load.backupCount,
    });
  }

  subscribeTodoStored(scheduleTodoFileWrite);
  return status;
}

/**
 * Moves the file to a folder the user picked. The new copy is written first, so
 * a failure leaves the old folder intact; the old file is only removed once the
 * new one is on disk.
 */
export async function relocateTodoDirectory(next: string) {
  const folder = await validateTodoDataDirectory(next);
  const saved = await saveTodoDocument(folder, composeDocument());

  const previous = loadTodoDataDirectory();
  saveTodoDataDirectory(folder);
  publish({
    kind: "synced",
    directory: saved.directory,
    filePath: saved.filePath,
    writable: true,
    backupCount: saved.backupCount,
  });

  if (previous && previous !== folder) {
    try {
      await removeTodoDocument(previous);
    } catch {
      // Leaving the old copy behind is the safe outcome; the status already
      // points at the new folder.
    }
  }

  return folder;
}
