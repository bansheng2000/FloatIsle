import { APPEARANCE_SETTING_KEYS, applyAppearanceSnapshot, COLOR_SETTING_KEYS } from "../settings.ts";
import { withColorFrom } from "../colorPreset.ts";
import type { IslandSettings } from "../types";
import type { AppearancePreset } from "./types.ts";

/** (E1) Maximum saved presets per kind; the two lists are kept apart so each gets its own eight. */
export const MAX_PRESETS = 8;

/** (E1) The shared list accepts either kind, with at most MAX_PRESETS of each. */
export function canAddPreset(list: readonly AppearancePreset[], kind: AppearancePreset["kind"]): boolean {
  return list.filter((preset) => preset.kind === kind).length < MAX_PRESETS;
}

/** (E1) Add a preset, or return the list unchanged when its kind is full. */
export function addPreset(
  list: readonly AppearancePreset[],
  preset: AppearancePreset,
): AppearancePreset[] {
  return canAddPreset(list, preset.kind) ? [...list, preset] : [...list];
}

/** (E1) Apply a preset to the live settings. Colour presets touch only palette fields. */
export function applyPreset(settings: IslandSettings, preset: AppearancePreset): IslandSettings {
  return preset.kind === "colors"
    ? withColorFrom(settings, preset.snapshot as IslandSettings)
    : applyAppearanceSnapshot(settings, preset.snapshot);
}

/** (E1) Build a colour preset snapshot from the current settings, ignoring user input noise. */
export function makeColorSnapshot(settings: IslandSettings): Partial<IslandSettings> {
  return Object.fromEntries(COLOR_SETTING_KEYS.map((key) => [key, settings[key]]));
}

/** (E1) Build a whole-island appearance snapshot from the current settings. */
export function makeAppearanceSnapshot(settings: IslandSettings): Partial<IslandSettings> {
  return Object.fromEntries(APPEARANCE_SETTING_KEYS.map((key) => [key, settings[key]]));
}
