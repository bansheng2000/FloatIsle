import type { AppearancePresetKind, IslandSettings } from "../types";

/** (E1) A named snapshot of appearance fields; `kind` decides which subset it captures. */
export type AppearancePreset = {
  id: string;
  name: string;
  createdAt: number;
  kind: AppearancePresetKind;
  snapshot: Partial<IslandSettings>;
};
