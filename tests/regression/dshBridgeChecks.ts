// The DSH bridge card's regression assertions, kept out of the settings page script so that page
// stays inside the file-size limit. Two scenarios: the healthy default, and a bridge whose plugin
// answers with the full new field set but has nothing registered (clients: 0).
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
  check(rowText("桥实例").includes("4 份") && rowText("桥实例").includes("同一个进程里被加载了 4 次") && !rowText("桥实例").includes("客户端连接"),
    "Bridge copies use the plugin's own words, not four connected clients");
  check(bridge?.textContent?.includes("正常") && bridge?.textContent?.includes("本版浮岛未实现接收端（47821）"),
    "A healthy bridge still calls out the island's own missing event receiver");
}

export function checkEmptyBridge(check: Check): void {
  const badge = bridgeRow("状态")?.querySelector<HTMLElement>(".dsh-bridge__badge");
  check(badge?.className === "dsh-bridge__badge dsh-bridge__badge--noClients" && badge.textContent === "无实例"
    && rowDescription("状态") === "插件进程在听，但没有任何 DSH 实例注册到桥，事件一条也不会到达。",
    "An empty bridge gets its own amber state and says why, not the healthy one");
  check(rowDescription("桥实例") === "0 份：插件进程在，但没有实例注册到桥，事件不会到达。"
    && bridgeRow("桥实例")?.querySelector(".dsh-bridge__value")?.textContent === "0 份",
    "The bridge row spells out that zero copies means no events arrive");
  check(!bridgeGroup()?.querySelector(".dsh-bridge__badge--ok") && !bridgeGroup()?.textContent?.includes("正常"),
    "Nothing on the card calls an empty bridge healthy");
}
