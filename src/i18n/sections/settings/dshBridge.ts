export type SettingsDshBridgeText = {
  title: string;
  probe: string;
  probing: string;
  copy: string;
  copied: string;
  failed: (error: string) => string;
  status: string;
  process: string;
  plugin: string;
  bridge: string;
  sessions: string;
  sourceTitle: string;
  sourceHint: string;
  states: {
    dshNotRunning: string;
    pluginSilent: string;
    portTaken: string;
    pluginOutdated: string;
    noClients: string;
    ok: string;
  };
  hints: {
    notRunning: string;
    silent: string;
    portTaken: string;
    outdated: (version: string) => string;
    noClients: string;
    ok: string;
  };
  mainProcess: (pid: number) => string;
  childProcesses: (count: number) => string;
  noProcess: string;
  startedAt: (when: string) => string;
  version: (version: string) => string;
  notInstalled: string;
  bridgeCount: (count: number) => string;
  bridgeHint: (count: number) => string;
  bridgeUnavailable: string;
  bridgeEmptyHint: string;
  running: (count: number) => string;
  receiverUnread: string;
  eventsReceived: (count: number) => string;
  eventsRejected: (count: number) => string;
  receiverOff: string;
  receiverDown: (error: string) => string;
  receiverDownUnknown: string;
  noEventsYet: string;
  lastEvent: (kind: string, when: string) => string;
  mountedSession: string;
  noPresence: string;
  presenceAt: (when: string) => string;
  presenceAge: (seconds: number) => string;
  presenceMore: (count: number) => string;
  presenceStale: (count: number) => string;
  mountsRefused: (error: string) => string;
};

export const zhCN: SettingsDshBridgeText = {
  title: "DSH 通知桥",
  probe: "重新检测",
  probing: "检测中…",
  copy: "复制诊断信息",
  copied: "已复制",
  failed: (error) => `检测失败：${error}`,
  status: "状态",
  process: "DSH 进程",
  plugin: "插件",
  bridge: "桥实例",
  sessions: "会话",
  sourceTitle: "来源",
  sourceHint: "在岛的 Agent 卡片里显示 DSH 会话；关掉后只保留 Claude Code 与 Codex。",
  states: {
    dshNotRunning: "未运行",
    pluginSilent: "插件未响应",
    portTaken: "端口被占",
    pluginOutdated: "旧版",
    noClients: "无实例",
    ok: "正常",
  },
  hints: {
    notRunning: "DSH 没有在运行。打开 DSH 之后插件才会被加载。",
    silent: "DSH 在跑，但 47822 端口没有应答：插件可能没启用，或者还没被加载。",
    portTaken: "47822 端口有别的东西在应答，那不是 DSH 的插件。",
    outdated: (version) => `磁盘上是 v${version}，但 DSH 里跑的还是老代码，重启 DSH 才会加载新版，不用重装。`,
    noClients: "插件进程在听，但没有任何 DSH 实例注册到桥，事件一条也不会到达。",
    ok: "插件在线，状态可读。",
  },
  mainProcess: (pid) => `主进程 PID ${pid}`,
  childProcesses: (count) => `另有 ${count} 个渲染/GPU 子进程`,
  noProcess: "未检测到",
  startedAt: (when) => `启动于 ${when}`,
  version: (version) => `v${version}`,
  notInstalled: "没有找到装好的插件",
  bridgeCount: (count) => (count === 0 ? "0 条" : `累计 ${count} 条`),
  bridgeHint: (count) => `hub 累计连接 ${count} 条（旧 hub 未重启，重启后归 1）。`,
  bridgeUnavailable: "旧版插件不提供",
  bridgeEmptyHint: "0 条：插件进程在，但没有客户端注册到桥，事件不会到达。",
  running: (count) => `${count} 个会话在跑`,
  receiverUnread: "接收端未读取",
  eventsReceived: (count) => `已收 ${count} 条`,
  eventsRejected: (count) => `拒收 ${count}`,
  receiverOff: "未运行",
  receiverDown: (error) => `接收端未运行：${error}`,
  receiverDownUnknown: "原因未知",
  noEventsYet: "还没有收到事件",
  lastEvent: (kind, when) => `最近：${kind} · ${when}`,
  mountedSession: "挂载会话",
  noPresence: "没有客户端上报 presence",
  presenceAt: (when) => `最近上报：${when}`,
  presenceAge: (seconds) => `hub 在 ${seconds} 秒前收到这条上报`,
  presenceMore: (count) => `另有 ${count} 页`,
  presenceStale: (count) => `${count} 页上报过，但目前都超过 60 秒没有回声`,
  mountsRefused: (error) => `插件未提供页面挂载（${error}）`,
};

