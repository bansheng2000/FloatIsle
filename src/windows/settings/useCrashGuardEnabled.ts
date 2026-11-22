import { useCallback, useEffect, useState } from "react";
import { readCrashGuardEnabled, saveCrashGuardEnabled } from "./api";

/** The crash-guard switch, updated optimistically and rolled back on failure. */
export function useCrashGuardEnabled() {
  const [crashGuardEnabled, setCrashGuardEnabled] = useState(true);

  useEffect(() => {
    void readCrashGuardEnabled()
      .then(setCrashGuardEnabled)
      .catch((error) => {
        console.error("Failed to read crash-guard switch", error);
      });
  }, []);

  const updateCrashGuardEnabled = useCallback(async (enabled: boolean) => {
    setCrashGuardEnabled(enabled);
    try {
      await saveCrashGuardEnabled(enabled);
    } catch (error) {
      console.error("Failed to update crash-guard switch", error);
      setCrashGuardEnabled(!enabled);
    }
  }, []);

  return [crashGuardEnabled, updateCrashGuardEnabled] as const;
}
