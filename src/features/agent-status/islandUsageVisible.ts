import { useSyncExternalStore } from "react";

/**
 * Whether the Agent page's usage rows are on screen right now.
 *
 * The window's height is computed from the page's parts rather than measured, so the rows have to
 * say whether they exist: with Magpie gone they are hidden, and the page must shrink with them
 * instead of keeping an empty strip. The rows own the answer (they are the ones polling), and the
 * window reads it.
 */
let visible = false;
const listeners = new Set<() => void>();

export function setIslandUsageVisible(next: boolean) {
  if (visible === next) return;
  visible = next;
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useIslandUsageVisible(): boolean {
  return useSyncExternalStore(subscribe, () => visible, () => false);
}
