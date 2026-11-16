export type SettingsDshUsageText = {
  title: string;
  readAgain: string;
  reading: string;
  session: string;
  sessionSource: string;
  sessionSourceWithoutLedger: string;
  sessionUnknown: string;
  spend: string;
  magpieSource: string;
  quota: string;
  unavailable: string;
  magpieNotRunning: string;
  ledgerMissingShort: string;
  dshDisconnected: string;
  footnote: string;
  /** The island's own two rows: shorter labels, three tiers each on one line. */
  islandSpend: string;
  islandQuota: string;
  tierToday: string;
  tierWeek: string;
  tierMonth: string;
  monthShort: string;
  /** Settings → AI Agent → how the island draws those rows. */
  settingsTitle: string;
  mode: string;
  modeRemaining: string;
  modeUsed: string;
  modeHint: string;
  refresh: string;
  refreshSeconds: string;
  refreshMinute: string;
  refreshMinutes: string;
  refreshManual: string;
  refreshHint: string;
  magpieOnline: string;
  magpieOffline: string;
  magpieRetry: string;
  reasonRefused: string;
  reasonTimeout: string;
  reasonSilent: string;
};

export const dshUsageZh: SettingsDshUsageText = {
  title: "用量",
  readAgain: "重新读取",
  reading: "读取中…",
  session: "本会话 token",
  sessionSource: "来源：usage.sqlite + 插件上报的当前会话",
  sessionSourceWithoutLedger: "账本缺失 · 来源：插件上报的当前会话",
  sessionUnknown: "还没收到当前会话，暂时读不出",
  spend: "花费",
  magpieSource: "来源：Magpie",
  quota: "剩余额度",
  unavailable: "未提供",
  magpieNotRunning: "Magpie 未运行",
  ledgerMissingShort: "账本不存在",
  dshDisconnected: "DSH 未连接",
  footnote:
    "本会话只报 token，不折算金额；花费与额度是 Magpie 自己的滚动周期，不能当成本会话的数字。额度行的百分比按「显示方式」取剩余或已用，两个数都由 Magpie 给出。",
  islandSpend: "花费",
  islandQuota: "额度",
  tierToday: "今天",
  tierWeek: "7天",
  tierMonth: "30天",
  monthShort: "月",
  settingsTitle: "用量与额度",
  mode: "显示方式",
  modeRemaining: "剩余",
  modeUsed: "已用",
  modeHint: "岛上额度行按这一项显示；两个数都由 Magpie 给出时直接用，只有某一档缺字段才用 100 减出来并标 ≈。",
  refresh: "刷新间隔",
  refreshSeconds: "30 秒",
  refreshMinute: "1 分钟",
  refreshMinutes: "5 分钟",
  refreshManual: "手动",
  refreshHint: "手动时只在打开 AI Agent 页时读一次，不自动刷新。",
  magpieOnline: "Magpie 已连接",
  magpieOffline: "Magpie 未连接",
  magpieRetry: "重试",
  reasonRefused: "连接被拒",
  reasonTimeout: "超时",
  reasonSilent: "端口无响应",
};

export const dshUsageEn: SettingsDshUsageText = {
  title: "Usage",
  readAgain: "Read again",
  reading: "Reading…",
  session: "Session tokens",
  sessionSource: "Source: usage.sqlite + the session the plugin reports",
  sessionSourceWithoutLedger: "Ledger missing · source: the session the plugin reports",
  sessionUnknown: "No session reported yet, so nothing can be read",
  spend: "Spend",
  magpieSource: "Source: Magpie",
  quota: "Quota left",
  unavailable: "Not provided",
  magpieNotRunning: "Magpie not running",
  ledgerMissingShort: "ledger missing",
  dshDisconnected: "DSH not connected",
  footnote:
    "The session row counts tokens only and never turns them into money; spend and quota follow Magpie's own rolling period and are not session figures. The quota row shows either what is left or what is used, as the Show setting asks.",
  islandSpend: "Spend",
  islandQuota: "Quota",
  tierToday: "today",
  tierWeek: "7d",
  tierMonth: "30d",
  monthShort: "mo",
  settingsTitle: "Usage and quota",
  mode: "Show",
  modeRemaining: "Remaining",
  modeUsed: "Used",
  modeHint: "The island's quota row follows this choice; both numbers come from Magpie, and only a window missing the wanted one has it worked out as 100 minus the other, marked ≈.",
  refresh: "Refresh",
  refreshSeconds: "30 seconds",
  refreshMinute: "1 minute",
  refreshMinutes: "5 minutes",
  refreshManual: "Manual",
  refreshHint: "On manual, the numbers are read once when the Agent page opens and then left alone.",
  magpieOnline: "Magpie connected",
  magpieOffline: "Magpie not connected",
  magpieRetry: "Retry",
  reasonRefused: "connection refused",
  reasonTimeout: "timed out",
  reasonSilent: "no answer on the port",
};
