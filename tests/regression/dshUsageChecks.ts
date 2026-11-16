// The usage card's regression assertions. Three scenarios: every source answering, Magpie gone
// while the ledger still answers, and the ledger gone while Magpie still answers. The last two
// exist to prove the three rows never borrow each other's numbers.
import { setDshUsageScenario } from "./settingsMocks";

type Check = (condition: unknown, message: string) => void;

const usageGroup = () =>
  [...document.querySelectorAll<HTMLElement>(".settings-group")].find((group) => group.textContent?.includes("本会话 token"));

const usageRow = (title: string) =>
  [...(usageGroup()?.querySelectorAll<HTMLElement>(".settings-row") ?? [])].find(
    (row) => row.querySelector(".settings-row__title")?.textContent === title,
  );

const rowText = (title: string) => usageRow(title)?.textContent ?? "";
const rowDescription = (title: string) => usageRow(title)?.querySelector(".settings-row__description")?.textContent ?? "";
const rowValue = (title: string) => usageRow(title)?.querySelector(".dsh-usage__value")?.textContent ?? "";

export function checkAllSources(check: Check): void {
  check(rowValue("本会话 token") === "1,200 in · 950 out" && rowDescription("本会话 token").includes("cache 3,400 read")
    && rowDescription("本会话 token").includes("6 calls"),
    "Session tokens come from the ledger, with their cache split and call count");
  check(rowValue("花费") === "近 30 天 · $11.19" && rowDescription("花费").includes("7,428 次调用")
    && rowDescription("花费").includes("有 3 次调用未计价"),
    "Spend keeps Magpie's period and flags the calls it could not price");
  check(rowValue("剩余额度") === "5 hours 86% · 7 days 90% · Month 89% · ¥0.00",
    "Remaining quota lists each window and repeats a balance string verbatim");
  check(!rowText("花费").includes(" in · ") && !rowText("剩余额度").includes(" in · "),
    "Spend and quota never borrow the session token shape");
}

export function checkMagpieMissing(check: Check): void {
  check(rowValue("花费") === "未提供"
    && rowDescription("花费").includes("未配置 Magpie（本机未检测到 127.0.0.1:3425）"),
    "Without Magpie, spend says it is unconfigured instead of showing zero");
  check(rowValue("剩余额度") === "未提供", "Without Magpie, quota is absent rather than an empty success");
  check(rowValue("本会话 token") === "1,200 in · 950 out",
    "A missing gateway leaves the session row untouched — the ledger is a separate source");
}

export function checkLedgerMissing(check: Check): void {
  check(rowValue("本会话 token") === "未提供" && rowDescription("本会话 token").includes("账本不存在"),
    "An unreadable ledger leaves the session row unprovided, never zero");
  check(rowValue("花费") === "近 30 天 · $11.19" && rowValue("剩余额度").startsWith("5 hours 86%"),
    "A missing ledger leaves spend and quota untouched");
}

/** Runs the three scenarios in order; each one reopens the page so the card reads again. */
export async function checkUsageScenarios(
  check: Check,
  openPage: (category: string) => Promise<void>,
  settle: () => Promise<void>,
): Promise<void> {
  const visit = async (scenario: "all" | "noMagpie" | "noLedger") => {
    setDshUsageScenario(scenario);
    await openPage("about");
    await openPage("agentConnect");
    await settle();
  };
  await visit("all");
  checkAllSources(check);
  await visit("noMagpie");
  checkMagpieMissing(check);
  await visit("noLedger");
  checkLedgerMissing(check);
  setDshUsageScenario("all");
}
