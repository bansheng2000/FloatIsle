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
  footnote: string;
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
  footnote:
    "本会话只报 token，不折算金额；花费与额度是 Magpie 自己的滚动周期，不能当成本会话的数字。额度百分比是剩余，不是已用。",
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
  footnote:
    "The session row counts tokens only and never turns them into money; spend and quota follow Magpie's own rolling period and are not session figures. Quota percentages are what is left, not what was used.",
};
