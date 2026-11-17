import type { IslandPage } from "./types";

/** Persisted page the island reopens on the next launch. */
export const LAST_PAGE_STORAGE_KEY = "focusd-island-last-page";
export const DEFAULT_ISLAND_PAGE: IslandPage = "todo";

const VALID_PAGES: readonly IslandPage[] = [
  "todo",
  "music",
  "clipboard",
  "shelf",
  "qq",
  "agents",
  "translation",
  "settings",
];

/** Only a page name this build knows is accepted; anything else is the default. */
export function parseLastPage(stored: string | null): IslandPage {
  if (!stored) return DEFAULT_ISLAND_PAGE;
  try {
    const value: unknown = JSON.parse(stored);
    return typeof value === "string" && VALID_PAGES.includes(value as IslandPage)
      ? (value as IslandPage)
      : DEFAULT_ISLAND_PAGE;
  } catch {
    return DEFAULT_ISLAND_PAGE;
  }
}

export function readLastPage(): IslandPage {
  try {
    return parseLastPage(window.localStorage.getItem(LAST_PAGE_STORAGE_KEY));
  } catch {
    return DEFAULT_ISLAND_PAGE;
  }
}

export function writeLastPage(page: IslandPage): void {
  try {
    window.localStorage.setItem(LAST_PAGE_STORAGE_KEY, JSON.stringify(page));
  } catch {
    // Ignore storage write failures (private mode, quota, etc.).
  }
}
