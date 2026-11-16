// The Tauri commands the settings window calls, answered as the island would, for the settings
// regression pages. Importing this installs the mocks; the arrays record what was called.
import { mockIPC, mockWindows } from "@tauri-apps/api/mocks";
import type { MailSnapshot } from "../../src/features/messages/mail/api";

let revision = 0;
export const commands: string[] = [];
export const mailCalls: { command: string; args: Record<string, unknown> }[] = [];
const mailbox = (email: string, extra: Partial<MailSnapshot> = {}): MailSnapshot => ({
  revision: ++revision, email, configured: true, enabled: true, syncing: false, status: "connected",
  unreadCount: 2, items: [], lastSyncedAt: 0, error: null, ...extra,
});
let mailboxes: MailSnapshot[] = [mailbox("demo@qq.com"), mailbox("louis@163.com", { status: "authRequired", enabled: false, unreadCount: null })];
let replyMarkerOn = false;
export const REPLY_MARKER = { path: "C:\\Users\\demo\\.claude\\rules\\focusd-island-reply-marker.md", content: "<!-- focusd-island: reply marker v2 -->\n## Reply status marker" };
const replyMarker = () => ({ installed: replyMarkerOn, outdated: false, ...REPLY_MARKER });
export const replyMarkerCalls: boolean[] = [];
export const translationShortcutCalls: string[] = [];
// Kept like the native store, so a page opened again reads back what was saved.
let clipboardSettings = { enabled: true, captureImages: true, maxItems: 100, shortcut: "Ctrl+Shift+V", shortcutEnabled: true };
// The bridge card has two regression scenarios. The healthy one is the default, so every other
// page keeps asserting what it asserted before. The second replays the reply this machine's DSH
// gave on 2026-11-16 06:0x: the full self-describing field set, but `clients: 0` with an empty
// `clientList`, which the card must never show as OK. Only the epoch value is a stand-in — it is
// a random runtime id, not something a test can read off the wire.
export type DshBridgeScenario = "ok" | "noClients" | "receiverDown";
let dshBridgeScenario: DshBridgeScenario = "ok";
export function setDshBridgeScenario(next: DshBridgeScenario): void {
  dshBridgeScenario = next;
}
const DSH_EXE = String.raw`E:\Programs\DeepSeek Harness\DeepSeek Harness.exe`;
const dshBridgeStatus = () => {
  const status = dshBridgeScenario === "noClients"
    ? {
        state: "noClients",
        processes: { mainPids: [3304], childCount: 8, startedAt: Date.parse("2026-11-16T06:09:19+08:00"), exe: DSH_EXE },
        plugin: { version: "0.12.1", profile: "desktop" },
        endpoint: "127.0.0.1:47822", reachable: true, httpStatus: 200, error: null,
        report: { pid: 19680, epoch: "e-noclients", clients: 0, clientIds: [], running: [], recent: 0, lastEventAt: "2026-11-16T06:20:37.000Z", eventsSent: 0, eventsFailed: 0, lastEventError: "connect ECONNREFUSED 127.0.0.1:47821", listening: true, selfDescribing: true },
        mounts: [], mountsError: null,
        raw: `{"ok":true,"running":[],"pending":[],"clients":0,"clientList":[],"pid":19680,"epoch":"e-noclients","stats":{"enabled":true,"listening":true,"eventsSent":0,"eventsFailed":0,"lastEventAt":"2026-11-16T06:20:37.000Z","lastEventError":"connect ECONNREFUSED 127.0.0.1:47821","heartbeats":114}}`,
      }
    : {
        // The healthy bridge: a self-describing plugin, four connections accumulated in the hub,
        // and the island's own missing event receiver still visible in the plugin's last error.
        state: "ok",
        processes: { mainPids: [20832], childCount: 8, startedAt: Date.now() - 3_600_000, exe: DSH_EXE },
        plugin: { version: "0.12.0", profile: "desktop" },
        endpoint: "127.0.0.1:47822", reachable: true, httpStatus: 200, error: null,
        report: { pid: 20832, epoch: "e1", clients: 4, clientIds: ["20832-1", "20832-2", "20832-3", "20832-4"], running: ["s1", "s2"], recent: 3, lastEventAt: "2026-11-15T13:59:36.830Z", eventsSent: 203, eventsFailed: 2059, lastEventError: "connect ECONNREFUSED 127.0.0.1:47821", listening: true, selfDescribing: true },
        mounts: [{ clientId: "20832-1", mountedSessionId: "s2", visible: true, focused: true, stale: false, reportedAt: "2026-11-16T07:10:00.000Z", receivedAt: "2026-11-16T07:10:00.500Z", ageMs: 1200 }],
        mountsError: null,
        raw: `{"ok":true}`,
      };
  // A plugin that does not serve /_task-notify/mounted at all: the refusal must be visible, not read
  // as "no page has a session".
  return dshBridgeScenario === "receiverDown" ? { ...status, mounts: null, mountsError: "HTTP 405" } : status;
};


