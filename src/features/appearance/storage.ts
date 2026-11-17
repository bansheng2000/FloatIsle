import { DEFAULT_SETTINGS, normalizeSettings } from "./settings.ts";
import type { IslandSettings } from "./types";

export const STORAGE_KEY = "focusd-island-settings";
export const COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY =
  "focusd-island-collapsed-text-default-v1";
export const TUCK_SHAPE_DEFAULT_MIGRATION_KEY = "focusd-island-collapsed-text-default-v1-tuck-shape-v1";

/**
 * One-time upgrade for the tuck-shape setting: on the first launch after the
 * upgrade the island holds a thin line at the top edge. Once the marker is set,
 * a shape the user picked later is returned untouched on every launch.
 */
export function applyTuckShapeMigration(
  settings: IslandSettings,
  migrationAlreadyDone: boolean,
): IslandSettings {
  if (migrationAlreadyDone) return settings;
  return { ...settings, tuckShape: "thread" };
}

export function loadSettings(): IslandSettings {
  const stored = window.localStorage.getItem(STORAGE_KEY);

  if (!stored) {
    window.localStorage.setItem(COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY, "1");
    window.localStorage.setItem(TUCK_SHAPE_DEFAULT_MIGRATION_KEY, "1");
    return DEFAULT_SETTINGS;
  }

  try {
    const parsed = JSON.parse(stored) as Partial<IslandSettings> & {
      margin?: number;
    };
    const normalized = normalizeSettings(parsed);

    const collapsedPending =
      window.localStorage.getItem(COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY) !== "1";
    const tuckPending =
      window.localStorage.getItem(TUCK_SHAPE_DEFAULT_MIGRATION_KEY) !== "1";

    let result = normalized;
    if (collapsedPending) {
      result = { ...result, showCollapsedText: true };
    }
    if (tuckPending) {
      result = applyTuckShapeMigration(result, false);
    }

    if (collapsedPending || tuckPending) {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(result));
    }
    if (collapsedPending) {
      window.localStorage.setItem(COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY, "1");
    }
    if (tuckPending) {
      window.localStorage.setItem(TUCK_SHAPE_DEFAULT_MIGRATION_KEY, "1");
    }

    return result;
  } catch {
    return DEFAULT_SETTINGS;
  }
}


export function saveSettings(settings: IslandSettings) {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
}
