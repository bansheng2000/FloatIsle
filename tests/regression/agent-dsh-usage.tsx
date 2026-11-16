// pnpm dev: /tests/regression/agent-dsh-usage.html?test=1 runs the checks; without it this is visual QA.
// The Agent page's two usage rows: Magpie's spend for today / 7 days / 30 days, and the quota
// windows as remaining or as used. When Magpie cannot be reached the whole block goes away rather
// than showing a zero, a last reading, or the 30-day figure passed off as today.
import { act } from "react";
import { createRoot } from "react-dom/client";
import { mockIPC } from "@tauri-apps/api/mocks";
import "../../src/styles/tokens.css";
import { I18nProvider, LOCALE_STORAGE_KEY } from "../../src/i18n";
import { AgentSessionsPanel } from "../../src/features/agent-status/AgentSessionsPanel";
import type { AgentUsages } from "../../src/features/agent-status/usage";
import type { UsageWindow } from "../../src/features/agent-status/api";
import type { AgentSessionSummary } from "../../src/features/agent-status/api";
import type { MagpieUsage, QuotaMode } from "../../src/features/agent-status/dshSpend";
import { getAgentPageExpandedHeight } from "../../src/windows/island/layout";

const ISLAND_CHROME_HEIGHT = 86;
type Source = "full" | "used" | "derived" | "zeroBalance" | "noWeek" | "noMagpie";

Object.assign(globalThis, { isTauri: true, IS_REACT_ACT_ENVIRONMENT: true });
const params = new URLSearchParams(location.search);
localStorage.setItem(LOCALE_STORAGE_KEY, "zh-CN");
const now = Date.now();

const window5h = (remaining: number | null, used: number | null) => ({
  name: "5 hours", kind: "plan", remaining, used, resetsAt: new Date(now + 3 * 3_600_000).toISOString(),
});
const window7d = (remaining: number | null, used: number | null) => ({
  name: "7 days", kind: "plan", remaining, used, resetsAt: new Date(now + 3 * 86_400_000).toISOString(),
});
const month = (remaining: number | null, used: number | null) => ({
  name: "Month", kind: "plan", remaining, used, resetsAt: new Date(now + 20 * 86_400_000).toISOString(),
});

/** Remaining and used are deliberately not complementary, so a mode that reads the wrong one shows. */
const USAGE = (overrides: Partial<MagpieUsage> = {}): MagpieUsage => ({
  base: "http://127.0.0.1:3425", reachable: true, error: null,
  spend: { cost: 14.11, period: "30d", calls: 8_526, errors: 0, unpriced: 0 },
  spendToday: 7.95, spendWeek: 14.10, spendMonth: 14.11,
  windows: [window5h(79, 20), window7d(87, 13), month(86, 12)],
  balances: ["¥0.00"], readAt: new Date(now).toISOString(),
  ...overrides,
});

const sourceFor = (mode: Source): MagpieUsage => {
  if (mode === "derived") return USAGE({ windows: [window5h(79, null), window7d(87, 13), month(86, 12)] });
  if (mode === "zeroBalance") return USAGE({ balances: ["¥0.00", "¥12.30"] });
  if (mode === "noWeek") return USAGE({ spendWeek: null });
  if (mode === "noMagpie") {
    // The gateway is down, but the numbers it gave before are still in the payload: they must not
    // reach the page, or "Magpie gone" would look like a fresh reading.
    return USAGE({
      base: "http://127.0.0.1:3999", reachable: false, error: "Magpie 未运行（http://127.0.0.1:3999）：error sending request: Connection refused",
    });
  }
  return USAGE();
};

let source: Source = "full";
let reads = 0;
mockIPC((command) => {
  if (command === "probe_magpie_usage") {
    reads += 1;
    return sourceFor(source);
  }
  if (command === "dsh_session_tokens") {
    return { available: true, session: "session-7f113c03", input: 111_860, cacheRead: 896, cacheWrite: 0, output: 12_028, calls: 17, source: "usage.sqlite", error: null };
  }
  if (command === "probe_dsh_bridge") return { state: "ok", reachable: true, error: null };
  if (command === "list_agent_effort_labels") return {};
  return null;
});

const sessions: AgentSessionSummary[] = ["claudeCode", "codex"].map((provider, index) => ({
  provider: provider as AgentSessionSummary["provider"], sessionId: `example-${index}`, cwd: "D:\\example-project",
  title: ["Fictional Claude session", "Fictional Codex session"][index],
  model: ["Opus 5.5", "gpt-6-astra"][index], effort: "high", phase: "running",
  lastMessage: "Example response.", updatedAt: now - index * 10_000, turnStartedAt: now - 60_000,
  host: { name: ["Claude", "Codex"][index], kind: "app", exe: null },
}));

