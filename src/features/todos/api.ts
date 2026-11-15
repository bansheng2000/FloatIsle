import { invoke } from "@tauri-apps/api/core";

// 对应 src-tauri/src/system/todo_export.rs 的 SaveTodoMarkdownResult。
export type SaveTodoResult = {
  filePath: string;
};

// 对应 src-tauri/src/system/todo_store.rs 的 TodoLoad。
export type TodoFileLoad = {
  state: "ok" | "missing" | "corrupt";
  writable: boolean;
  directory: string;
  filePath: string;
  document: TodoDocument | null;
  updatedAt: string | null;
  corruptPath: string | null;
  restoredFrom: string | null;
  backupCount: number;
  detail: string | null;
};

// 对应 todo_store.rs 的 TodoSave。
export type TodoFileSave = {
  directory: string;
  filePath: string;
  bytes: number;
  backupPath: string | null;
  backupCount: number;
};

/** todos.json 的形状；版本 1 是第一个版本。 */
export type TodoDocument = {
  schemaVersion?: number;
  updatedAt?: string;
  todos?: unknown;
  scheduled?: unknown;
  archives?: unknown;
  timer?: unknown;
  activeTodoId?: unknown;
  currentDate?: unknown;
  dailyNote?: unknown;
  durations?: unknown;
  migratedAt?: string;
};

export function saveTodoMarkdown(directory: string, date: string, content: string) {
  return invoke<SaveTodoResult>("save_todo_markdown", { directory, date, content });
}

export function getDefaultTodoSaveDirectory() {
  return invoke<string>("get_default_todo_save_directory");
}

/** `directory` 为空表示用默认目录（文档文件夹下的 FloatIsle）。 */
export function getTodoDataDirectory(directory: string) {
  return invoke<string>("get_todo_data_directory", { directory: directory || null });
}

export function getTodoExportDirectory(directory: string) {
  return invoke<string>("get_todo_export_directory", { directory: directory || null });
}

export function loadTodoDocument(directory: string) {
  return invoke<TodoFileLoad>("load_todo_document", { directory: directory || null });
}

export function saveTodoDocument(directory: string, content: string) {
  return invoke<TodoFileSave>("save_todo_document", { directory: directory || null, content });
}

export function backupTodoLocalStorage(directory: string, content: string) {
  return invoke<string>("backup_todo_local_storage", { directory: directory || null, content });
}

export function validateTodoDataDirectory(directory: string) {
  return invoke<string>("validate_todo_data_directory", { directory });
}

export function openTodoDataDirectory(directory: string) {
  return invoke<void>("open_todo_data_directory", { directory: directory || null });
}

export function removeTodoDocument(directory: string) {
  return invoke<boolean>("remove_todo_document", { directory: directory || null });
}
