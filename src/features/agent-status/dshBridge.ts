/**
 * What the DSH notifier bridge reports about itself. Mirrors DshBridgeStatus in
 * src-tauri/src/agent_status/dsh_bridge.rs: every field but the state may be missing,
 * because an older plugin answers without the self-describing ones.
 */
export type DshBridgeState =
  | "dshNotRunning"
  | "pluginSilent"
  | "portTaken"
  | "pluginOutdated"
  | "noClients"
  | "ok";

export type DshProcessSummary = {
  /** Top-level DSH processes; children are renderer, GPU and utility processes. */
  mainPids: number[];
  childCount: number;
  /** Unix milliseconds. */
  startedAt: number | null;
  exe: string | null;
};

export type DshPluginInfo = {
  /** Version of the copy installed on disk, not the one a running DSH has loaded. */
  version: string | null;
  profile: string | null;
};

export type DshBridgeReport = {
  pid: number | null;
  epoch: string | null;
  /** How many copies of the plugin this process loaded; not a count of connected clients. */
  clients: number | null;
  clientIds: string[];
  running: string[];
  recent: number;
  lastEventAt: string | null;
  eventsSent: number | null;
  eventsFailed: number | null;
  lastEventError: string | null;
  listening: boolean | null;
  /** Whether the plugin answered with `clients`, `pid` and `epoch`. */
  selfDescribing: boolean;
};

export type DshBridgeStatus = {
  state: DshBridgeState;
  processes: DshProcessSummary;
  plugin: DshPluginInfo;
  endpoint: string;
  reachable: boolean;
  httpStatus: number | null;
  error: string | null;
  report: DshBridgeReport;
  /** Every page's mount report, or null when the plugin refused `GET /_task-notify/mounted`. */
  mounts: DshMount[] | null;
  /** Why the mount read failed, verbatim, when `mounts` is null. */
  mountsError: string | null;
  /** The `/status` body (or whatever answered), already truncated by the native side. */
  raw: string | null;
};

/**
 * One page's entry from `/_task-notify/mounted`. Pages are never merged here: two windows can
 * hold two different sessions, and the card must not pretend they are one.
 */
export type DshMount = {
  clientId: string | null;
  mountedSessionId: string | null;
  visible: boolean | null;
  focused: boolean | null;
  stale: boolean | null;
  reportedAt: string | null;
  receivedAt: string | null;
  /** Milliseconds since the hub took the report; the only clock worth trusting for freshness. */
  ageMs: number | null;
};

/** One read of the bridge: the process snapshot, the plugin manifest and one GET on 47822. */
// The command wrapper lives in api.ts, which is the only module allowed to import invoke.

/**
 * The presence a plugin window posts to the island's own listener on 47821. Mirrors Presence in
 * src-tauri/src/agent_status/event_receiver.rs; the older deployed plugin sends `sessionId`
 * instead of `mountedSessionId`, and the native side folds that into the same field.
 */
export type DshPresence = {
  clientId: string | null;
  visible: boolean | null;
  focused: boolean | null;
  mountedSessionId: string | null;
  reportedAt: string | null;
};

/** The island's listener on 47821: whether it runs, and the last presence it read. */
export type DshReceiverStatus = {
  port: number;
  listening: boolean;
  /** Why the listener could not bind, when it did not. */
  error: string | null;
  presence: DshPresence | null;
  presences: number;
  lastPresenceAtMs: number | null;
  events: number;
  rejected: number;
  lastEventAtMs: number | null;
  lastEventType: string | null;
};

/** Pastes into a chat or an issue: the state, the four numbers, and the raw body. */
export function dshBridgeDiagnostics(status: DshBridgeStatus, receiver?: DshReceiverStatus | null): string {
  const { processes, plugin, report } = status;
  const main = processes.mainPids.join(", ") || "none";
  // The start time is kept in milliseconds for machines and in UTC for the person pasting
  // this into a chat, who would otherwise have to convert it.
  const startedAt =
    processes.startedAt === null
      ? "unknown"
      : `${processes.startedAt} (${new Date(processes.startedAt).toISOString()})`;
  const bridge = report.selfDescribing
    ? `clients=${report.clients} pid=${report.pid} epoch=${report.epoch}`
    : "not self-describing (old plugin)";
  const listener = receiver
    ? `receiver: port=${receiver.port} listening=${receiver.listening} presences=${receiver.presences} events=${receiver.events} rejected=${receiver.rejected} error=${receiver.error ?? "none"}`
    : "receiver: not read";
  const mounts = status.mounts
    ? status.mounts
        .map((mount) => `${mount.clientId ?? "?"}:${mount.mountedSessionId ?? "none"}${mount.stale ? " stale" : ""}`)
        .join(", ")
    : `refused (${status.mountsError ?? "unknown"})`;
  const mounted = receiver
    ? `mounted: session=${receiver.presence?.mountedSessionId ?? "none"} visible=${receiver.presence?.visible ?? "?"} focused=${receiver.presence?.focused ?? "?"} reportedAt=${receiver.presence?.reportedAt ?? "none"} lastPresenceAt=${receiver.lastPresenceAtMs ?? "never"}`
    : "mounted: not read";
  return [
    `state: ${status.state}`,
    `endpoint: ${status.endpoint} reachable=${status.reachable} http=${status.httpStatus ?? "none"}`,
    `process: main=${main} children=${processes.childCount} startedAt=${startedAt}`,
    `plugin: ${plugin.version ?? "unknown"}${plugin.profile ? ` (${plugin.profile})` : ""}`,
    `bridge: ${bridge}`,
    `sessions: running=${report.running.length} recentCount=${report.recent} lastEventAt=${report.lastEventAt ?? "never"}`,
    `events: sent=${report.eventsSent ?? "?"} failed=${report.eventsFailed ?? "?"} lastError=${report.lastEventError ?? "none"}`,
    `mounts: ${mounts}`,
    listener,
    mounted,
    `error: ${status.error ?? "none"}`,
    `raw: ${status.raw ?? "(no body)"}`,
  ].join("\n");
}