const fixture = document.getElementById("fixture")!;
const results = document.getElementById("results")!;
const root = createRoot(fixture);
const ring = (used: number): UsageWindow => ({ name: "", used, resetsAt: now + 3 * 3_600_000 });
const usages: AgentUsages = {
  claude: { updatedAt: now, plan: null, fiveHour: ring(27), sevenDay: ring(62), models: [] },
  codex: { updatedAt: now, plan: "Plus", source: "codex", fiveHour: ring(9), sevenDay: ring(91), models: [] },
  readAt: now,
};
const heightFor = (rows: number) => `${getAgentPageExpandedHeight(rows, 1, "list", 1) - ISLAND_CHROME_HEIGHT}px`;

const messages: string[] = [];
function check(condition: unknown, label: string) {
  if (!condition) throw new Error(label);
  messages.push(`PASS ${label}`);
}
const render = async (rows: number, mode: QuotaMode, tag: string, refreshMs = 60_000) => {
  fixture.style.height = heightFor(rows);
  await act(async () => {
    root.render(
      <I18nProvider>
        <AgentSessionsPanel key={tag} sessions={sessions} hostDisplay="both" showUsage usages={usages} usageMode={mode} usageRefreshMs={refreshMs} />
      </I18nProvider>,
    );
    await new Promise((resolve) => setTimeout(resolve, 60));
  });
};
const block = () => document.querySelector<HTMLElement>(".agent-dsh-usage");
const row = (name: string) => document.querySelector<HTMLElement>(`.agent-dsh-usage__line[data-row=${name}]`);
const tiers = (name: string) => [...(row(name)?.querySelectorAll(".agent-dsh-usage__tier") ?? [])].map((tier) => tier.textContent ?? "");
const label = (name: string) => row(name)?.querySelector(".agent-dsh-usage__label")?.textContent ?? "";

try {
  if (params.has("test")) {
    await render(3, "remaining", "remaining");
    check(label("spend") === "花费" && label("quota") === "额度", "两行各有名字：花费 / 额度");
    check(tiers("spend").join(" · ") === "今天 $7.95 · 7天 $14.10 · 30天 $14.11", `花费三档各带自己的周期：${tiers("spend").join(" · ")}`);
    check(tiers("quota").join(" · ") === "5h 79% · 7d 87% · 月 86%", `剩余档直接读 remaining，写成 5h / 7d / 月：${tiers("quota").join(" · ")}`);
    check(tiers("spend").length === 3 && tiers("quota").length === 3, "每行三档都在，一档一个格子");
    check(!document.body.textContent?.includes("¥0.00"), "余额为 0 时不显示 ¥0.00");
    check(!document.querySelector("[data-row=session]"), "岛上不再有本会话 token 那一行");
    check(block()!.previousElementSibling?.classList.contains("agent-quota"), "两行排在各 agent 的额度环下面");
    const list = document.querySelector<HTMLElement>(".agent-sessions__list")!;
    check(list.scrollHeight <= list.clientHeight + 1, "加上这两行后，设为 3 条仍放下 3 个会话（高度算进岛屿）");
    check(!list.querySelector(".agent-dsh-usage"), "两行在列表外面：滚动会话列表不会把它们带着滚");

    await render(3, "used", "used");
    check(tiers("quota").join(" · ") === "5h 20% · 7d 13% · 月 12%", `已用档直接读 used，不用 100 减：${tiers("quota").join(" · ")}`);

    source = "derived";
    await render(3, "used", "derived");
    check(tiers("quota")[0] === "5h ≈21%", `某一档只有 remaining 时才用 100 减，并标 ≈：${tiers("quota")[0]}`);

    source = "zeroBalance";
    await render(3, "remaining", "zeroBalance");
    check(tiers("quota").includes("¥12.30") && !tiers("quota").includes("¥0.00"), "余额非 0 照原串显示，0 不显示");

    source = "noWeek";
    await render(3, "remaining", "noWeek");
    check(tiers("spend").join(" · ") === "今天 $7.95 · 7天 — · 30天 $14.11", `缺哪一档就留破折号，不拿 30 天填 7 天：${tiers("spend").join(" · ")}`);

    source = "full";
    const before = reads;
    await render(3, "remaining", "manual", 0);
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 400)); });
    check(reads === before + 1, `刷新间隔为手动时只在挂载时读一次：${reads - before} 次`);

    source = "noMagpie";
    await render(3, "remaining", "noMagpie");
    check(!block(), "Magpie 连不上时整块不显示，即使报文里还带着上次的数");
    check(!document.body.textContent?.includes("$7.95") && !document.body.textContent?.includes("5h 79%"), "旧数字一个都没留在页面上");
    check(reads >= 1, "连不上也真的问过 Magpie");

    source = "full";
    await render(3, "remaining", "back", 60_000);
    check(tiers("spend").length === 3, "Magpie 回来后不重启就自动显示");
    results.textContent = `${messages.join("\n")}\n\n全部 ${messages.length} 项检查通过。`;
    results.dataset.result = "passed";
  } else {
    await render(3, "remaining", "visual");
    results.textContent = "视觉检查：花费三档与额度三档在额度环下面，各占一列。";
  }
} catch (error) {
  results.textContent = `${messages.join("\n")}\nFAIL ${String(error)}`;
  results.dataset.result = "failed";
}
