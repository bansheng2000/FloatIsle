import { useTranslation } from "../../i18n";
import { SettingsChoice } from "../../components/settings/SettingsChoice";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { SettingsNumberRow } from "../../components/settings/SettingsNumberRow";
import { SettingsToggleRow } from "../../components/settings/SettingsToggleRow";
import { AUTO_COLLAPSE_SECONDS, TUCK_DELAY_SECONDS } from "./settingsLimits";
import type { ExpandTrigger, IslandSettings, TuckShape } from "./types";

/**
 * Settings → General → Island: how the island opens, (A) how long the open island stays after the
 * cursor leaves, whether and (B) how soon the collapsed island tucks into a line at the top edge,
 * game mode, and a way back to its default position. Timings, the monitor, the margin and
 * tucking while full screen keep their defaults (settingsDefaults.ts).
 */
export function ExpandSettingsSection({
  settings,
  onSettingsChange,
  onResetPosition,
  onPreviewTuckShape,
}: {
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
  onResetPosition: () => void;
  /** E3-C: ask the island to briefly show a tuck shape while the choice is being made. */
  onPreviewTuckShape?: (shape: TuckShape) => void;
}) {
  const t = useTranslation();
  const expand = t.settings.expand;
  const update = (patch: Partial<IslandSettings>) => onSettingsChange({ ...settings, ...patch });
  const triggers: { value: ExpandTrigger; label: string }[] = [
    { value: "click", label: expand.click },
    { value: "hover", label: expand.hover },
  ];
  const tuckShapes: { value: TuckShape; label: string }[] = [
    { value: "thread", label: expand.thread },
    { value: "hidden", label: expand.hidden },
    { value: "edge", label: expand.edge },
  ];

  return (
    <SettingsGroup title={expand.title} footer={settings.positionLocked ? undefined : t.settings.position.moveHint}>
      <SettingsRow
        title={expand.trigger}
        control={
          <SettingsChoice
            value={settings.expandTrigger}
            options={triggers}
            ariaLabel={expand.trigger}
            onChange={(expandTrigger) => update({ expandTrigger })}
          />
        }
      />
      <SettingsNumberRow
        title={expand.autoCollapse}
        description={expand.autoCollapseHint}
        value={settings.autoCollapseSeconds}
        min={AUTO_COLLAPSE_SECONDS.min}
        max={AUTO_COLLAPSE_SECONDS.max}
        unit={expand.secondsUnit}
        onChange={(autoCollapseSeconds) => update({ autoCollapseSeconds })}
      />
      <SettingsToggleRow
        title={t.settings.layout.autoTuck}
        description={t.settings.layout.autoTuckHint}
        checked={settings.autoTuck}
        onChange={(autoTuck) => update({ autoTuck })}
      />
      {settings.autoTuck ? (
        <>
          <SettingsNumberRow
            title={expand.tuckDelay}
            description={expand.tuckDelayHint}
            value={settings.tuckDelaySeconds}
            min={TUCK_DELAY_SECONDS.min}
            max={TUCK_DELAY_SECONDS.max}
            unit={expand.secondsUnit}
            indent
            onChange={(tuckDelaySeconds) => update({ tuckDelaySeconds })}
          />
          <SettingsRow
            title={expand.tuckShape}
            description={expand.tuckShapeHint}
            control={
              <SettingsChoice
                value={settings.tuckShape}
                options={tuckShapes}
                ariaLabel={expand.tuckShape}
                onChange={(tuckShape) => {
                  update({ tuckShape });
                  onPreviewTuckShape?.(tuckShape);
                }}
              />
            }
          />
          <SettingsRow title={expand.noTuckGroup} />
          <SettingsToggleRow
            title={expand.noTuckWhileAgent}
            description={expand.noTuckWhileAgentHint}
            checked={settings.noTuckWhileAgent}
            indent
            onChange={(noTuckWhileAgent) => update({ noTuckWhileAgent })}
          />
          <SettingsToggleRow
            title={expand.noTuckWhileLyrics}
            description={expand.noTuckWhileLyricsHint}
            checked={settings.noTuckWhileLyrics}
            indent
            onChange={(noTuckWhileLyrics) => update({ noTuckWhileLyrics })}
          />
          <SettingsToggleRow
            title={expand.noTuckWhileTimer}
            description={expand.noTuckWhileTimerHint}
            checked={settings.noTuckWhileTimer}
            indent
            onChange={(noTuckWhileTimer) => update({ noTuckWhileTimer })}
          />
        </>
      ) : null}
      <SettingsToggleRow
        title={t.settings.layout.gameQuiet}
        description={t.settings.layout.gameQuietHint}
        checked={settings.gameDoNotDisturb}
        onChange={(gameDoNotDisturb) => update({ gameDoNotDisturb })}
      />
      <SettingsRow
        title={t.settings.position.resetRow}
        control={<SettingsLinkButton onClick={onResetPosition}>{t.settings.position.reset}</SettingsLinkButton>}
      />
      <SettingsToggleRow
        title={t.settings.position.lock}
        description={t.settings.position.lockHint}
        checked={settings.positionLocked}
        onChange={(positionLocked) => update({ positionLocked })}
      />
    </SettingsGroup>
  );
}
