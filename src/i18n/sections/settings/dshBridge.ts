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
    ok: string;
  };
  hints: {
    notRunning: string;
    silent: string;
    portTaken: string;
    outdated: (version: string) => string;
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
  running: (count: number) => string;
  recent: (when: string) => string;
  recentNever: string;
  eventGap: string;
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
    ok: "正常",
  },
  hints: {
    notRunning: "DSH 没有在运行。打开 DSH 之后插件才会被加载。",
    silent: "DSH 在跑，但 47822 端口没有应答：插件可能没启用，或者还没被加载。",
    portTaken: "47822 端口有别的东西在应答，那不是 DSH 的插件。",
    outdated: (version) => `磁盘上是 v${version}，但 DSH 里跑的还是老代码，重启 DSH 才会加载新版，不用重装。`,
    ok: "插件在线，状态可读。",
  },
  mainProcess: (pid) => `主进程 PID ${pid}`,
  childProcesses: (count) => `另有 ${count} 个渲染/GPU 子进程`,
  noProcess: "未检测到",
  startedAt: (when) => `启动于 ${when}`,
  version: (version) => `v${version}`,
  notInstalled: "没有找到装好的插件",
  bridgeCount: (count) => `${count} 份`,
  bridgeHint: (count) => `同一个进程里被加载了 ${count} 次；原因待确认，插件内已改为进程级单例共享一套资源。`,
  bridgeUnavailable: "旧版插件不提供",
  running: (count) => `${count} 个会话在跑`,
  recent: (when) => `最近事件：${when}`,
  recentNever: "最近事件：无",
  eventGap: "事件通道：本版浮岛未实现接收端（47821），插件发的事件目前全部失败。",
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
    ok: "OK",
  },
  hints: {
    notRunning: "DSH is not running; the plugin is loaded only once DSH is open.",
    silent: "DSH runs, but nothing answers on 47822: the plugin may be disabled or not loaded yet.",
    portTaken: "Something else answers on 47822, and it is not the DSH plugin.",
    outdated: (version) => `The copy on disk is v${version}, but DSH still runs the old code; restart DSH to load it, no reinstall needed.`,
    ok: "The plugin is online and its state is readable.",
  },
  mainProcess: (pid) => `main process PID ${pid}`,
  childProcesses: (count) => (count === 1 ? "1 renderer/GPU child process" : `${count} renderer/GPU child processes`),
  noProcess: "Not detected",
  startedAt: (when) => `started ${when}`,
  version: (version) => `v${version}`,
  notInstalled: "No installed plugin found",
  bridgeCount: (count) => (count === 1 ? "1 copy" : `${count} copies`),
  bridgeHint: (count) => `loaded ${count} times in one process; the cause is unconfirmed, and the plugin now shares one set of resources through a process-wide singleton.`,
  bridgeUnavailable: "Not provided by the old plugin",
  running: (count) => (count === 1 ? "1 session running" : `${count} sessions running`),
  recent: (when) => `last event: ${when}`,
  recentNever: "last event: never",
  eventGap: "Event channel: this build of the island has no receiver on 47821, so the plugin's event notices currently all fail.",
};
