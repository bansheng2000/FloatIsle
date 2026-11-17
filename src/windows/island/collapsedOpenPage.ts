import type { CollapsedItem } from "../../features/appearance/types";
import type { IslandPage } from "./types";

/** The clickable zones of the collapsed island. */
export type CollapsedClickZone = "text" | "agent" | "music" | "shelf";

/**
 * The page a click on each zone of the collapsed island opens. The status light and a running
 * agent the text names open the Agent page; the music waveform and the shelf badge open their own
 * pages; the middle text reopens the page that was open last. The music page has its own hot zone,
 * the waveform button, so a lyric line does not claim the click.
 */
export function collapsedClickTarget(
  zone: CollapsedClickZone,
  lastPage: IslandPage,
  namesAgent: boolean,
): IslandPage {
  if (zone === "music") return "music";
  if (zone === "shelf") return "shelf";
  if (zone === "agent" || namesAgent) return "agents";
  return lastPage;
}

/** The text button and the container's own background reopen the last page. */
export function collapsedOpenPage(
  item: CollapsedItem | null,
  namesAgent: boolean,
  lastPage: IslandPage,
): IslandPage {
  return collapsedClickTarget("text", lastPage, item === "agent" && namesAgent);
}
