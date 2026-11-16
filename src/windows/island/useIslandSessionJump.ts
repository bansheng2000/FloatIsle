import { useCallback, useEffect, useState } from "react";
import { onSessionJump, type SessionJumpRequest } from "../../features/agent-status/api";
import type { IslandPage } from "./types";

/**
 * A `POST /jump` on the event port (session_jump.rs): the island opens the Agent page for the
 * session it names, and the page scrolls to its row and answers the post with what it found.
 * `clear` drops the request once answered, so reopening the page does not jump again.
 */
export function useIslandSessionJump(openIslandPage: (page: IslandPage) => void) {
  const [jump, setJump] = useState<SessionJumpRequest | null>(null);

  useEffect(() => {
    let disposed = false;
    let stop: (() => void) | null = null;
    onSessionJump((request) => {
      if (disposed) return;
      setJump(request);
      openIslandPage("agents");
    })
      .then((unlisten) => {
        if (disposed) unlisten();
        else stop = unlisten;
      })
      .catch((error) => console.error("Failed to listen for session jumps", error));
    return () => {
      disposed = true;
      stop?.();
    };
  }, [openIslandPage]);

  const clear = useCallback(() => setJump(null), []);
  return { jump, clear };
}
