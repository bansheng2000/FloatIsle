import { useEffect, useRef } from "react";
import { TUCK_PREVIEW_MS } from "../../features/appearance/settingsLimits";
import { TUCK_PREVIEW_EVENT, TUCK_PREVIEW_STORAGE_KEY, parseTuckPreview } from "../settings/storage";
import type { IslandMode, IslandPage } from "./types";

type TuckPreviewOptions = {
  mode: IslandMode;
  isTucked: boolean;
  page: IslandPage;
  onTuck: () => void;
  onCollapse: () => void;
  onOpenPage: (page: IslandPage) => void;
};

type Snapshot = { mode: IslandMode; isTucked: boolean; page: IslandPage };

/**
 * Briefly tucks the island in the shape a settings picker just chose so the change is
 * visible even when the island is expanded or on another window. The original state is
 * restored after TUCK_PREVIEW_MS; a generation token keeps overlapping requests from
 * letting an older timer restore over a newer preview.
 */
export function useTuckPreview({ mode, isTucked, page, onTuck, onCollapse, onOpenPage }: TuckPreviewOptions) {
  const stateRef = useRef<Snapshot>({ mode, isTucked, page });
  useEffect(() => {
    stateRef.current = { mode, isTucked, page };
  }, [mode, isTucked, page]);

  const actionsRef = useRef({ onTuck, onCollapse, onOpenPage });
  useEffect(() => {
    actionsRef.current = { onTuck, onCollapse, onOpenPage };
  }, [onTuck, onCollapse, onOpenPage]);

  const timerRef = useRef<number | undefined>(undefined);
  const generationRef = useRef(0);

  useEffect(() => {
    const restore = (snapshot: Snapshot) => {
      if (snapshot.mode === "expanded") {
        actionsRef.current.onOpenPage(snapshot.page);
      } else if (snapshot.isTucked) {
        actionsRef.current.onTuck();
      } else {
        actionsRef.current.onCollapse();
      }
    };

    const startPreview = (stored: string | null) => {
      if (parseTuckPreview(stored) === null) {
        return;
      }

      const snapshot = { ...stateRef.current };
      const generation = ++generationRef.current;
      window.clearTimeout(timerRef.current);
      actionsRef.current.onTuck();
      timerRef.current = window.setTimeout(() => {
        if (generationRef.current === generation) {
          restore(snapshot);
        }
      }, TUCK_PREVIEW_MS);
    };

    const handleStorage = (event: StorageEvent) => {
      if (event.key === TUCK_PREVIEW_STORAGE_KEY) {
        startPreview(event.newValue);
      }
    };

    const handleAttached = (event: Event) => {
      startPreview((event as CustomEvent<string>).detail);
    };

    window.addEventListener("storage", handleStorage);
    window.addEventListener(TUCK_PREVIEW_EVENT, handleAttached);
    return () => {
      window.removeEventListener("storage", handleStorage);
      window.removeEventListener(TUCK_PREVIEW_EVENT, handleAttached);
      window.clearTimeout(timerRef.current);
    };
  }, []);
}
