import { CircleAlert, ClipboardCopy, Check } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { copyTextToClipboard } from "../clipboard/api";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { SettingsToggleRow } from "../../components/settings/SettingsToggleRow";
import { useTranslation } from "../../i18n";
import { probeDshBridge } from "./api";
import { dshBridgeDiagnostics, eventChannelGap, type DshBridgeStatus } from "./dshBridge";
import "./DshBridgeSection.css";

/** An ISO timestamp as local time; the raw string stays for the diagnostics block. */
function localTime(iso: string | null): string {
  if (!iso) return "";
  const at = new Date(iso);
  return Number.isNaN(at.getTime()) ? iso : at.toLocaleString();
}

/**
 * Settings → AI Agent → Connection: what the DSH notifier bridge reports about itself, read
 * once when the page opens and again on demand. Every number comes from the process table,
 * the installed manifest, or one GET on the plugin's status port; nothing is inferred, and a
 * missing answer shows as missing rather than as zero.
 */
export function DshBridgeSection({ agentShowDsh, onAgentShowDshChange }: {
  agentShowDsh: boolean;
  onAgentShowDshChange: (enabled: boolean) => void;
}) {
  const t = useTranslation();
  const text = t.settings.dshBridge;
  const [status, setStatus] = useState<DshBridgeStatus | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(true);
  const [copied, setCopied] = useState(false);

  const read = useCallback(async () => {
    setBusy(true);
    try {
      setStatus(await probeDshBridge());
      setError("");
    } catch (failure) {
      setError(String(failure));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    // Nothing to cancel: a late answer only sets state on a component that is still mounted
    // because the settings page stays open while it renders.
    void read();
  }, [read]);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 2_000);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const copy = async () => {
    if (!status) return;
    try {
      await copyTextToClipboard(dshBridgeDiagnostics(status));
      setCopied(true);
    } catch (failure) {
      setError(String(failure));
    }
  };

  const processes = status?.processes;
  const report = status?.report;
  const plugin = status?.plugin;
  const mainPid = processes?.mainPids[0];

  const hint =
    !status
      ? ""
      : status.state === "dshNotRunning"
        ? text.hints.notRunning
        : status.state === "pluginSilent"
          ? text.hints.silent
          : status.state === "portTaken"
            ? text.hints.portTaken
            : status.state === "pluginOutdated"
              ? text.hints.outdated(plugin?.version ?? "?")
              : text.hints.ok;

  const footer = error ? (
    <span className="dsh-bridge__note dsh-bridge__note--error" role="alert">{text.failed(error)}</span>
  ) : report && eventChannelGap(report) ? (
    <span className="dsh-bridge__note dsh-bridge__note--attention" role="status">
      <CircleAlert size={13} strokeWidth={2.4} aria-hidden="true" />
      {text.eventGap}
    </span>
  ) : undefined;

  const action = (
    <span className="dsh-bridge__actions">
      {status ? (
        <button className="dsh-bridge__copy" type="button" onClick={() => void copy()} title={copied ? text.copied : text.copy} aria-label={copied ? text.copied : text.copy}>
          {copied ? <Check size={14} strokeWidth={2.6} aria-hidden="true" /> : <ClipboardCopy size={14} strokeWidth={2.2} aria-hidden="true" />}
        </button>
      ) : null}
      <SettingsLinkButton disabled={busy} onClick={() => void read()}>{busy ? text.probing : text.probe}</SettingsLinkButton>
    </span>
  );

  return (
    <>
      <SettingsGroup title={text.title} action={action} footer={footer}>
        <SettingsRow
          title={text.status}
          description={hint}
          control={
            status ? (
              <span className={`dsh-bridge__badge dsh-bridge__badge--${status.state}`}>{text.states[status.state]}</span>
            ) : (
              <span className="dsh-bridge__badge dsh-bridge__badge--unknown">{text.probing}</span>
            )
          }
        />
        <SettingsRow
          title={text.process}
          description={
            processes?.startedAt
              ? [processes.childCount > 0 ? text.childProcesses(processes.childCount) : "", text.startedAt(localTime(new Date(processes.startedAt).toISOString()))]
                  .filter(Boolean)
                  .join(" · ")
              : processes && processes.childCount > 0
                ? text.childProcesses(processes.childCount)
                : undefined
          }
          control={<span className="dsh-bridge__value">{mainPid ? text.mainProcess(mainPid) : text.noProcess}</span>}
        />
        <SettingsRow
          title={text.plugin}
          description={undefined}
          control={
            plugin?.version ? (
              <span className="dsh-bridge__value">
                {text.version(plugin.version)}
                {plugin.profile ? <span className="dsh-bridge__aside">{plugin.profile}</span> : null}
              </span>
            ) : (
              <span className="dsh-bridge__value dsh-bridge__value--missing">{text.notInstalled}</span>
            )
          }
        />
        <SettingsRow
          title={text.bridge}
          description={report?.clients != null && report.clients > 1 ? text.bridgeHint(report.clients) : undefined}
          control={
            report?.clients != null ? (
              <span className="dsh-bridge__value">{text.bridgeCount(report.clients)}</span>
            ) : (
              <span className="dsh-bridge__value dsh-bridge__value--missing">{text.bridgeUnavailable}</span>
            )
          }
        />
        <SettingsRow
          title={text.sessions}
          description={report?.lastEventAt ? text.recent(localTime(report.lastEventAt)) : text.recentNever}
          control={<span className="dsh-bridge__value">{text.running(report?.running.length ?? 0)}</span>}
        />
      </SettingsGroup>
      <SettingsGroup title={text.sourceTitle}>
        <SettingsToggleRow
          title={t.settings.agent.sources.dsh}
          description={text.sourceHint}
          checked={agentShowDsh}
          onChange={onAgentShowDshChange}
        />
      </SettingsGroup>
    </>
  );
}
