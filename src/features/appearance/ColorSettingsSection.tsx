import { SettingsColor } from "../../components/settings/SettingsColor";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { useTranslation } from "../../i18n";
import { clearColorRestoreOffer, shouldOfferColorRestore } from "./storage";
import { DEFAULT_SETTINGS } from "./settings";
import type { IslandSettings } from "./types";
import { COLOR_SETTING_KEYS } from "./settings";
import { PresetPicker } from "./presets/PresetPicker";

type ColorKey = "islandBackgroundColor" | "taskTextColor" | "pulseColor";

/** Appearance → Colours: the island, its text, its status light, page text, and colour presets. */
export function ColorSettingsSection({
  settings,
  onSettingsChange,
  onPreviewIsland,
}: {
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
  /** (E1) ⑨ The live island expands so the new page-text colour can be judged. */
  onPreviewIsland?: (mode: "expanded") => void;
}) {
  const t = useTranslation();
  const text = t.settings.colors;
  const colors: readonly [ColorKey, string][] = [
    ["islandBackgroundColor", text.islandBackground],
    ["taskTextColor", text.taskText],
    ["pulseColor", text.pulse],
  ];

  // (E1) ⑨ null means "follow the theme"; a colour input needs a concrete hex to show.
  const pageText = settings.pageTextColor ?? DEFAULT_SETTINGS.taskTextColor;

  return (
    <SettingsGroup title={text.title}>
      {colors.map(([key, label]) => (
        <SettingsRow
          key={key}
          title={label}
          control={
            <SettingsColor value={settings[key]} ariaLabel={label} onChange={(value) => onSettingsChange({ ...settings, [key]: value })} />
          }
        />
      ))}
      <SettingsRow
        title={text.pageText}
        control={
          <SettingsColor
            value={pageText}
            ariaLabel={text.pageText}
            onChange={(value) => {
              onSettingsChange({ ...settings, pageTextColor: value });
              onPreviewIsland?.("expanded");
            }}
          />
        }
      />
      {shouldOfferColorRestore() ? (
        <SettingsRow
          title={text.restoreDefault}
          control={
            <SettingsLinkButton
              onClick={() => {
                const restored = { ...settings };
                for (const key of COLOR_SETTING_KEYS) {
                  (restored as unknown as Record<string, unknown>)[key] = DEFAULT_SETTINGS[key];
                }
                onSettingsChange(restored);
                clearColorRestoreOffer();
              }}
            >
              {text.restoreDefault}
            </SettingsLinkButton>
          }
        />
      ) : null}
      <PresetPicker kind="colors" settings={settings} onSettingsChange={onSettingsChange} labels={{
        title: text.presetsTitle,
        save: text.presetSave,
        namePlaceholder: text.presetNamePlaceholder,
        empty: text.presetEmpty,
        remove: text.presetDelete,
      }} />
    </SettingsGroup>
  );
}
