import { DEFAULT_SETTINGS, normalizeSettings } from "./settings.ts";
import type { IslandSettings } from "./types";

export const STORAGE_KEY = "focusd-island-settings";
export const COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY =
  "focusd-island-collapsed-text-default-v1";
export const TUCK_SHAPE_DEFAULT_MIGRATION_KEY = "focusd-island-collapsed-text-default-v1-tuck-shape-v1";
// (E1) One-time migrations: default colour refresh, its restore offer, and the session-sort default.
export const E1_COLOR_REFRESH_MIGRATION_KEY = "focusd-e1-default-color-refresh";
export const E1_COLOR_RESTORE_OFFER_KEY = "focusd-e1-color-restore-offer";
export const E1_SORT_DEFAULT_MIGRATION_KEY = "focusd-e1-session-sort-recent";

export const COLOR_FIELDS = [
  "taskTextColor", "pageTextColor", "pulseColor", "pulseBrightness",
  "islandBackgroundColor", "todoBackgroundColor",
] as const;

/** True only while every palette field still matches the shipped default: the user never recoloured. */
function colorsMatchDefault(settings: IslandSettings): boolean {
  return COLOR_FIELDS.every((field) => settings[field] === DEFAULT_SETTINGS[field]);
}

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

/**
 * (E1) Colour refresh and session-sort default, each once. Someone who never changed a colour is
 * moved onto the shipped palette and gets a one-shot restore entry; someone who did change colours
 * keeps them. Session order flips from "status" to "recent" only for users still on the old default.
 */
export function applyE1Migrations(
  settings: IslandSettings,
  flags: { colorRefreshDone: boolean; sortDone: boolean },
): { settings: IslandSettings; offerColorRestore: boolean } {
  let result = settings;
  let offerColorRestore = false;
  if (!flags.colorRefreshDone && colorsMatchDefault(result)) {
    const colors = Object.fromEntries(
      COLOR_FIELDS.map((field) => [field, DEFAULT_SETTINGS[field]]),
    ) as Pick<IslandSettings, (typeof COLOR_FIELDS)[number]>;
    result = { ...result, ...colors };
    offerColorRestore = true;
  }
  if (!flags.sortDone && result.agentSessionSort === "status") {
    result = { ...result, agentSessionSort: "recent" };
  }
  return { settings: result, offerColorRestore };
}

export function loadSettings(): IslandSettings {
  const stored = window.localStorage.getItem(STORAGE_KEY);

  if (!stored) {
    window.localStorage.setItem(COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY, "1");
    window.localStorage.setItem(TUCK_SHAPE_DEFAULT_MIGRATION_KEY, "1");
    window.localStorage.setItem(E1_COLOR_REFRESH_MIGRATION_KEY, "1");
    window.localStorage.setItem(E1_SORT_DEFAULT_MIGRATION_KEY, "1");
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
    const colorRefreshPending =
      window.localStorage.getItem(E1_COLOR_REFRESH_MIGRATION_KEY) !== "1";
    const sortPending =
      window.localStorage.getItem(E1_SORT_DEFAULT_MIGRATION_KEY) !== "1";

    let result = normalized;
    if (collapsedPending) {
      result = { ...result, showCollapsedText: true };
    }
    if (tuckPending) {
      result = applyTuckShapeMigration(result, false);
    }
    let offerColorRestore = false;
    if (colorRefreshPending || sortPending) {
      const migrated = applyE1Migrations(result, {
        colorRefreshDone: !colorRefreshPending,
        sortDone: !sortPending,
      });
      result = migrated.settings;
      offerColorRestore = migrated.offerColorRestore;
    }

    if (collapsedPending || tuckPending || colorRefreshPending || sortPending) {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(result));
    }
    if (collapsedPending) {
      window.localStorage.setItem(COLLAPSED_TEXT_DEFAULT_MIGRATION_KEY, "1");
    }
    if (tuckPending) {
      window.localStorage.setItem(TUCK_SHAPE_DEFAULT_MIGRATION_KEY, "1");
    }
    if (colorRefreshPending) {
      window.localStorage.setItem(E1_COLOR_REFRESH_MIGRATION_KEY, "1");
    }
    if (sortPending) {
      window.localStorage.setItem(E1_SORT_DEFAULT_MIGRATION_KEY, "1");
    }
    if (offerColorRestore) {
      window.localStorage.setItem(E1_COLOR_RESTORE_OFFER_KEY, "1");
    }

    return result;
  } catch {
    return DEFAULT_SETTINGS;
  }
}

/** (E1) Whether the one-shot "restore default colours" entry should be shown after the refresh. */
export function shouldOfferColorRestore() {
  return window.localStorage.getItem(E1_COLOR_RESTORE_OFFER_KEY) === "1";
}

/** (E1) Dismiss the one-shot restore entry once the user has acted on it. */
export function clearColorRestoreOffer() {
  window.localStorage.removeItem(E1_COLOR_RESTORE_OFFER_KEY);
}

export function saveSettings(settings: IslandSettings) {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
}
