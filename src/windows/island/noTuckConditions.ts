import type { IslandSettings } from "../../features/appearance/types";

/**
 * E3: the live state of the three conditions the "don't tuck while" switches watch.
 * Kept as a plain value so the blocking rule can be unit-tested without React.
 */
export type NoTuckConditionState = {
  /** At least one agent session is running. */
  agentActive: boolean;
  /**
   * The collapsed bar is actually showing the lyrics slot. Per the shipped wording this
   * means lyrics are on screen, not merely that a song is playing in the background.
   */
  lyricsShown: boolean;
  /** A todo timer is counting down. */
  timerRunning: boolean;
};

type NoTuckSwitches = Pick<IslandSettings, "noTuckWhileAgent" | "noTuckWhileLyrics" | "noTuckWhileTimer">;

/** True when any enabled "don't tuck while" switch has its condition met right now. */
export function isNoTuckBlocked(switches: NoTuckSwitches, state: NoTuckConditionState): boolean {
  return (switches.noTuckWhileAgent && state.agentActive)
    || (switches.noTuckWhileLyrics && state.lyricsShown)
    || (switches.noTuckWhileTimer && state.timerRunning);
}

/** True only on the frame a block first becomes active, for the "call the island out" edge. */
export function noTuckEdge(blockedNow: boolean, blockedBefore: boolean): boolean {
  return blockedNow && !blockedBefore;
}
