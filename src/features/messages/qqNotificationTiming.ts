export type QQPreviewTiming = {
  burstStartedAt: number;
  lastArrivalAt: number;
};

/**
 * (E5-1) How long a QQ message preview stays, in milliseconds. `stayMs` is the
 * per-preview stay; a burst groups arrivals within one stay, and no single
 * burst lasts beyond twice the stay. All three spans scale with the setting.
 */
export function nextQQPreview(
  previous: QQPreviewTiming | null,
  now: number,
  stayMs = 5000,
): { timing: QQPreviewTiming; expiresAt: number | null } {
  const burstStartedAt =
    previous && now - previous.lastArrivalAt <= stayMs
      ? previous.burstStartedAt
      : now;
  const deadline = burstStartedAt + stayMs * 2;
  return {
    timing: { burstStartedAt, lastArrivalAt: now },
    expiresAt: now < deadline ? Math.min(now + stayMs, deadline) : null,
  };
}
