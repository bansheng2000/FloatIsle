import type { ColorScheme } from "./types";

export type ResolvedColorScheme = "dark" | "light";

/**
 * (E1) ⑤ Resolve the "system" palette choice to a concrete set. Kept pure so both the entry point
 * and React can use the same rule; the OS query itself is read by the caller.
 */
export function resolveColorScheme(scheme: ColorScheme, systemPrefersLight: boolean): ResolvedColorScheme {
  if (scheme === "light") return "light";
  if (scheme === "dark") return "dark";
  return systemPrefersLight ? "light" : "dark";
}
