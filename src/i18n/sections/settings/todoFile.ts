export type SettingsTodoFileText = {
  title: string;
  folder: string;
  openFolder: string;
  change: string;
  changeLabel: string;
  folderPlaceholder: string;
  apply: string;
  cancel: string;
  status: string;
  synced: string;
  readOnly: string;
  corrupt: string;
  migrated: string;
  failed: string;
  hint: string;
};

export const zhCN: SettingsTodoFileText = {
  title: "待办数据文件",
  folder: "位置",
  openFolder: "打开文件夹",
  change: "更改…",
  changeLabel: "新的待办数据文件夹",
  folderPlaceholder: "D:/文档/FloatIsle",
  apply: "应用",
  cancel: "取消",
  status: "状态",
  synced: "已保存到文件",
  readOnly: "只读：另一个窗口正在使用这个文件",
  corrupt: "文件损坏，已从备份恢复",
  migrated: "已从旧缓存迁移到文件",
  failed: "写入失败",
  hint: "待办与每日笔记保存在一个 JSON 文件里，清空浏览器缓存不会丢数据；每次写入前会留一份备份。",
};

export const en: SettingsTodoFileText = {
  title: "Task data file",
  folder: "Location",
  openFolder: "Open folder",
  change: "Change…",
  changeLabel: "New task data folder",
  folderPlaceholder: "D:/Documents/FloatIsle",
  apply: "Apply",
  cancel: "Cancel",
  status: "Status",
  synced: "Saved to file",
  readOnly: "Read-only: another window is using this file",
  corrupt: "The file was damaged and restored from a backup",
  migrated: "Migrated from the old cache into a file",
  failed: "Could not write",
  hint: "Tasks and daily notes live in one JSON file, so clearing the browser cache keeps them; a backup is kept before every write.",
};
