// E2: pure selection rules for clipboard batch delete. Kept free of React so the
// "without favourites" rule can be unit-tested and failed on purpose in negatives.
import type { ClipboardHistoryItem } from "./api";

/** Ids that "select all" may touch: every non-favourite, in list order. */
export function selectableIds(items: ClipboardHistoryItem[]): string[] {
  return items.filter((item) => !item.favorite).map((item) => item.id);
}

/** A new set with `id` toggled, leaving the input untouched. */
export function toggleSelected(selected: ReadonlySet<string>, id: string): Set<string> {
  const next = new Set(selected);
  if (next.has(id)) {
    next.delete(id);
  } else {
    next.add(id);
  }
  return next;
}

/** Ids actually deletable: selected, present, and never a favourite. */
export function deletableSelectedIds(
  selected: ReadonlySet<string>,
  items: ClipboardHistoryItem[],
): string[] {
  return items
    .filter((item) => selected.has(item.id) && !item.favorite)
    .map((item) => item.id);
}

/** Whether every non-favourite is already selected. */
export function allNonFavoritesSelected(
  selected: ReadonlySet<string>,
  items: ClipboardHistoryItem[],
): boolean {
  const candidates = selectableIds(items);
  return candidates.length > 0 && candidates.every((id) => selected.has(id));
}
