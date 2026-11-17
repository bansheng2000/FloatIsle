/**
 * A wall-clock refresh key for the lyric clock.
 *
 * While the island is expanded, the collapsed bar has no lyric lines; they arrive the moment it
 * collapses. The player's `updatedAt` need not move across that flip, so watching the player
 * alone would keep the position captured before expansion and show the wrong line for a beat.
 * Folding line availability into the key makes the clock re-read the current time on that flip.
 *
 * This is a value, not a hook: tests can pin the exact boundary without rendering React.
 */
export function lyricClockRefreshKey(
  hasLines: boolean,
  updatedAt: number,
  playbackStatus: string,
  offsetMs: number,
): string {
  return `${hasLines ? "lines" : "none"}:${updatedAt}:${playbackStatus}:${offsetMs}`;
}
