/** How long to wait before a scheduled update check. All timing lives here so it can be tested. */
export const CHECK_RETRY_MS = 5 * 60_000;
export const PERIODIC_CHECK_MS = 6 * 60 * 60_000;

/** A failed check retries sooner than the steady periodic sweep. */
export function nextCheckDelayMs(lastCheckFailed: boolean): number {
  return lastCheckFailed ? CHECK_RETRY_MS : PERIODIC_CHECK_MS;
}

/** Whether enough wall-clock time has passed for the next check to be due. */
export function isCheckDue(now: number, lastCheck: number, lastCheckFailed: boolean): boolean {
  return now - lastCheck >= nextCheckDelayMs(lastCheckFailed);
}
