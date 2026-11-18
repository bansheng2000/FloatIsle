export type SettingsMessageSourcesText = {
  qqTitle: string;
  qqHint: string;
  notificationStayTitle: string;
  notificationStayHint: string;
  notificationStayUnit: string;
  wechatTitle: string;
  wechatHint: string;
};

export const zhCN: SettingsMessageSourcesText = {
  qqTitle: "QQ 通知",
  qqHint: "通过 Windows 通知读取 QQ 消息的标题和预览，只保留在本次运行的内存中。需要 QQ 已登录并开启系统通知。",
  notificationStayTitle: "消息预览停留时长",
  notificationStayHint: "QQ 消息预览在岛上停留的秒数（3–30 秒，默认 5 秒）。一次连续消息的整组展示不超过停留时长的两倍。",
  notificationStayUnit: "秒",
  wechatTitle: "微信提醒",
  wechatHint: "只监听电脑版微信在任务栏的闪烁提醒，读取不到消息内容、发送人或未读数。需要微信保持登录。",
};

export const en: SettingsMessageSourcesText = {
  qqTitle: "QQ notifications",
  qqHint: "Reads the title and preview of QQ messages from Windows notifications and keeps them in memory for this run only. QQ must be signed in with system notifications on.",
  notificationStayTitle: "Message preview stay time",
  notificationStayHint: "Seconds a QQ message preview stays on the island (3–30, default 5). A whole burst never lasts beyond twice the stay.",
  notificationStayUnit: "s",
  wechatTitle: "WeChat reminders",
  wechatHint: "Only watches WeChat desktop flashing in the taskbar. Message text, senders and unread counts are not available. WeChat must stay signed in.",
};
