import { useCallback, useEffect, useState } from "react";
import type { MediaState } from "./api";
import { appendSessions, archiveMusicSource, restoreMusicSource } from "./musicSourceArchive";
import {
  MUSIC_SOURCE_ARCHIVE_SAVED_EVENT,
  MUSIC_SOURCE_ARCHIVE_STORAGE_KEY,
  MUSIC_SOURCE_PRIORITY_SAVED_EVENT,
  MUSIC_SOURCE_PRIORITY_STORAGE_KEY,
  loadArchivedMusicSources,
  loadMusicSourcePriority,
  saveArchivedMusicSources,
  saveMusicSourcePriority,
  type KnownMediaSource,
} from "./storage";

/** The user's preferred media source order; newly seen sessions are appended and the order is persisted. */
export function useMusicSourcePriority(sessions: MediaState["sessions"]) {
  const [musicSourcePriority, setMusicSourcePriority] = useState<
    KnownMediaSource[]
  >(loadMusicSourcePriority);
  const [archivedSources, setArchivedSources] = useState<KnownMediaSource[]>(
    loadArchivedMusicSources,
  );

  const reorderMusicSource = useCallback(
    (sourceId: string, targetId: string) => {
      if (sourceId === targetId) {
        return;
      }

      setMusicSourcePriority((current) => {
        const sourceIndex = current.findIndex(
          (source) => source.id === sourceId,
        );
        const targetIndex = current.findIndex(
          (source) => source.id === targetId,
        );

        if (
          sourceIndex < 0 ||
          targetIndex < 0 ||
          sourceIndex === targetIndex
        ) {
          return current;
        }

        const next = [...current];
        const [moved] = next.splice(sourceIndex, 1);
        next.splice(targetIndex, 0, moved);
        return next;
      });
    },
    [],
  );

  /** Puts the known sources in the order of `ids` (Settings → Music); ids it does not know are ignored. */
  const setMusicSourceOrder = useCallback((ids: string[]) => {
    setMusicSourcePriority((current) => {
      const byId = new Map(current.map((source) => [source.id, source]));
      const next = ids.flatMap((id) => byId.get(id) ?? []);
      return next.length === current.length ? next : current;
    });
  }, []);

  const removeMusicSource = useCallback((sourceId: string) => {
    setMusicSourcePriority((current) =>
      current.filter((source) => source.id !== sourceId),
    );
  }, []);

  /** Archive a player: it leaves the order and the island stops following it. */
  const archiveSource = useCallback((sourceId: string) => {
    const next = archiveMusicSource(musicSourcePriority, archivedSources, sourceId);
    if (next.priority === musicSourcePriority && next.archived === archivedSources) return;
    setMusicSourcePriority(next.priority);
    setArchivedSources(next.archived);
  }, [musicSourcePriority, archivedSources]);

  /** Restore an archived player to the end of the order. */
  const restoreSource = useCallback((sourceId: string) => {
    const next = restoreMusicSource(musicSourcePriority, archivedSources, sourceId);
    setMusicSourcePriority(next.priority);
    setArchivedSources(next.archived);
  }, [musicSourcePriority, archivedSources]);

  useEffect(() => {
    if (sessions.length === 0) {
      return;
    }
    setMusicSourcePriority((current) => appendSessions(current, archivedSources, sessions));
  }, [sessions, archivedSources]);

  useEffect(() => {
    saveMusicSourcePriority(musicSourcePriority);
  }, [musicSourcePriority]);

  useEffect(() => {
    saveArchivedMusicSources(archivedSources);
  }, [archivedSources]);

  // The island and Settings > Music both edit the order, in two windows or (Settings inside the
  // island) in one. A saved order that matches this copy changes nothing, so the two copies do
  // not keep saving to each other.
  useEffect(() => {
    const sync = () => {
      const stored = loadMusicSourcePriority();
      setMusicSourcePriority((current) => (JSON.stringify(current) === JSON.stringify(stored) ? current : stored));
      const storedArchive = loadArchivedMusicSources();
      setArchivedSources((current) => (JSON.stringify(current) === JSON.stringify(storedArchive) ? current : storedArchive));
    };
    const syncFromOtherWindow = (event: StorageEvent) => {
      if (
        event.key === MUSIC_SOURCE_PRIORITY_STORAGE_KEY ||
        event.key === MUSIC_SOURCE_ARCHIVE_STORAGE_KEY
      ) {
        sync();
      }
    };
    window.addEventListener("storage", syncFromOtherWindow);
    window.addEventListener(MUSIC_SOURCE_PRIORITY_SAVED_EVENT, sync);
    window.addEventListener(MUSIC_SOURCE_ARCHIVE_SAVED_EVENT, sync);
    return () => {
      window.removeEventListener("storage", syncFromOtherWindow);
      window.removeEventListener(MUSIC_SOURCE_PRIORITY_SAVED_EVENT, sync);
      window.removeEventListener(MUSIC_SOURCE_ARCHIVE_SAVED_EVENT, sync);
    };
  }, []);

  return {
    musicSourcePriority,
    archivedSources,
    reorderMusicSource,
    removeMusicSource,
    setMusicSourceOrder,
    archiveSource,
    restoreSource,
  };
}
