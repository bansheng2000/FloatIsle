import { useEffect, useState } from "react";
import { useTranslation } from "../../i18n";
import type { QuotaMode } from "./dshSpend";
import { probeMagpieUsage } from "./api";
import { quotaTiers, spendTiers, type MagpieUsage } from "./dshSpend";
import { setIslandUsageVisible } from "./islandUsageVisible";
import "./DshUsageLines.css";

/**
 * The Agent page's two usage rows, below the quota rings: Magpie's spend for today, the last seven
 * days and the last thirty, and the quota windows with the balance beside them.
 *
 * Everything here comes from Magpie, and the rows keep its own periods: thirty days is never
 * passed off as today, and tokens are never turned into money. When the gateway cannot be reached
 * the whole block goes away — a hidden block says nothing rather than showing a zero or an old
 * reading — and it comes back by itself once the gateway answers again, without a restart.
 */
export function DshUsageLines({ mode, refreshMs }: { mode: QuotaMode; refreshMs: number }) {
  const text = useTranslation().settings.dshUsage;
  const [usage, setUsage] = useState<MagpieUsage | null>(null);
  const [settled, setSettled] = useState(false);

  useEffect(() => {
    let live = true;
    const read = async () => {
      try {
        const magpie = await probeMagpieUsage();
        if (!live) return;
        // A failed read is not a reading: drop the numbers rather than pass the last ones off as now.
        setUsage(magpie);
      } catch (failure) {
        console.error("Failed to read Magpie usage for the island", failure);
        if (!live) return;
        setUsage(null);
      } finally {
        if (live) setSettled(true);
      }
    };
    void read();
    if (refreshMs <= 0) return () => { live = false; };
    const timer = window.setInterval(() => void read(), refreshMs);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [refreshMs]);

  const spend = spendTiers(usage, { today: text.tierToday, week: text.tierWeek, month: text.tierMonth });
  const quota = usage?.reachable ? quotaTiers(usage, mode, text.monthShort) : null;
  const shown = Boolean(usage?.reachable && (spend || quota));

  // The window resizes from this, so an absent block leaves no empty gap behind it.
  useEffect(() => {
    setIslandUsageVisible(shown);
    return () => setIslandUsageVisible(false);
  }, [shown]);

  if (!settled || !shown) return null;
  const rows = [
    { key: "spend", label: text.islandSpend, value: spend, note: usage?.error ?? "" },
    { key: "quota", label: text.islandQuota, value: quota, note: usage?.readAt ?? "" },
  ].filter((row) => row.value);

  return (
    <div className="agent-dsh-usage">
      {rows.map((row) => (
        <div key={row.key} className="agent-dsh-usage__line" data-row={row.key}>
          <span className="agent-dsh-usage__label">{row.label}</span>
          <span className="agent-dsh-usage__value" title={row.note || undefined}>
            {/* One column per tier, so the three figures sit at even distances whatever their width. */}
            {(row.value ?? "").split(" · ").map((tier) => (
              <span key={tier} className="agent-dsh-usage__tier">{tier}</span>
            ))}
          </span>
        </div>
      ))}
    </div>
  );
}