export const en: SettingsDshBridgeText = {
  title: "DSH notifier bridge",
  probe: "Check again",
  probing: "Checking…",
  copy: "Copy diagnostics",
  copied: "Copied",
  failed: (error) => `Check failed: ${error}`,
  status: "State",
  process: "DSH process",
  plugin: "Plugin",
  bridge: "Bridge copies",
  sessions: "Sessions",
  sourceTitle: "Source",
  sourceHint: "Shows DSH sessions among the island's agent cards; off leaves only Claude Code and Codex.",
  states: {
    dshNotRunning: "Not running",
    pluginSilent: "Plugin silent",
    portTaken: "Port taken",
    pluginOutdated: "Outdated",
    noClients: "No instances",
    ok: "OK",
  },
  hints: {
    notRunning: "DSH is not running; the plugin is loaded only once DSH is open.",
    silent: "DSH runs, but nothing answers on 47822: the plugin may be disabled or not loaded yet.",
    portTaken: "Something else answers on 47822, and it is not the DSH plugin.",
    outdated: (version) => `The copy on disk is v${version}, but DSH still runs the old code; restart DSH to load it, no reinstall needed.`,
    noClients: "The plugin listens, but no DSH instance is registered with the bridge, so no event arrives.",
    ok: "The plugin is online and its state is readable.",
  },
  mainProcess: (pid) => `main process PID ${pid}`,
  childProcesses: (count) => (count === 1 ? "1 renderer/GPU child process" : `${count} renderer/GPU child processes`),
  noProcess: "Not detected",
  startedAt: (when) => `started ${when}`,
  version: (version) => `v${version}`,
  notInstalled: "No installed plugin found",
  bridgeCount: (count) => (count === 0 ? "0 connections" : count === 1 ? "1 connection" : `${count} connections`),
  bridgeHint: (count) => `${count} connections recorded since the hub started; a hub restart starts over at 1.`,
  bridgeUnavailable: "Not provided by the old plugin",
  bridgeEmptyHint: "0 connections: the plugin process is up, but nothing is registered with the bridge, so no event arrives.",
  running: (count) => (count === 1 ? "1 session running" : `${count} sessions running`),
  receiverUnread: "receiver not read",
  eventsReceived: (count) => (count === 1 ? "1 received" : `${count} received`),
  eventsRejected: (count) => (count === 1 ? "1 refused" : `${count} refused`),
  receiverOff: "Not running",
  receiverDown: (error) => `Receiver not running: ${error}`,
  receiverDownUnknown: "reason unknown",
  noEventsYet: "no event received yet",
  lastEvent: (kind, when) => `latest: ${kind} · ${when}`,
  mountedSession: "Mounted session",
  noPresence: "no client reported presence yet",
  presenceAt: (when) => `reported at ${when}`,
  presenceAge: (seconds) => (seconds === 1 ? "the hub took this report 1 second ago" : `the hub took this report ${seconds} seconds ago`),
  presenceMore: (count) => (count === 1 ? "1 more page" : `${count} more pages`),
  presenceStale: (count) => (count === 1 ? "1 page reported, but nothing heard from it for 60 seconds" : `${count} pages reported, but nothing heard from them for 60 seconds`),
  mountsRefused: (error) => `the plugin does not offer page mounts (${error})`,
};
