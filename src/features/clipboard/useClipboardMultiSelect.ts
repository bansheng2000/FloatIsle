// E2: multi-select state and the two-tap batch delete. Favourites are never
// selectable, matching the "select all without favourites" rule from the spec.
import { useCallback, useEffect, useRef, useState } from "react";
import type { ClipboardHistoryItem } from "./api";
import {
  deletableSelectedIds,
  selectableIds,
  toggleSelected,
} from "./clipboardSelection.ts";

export function useClipboardMultiSelect(
  items: ClipboardHistoryItem[],
  onBatchDelete: (ids: string[]) => Promise<void> | void,
) {
  const [multiSelect, setMultiSelect] = useState(false);
  const [selectedIds, setSelectedIds] = useState<ReadonlySet<string>>(new Set());
  const [confirming, setConfirming] = useState(false);
  const resetRef = useRef<number | null>(null);

  const clearConfirm = useCallback(() => {
    if (resetRef.current !== null) {
      window.clearTimeout(resetRef.current);
      resetRef.current = null;
    }
    setConfirming(false);
  }, []);

  useEffect(
    () => () => {
      if (resetRef.current !== null) {
        window.clearTimeout(resetRef.current);
      }
    },
    [],
  );

  useEffect(() => {
    if (!confirming) {
      return;
    }
    resetRef.current = window.setTimeout(() => {
      resetRef.current = null;
      setConfirming(false);
    }, 3000);
    return () => {
      if (resetRef.current !== null) {
        window.clearTimeout(resetRef.current);
        resetRef.current = null;
      }
    };
  }, [confirming]);

  // Drop ids that vanished from the snapshot so the count stays truthful.
  useEffect(() => {
    const present = new Set(items.map((item) => item.id));
    setSelectedIds((current) => {
      const kept = [...current].filter((id) => present.has(id));
      return kept.length === current.size ? current : new Set(kept);
    });
  }, [items]);

  const enter = useCallback(() => {
    setMultiSelect(true);
    setSelectedIds(new Set());
    clearConfirm();
  }, [clearConfirm]);

  const exit = useCallback(() => {
    setMultiSelect(false);
    setSelectedIds(new Set());
    clearConfirm();
  }, [clearConfirm]);

  const toggle = useCallback((id: string) => {
    setSelectedIds((current) => toggleSelected(current, id));
    clearConfirm();
  }, [clearConfirm]);

  const selectAll = useCallback(() => {
    setSelectedIds(new Set(selectableIds(items)));
    clearConfirm();
  }, [items, clearConfirm]);

  const selectedDeletable = deletableSelectedIds(selectedIds, items);

  const requestDelete = useCallback(() => {
    const targets = deletableSelectedIds(selectedIds, items);
    if (targets.length === 0) {
      return;
    }
    if (!confirming) {
      setConfirming(true);
      return;
    }
    clearConfirm();
    setMultiSelect(false);
    setSelectedIds(new Set());
    void Promise.resolve(onBatchDelete(targets));
  }, [confirming, selectedIds, items, onBatchDelete, clearConfirm]);

  return {
    multiSelect,
    selectedIds,
    selectedCount: selectedDeletable.length,
    confirming,
    enter,
    exit,
    toggle,
    selectAll,
    requestDelete,
  };
}
