import { useSyncExternalStore } from "react";
import { getUpdateState, subscribeToUpdate } from "./updaterController";
import { updateNeedsAttention } from "./updateAttention";

/**
 * Whether a pending update should light an attention dot. Shared by the todo
 * page dot and the update button so both use the same latched "found" signal.
 */
export function useUpdateAttention(): boolean {
  return useSyncExternalStore(subscribeToUpdate, () => {
    const { found, stage, dismissed } = getUpdateState();
    return updateNeedsAttention(found, stage, dismissed);
  });
}
