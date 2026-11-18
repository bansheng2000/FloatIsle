import { clamp } from "../../utils/number.ts";
import type { ClipboardHistorySettings, ClipboardHistorySnapshot } from "./api";
import { DEFAULT_CLIPBOARD_SHORTCUT, normalizeClipboardShortcut } from "./shortcut.ts";

export const DEFAULT_CLIPBOARD_HISTORY: ClipboardHistorySnapshot = {
  settings: {
    enabled: true,
    captureImages: true,
    maxItems: 30,
    shortcut: DEFAULT_CLIPBOARD_SHORTCUT,
    shortcutEnabled: true,
  },
  items: [],
};

export function normalizeClipboardSettings(
  settings: ClipboardHistorySettings,
): ClipboardHistorySettings {
  return {
    ...settings,
    // E2: matches MAX_MAX_ITEMS in clipboard_history/mod.rs (raised from 200).
    maxItems: clamp(Math.round(settings.maxItems), 5, 500),
    shortcut: normalizeClipboardShortcut(settings.shortcut),
    shortcutEnabled: settings.shortcutEnabled !== false,
  };
}
