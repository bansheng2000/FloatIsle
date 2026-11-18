import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsNumberRow } from "../../components/settings/SettingsNumberRow";
import { SettingsToggleRow } from "../../components/settings/SettingsToggleRow";
import { useTranslation } from "../../i18n";
import { CLIPBOARD_VISIBLE_ROWS } from "../appearance/settingsLimits.ts";
import type { IslandSettings } from "../appearance/types";
import type { ClipboardHistorySettings } from "./api";

/**
 * The clipboard history options below the module switch (which is `enabled`, set by the
 * page). Its shortcut is the next group on the page.
 */
export function ClipboardSettingsSection({ clipboardSettings, onClipboardSettingsChange, settings, onSettingsChange }: {
  clipboardSettings: ClipboardHistorySettings;
  onClipboardSettingsChange: (settings: ClipboardHistorySettings) => void;
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
}) {
  const t = useTranslation();

  return (
    <SettingsGroup title={t.settings.clipboard.title}>
      <SettingsToggleRow
        title={t.settings.clipboard.captureImages}
        checked={clipboardSettings.captureImages}
        onChange={(captureImages) => onClipboardSettingsChange({ ...clipboardSettings, captureImages })}
      />
      <SettingsNumberRow
        title={t.settings.clipboard.maxItems}
        value={clipboardSettings.maxItems}
        min={5}
        max={500}
        onChange={(maxItems) => onClipboardSettingsChange({ ...clipboardSettings, maxItems })}
      />
      <SettingsNumberRow
        title={t.settings.clipboard.visibleRows}
        value={settings.clipboardVisibleRows}
        min={CLIPBOARD_VISIBLE_ROWS.min}
        max={CLIPBOARD_VISIBLE_ROWS.max}
        onChange={(clipboardVisibleRows) => onSettingsChange({ ...settings, clipboardVisibleRows })}
      />
    </SettingsGroup>
  );
}