// The island's own listener on 47821. Healthy: one client has reported that session s2 is the
// mounted one. "noClients" and "receiverDown" remove that report and then the whole listener.
const dshReceiverStatus = () =>
  dshBridgeScenario === "ok"
    ? {
        port: 47821, listening: true, error: null, presences: 42, events: 3, rejected: 0,
        presence: { clientId: "20832-1", visible: true, focused: true, mountedSessionId: "s2", reportedAt: "2026-11-16T07:10:00.000Z" },
        lastPresenceAtMs: Date.parse("2026-11-16T07:10:00.000Z"),
        lastEventAtMs: Date.parse("2026-11-16T07:09:40.000Z"), lastEventType: "task",
      }
    : dshBridgeScenario === "noClients"
      ? { port: 47821, listening: true, error: null, presences: 0, events: 0, rejected: 0, presence: null, lastPresenceAtMs: null, lastEventAtMs: null, lastEventType: null }
      : { port: 47821, listening: false, error: "127.0.0.1:47821 is already in use", presences: 0, events: 0, rejected: 0, presence: null, lastPresenceAtMs: null, lastEventAtMs: null, lastEventType: null };

// The three usage figures, each from its own source. "noMagpie" removes only the gateway
// answers; "noLedger" removes only the token ledger, so a test can prove the rows stay
// independent rather than collapsing together.
export type DshUsageScenario = "all" | "noMagpie" | "noLedger";
let dshUsageScenario: DshUsageScenario = "all";
export function setDshUsageScenario(next: DshUsageScenario): void {
  dshUsageScenario = next;
}

const SESSION_TOKENS = {
  available: true, session: "s2", input: 1200, cacheRead: 3400, cacheWrite: 88, output: 950,
  calls: 6, source: "usage.sqlite", error: null,
};

const dshSessionTokens = () =>
  dshUsageScenario === "noLedger"
    ? { ...SESSION_TOKENS, available: false, input: 0, cacheRead: 0, cacheWrite: 0, output: 0, calls: 0,
        error: "账本不存在：C:\\Users\\demo\\.dsh\\bot\\usage.sqlite" }
    : SESSION_TOKENS;

const magpieUsage = () =>
  dshUsageScenario === "noMagpie"
    ? { base: "http://127.0.0.1:3425", reachable: false, error: "connect ECONNREFUSED 127.0.0.1:3425",
        spend: null, spendToday: null, spendWeek: null, spendMonth: null, windows: [], balances: [], readAt: null }
    : {
        base: "http://127.0.0.1:3425", reachable: true, error: null,
        spend: { cost: 11.190596045999968, period: "30d", calls: 7428, errors: 24, unpriced: 3 },
        spendToday: 7.9466, spendWeek: 14.1033, spendMonth: 14.1,
        windows: [
          { name: "5 hours", kind: "plan", remaining: 86, used: 14, resetsAt: "2026-11-16T21:00:00.000Z" },
          { name: "7 days", kind: "plan", remaining: 90, used: 10, resetsAt: "2026-11-20T00:00:00.000Z" },
          { name: "Month", kind: "plan", remaining: 89, used: 11, resetsAt: "2026-12-01T00:00:00.000Z" },
        ],
        balances: ["¥0.00"], readAt: "2026-11-16T08:30:00.000Z",
      };

