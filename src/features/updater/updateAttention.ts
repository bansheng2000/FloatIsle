import type { UpdateStage } from "./updaterController";

/**
 * The attention dot follows a latched "an update was found" flag rather than
 * the transient update stage. A later re-check that finds nothing (`latest`)
 * or fails (`error`) must not blink the dot off while a pending update is still
 * waiting to be installed. It clears only after installation (`done`) or when
 * the user dismisses the prompt.
 */
export function updateNeedsAttention(found: boolean, stage: UpdateStage, dismissed: boolean): boolean {
  if (dismissed || stage === "done") return false;
  return found;
}
