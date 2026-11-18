// pnpm dev, then open /tests/regression/clipboard.html. Drives the real ClipboardHistoryPanel
// with the clipboard IPC mocked, guarding E2: multi-select, select-all that skips favourites,
// two-tap batch delete that never deletes favourites, and the full-size image lightbox.
import "../../src/styles/tokens.css";
import "../../src/styles/base.css";
import { act, StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { mockIPC } from "@tauri-apps/api/mocks";
import { I18nProvider, LOCALE_STORAGE_KEY } from "../../src/i18n";
import type { ClipboardHistoryItem, ClipboardHistorySnapshot } from "../../src/features/clipboard/api";
import { ClipboardHistoryPanel } from "../../src/features/clipboard/ClipboardHistoryPanel";

Object.assign(globalThis, { isTauri: true, IS_REACT_ACT_ENVIRONMENT: true });
localStorage.setItem(LOCALE_STORAGE_KEY, "zh-CN");

const thumb = "data:image/png;base64,iVBORw0KGgo=";
const item = (id: string, extra: Partial<ClipboardHistoryItem> = {}): ClipboardHistoryItem => ({
  id, kind: "text", hash: id, createdAt: 0, copiedAt: 0, preview: `entry ${id}`, ...extra,
});

let snapshot: ClipboardHistorySnapshot = {
  settings: { enabled: true, captureImages: true, maxItems: 500, shortcut: "Alt+C", shortcutEnabled: false },
  items: [
    item("a"),
    item("b", { favorite: true }),
    item("c", { kind: "image", image: { width: 800, height: 400, byteSize: 1234, originalPath: "c.png", thumbnailPath: "c-thumb.png", thumbnailDataUrl: thumb }, preview: "image c" }),
  ],
};
const batchCalls: string[][] = [];
const originalReads: string[] = [];

mockIPC((command, payload) => {
  const args = payload as { ids?: string[]; id?: string };
  if (command === "delete_clipboard_items") {
    const ids = args.ids ?? [];
    batchCalls.push(ids);
    const removed = new Set(ids.filter((id) => !snapshot.items.find((it) => it.id === id)?.favorite));
    snapshot = { ...snapshot, items: snapshot.items.filter((it) => !removed.has(it.id)) };
    return snapshot;
  }
  if (command === "get_clipboard_image_data_url") {
    originalReads.push(String(args.id));
    return `data:image/png;base64,ORIGINAL-${args.id}`;
  }
  if (command === "toggle_clipboard_history_favorite") {
    snapshot = { ...snapshot, items: snapshot.items.map((it) => it.id === args.id ? { ...it, favorite: !it.favorite } : it) };
    return snapshot;
  }
  return snapshot;
});

const noop = () => {};
const yes = () => true;
const q = <T extends Element>(selector: string) => document.querySelector<T>(selector);
const qa = <T extends Element>(selector: string) => [...document.querySelectorAll<T>(selector)];
const click = async (el: Element | null) => {
  if (!el) throw new Error("missing element to click");
  await act(async () => { el.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true })); el.dispatchEvent(new MouseEvent("click", { bubbles: true })); });
};
const wait = async (ms = 30) => act(async () => { await new Promise((resolve) => setTimeout(resolve, ms)); });

const messages: string[] = [];
function check(condition: unknown, label: string) {
  if (!condition) throw new Error(label);
  messages.push(`PASS ${label}`);
}

function Harness() {
  return (
    <ClipboardHistoryPanel
      snapshot={snapshot}
      onCopyItem={yes}
      onToggleFavorite={noop}
      onUpdateNote={yes}
      onDeleteItem={noop}
      onClear={noop}
      onBatchDelete={(ids) => { batchCalls.push(ids); }}
      onLoadOriginal={async (id) => `data:image/png;base64,ORIGINAL-${id}`}
    />
  );
}

async function run() {
  await act(async () => {
    createRoot(document.getElementById("fixture")!).render(
      <StrictMode><I18nProvider><Harness /></I18nProvider></StrictMode>,
    );
  });
  await wait();

  // Enter multi-select; a checkbox appears on each row.
  check(q(".clipboard-select-toggle") !== null, "未多选时有进入多选按钮");
  await click(q(".clipboard-select-toggle"));
  check(qa(".clipboard-item__check").length === 3, "多选态每条记录前置勾选框");
  const checkboxes = qa<HTMLButtonElement>(".clipboard-item__check");
  check(checkboxes[1].disabled, "收藏项的勾选框禁用");

  // Select all must select only the two non-favourites.
  await click(q(".clipboard-select-all"));
  check(
    qa(".clipboard-item__check--on").length === 2,
    "全选（不含收藏）只勾两个非收藏项",
  );
  check(q(".clipboard-select-count")?.textContent === "已选 2 项", "选中数显示 2 项");

  // Two-tap batch delete; even with the favourite selected, it is never sent.
  const deleteButton = q(".clipboard-panel__tools .clipboard-clear-button:not(.clipboard-select-toggle)");
  await click(deleteButton);
  check(q(".clipboard-clear-button--confirming") !== null, "首次点删除进入确认态");
  await click(q(".clipboard-clear-button--confirming"));
  check(batchCalls.length === 1, "二次点删除只发一次批量命令");
  check(JSON.stringify(batchCalls[0]) === JSON.stringify(["a", "c"]), "批量删除不含收藏项 id b");

  // Image rows keep an original-preview button; clicking opens a lightbox that loads the original.
  await click(q(".clipboard-original-button"));
  check(q('[role="dialog"]') !== null, "点图片放大镜打开原图 lightbox");
  check(q(".clipboard-lightbox__image")?.getAttribute("src") === "data:image/png;base64,ORIGINAL-c", "lightbox 显示按需读取的原图");
  await act(async () => { document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); });
  check(q('[role="dialog"]') === null, "Esc 关闭原图 lightbox");
}

try {
  await run();
  const results = document.getElementById("results")!;
  results.textContent = `${messages.join("\n")}\n\n全部 ${messages.length} 项检查通过。`;
  results.dataset.result = "passed";
} catch (error) {
  const results = document.getElementById("results")!;
  results.textContent = `${messages.join("\n")}\nFAIL ${String(error)}`;
  results.dataset.result = "failed";
  console.error(error);
}
