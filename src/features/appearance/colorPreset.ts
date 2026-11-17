import { COLOR_SETTING_KEYS } from "./settings.ts";
import type { IslandSettings } from "./types";

type ColorSettings = Pick<IslandSettings, (typeof COLOR_SETTING_KEYS)[number]>;

/** (E1) Apply only the palette fields from `source`, keeping sizes and behaviour. */
export function withColorFrom(settings: IslandSettings, source: IslandSettings): IslandSettings {
  const colors = Object.fromEntries(
    COLOR_SETTING_KEYS.filter((key) => source[key] !== undefined).map((key) => [key, source[key]]),
  ) as Partial<ColorSettings>;
  return { ...settings, ...colors };
}
