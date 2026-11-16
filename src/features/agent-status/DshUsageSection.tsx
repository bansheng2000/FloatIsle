import { useCallback, useEffect, useState } from "react";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { useTranslation } from "../../i18n";
import { probeMagpieUsage, readDshSessionTokens } from "./api";
import {
  magpieMissingLabel,
  quotaDetail,
  quotaTiers,
  sessionTokenDetail,
  sessionTokenSummary,
  spendDetail,
  spendLabel,
  type MagpieUsage,
  type QuotaMode,
  type SessionTokens,
} from "./dshSpend";
import "./DshUsageSection.css";

/**
 * Settings → AI Agent → Connection: the three usage numbers, each from its own source.
 *
 * "This session" comes from the DSS ledger keyed by the session the plugin reports, so it
 * is unknown until the plugin says which session is open — it then says so instead of
 * borrowing Magpie's rolling 30-day figure. Spend and quota come from Magpie and are
 * absent, with the reason, when that gateway is not running.
 */
export function DshUsageSection({ mode }: { mode: QuotaMode }) {
  const t = useTranslation();
  const text = t.settings.dshUsage;
  const [tokens, setTokens] = useState<SessionTokens | null>(null);
  const [usage, setUsage] = useState<MagpieUsage | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(true);

  const read = useCallback(async () => {
    setBusy(true);
    try {
      const [session, magpie] = await Promise.all([readDshSessionTokens(), probeMagpieUsage()]);
      setTokens(session);
      setUsage(magpie);
      setError("");
    } catch (failure) {
      setError(String(failure));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void read();
  }, [read]);

  const sessionValue = sessionTokenSummary(tokens);
  const spend = spendLabel(usage);
  const quota = quotaTiers(usage, mode, text.monthShort);
  const magpieGap = magpieMissingLabel(usage);
  // A missing ledger must not still be named as the source: the row would claim to read a
  // file it just said it could not open.
  const ledgerMissing = Boolean(
    tokens && !tokens.available && (tokens.error ?? "").startsWith("账本不存在"),
  );
  const sessionSource = ledgerMissing ? text.sessionSourceWithoutLedger : text.sessionSource;

  return (
    <SettingsGroup
      title={text.title}
      action={
        <SettingsLinkButton onClick={() => void read()} disabled={busy}>
          {busy ? text.reading : text.readAgain}
        </SettingsLinkButton>
      }
    >
      <SettingsRow
        title={text.session}
        description={sessionValue
          ? `${sessionTokenDetail(tokens)} · ${sessionSource}`
          : `${tokens?.error ?? text.sessionUnknown} · ${sessionSource}`}
        control={
          <span className="dsh-usage__value">{sessionValue ?? text.unavailable}</span>
        }
      />
      <SettingsRow
        title={text.spend}
        description={spend ? spendDetail(usage) : `${magpieGap} · ${text.magpieSource}`}
        control={<span className="dsh-usage__value">{spend ?? text.unavailable}</span>}
      />
      <SettingsRow
        title={text.islandQuota}
        description={quota ? quotaDetail(usage) : `${magpieGap} · ${text.magpieSource}`}
        control={<span className="dsh-usage__value">{quota ?? text.unavailable}</span>}
      />
      {error ? <p className="dsh-usage__note dsh-usage__note--error">{error}</p> : null}
      <p className="dsh-usage__note">{text.footnote}</p>
    </SettingsGroup>
  );
}
