// The DSH bridge card's regression assertions, kept out of the settings page script so that page
// stays inside the file-size limit. Three scenarios: the healthy default, a bridge whose plugin
// answers with the full new field set but has nothing registered (clients: 0), and an island whose
// own listener on 47821 never came up.
import { setDshBridgeScenario } from "./settingsMocks";

type Check = (condition: unknown, message: string) => void;

const bridgeGroup = () =>
  [...document.querySelectorAll<HTMLElement>(".settings-group")].find((group) => group.textContent?.includes("DSH 通知桥"));

const bridgeRow = (title: string) =>
  [...(bridgeGroup()?.querySelectorAll<HTMLElement>(".settings-row") ?? [])].find(
    (row) => row.querySelector(".settings-row__title")?.textContent === title,
  );

// The whole row: the healthy checks read a value from the control and a count from the description.
const rowText = (title: string) => bridgeRow(title)?.textContent ?? "";
const rowDescription = (title: string) => bridgeRow(title)?.querySelector(".settings-row__description")?.textContent ?? "";

export function checkHealthyBridge(check: Check): void {
  const bridge = bridgeGroup();
  check(rowText("DSH 进程").includes("主进程 PID 20832") && rowText("DSH 进程").includes("另有 8 个渲染/GPU 子进程"),
    "DSH's main process and its renderer children are reported apart");
  check(rowText("桥实例").includes("累计 4 条") && rowText("桥实例").includes("hub 累计连接 4 条（旧 hub 未重启，重启后归 1）") && !rowText("桥实例").includes("客户端连接"),
    "Bridge connections read as the hub's running total, not as process loads");
  check(bridge?.textContent?.includes("正常") && rowText("会话").includes("已收 3 条") && rowText("会话").includes("最近：task ·"),
    "A healthy bridge reports what the island's own receiver has taken, and what arrived last");
  check(rowText("挂载会话").includes("s2") && rowDescription("挂载会话") === "hub 在 1 秒前收到这条上报",
    "The mounted page's session comes from /mounted, with the hub's own age for it");
}

export function checkEmptyBridge(check: Check): void {
  const badge = bridgeRow("状态")?.querySelector<HTMLElement>(".dsh-bridge__badge");
  check(badge?.className === "dsh-bridge__badge dsh-bridge__badge--noClients" && badge.textContent === "无实例"
    && rowDescription("状态") === "插件进程在听，但没有任何 DSH 实例注册到桥，事件一条也不会到达。",
    "An empty bridge gets its own amber state and says why, not the healthy one");
  check(rowDescription("桥实例") === "0 条：插件进程在，但没有客户端注册到桥，事件不会到达。"
    && bridgeRow("桥实例")?.querySelector(".dsh-bridge__value")?.textContent === "0 条",
    "The bridge row spells out that zero connections means no events arrive");
  check(!bridgeGroup()?.querySelector(".dsh-bridge__badge--ok") && !bridgeGroup()?.textContent?.includes("正常"),
    "Nothing on the card calls an empty bridge healthy");
  check(rowText("会话").includes("已收 0 条") && rowText("会话").includes("还没有收到事件")
    && rowText("挂载会话").includes("没有客户端上报 presence"),
    "A listening receiver with no report shows zero events and says nobody reported presence");
}

export function checkReceiverDown(check: Check): void {
  const note = bridgeGroup()?.querySelector<HTMLElement>(".dsh-bridge__note--attention");
  check(note?.textContent?.includes("接收端未运行：127.0.0.1:47821 is already in use"),
    "A listener that failed to bind says so with the reason it failed");
  check(rowDescription("会话") === "未运行" && rowText("挂载会话").includes("插件未提供页面挂载（HTTP 405）"),
    "With no listener the sessions row says so, and the mount read is reported as refused, never as empty");
}

/** Runs every bridge scenario in turn; each one needs the page to remount so the card probes again. */
export async function checkBridgeScenarios(
  check: Check,
  openPage: (category: string) => Promise<unknown>,
  settle: () => Promise<unknown>,
): Promise<void> {
  checkHealthyBridge(check);
  for (const [scenario, verify] of [["noClients", checkEmptyBridge], ["receiverDown", checkReceiverDown]] as const) {
    setDshBridgeScenario(scenario);
    await openPage("agents");
    await openPage("agentConnect");
    await settle();
    verify(check);
  }
  setDshBridgeScenario("ok");
}
