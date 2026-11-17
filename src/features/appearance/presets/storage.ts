import { readJson, writeJson } from "../../../lib/storage.ts";
import type { AppearancePresetKind } from "../types.ts";
import { canAddPreset, MAX_PRESETS } from "./presetLogic.ts";
import type { AppearancePreset } from "./types.ts";

export const PRESETS_STORAGE_KEY = "focusd-island-appearance-presets";
const KINDS: readonly AppearancePresetKind[] = ["colors", "appearance"];

function isValid(value: unknown): value is AppearancePreset {
  if (typeof value !== "object" || value === null) return false;
  const item = value as Record<string, unknown>;
  return typeof item.id === "string" && typeof item.name === "string"
    && typeof item.createdAt === "number"
    && KINDS.includes(item.kind as AppearancePresetKind)
    && typeof item.snapshot === "object" && item.snapshot !== null;
}

/** (E1) Saved presets. Malformed rows and overflow are dropped without throwing. */
export function loadPresets(): AppearancePreset[] {
  const raw = readJson(PRESETS_STORAGE_KEY);
  if (!Array.isArray(raw)) return [];
  const kept: AppearancePreset[] = [];
  for (const item of raw) {
    if (!isValid(item) || !canAddPreset(kept, item.kind)) continue;
    kept.push(item);
  }
  return kept;
}

/** (E1) Persist presets, capped to MAX_PRESETS per kind. Returns the list actually stored. */
export function savePresets(list: readonly AppearancePreset[]): AppearancePreset[] {
  const counts: Record<AppearancePresetKind, number> = { colors: 0, appearance: 0 };
  const kept = list.filter((preset) => {
    if (counts[preset.kind] >= MAX_PRESETS) return false;
    counts[preset.kind] += 1;
    return true;
  });
  writeJson(PRESETS_STORAGE_KEY, kept);
  return kept;
}

/** (E1) Whether a value names a known preset kind. */
export function isPresetKind(value: unknown): value is AppearancePresetKind {
  return typeof value === "string" && KINDS.includes(value as AppearancePresetKind);
}
