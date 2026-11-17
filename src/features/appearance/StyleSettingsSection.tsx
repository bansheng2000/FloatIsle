import { SettingsChoice } from "../../components/settings/SettingsChoice";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { SettingsSliderRow } from "../../components/settings/SettingsSliderRow";
import { useTranslation } from "../../i18n";
import { DEFAULT_SETTINGS, LIQUID_GLASS_ENABLED, MAX_EXPANDED_SCALE, MIN_EXPANDED_SCALE } from "./settings";
import { COLLAPSED_HEIGHT, COLLAPSED_WIDTH, COLOR_SCHEMES, ISLAND_FONT_SIZE, TEXTURES } from "./settingsLimits";
import type { AppearanceMode, ColorScheme, IslandFont, IslandSettings, PageButtonStyle, Texture } from "./types";
import { PresetPicker } from "./presets/PresetPicker";

const percent = (value: number) => `${Math.round(value)}%`;
const times = (value: number) => `${value.toFixed(2)}x`;

/** Appearance → Style: classic or liquid glass, the font, how see-through, and how big the island is collapsed and opened. */
export function StyleSettingsSection({
  settings,
  onSettingsChange,
  onPreviewIsland,
  onExpandedScalePreview,
}: {
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
  /** Called while a size changes, so the island shows that size (collapsed or opened). */
  onPreviewIsland?: (mode: "collapsed" | "expanded") => void;
  onExpandedScalePreview?: (value: number | null) => void;
}) {
  const t = useTranslation();
  const text = t.settings.appearance;
  const modes = [
    { value: "classic", label: text.classic },
    { value: "liquidGlass", label: text.liquidGlass },
  ] as const;
  const fonts = [
    { value: "system", label: text.systemFont },
    { value: "pixel", label: text.pixelFont },
  ] as const;


  const schemeLabels: Record<ColorScheme, string> = {
    dark: text.schemeDark,
    light: text.schemeLight,
    system: text.schemeSystem,
  };
  const textureLabels: Record<Texture, string> = {
    classic: text.classic,
    liquidGlass: text.liquidGlass,
    frosted: text.textureFrosted,
  };
  const schemes = COLOR_SCHEMES.map((value) => ({ value, label: schemeLabels[value] }));
  const textures = TEXTURES.map((value) => ({ value, label: textureLabels[value] }));
  const px = (value: number) => `${Math.round(value)}px`;

  return (
    <SettingsGroup title={text.styleTitle}>
      {/* (E1) ⑤ Palette and ⑦ texture are the primary style switches; appearanceMode stays for compatibility. */}
      <SettingsRow
        title={text.colorScheme}
        control={
          <SettingsChoice<ColorScheme>
            value={settings.colorScheme}
            options={schemes}
            ariaLabel={text.colorScheme}
            onChange={(colorScheme) => onSettingsChange({ ...settings, colorScheme })}
          />
        }
      />
      <SettingsRow
        title={text.texture}
        control={
          <SettingsChoice<Texture>
            value={settings.texture}
            options={textures}
            ariaLabel={text.texture}
            onChange={(texture) => onSettingsChange({ ...settings, texture })}
          />
        }
      />
      <SettingsSliderRow
        title={text.fontSize}
        value={settings.islandFontSize}
        min={ISLAND_FONT_SIZE.min}
        max={ISLAND_FONT_SIZE.max}
        step={1}
        format={px}
        defaultValue={DEFAULT_SETTINGS.islandFontSize}
        resetLabel={t.settings.layout.resetToDefault}
        onChange={(islandFontSize) => onSettingsChange({ ...settings, islandFontSize })}
      />
      <SettingsSliderRow
        title={text.collapsedWidth}
        value={settings.collapsedWidth === 0 ? COLLAPSED_WIDTH.min : settings.collapsedWidth}
        min={COLLAPSED_WIDTH.min}
        max={COLLAPSED_WIDTH.max}
        step={1}
        format={px}
        defaultValue={COLLAPSED_WIDTH.min}
        resetLabel={t.settings.layout.resetToDefault}
        onChange={(collapsedWidth) => onSettingsChange({ ...settings, collapsedWidth })}
      />
      <SettingsSliderRow
        title={text.collapsedHeight}
        value={settings.collapsedHeight}
        min={COLLAPSED_HEIGHT.min}
        max={COLLAPSED_HEIGHT.max}
        step={1}
        format={percent}
        defaultValue={DEFAULT_SETTINGS.collapsedHeight}
        resetLabel={t.settings.layout.resetToDefault}
        onChange={(collapsedHeight) => {
          onSettingsChange({ ...settings, collapsedHeight });
          onPreviewIsland?.("collapsed");
        }}
      />
      {LIQUID_GLASS_ENABLED && <SettingsRow
        title={text.title}
        control={
          <SettingsChoice<AppearanceMode>
            value={settings.appearanceMode}
            options={modes}
            ariaLabel={text.title}
            onChange={(appearanceMode) => onSettingsChange({ ...settings, appearanceMode })}
          />
        }
      />}
      {LIQUID_GLASS_ENABLED && settings.appearanceMode === "liquidGlass" && (
        <SettingsSliderRow
          title={text.glassStrength}
          value={settings.glassIntensity}
          min={25}
          max={100}
          step={1}
          format={percent}
          defaultValue={DEFAULT_SETTINGS.glassIntensity}
          resetLabel={t.settings.layout.resetToDefault}
          indent
          onChange={(glassIntensity) => onSettingsChange({ ...settings, glassIntensity })}
        />
      )}
      <SettingsRow
        title={text.font}
        control={
          <SettingsChoice<IslandFont>
            value={settings.fontFamily}
            options={fonts}
            ariaLabel={text.fontLabel}
            onChange={(fontFamily) => onSettingsChange({ ...settings, fontFamily })}
          />
        }
      />
      <SettingsRow
        title={text.pageButtons}
        control={
          <SettingsChoice<PageButtonStyle>
            value={settings.pageButtonStyle}
            options={[
              { value: "capsule", label: text.capsules },
              { value: "icon", label: text.icons },
            ]}
            ariaLabel={text.pageButtons}
            onChange={(pageButtonStyle) => onSettingsChange({ ...settings, pageButtonStyle })}
          />
        }
      />
      <SettingsSliderRow
        title={t.settings.layout.opacity}
        value={settings.opacity}
        min={50}
        max={100}
        step={1}
        format={percent}
        defaultValue={DEFAULT_SETTINGS.opacity}
        resetLabel={t.settings.layout.resetToDefault}
        onChange={(opacity) => onSettingsChange({ ...settings, opacity })}
      />
      <SettingsSliderRow
        title={t.settings.layout.collapsedScale}
        value={settings.collapsedScale}
        min={0.75}
        max={1.4}
        step={0.01}
        format={times}
        defaultValue={DEFAULT_SETTINGS.collapsedScale}
        resetLabel={t.settings.layout.resetToDefault}
        onChange={(collapsedScale) => {
          onSettingsChange({ ...settings, collapsedScale });
          onPreviewIsland?.("collapsed");
        }}
      />
      {/* Applied when the thumb is let go: the island (which may hold this very slider) resizes
          once, with its spring, rather than on every step of the drag. */}
      <SettingsSliderRow
        title={t.settings.layout.expandedScale}
        value={settings.expandedScale}
        min={MIN_EXPANDED_SCALE}
        max={MAX_EXPANDED_SCALE}
        step={0.01}
        format={times}
        defaultValue={DEFAULT_SETTINGS.expandedScale}
        resetLabel={t.settings.layout.resetToDefault}
        commitOnRelease
        onPreviewChange={onExpandedScalePreview}
        onChange={(expandedScale) => {
          onSettingsChange({ ...settings, expandedScale });
          onPreviewIsland?.("expanded");
        }}
      />
      {/* (E1) ⑩ Whole-island presets share storage with colour presets but keep their own eight slots. */}
      <PresetPicker kind="appearance" settings={settings} onSettingsChange={onSettingsChange} labels={{
        title: text.presetsTitle,
        save: text.presetSave,
        namePlaceholder: text.presetNamePlaceholder,
        empty: text.presetEmpty,
        remove: text.presetDelete,
      }} />
    </SettingsGroup>
  );
}
