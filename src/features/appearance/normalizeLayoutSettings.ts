// Reading saved settings, part 1 (normalizeSettings in settings.ts): the island's look and size.
import { clamp } from "../../utils/number.ts";
import { getColorSetting, HEX_COLOR_PATTERN } from "../../utils/color.ts";
import { DEFAULT_SETTINGS } from "./settingsDefaults.ts";
import {
  COLOR_SCHEMES, COLLAPSED_HEIGHT, COLLAPSED_WIDTH, ISLAND_FONT_SIZE, TEXTURES,
  normalizeExpandedScale, oneOf,
} from "./settingsLimits.ts";
import type { IslandSettings, LegacyIslandSettings } from "./types";

/** The look that can still be set: style, font, opacity, the two sizes and the three colours. */
export function normalizeLayoutSettings(settings: LegacyIslandSettings | null | undefined) {
  const taskTextColor = getColorSetting(
    settings?.taskTextColor ?? settings?.pendingTodoColor,
    getColorSetting(settings?.taskTitleColor, DEFAULT_SETTINGS.taskTextColor),
  );
  const hasPreviousDefaultScales =
    settings?.collapsedScale === 0.81 && settings.expandedScale === 0.88;
  const collapsedScale = hasPreviousDefaultScales
    ? 0.82
    : settings?.collapsedScale ?? settings?.sizeScale ?? DEFAULT_SETTINGS.collapsedScale;
  return {
    topEdgeLayoutVersion: 1,
    fontFamily: settings?.fontFamily === "pixel" ? "pixel" : "system",
    pageButtonStyle: settings?.pageButtonStyle === "capsule" ? "capsule" : DEFAULT_SETTINGS.pageButtonStyle,
    appearanceMode:
      settings?.appearanceMode === "classic" ||
      settings?.appearanceMode === "liquidGlass"
        ? settings.appearanceMode
        : "classic",
    // E1 additions: a saved value outside the allowed set falls back to the dark/classic default.
    colorScheme: oneOf(COLOR_SCHEMES, settings?.colorScheme, DEFAULT_SETTINGS.colorScheme),
    texture: oneOf(TEXTURES, settings?.texture, DEFAULT_SETTINGS.texture),
    islandFontSize: clamp(
      Number(settings?.islandFontSize ?? DEFAULT_SETTINGS.islandFontSize),
      ISLAND_FONT_SIZE.min,
      ISLAND_FONT_SIZE.max,
    ),
    collapsedWidth: settings?.collapsedWidth
      ? clamp(Number(settings.collapsedWidth), COLLAPSED_WIDTH.min, COLLAPSED_WIDTH.max)
      : DEFAULT_SETTINGS.collapsedWidth,
    collapsedHeight: clamp(
      Number(settings?.collapsedHeight ?? DEFAULT_SETTINGS.collapsedHeight),
      COLLAPSED_HEIGHT.min,
      COLLAPSED_HEIGHT.max,
    ),
    pageTextColor:
      typeof settings?.pageTextColor === "string" && HEX_COLOR_PATTERN.test(settings.pageTextColor)
        ? settings.pageTextColor
        : null,
    glassIntensity: clamp(
      Number(settings?.glassIntensity ?? DEFAULT_SETTINGS.glassIntensity),
      25,
      100,
    ),
    opacity: clamp(Number(settings?.opacity ?? DEFAULT_SETTINGS.opacity), 50, 100),
    collapsedScale: clamp(Number(collapsedScale), 0.75, 1.4),
    // On the slider's range; the three sizes of 3.0.0 keep their value.
    expandedScale: normalizeExpandedScale(settings?.expandedScale ?? DEFAULT_SETTINGS.expandedScale),
    taskTextColor,
    pulseColor: getColorSetting(
      settings?.pulseColor,
      DEFAULT_SETTINGS.pulseColor,
    ),
    islandBackgroundColor: getColorSetting(
      settings?.islandBackgroundColor,
      DEFAULT_SETTINGS.islandBackgroundColor,
    ),
  } satisfies Partial<IslandSettings>;
}
