import { useState } from "react";
import { useTranslation } from "../../i18n";
import type { ClipboardHistorySnapshot } from "./api";
import { ClipboardFilters } from "./ClipboardFilters";
import { ClipboardHistoryItemRow } from "./ClipboardHistoryItemRow";
import { ClipboardImageLightbox } from "./ClipboardImageLightbox";
import { ClipboardPanelHeader } from "./ClipboardPanelHeader";
import { selectableIds } from "./clipboardSelection";
import { useClipboardConfirmations } from "./useClipboardConfirmations";
import { useClipboardHistoryList } from "./useClipboardHistoryList";
import { useClipboardMultiSelect } from "./useClipboardMultiSelect";
import { useClipboardNoteEditor } from "./useClipboardNoteEditor";
import "./ClipboardHistoryPanel.css";

export function ClipboardHistoryPanel({
  snapshot,
  onCopyItem,
  onToggleFavorite,
  onUpdateNote,
  onDeleteItem,
  onClear,
  onBatchDelete,
  onLoadOriginal,
}: {
  snapshot: ClipboardHistorySnapshot;
  onCopyItem: (id: string) => Promise<boolean> | boolean;
  onToggleFavorite: (id: string) => Promise<void> | void;
  onUpdateNote: (id: string, note: string) => Promise<boolean> | boolean;
  onDeleteItem: (id: string) => Promise<void> | void;
  onClear: () => Promise<void> | void;
  // E2: batch delete and on-demand original-image read from the native side.
  onBatchDelete: (ids: string[]) => Promise<void> | void;
  onLoadOriginal: (id: string) => Promise<string | null>;
}) {
  const t = useTranslation();
  const {
    query,
    setQuery,
    clipboardView,
    setClipboardView,
    favoriteItems,
    filteredItems,
    itemElementsRef,
  } = useClipboardHistoryList(snapshot.items);
  const {
    confirmDeleteId,
    isConfirmingClear,
    dismissConfirmations,
    handleDeleteItem,
    handleClear,
  } = useClipboardConfirmations(snapshot.items, onDeleteItem, onClear);
  const {
    copiedItemId,
    editingNoteId,
    noteDraft,
    setNoteDraft,
    isSavingNote,
    handleCopyItem,
    handleToggleFavorite,
    startEditingNote,
    cancelEditingNote,
    saveNote,
  } = useClipboardNoteEditor(
    snapshot.items,
    onCopyItem,
    onToggleFavorite,
    onUpdateNote,
    dismissConfirmations,
  );

  const multi = useClipboardMultiSelect(snapshot.items, onBatchDelete);
  const [lightboxId, setLightboxId] = useState<string | null>(null);
  const lightboxItem = snapshot.items.find(
    (item) => item.id === lightboxId && item.kind === "image",
  );
  const selectableCount = selectableIds(snapshot.items).length;

  const toggleSelectMode = () => {
    dismissConfirmations();
    if (multi.multiSelect) {
      multi.exit();
    } else {
      multi.enter();
    }
  };

  return (
    <section className="clipboard-panel" aria-label={t.clipboard.title}>
      <ClipboardPanelHeader
        itemCount={snapshot.items.length}
        favoriteCount={favoriteItems.length}
        shortcutEnabled={snapshot.settings.shortcutEnabled}
        shortcut={snapshot.settings.shortcut}
        isConfirmingClear={isConfirmingClear}
        onClear={handleClear}
        multiSelect={multi.multiSelect}
        selectedCount={multi.selectedCount}
        confirmingDelete={multi.confirming}
        selectableCount={selectableCount}
        onToggleSelectMode={toggleSelectMode}
        onSelectAll={multi.selectAll}
        onRequestDelete={multi.requestDelete}
      />

      <ClipboardFilters
        view={clipboardView}
        query={query}
        onViewChange={setClipboardView}
        onQueryChange={setQuery}
      />

      <div className="clipboard-list" role="list">
        {filteredItems.length === 0 ? (
          <div className="clipboard-empty">
            {snapshot.items.length === 0
              ? t.clipboard.emptyAll
              : clipboardView === "favorites" && favoriteItems.length === 0
                ? t.clipboard.emptyFavorites
                : t.clipboard.emptySearch}
          </div>
        ) : (
          filteredItems.map((item) => (
            <ClipboardHistoryItemRow
              key={item.id}
              item={item}
              elementRef={(node) => {
                if (node) {
                  itemElementsRef.current.set(item.id, node);
                } else {
                  itemElementsRef.current.delete(item.id);
                }
              }}
              isEditingNote={editingNoteId === item.id}
              noteDraft={noteDraft}
              isSavingNote={isSavingNote}
              isConfirmingDelete={confirmDeleteId === item.id}
              isCopied={copiedItemId === item.id}
              onCopy={handleCopyItem}
              onToggleFavorite={handleToggleFavorite}
              onStartEditNote={startEditingNote}
              onNoteDraftChange={setNoteDraft}
              onSaveNote={saveNote}
              onCancelEditNote={cancelEditingNote}
              onDelete={handleDeleteItem}
              multiSelect={multi.multiSelect}
              selected={multi.selectedIds.has(item.id)}
              onToggleSelect={multi.toggle}
              onOpenOriginal={setLightboxId}
            />
          ))
        )}
      </div>
      {lightboxItem ? (
        <ClipboardImageLightbox
          item={lightboxItem}
          onLoadOriginal={onLoadOriginal}
          onClose={() => setLightboxId(null)}
        />
      ) : null}
    </section>
  );
}
