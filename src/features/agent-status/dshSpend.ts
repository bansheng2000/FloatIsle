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
  windows: MagpieWindow[];
  balances: string[];
  readAt: string | null;
};

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

/** What the two Magpie rows say when the gateway is not there. */
export function magpieMissingLabel(usage: MagpieUsage | null): string {
  if (!usage || !usage.reachable) return MAGPIE_MISSING;
  return usage.error ?? "Magpie 没有给出这个数字";
}
