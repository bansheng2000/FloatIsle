export type SettingsProjectText = {
  title: string;
  appName: string;
  versionChip: (version: string) => string;
  description: string;
  /** The last card of About: resetting every setting, and quitting the island. */
  quit: string;
  quitHint: string;
  quitAction: string;
  /** About: the changelog, built from every version's release notes. */
  changelog: string;
  changelogAll: (count: number) => string;
  changelogLess: string;
  /** Upstream (MIT, 3.0.8 and earlier) notes, folded away from our own timeline. */
  upstreamTitle: string;
  upstreamAttribution: string;
  upstreamShow: (count: number) => string;
  upstreamHide: string;
  /** Where the project lives; the only outbound link About keeps. */
  repository: string;
  repositoryHint: string;
  /** The sidebar link to the author's site, under About. */
  author: string;
  openFailed: string;
  /** The todo the island adds when a new version is found, and its button. */
  update: {
    downloadingUnknown: string;
    updateTodo: string;
    updateNow: string;
    updateDownloading: (percent: number) => string;
    installing: string;
    /** About: the always-visible update group and its controls. */
    title: string;
    checkAction: string;
    statusLatest: string;
    statusFound: string;
    statusDownloading: string;
    statusReady: string;
    statusChecking: string;
    statusInstalling: string;
    statusError: string;
  };
};

export const zhCN: SettingsProjectText = {
  title: "关于 FloatIsle",
  appName: "FloatIsle",
  versionChip: (version) => version,
  description: "屏幕顶部的 AI 与开发控制台：DSH、Claude Code、Codex 的会话状态与额度用量，外加待办、剪贴板、音乐和翻译。数据全部本地读取，不上传。",
  repository: "GitHub",
  repositoryHint: "源码、更新记录与问题反馈",
  quit: "退出 FloatIsle",
  quitHint: "和托盘菜单里的「退出」一样；要再打开，从开始菜单或桌面快捷方式启动。",
  quitAction: "退出",
  changelog: "更新日志",
  changelogAll: (count) => `显示全部 ${count} 个版本`,
  changelogLess: "只看最近几个版本",
  upstreamTitle: "上游历史（3.0.8 及更早）",
  upstreamAttribution: "这是 fly-island 3.0.8（MIT, Copyright (c) 2026 flyl1u and contributors）的发布说明原文，只作历史留存，不是本项目的功能清单。",
  upstreamShow: (count) => `展开上游历史（${count} 个版本）`,
  upstreamHide: "收起上游历史",
  author: "GitHub",
  openFailed: "暂时没能打开浏览器，请再试一次。",
  update: {
    downloadingUnknown: "正在下载…",
    updateTodo: "检测到新版本",
    updateNow: "更新",
    updateDownloading: (percent) => `下载 ${percent}%`,
    installing: "正在安装，小岛会自动重启",
    title: "更新",
    checkAction: "检查更新",
    statusLatest: "已是最新版本",
    statusFound: "有可用更新",
    statusDownloading: "正在下载更新",
    statusReady: "更新已下载，等待安装",
    statusChecking: "正在检查更新",
    statusInstalling: "正在安装更新",
    statusError: "检查更新失败",
  },
};

export const en: SettingsProjectText = {
  title: "About the island",
  appName: "FloatIsle",
  versionChip: (version) => version,
  description: "The AI and dev console at the top of the screen: DSH, Claude Code and Codex sessions with quota and spend, next to todos, clipboard, music and translation. Everything is read locally; nothing is uploaded.",
  repository: "GitHub",
  repositoryHint: "Source, release notes and issue reports",
  quit: "Quit the island",
  quitHint: "Same as Quit in the tray menu; start it again from the Start menu or a desktop shortcut.",
  quitAction: "Quit",
  changelog: "Changelog",
  changelogAll: (count) => `Show all ${count} versions`,
  changelogLess: "Show recent versions only",
  upstreamTitle: "Upstream history (3.0.8 and earlier)",
  upstreamAttribution: "The original release notes of fly-island 3.0.8 (MIT, Copyright (c) 2026 flyl1u and contributors). Kept for the record only; they are not a feature list of this project.",
  upstreamShow: (count) => `Show upstream history (${count} versions)`,
  upstreamHide: "Hide upstream history",
  author: "About the author",
  openFailed: "Couldn't open your browser. Please try again.",
  update: {
    downloadingUnknown: "Downloading…",
    updateTodo: "New version available",
    updateNow: "Update",
    updateDownloading: (percent) => `Downloading ${percent}%`,
    installing: "Installing — the island will restart itself",
    title: "Updates",
    checkAction: "Check for updates",
    statusLatest: "You're up to date",
    statusFound: "An update is available",
    statusDownloading: "Downloading the update",
    statusReady: "Update downloaded, waiting to install",
    statusChecking: "Checking for updates",
    statusInstalling: "Installing the update",
    statusError: "Update check failed",
  },
};
