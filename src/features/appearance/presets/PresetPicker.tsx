import { useState } from "react";
import { SettingsInput } from "../../../components/settings/SettingsInput";
import { SettingsLinkButton } from "../../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../../components/settings/SettingsRow";
import type { IslandSettings } from "../types";
import { addPreset, applyPreset, canAddPreset, makeAppearanceSnapshot, makeColorSnapshot, MAX_PRESETS } from "./presetLogic.ts";
import { loadPresets, savePresets } from "./storage.ts";
import type { AppearancePreset } from "./types.ts";
import "./PresetPicker.css";

type Labels = {
  title: string;
  save: string;
  namePlaceholder: string;
  empty: string;
  remove: string;
};

/**
 * (E1) One list, two kinds: colour presets and whole-island presets share storage but each keeps
 * its own eight slots. Applying writes the snapshot onto live settings so the user can keep editing.
 */
export function PresetPicker({
  kind,
  settings,
  onSettingsChange,
  labels,
}: {
  kind: AppearancePreset["kind"];
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
  labels: Labels;
}) {
  const [presets, setPresets] = useState<AppearancePreset[]>(() => loadPresets());
  const [name, setName] = useState("");
  const mine = presets.filter((preset) => preset.kind === kind);
  const canSave = canAddPreset(presets, kind) && name.trim().length > 0;

  const persist = (next: AppearancePreset[]) => setPresets(savePresets(next));

  const save = () => {
    if (!canSave) return;
    const preset: AppearancePreset = {
      id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      name: name.trim(),
      createdAt: Date.now(),
      kind,
      snapshot: kind === "colors" ? makeColorSnapshot(settings) : makeAppearanceSnapshot(settings),
    };
    persist(addPreset(presets, preset));
    setName("");
  };

  const remove = (id: string) => persist(presets.filter((preset) => preset.id !== id));

  return (
    <div className="preset-picker">
      <SettingsRow title={labels.title}>
        <div className="preset-picker__tools">
          <SettingsInput
            className="preset-picker__name"
            value={name}
            placeholder={labels.namePlaceholder}
            aria-label={labels.namePlaceholder}
            onChange={(event) => setName(event.currentTarget.value)}
          />
          <SettingsLinkButton disabled={!canSave} onClick={save}>
            {labels.save}
          </SettingsLinkButton>
          {!canAddPreset(presets, kind) ? <span className="preset-picker__full">{MAX_PRESETS}/8</span> : null}
        </div>
        {mine.length === 0 ? (
          <div className="preset-picker__empty">{labels.empty}</div>
        ) : (
          <div className="preset-picker__list">
            {mine.map((preset) => (
              <div className="preset-picker__item" key={preset.id}>
                <button
                  className="preset-picker__apply"
                  type="button"
                  onClick={() => onSettingsChange(applyPreset(settings, preset))}
                >
                  {preset.name}
                </button>
                <button
                  className="preset-picker__remove"
                  type="button"
                  aria-label={`${labels.remove} ${preset.name}`}
                  onClick={() => remove(preset.id)}
                >
                  ×
                </button>
              </div>
            ))}
          </div>
        )}
      </SettingsRow>
    </div>
  );
}
