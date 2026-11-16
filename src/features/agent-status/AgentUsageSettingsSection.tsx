import { useCallback, useEffect, useState } from "react";
import { SettingsChoice } from "../../components/settings/SettingsChoice";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { SettingsSelect } from "../../components/settings/SettingsSelect";
import { useTranslation } from "../../i18n";
import type { IslandSettings } from "../../features/appearance/types";
import type { AgentUsageMode } from "../../features/appearance/types";
import { probeMagpieUsage } from "./api";
import { magpieStateLabel, type MagpieUsage } from "./dshSpend";

/** The four refresh choices, in milliseconds; 0 is "only when asked". */
const REFRESH_CHOICES = [30_000, 60_000, 300_000, 0] as const;

/**
 * Settings → AI Agent → Usage and quota: whether the island's quota row counts down what is left
 * or up what was used, how often it asks Magpie again, and whether Magpie answers at all.
 *
 * Both choices are saved with the rest of the island's settings, so they survive a restart. The
 * state line is the gateway's own: a refused connection, a timeout and a silent port each say so.
 *
 * "Retry" stays in the group header whatever the state: with the interval set to "manual" it is the
 * only way to ask again, and while connected it is a plain refresh. It is not a failure-only action.
 */
export function AgentUsageSettingsSection({ settings, onSettingsChange }: {
  settings: IslandSettings;
  onSettingsChange: (settings: IslandSettings) => void;
}) {
  const text = useTranslation().settings.dshUsage;
  const [usage, setUsage] = useState<MagpieUsage | null>(null);
  const [busy, setBusy] = useState(false);

  const check = useCallback(async () => {
    setBusy(true);
    try {
      setUsage(await probeMagpieUsage());
    } catch (failure) {
      setUsage(null);
      console.error("Failed to probe Magpie", failure);
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void check();
  }, [check]);

  // Magpie coming back has to show up here without a restart, just as it does on the island; the
  // interval follows the same setting, and "manual" leaves only the Retry button.
  const interval = settings.agentUsageRefreshMs;
  useEffect(() => {
    if (interval <= 0) return;
    const timer = window.setInterval(() => void check(), interval);
    return () => window.clearInterval(timer);
  }, [check, interval]);

  const refreshLabel = (value: number) => {
    if (value === 30_000) return text.refreshSeconds;
    if (value === 60_000) return text.refreshMinute;
    if (value === 300_000) return text.refreshMinutes;
    return text.refreshManual;
  };

  return (
    <SettingsGroup
      title={text.settingsTitle}
      action={
        <SettingsLinkButton onClick={() => void check()} disabled={busy}>
          {busy ? text.reading : text.magpieRetry}
        </SettingsLinkButton>
      }
    >
      <SettingsRow
        className="dsh-usage-settings__row"
        title={text.mode}
        description={text.modeHint}
        control={
          <SettingsChoice<AgentUsageMode>
            value={settings.agentUsageMode}
            ariaLabel={text.mode}
            options={[
              { value: "remaining", label: text.modeRemaining },
              { value: "used", label: text.modeUsed },
            ]}
            onChange={(agentUsageMode) => onSettingsChange({ ...settings, agentUsageMode })}
          />
        }
      />
      <SettingsRow
        className="dsh-usage-settings__row"
        title={text.refresh}
        description={settings.agentUsageRefreshMs === 0 ? text.refreshHint : undefined}
        control={
          <SettingsSelect<string>
            value={String(settings.agentUsageRefreshMs)}
            ariaLabel={text.refresh}
            options={REFRESH_CHOICES.map((value) => ({ value: String(value), label: refreshLabel(value) }))}
            onChange={(next) => onSettingsChange({ ...settings, agentUsageRefreshMs: Number(next) })}
          />
        }
      />
      <SettingsRow
        className="dsh-usage-settings__row"
        title="Magpie"
        description={usage ? magpieStateLabel(usage, text) : text.reading}
        control={
          <span
            className={usage && usage.reachable ? "dsh-usage__state dsh-usage__state--ok" : "dsh-usage__state dsh-usage__state--warn"}
          >
            {usage ? (usage.reachable ? text.magpieOnline : text.magpieOffline) : text.reading}
          </span>
        }
      />
    </SettingsGroup>
  );
}
