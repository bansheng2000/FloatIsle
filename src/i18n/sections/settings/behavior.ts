export type SettingsBehaviorText = {
  title: string;
  /** Settings open in their own window instead of as a page of the island. */
  detachedSettings: string;
  detachedSettingsHint: string;
  /** In-product watchdog that relaunches the island after a crash. */
  crashGuard: string;
  crashGuardHint: string;
};

export const zhCN: SettingsBehaviorText = {
  title: "系统",
  detachedSettings: "独立设置窗口",
  detachedSettingsHint: "关闭时设置作为岛屿的一页打开",
  crashGuard: "崩溃自愈守护",
  crashGuardHint: "开启后，浮岛意外退出会自动重新拉起；主动退出不会",
};

export const en: SettingsBehaviorText = {
  title: "System",
  detachedSettings: "Separate settings window",
  detachedSettingsHint: "When off, settings open as a page of the island",
  crashGuard: "Crash self-healing",
  crashGuardHint: "When on, an unexpected exit relaunches the island; a deliberate quit does not",
};