mockIPC((command, payload) => {
  commands.push(command);
  const args = (payload ?? {}) as Record<string, unknown>;
  switch (command) {
    case "netease_plugin_status": return { state: "none", supported: true, tested: true, version: "3.1.41", arch: "x64", installDir: "D:\\CloudMusic", running: false, legacyInfLink: false, pluginDir: "C:\\betterncm\\plugins" };
    case "install_netease_plugin":
    case "uninstall_netease_plugin": return { keptForeignLoader: false, preservedFiles: [] };
    case "get_media_diagnostics": return { players: [
      { sourceAppId: "QQMusic", label: "QQ音乐", hasSession: true, hasTimeline: true, hasPluginTrackId: false,
        exePath: null, directory: null, version: "21.10.2962.0", status: "ok", neteasePluginState: null },
      { sourceAppId: "cloudmusic.exe", label: "网易云音乐", hasSession: true, hasTimeline: false, hasPluginTrackId: false,
        exePath: null, directory: null, version: null, status: "neteaseNeedsPlugin", neteasePluginState: "none" },
    ] };
    case "get_reply_marker_state": return replyMarker();
    case "set_reply_marker": replyMarkerCalls.push(replyMarkerOn = Boolean(args.enabled)); return replyMarker();
    // Two players open, NetEase playing; Spotify is in the saved order but not open.
    case "get_media_state": return { sessions: [{ id: "QQMusic", label: "QQ音乐", playing: false }, { id: "cloudmusic.exe", label: "网易云音乐", playing: true }] };
    case "get_launch_at_startup": return false;
    case "set_launch_at_startup": return null;
    case "get_default_todo_save_directory": return "";
    case "get_clipboard_history": return { settings: clipboardSettings, items: [] };
    case "set_clipboard_history_settings":
      clipboardSettings = { ...clipboardSettings, ...(args.settings as object | undefined) };
      return { settings: clipboardSettings, items: [] };
    case "get_agent_status": return { codex: { phase: "idle", updatedAt: 0 }, claudeCode: { phase: "idle", updatedAt: 0 }, updatedAt: 0, statusPath: "C:\\Users\\demo\\AppData\\Roaming\\com.focusd.island\\agent-status.json" };
    // The DSH bridge healthy: a self-describing plugin, four connections accumulated in the hub,
    // and the island's own missing event receiver still visible in the plugin's last error.
    case "probe_dsh_bridge": return dshBridgeStatus();
    case "dsh_receiver_status": return dshReceiverStatus();
    case "dsh_session_tokens": return dshSessionTokens();
    case "probe_magpie_usage": return magpieUsage();
    case "copy_text_to_clipboard": return null;
    case "get_qq_mail_state": return mailboxes;
    case "connect_qq_mail":
    case "disconnect_qq_mail":
    case "set_qq_mail_enabled": {
      mailCalls.push({ command, args });
      const email = String(args.email).trim().toLowerCase();
      const next = command === "disconnect_qq_mail"
        ? mailbox(email, { configured: false, enabled: false, status: "disconnected", unreadCount: null })
        : mailbox(email, { enabled: args.enabled !== false, status: args.enabled === false ? "paused" : "connected" });
      mailboxes = [...mailboxes.filter((box) => box.email !== email), ...(next.configured ? [next] : [])];
      return next;
    }
    case "get_qq_notification_state": return { revision: ++revision, enabled: false, status: "disabled", items: [], error: null };
    case "set_qq_notifications_enabled": return { revision: ++revision, enabled: args.enabled, status: args.enabled ? "listening" : "disabled", items: [], error: null };
    case "get_wechat_reminder_state":
    case "set_wechat_reminders_enabled": return { revision: ++revision, enabled: args.enabled ?? true, status: args.enabled === false ? "disabled" : "listening", pending: false, lastAttentionAt: null, error: null, previewEnabled: false, previewStatus: "off", hideMinimizedTaskbar: false, messages: [] };
    case "has_translation_secret": return args.provider === "deepseek";
    case "set_translation_shortcut":
      translationShortcutCalls.push(String(args.shortcut));
      // Taken by another program, as Windows would answer.
      if (args.shortcut === "Ctrl+Alt+Q") throw "快捷键 Ctrl+Alt+Q 已被占用或无法注册：热键已被注册。";
      return args.shortcut;
    case "updater_configured": return true;
    case "get_updater_proxy": return null;
    case "plugin:app|version": return "0.0.0-test";
    case "get_agent_hooks_state": return { codex: "notInstalled", claudeCode: "notInstalled", scriptsOutdated: false };
    case "list_sound_packs": return [{ id: "chimes-1", name: "Chimes", author: "Ann", license: "CC0-1.0", sounds: { complete: "complete.wav", error: "error.wav" } }];
    default: return null;
  }
}, { shouldMockEvents: true });
mockWindows("settings");
