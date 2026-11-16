/**
 * The three usage numbers the card shows, and how each one is labelled.
 *
 * Sources stay separate: `usage.sqlite` (with the session id the plugin reports) answers
 * "this session" in tokens, Magpie answers spend and quota for its own rolling period.
 * Neither may stand in for the other, and tokens are never converted into money.
 */

export type SessionTokens = {
  available: boolean;
  session: string | null;
  input: number;
  cacheRead: number;
  cacheWrite: number;
  output: number;
  calls: number;
  source: string;
  error: string | null;
};

export type MagpieSpend = {
  cost: number;
  period: string;
  calls: number;
  errors: number;
  unpriced: number;
};

export type MagpieWindow = {
  name: string;
  kind: string | null;
  remaining: number | null;
  used: number | null;
  resetsAt: string | null;
};

export type MagpieUsage = {
  base: string;
  reachable: boolean;
  error: string | null;
  spend: MagpieSpend | null;
  /** Today's spend, the top-level `cost` of `?period=today`. */
  spendToday: number | null;
  /** Seven days, summed from the seven daily buckets: the gateway answers any other period with 30 days. */
  spendWeek: number | null;
  spendMonth: number | null;
  windows: MagpieWindow[];
  balances: string[];
  readAt: string | null;
};

/** Whether a quota window is read as what is left of it, or as what was already used. */
export type QuotaMode = "remaining" | "used";

export const MAGPIE_MISSING = "未配置 Magpie（本机未检测到 127.0.0.1:3425）";

/** `1,234` grouping, so a six-digit token count stays readable. */
export function tokenCount(value: number): string {
  return Math.round(value).toLocaleString("en-US");
}

/** Session tokens as one line; `null` means the number is not known. */
export function sessionTokenSummary(tokens: SessionTokens | null): string | null {
  if (!tokens?.available) return null;
  return `${tokenCount(tokens.input)} in · ${tokenCount(tokens.output)} out`;
}

export function sessionTokenDetail(tokens: SessionTokens | null): string | null {
  if (!tokens?.available) return null;
  return [
    `cache ${tokenCount(tokens.cacheRead)} read`,
    `${tokenCount(tokens.cacheWrite)} write`,
    `${tokenCount(tokens.calls)} calls`,
  ].join(" · ");
}

/** Spend always carries its own period; a bare number would read as "this session". */
export function spendLabel(usage: MagpieUsage | null): string | null {
  if (!usage?.spend) return null;
  const period = usage.spend.period === "30d" ? "近 30 天" : usage.spend.period;
  return `${period} · $${usage.spend.cost.toFixed(2)}`;
}

export function spendDetail(usage: MagpieUsage | null): string | null {
  if (!usage?.spend) return null;
  const parts = [`${tokenCount(usage.spend.calls)} 次调用`];
  if (usage.spend.unpriced > 0) parts.push(`有 ${usage.spend.unpriced} 次调用未计价`);
  return parts.join(" · ");
}

/** Remaining quota per window, plus any balance string the gateway reports verbatim. */
export function quotaSummary(usage: MagpieUsage | null): string | null {
  if (!usage) return null;
  const parts = usage.windows
    .filter((window) => window.remaining !== null)
    .map((window) => `剩余 ${window.name} ${Math.round(window.remaining ?? 0)}%`);
  if (usage.balances.length > 0) parts.push(usage.balances.join(" · "));
  return parts.length > 0 ? parts.join(" · ") : null;
}

/** A timestamp as the clock time a person reads; the date appears only when it is not today. */
export function shortTime(iso: string | null): string {
  if (!iso) return "";
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return iso;
  const time = at.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  const today = new Date();
  const sameDay = at.getFullYear() === today.getFullYear()
    && at.getMonth() === today.getMonth()
    && at.getDate() === today.getDate();
  return sameDay ? time : `${at.getMonth() + 1}/${at.getDate()} ${time}`;
}

export function quotaDetail(usage: MagpieUsage | null): string | null {
  if (!usage) return null;
  const first = usage.windows.find((window) => window.resetsAt);
  const parts: string[] = [];
  if (first?.resetsAt) parts.push(`重置 ${first.name} · ${shortTime(first.resetsAt)}`);
  if (usage.readAt) parts.push(`读取 ${shortTime(usage.readAt)}`);
  return parts.length > 0 ? parts.join(" · ") : null;
}

