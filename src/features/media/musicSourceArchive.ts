import type { MediaState } from "./api";
import type { KnownMediaSource } from "./storage";

export type PriorityAndArchive = {
  priority: KnownMediaSource[];
  archived: KnownMediaSource[];
};

/** Move a source out of the order and into the archive. Archived sources are not followed. */
export function archiveMusicSource(
  priority: KnownMediaSource[],
  archived: KnownMediaSource[],
  sourceId: string,
): PriorityAndArchive {
  const source = priority.find((entry) => entry.id === sourceId);
  if (!source) return { priority, archived };
  return {
    priority: priority.filter((entry) => entry.id !== sourceId),
    archived: archived.some((entry) => entry.id === sourceId)
      ? archived
      : [...archived, source],
  };
}

/** Restore an archived source to the end of the order. */
export function restoreMusicSource(
  priority: KnownMediaSource[],
  archived: KnownMediaSource[],
  sourceId: string,
): PriorityAndArchive {
  const source = archived.find((entry) => entry.id === sourceId);
  if (!source) return { priority, archived };
  if (priority.some((entry) => entry.id === sourceId)) {
    return { priority, archived: archived.filter((entry) => entry.id !== sourceId) };
  }
  return {
    priority: [...priority, source],
    archived: archived.filter((entry) => entry.id !== sourceId),
  };
}

/** Add newly seen sessions, but never re-add an archived player. */
export function appendSessions(
  priority: KnownMediaSource[],
  archived: KnownMediaSource[],
  sessions: MediaState["sessions"],
): KnownMediaSource[] {
  const known = new Set(priority.map((source) => source.id));
  const hidden = new Set(archived.map((source) => source.id));
  const additions = sessions
    .filter((session) => !known.has(session.id) && !hidden.has(session.id))
    .map((session) => ({ id: session.id, label: session.label }));
  return additions.length > 0 ? [...priority, ...additions] : priority;
}
