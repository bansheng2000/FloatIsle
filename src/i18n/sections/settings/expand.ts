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
  secondsUnit: "s",
};