/** A balance string the gateway hands over verbatim, unless it is zero: a zero balance says nothing. */
export function nonzeroBalances(balances: readonly string[]): string[] {
  return balances.filter((balance) => {
    const amount = Number.parseFloat(balance.replace(/[^0-9.-]/g, ""));
    return Number.isNaN(amount) || amount !== 0;
  });
}

/** `5 hours` → `5h`, `7 days` → `7d`, `Month` → the month word: the island's row is one line. */
export function windowShortName(name: string, month: string): string {
  const text = name.trim();
  const hours = /^(\d+)\s*hours?$/i.exec(text);
  if (hours) return `${hours[1]}h`;
  const days = /^(\d+)\s*days?$/i.exec(text);
  if (days) return `${days[1]}d`;
  if (/^(month|30 days)$/i.test(text)) return month;
  return text;
}

/**
 * One quota window in percent, from the field the mode asks for.
 *
 * The gateway sends both `used` and `remaining`; only when the wanted one is missing is the other
 * turned around (100 − x), and the caller marks that as derived rather than passing it off as read.
 */
export function windowPercent(
  window: MagpieWindow,
  mode: QuotaMode,
): { value: number; derived: boolean } | null {
  const wanted = mode === "used" ? window.used : window.remaining;
  if (wanted !== null) return { value: Math.round(wanted), derived: false };
  const other = mode === "used" ? window.remaining : window.used;
  if (other === null) return null;
  return { value: Math.round(100 - other), derived: true };
}

/** The island's quota row: `5h 87% · 7d 82% · 月 85%`, with any non-zero balance last. */
export function quotaTiers(usage: MagpieUsage | null, mode: QuotaMode, month: string): string | null {
  if (!usage) return null;
  const parts: string[] = [];
  for (const window of usage.windows) {
    const percent = windowPercent(window, mode);
    if (!percent) continue;
    parts.push(`${windowShortName(window.name, month)} ${percent.derived ? "≈" : ""}${percent.value}%`);
  }
  parts.push(...nonzeroBalances(usage.balances));
  return parts.length > 0 ? parts.join(" · ") : null;
}

/** The island's spend row: `今天 $7.95 · 7天 $14.10 · 30天 $14.10`; a missing figure stays a dash. */
export function spendTiers(
  usage: MagpieUsage | null,
  labels: { today: string; week: string; month: string },
): string | null {
  if (!usage) return null;
  const tiers = [
    [labels.today, usage.spendToday],
    [labels.week, usage.spendWeek],
    [labels.month, usage.spendMonth],
  ] as const;
  if (!tiers.some(([, cost]) => cost !== null)) return null;
  return tiers.map(([label, cost]) => `${label} ${cost === null ? "—" : `$${cost.toFixed(2)}`}`).join(" · ");
}

/** Why Magpie did not answer, read off the transport error: refused, timed out, or nothing there. */
export function magpieFailureReason(error: string | null): "refused" | "timeout" | "silent" {
  const text = (error ?? "").toLowerCase();
  if (/refused|actively refused|10061|拒绝/.test(text)) return "refused";
  if (/timed out|timeout|超时/.test(text)) return "timeout";
  return "silent";
}

/** The state line under Settings → AI Agent → Usage and quota: where it asked, and what came back. */
export function magpieStateLabel(
  usage: MagpieUsage,
  words: { reasonRefused: string; reasonTimeout: string; reasonSilent: string },
): string {
  if (usage.reachable) return usage.base;
  const reason = magpieFailureReason(usage.error);
  const said = reason === "refused" ? words.reasonRefused : reason === "timeout" ? words.reasonTimeout : words.reasonSilent;
  return `${said}（${usage.base}）`;
}

/** What the two Magpie rows say when the gateway is not there. */
export function magpieMissingLabel(usage: MagpieUsage | null): string {
  if (!usage || !usage.reachable) return MAGPIE_MISSING;
  return usage.error ?? "Magpie 没有给出这个数字";
}
