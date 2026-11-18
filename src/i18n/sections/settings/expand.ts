export type SettingsExpandText = {
  title: string;
  trigger: string;
  click: string;
  hover: string;
  /** (A) Seconds after the cursor leaves the open island before it collapses. */
  autoCollapse: string;
  autoCollapseHint: string;
  /** (B) Seconds the collapsed island idles before it tucks into a line. */
  tuckDelay: string;
  tuckDelayHint: string;
  /** Tucked shape at the top edge. */
  tuckShape: string;
  tuckShapeHint: string;
  thread: string;
  hidden: string;
  edge: string;
  /** E3: don't auto-tuck while a given condition holds. */
  noTuckWhileAgent: string;
  noTuckWhileAgentHint: string;
  noTuckWhileLyrics: string;
  noTuckWhileLyricsHint: string;
  noTuckWhileTimer: string;
  noTuckWhileTimerHint: string;
  noTuckGroup: string;
  /** Unit after the number fields. */
  secondsUnit: string;
};

export const zhCN: SettingsExpandText = {
  title: "岛屿",
  trigger: "展开方式",
  click: "点击",
  hover: "鼠标悬停",
  autoCollapse: "展开后自动收起",
  autoCollapseHint: "鼠标离开展开的岛屿多少秒后收回成小岛；填 0 不自动收起。",
  tuckDelay: "收起后贴边成细线",
  tuckDelayHint: "收起的小岛闲置多少秒后缩到屏幕顶部，只剩一条线。",
  tuckShape: "贴边形态",
  tuckShapeHint: "收起后停在屏幕顶部的样子；细线只留一条线，隐藏完全滑走，露边留一小截岛身。",
  thread: "细线",
  hidden: "隐藏",
  edge: "露边",
  noTuckGroup: "这些时候不贴边",
  noTuckWhileAgent: "Agent 正在运行",
  noTuckWhileAgentHint: "打开后有 Agent 会话运行时，小岛不自动贴边。",
  noTuckWhileLyrics: "正在显示歌词",
  noTuckWhileLyricsHint: "打开后折叠栏确实在显示歌词时，小岛不自动贴边。",
  noTuckWhileTimer: "待办已开始计时",
  noTuckWhileTimerHint: "打开后有待办计时在进行时，小岛不自动贴边。",
  secondsUnit: "秒",
};

export const en: SettingsExpandText = {
  title: "Island",
  trigger: "Open the island by",
  click: "Click",
  hover: "Hover",
  autoCollapse: "Collapse the open island",
  autoCollapseHint: "Seconds after the cursor leaves the open island before it collapses; 0 keeps it open.",
  tuckDelay: "Tuck the collapsed island",
  tuckDelayHint: "Seconds the collapsed island sits idle before it slides up into a thin line.",
  tuckShape: "Tucked shape",
  tuckShapeHint: "What the island holds at the top edge when tucked: a thin line, fully slid away, or a small sliver left visible.",
  thread: "Thin line",
  hidden: "Hidden",
  edge: "Sliver",
  noTuckGroup: "Don't tuck during these",
  noTuckWhileAgent: "An agent is running",
  noTuckWhileAgentHint: "When on, the island will not auto-tuck while an agent session is running.",
  noTuckWhileLyrics: "Lyrics are showing",
  noTuckWhileLyricsHint: "When on, the island will not auto-tuck while the collapsed bar is actually showing lyrics.",
  noTuckWhileTimer: "A todo timer is running",
  noTuckWhileTimerHint: "When on, the island will not auto-tuck while a todo timer is counting down.",
  secondsUnit: "s",
};
