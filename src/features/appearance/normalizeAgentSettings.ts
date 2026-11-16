// Reading saved settings, part 2 (normalizeSettings in settings.ts): the Agent page and its alerts.
import { DEFAULT_SETTINGS } from "./settingsDefaults.ts";
import {
  AGENT_SESSION_GROUPS, AGENT_SESSION_SORTS, AGENT_USAGE_MODES, AGENT_USAGE_REFRESH_MS, oneOf,
} from "./settingsLimits.ts";
import type { IslandSettings, LegacyIslandSettings } from "./types";

/** The Agent page's order and headings, and which session events open it. */
export function normalizeAgentSettings(settings: LegacyIslandSettings | null | undefined) {
  // A saved interval outside the four choices falls back to the default, so an old or hand-edited
  // file cannot leave the island polling Magpie on every frame.
  const refresh = settings?.agentUsageRefreshMs;
  const agentUsageRefreshMs = AGENT_USAGE_REFRESH_MS.some((value) => value === refresh)
    ? (refresh as number)
    : DEFAULT_SETTINGS.agentUsageRefreshMs;
  return {
    agentShowDsh: settings?.agentShowDsh !== false,
    agentSessionSort: oneOf(AGENT_SESSION_SORTS, settings?.agentSessionSort, DEFAULT_SETTINGS.agentSessionSort),
    agentSessionGroup: oneOf(AGENT_SESSION_GROUPS, settings?.agentSessionGroup, DEFAULT_SETTINGS.agentSessionGroup),
    agentAlertCompleted: settings?.agentAlertCompleted !== false,
    agentAlertWaiting: settings?.agentAlertWaiting !== false,
    agentUsageMode: oneOf(AGENT_USAGE_MODES, settings?.agentUsageMode, DEFAULT_SETTINGS.agentUsageMode),
    agentUsageRefreshMs,
  } satisfies Partial<IslandSettings>;
}
