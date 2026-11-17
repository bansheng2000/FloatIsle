export type SettingsColorsText = {
  title: string;
  taskText: string;
  pulse: string;
  islandBackground: string;
  /** (E1) ⑨ Expanded-page text colour; only text follows it. */
  pageText: string;
  /** (E1) ① Colour presets and the one-time "restore default" offer. */
  presetsTitle: string;
  presetSave: string;
  presetNamePlaceholder: string;
  presetEmpty: string;
  presetDelete: string;
  restoreDefault: string;
};

export const zhCN: SettingsColorsText = {
  title: "颜色",
  taskText: "任务/待办字样",
  pulse: "亮点颜色",
  islandBackground: "岛屿背景",
  pageText: "页面文字色",
  presetsTitle: "配色预设",
  presetSave: "保存当前配色",
  presetNamePlaceholder: "预设名称",
  presetEmpty: "还没有配色预设",
  presetDelete: "删除预设",
  restoreDefault: "恢复默认配色",
};

export const en: SettingsColorsText = {
  title: "Colors",
  taskText: "Task text",
  pulse: "Accent",
  islandBackground: "Island background",
  pageText: "Page text",
  presetsTitle: "Colour presets",
  presetSave: "Save current colours",
  presetNamePlaceholder: "Preset name",
  presetEmpty: "No colour presets yet",
  presetDelete: "Delete preset",
  restoreDefault: "Restore default colours",
};
