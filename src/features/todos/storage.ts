import { withoutUpdateTodo } from "./updateTodo.ts";
import type { ScheduledTodo, TodoArchive, TodoItem } from "./types";
import { readJson, readString, removeItem, writeJson, writeString } from "../../lib/storage.ts";
import { normalizeTodoDurations, normalizeTodoTimer, type TodoDurations, type TodoTimer } from "./timer.ts";
import {
  createTodoId,
  normalizeTodo,
  parseCurrentTodoDate,
  parseScheduledTodos,
  parseTodoArchives,
  parseTodos,
} from "./normalize.ts";

export { createTodoId, normalizeTodo };

// Todos planned for later days (ScheduledTodo[]); added for upstream issue #30.
export const SCHEDULED_TODOS_STORAGE_KEY = "focusd-island-scheduled-todos";
export const TODOS_STORAGE_KEY = "focusd-island-todos";
export const ACTIVE_TODO_STORAGE_KEY = "focusd-island-active-todo";
export const TODO_TIMER_STORAGE_KEY = "focusd-island-todo-timer";
// 旧的 "focusd-island-todo-duration"（所有待办共用一个时长）已不再读取：每项待办各自记时长。
export const TODO_DURATIONS_STORAGE_KEY = "focusd-island-todo-durations";
export const TODO_DATE_STORAGE_KEY = "focusd-island-current-date";
export const TODO_ARCHIVE_STORAGE_KEY = "focusd-island-archives";
export const DAILY_NOTE_STORAGE_KEY = "focusd-island-daily-note";
// Settings for exporting archives; they stay in localStorage.
export const TODO_SAVE_DIRECTORY_STORAGE_KEY = "focusd-island-save-directory";
export const TODO_LAST_SAVED_SIGNATURE_STORAGE_KEY =
  "focusd-island-last-saved-signature";
// 待办数据文件所在目录的覆盖值；空串表示默认（文档文件夹下的 FloatIsle）。
export const TODO_DATA_DIRECTORY_STORAGE_KEY = "focusd-island-todo-data-directory";

// Every writer below keeps localStorage as the running copy of the list; the
// file mirror in fileSync.ts listens here so no call site has to know about it.
const storedListeners = new Set<() => void>();

export function subscribeTodoStored(listener: () => void) {
  storedListeners.add(listener);
  return () => {
    storedListeners.delete(listener);
  };
}

function noteTodoStored() {
  storedListeners.forEach((listener) => listener());
}

export function loadScheduledTodos(): ScheduledTodo[] {
  return parseScheduledTodos(readString(SCHEDULED_TODOS_STORAGE_KEY));
}

export function saveScheduledTodos(todos: ScheduledTodo[]) {
  writeString(SCHEDULED_TODOS_STORAGE_KEY, JSON.stringify(todos));
  noteTodoStored();
}

export function loadTodos(): TodoItem[] {
  return parseTodos(readString(TODOS_STORAGE_KEY));
}

export function loadActiveTodoId() {
  return readString(ACTIVE_TODO_STORAGE_KEY);
}

export function loadTodoTimer() {
  return normalizeTodoTimer(readJson(TODO_TIMER_STORAGE_KEY));
}

export function saveTodoTimer(timer: TodoTimer | null) {
  const stored = timer
    ? writeJson(TODO_TIMER_STORAGE_KEY, timer)
    : removeItem(TODO_TIMER_STORAGE_KEY);
  noteTodoStored();
  return stored;
}

export function loadTodoDurations(): TodoDurations {
  return normalizeTodoDurations(readJson(TODO_DURATIONS_STORAGE_KEY));
}

export function saveTodoDurations(durations: TodoDurations) {
  const stored = writeJson(TODO_DURATIONS_STORAGE_KEY, durations);
  noteTodoStored();
  return stored;
}

export function loadCurrentTodoDate() {
  return parseCurrentTodoDate(readString(TODO_DATE_STORAGE_KEY));
}

export function loadTodoArchives(): TodoArchive[] {
  return parseTodoArchives(readString(TODO_ARCHIVE_STORAGE_KEY));
}

export function loadSaveDirectory() {
  return readString(TODO_SAVE_DIRECTORY_STORAGE_KEY) ?? "";
}

export function loadTodoDataDirectory() {
  return readString(TODO_DATA_DIRECTORY_STORAGE_KEY) ?? "";
}

export function loadDailyNote() {
  return readString(DAILY_NOTE_STORAGE_KEY) ?? "";
}

/** Returns false when the value could not be stored. */
export function saveSaveDirectory(directory: string) {
  return writeString(TODO_SAVE_DIRECTORY_STORAGE_KEY, directory);
}

export function saveTodoDataDirectory(directory: string) {
  return writeString(TODO_DATA_DIRECTORY_STORAGE_KEY, directory);
}

export function hasStoredSaveDirectory() {
  return readString(TODO_SAVE_DIRECTORY_STORAGE_KEY) !== null;
}

export function saveTodos(todos: TodoItem[]) {
  writeString(TODOS_STORAGE_KEY, JSON.stringify(withoutUpdateTodo(todos)));
  noteTodoStored();
}

export function saveActiveTodoId(activeTodoId: string | null) {
  if (activeTodoId) writeString(ACTIVE_TODO_STORAGE_KEY, activeTodoId);
  else removeItem(ACTIVE_TODO_STORAGE_KEY);
  noteTodoStored();
}

export function saveDailyNote(dailyNote: string) {
  writeString(DAILY_NOTE_STORAGE_KEY, dailyNote);
  noteTodoStored();
}

export function saveCurrentTodoDate(date: string) {
  writeString(TODO_DATE_STORAGE_KEY, date);
  noteTodoStored();
}

export function saveTodoArchives(archives: TodoArchive[]) {
  writeString(TODO_ARCHIVE_STORAGE_KEY, JSON.stringify(archives.map((archive) => ({ ...archive, todos: withoutUpdateTodo(archive.todos) }))));
  noteTodoStored();
}

export function loadLastSavedSignature() {
  return readString(TODO_LAST_SAVED_SIGNATURE_STORAGE_KEY);
}

export function saveLastSavedSignature(signature: string) {
  writeString(TODO_LAST_SAVED_SIGNATURE_STORAGE_KEY, signature);
}

export function clearLastSavedSignature() {
  removeItem(TODO_LAST_SAVED_SIGNATURE_STORAGE_KEY);
}
