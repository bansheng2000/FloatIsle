/**
 * What the DSH notifier bridge reports about itself. Mirrors DshBridgeStatus in
 * src-tauri/src/agent_status/dsh_bridge.rs: every field but the state may be missing,
 * because an older plugin answers without the self-describing ones.
 */
export type DshBridgeState = "dshNotRunning" | "pluginSilent" | "portTaken" | "pluginOutdated" | "ok";

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
  /** The `/status` body (or whatever answered), already truncated by the native side. */
  raw: string | null;
};

/** One read of the bridge: the process snapshot, the plugin manifest and one GET on 47822. */
// The command wrapper lives in api.ts, which is the only module allowed to import invoke.

/**
 * Whether the plugin is telling us it cannot deliver event notices. The island has no
 * receiver on 47821 yet, so this is true on every healthy install; the line that explains
 * it disappears by itself once the receiver exists, because the error goes away.
 */
export function eventChannelGap(report: DshBridgeReport): boolean {
  return report.lastEventError?.includes("47821") ?? false;
}

/** Pastes into a chat or an issue: the state, the four numbers, and the raw body. */
export function dshBridgeDiagnostics(status: DshBridgeStatus): string {
  const { processes, plugin, report } = status;
  const main = processes.mainPids.join(", ") || "none";
  const bridge = report.selfDescribing
    ? `clients=${report.clients} pid=${report.pid} epoch=${report.epoch}`
    : "not self-describing (old plugin)";
  return [
    `state: ${status.state}`,
    `endpoint: ${status.endpoint} reachable=${status.reachable} http=${status.httpStatus ?? "none"}`,
    `process: main=${main} children=${processes.childCount} startedAt=${processes.startedAt ?? "unknown"}`,
    `plugin: ${plugin.version ?? "unknown"}${plugin.profile ? ` (${plugin.profile})` : ""}`,
    `bridge: ${bridge}`,
    `sessions: running=${report.running.length} recent=${report.recent} lastEventAt=${report.lastEventAt ?? "never"}`,
    `events: sent=${report.eventsSent ?? "?"} failed=${report.eventsFailed ?? "?"} lastError=${report.lastEventError ?? "none"}`,
    `error: ${status.error ?? "none"}`,
    `raw: ${status.raw ?? "(no body)"}`,
  ].join("\n");
}
