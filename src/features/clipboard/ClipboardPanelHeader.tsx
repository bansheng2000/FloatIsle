import { Check, ClipboardList, Keyboard, SquareCheckBig, Star, Trash2, X } from "lucide-react";
import { useTranslation } from "../../i18n";
import { normalizeClipboardShortcut } from "./shortcut";
import "./ClipboardPanelHeader.css";

export function ClipboardPanelHeader({
  itemCount,
  favoriteCount,
  shortcutEnabled,
  shortcut,
  isConfirmingClear,
  onClear,
  // E2: multi-select controls. The header owns the affordances; selection state lives in the panel.
  multiSelect,
  selectedCount,
  confirmingDelete,
  selectableCount,
  onToggleSelectMode,
  onSelectAll,
  onRequestDelete,
}: {
  itemCount: number;
  favoriteCount: number;
  shortcutEnabled: boolean;
  shortcut: string;
  isConfirmingClear: boolean;
  onClear: () => void;
  multiSelect?: boolean;
  selectedCount?: number;
  confirmingDelete?: boolean;
  selectableCount?: number;
  onToggleSelectMode?: () => void;
  onSelectAll?: () => void;
  onRequestDelete?: () => void;
}) {
  const t = useTranslation();

  return (
    <header className="clipboard-panel__header">
      <div className="clipboard-panel__title">
        <ClipboardList size={16} strokeWidth={2.2} />
        <span>{t.clipboard.title}</span>
        <strong>{itemCount}</strong>
        {favoriteCount > 0 && (
          <em aria-label={t.clipboard.favoriteCount(favoriteCount)}>
            <Star size={10} strokeWidth={2.4} fill="currentColor" />
            {favoriteCount}
          </em>
        )}
      </div>
      <div className="clipboard-panel__tools">
        {multiSelect ? (
          <>
            <span className="clipboard-select-count" aria-live="polite">
              {t.clipboard.selectedCount(selectedCount ?? 0)}
            </span>
            <button
              className="clipboard-select-all"
              type="button"
              disabled={(selectableCount ?? 0) === 0}
              aria-label={t.clipboard.selectAllNoFavorites}
              onClick={onSelectAll}
              data-clipboard-confirm-control="true"
            >
              <SquareCheckBig size={14} strokeWidth={2.2} />
              <span>{t.clipboard.selectAllNoFavorites}</span>
            </button>
            <button
              className={[
                "clipboard-clear-button",
                confirmingDelete ? "clipboard-clear-button--confirming" : "",
              ]
                .filter(Boolean)
                .join(" ")}
              type="button"
              disabled={(selectedCount ?? 0) === 0}
              aria-label={
                confirmingDelete
                  ? t.clipboard.confirmDeleteSelected(selectedCount ?? 0)
                  : t.clipboard.delete
              }
              onClick={onRequestDelete}
              data-clipboard-confirm-control="true"
            >
              {confirmingDelete ? (
                <Check className="save-check-icon" size={14} strokeWidth={2.7} />
              ) : (
                <Trash2 size={14} strokeWidth={2.2} />
              )}
            </button>
            <button
              className="clipboard-clear-button"
              type="button"
              aria-label={t.clipboard.exitSelectMode}
              onClick={onToggleSelectMode}
              data-clipboard-confirm-control="true"
            >
              <X size={14} strokeWidth={2.2} />
            </button>
          </>
        ) : (
          <>
            {shortcutEnabled ? (
              <span
                className="clipboard-shortcut-display"
                aria-label={t.clipboard.expandShortcut}
              >
                <Keyboard size={14} strokeWidth={2.3} />
                <span>{normalizeClipboardShortcut(shortcut)}</span>
              </span>
            ) : null}
            <button
              className="clipboard-select-toggle"
              type="button"
              disabled={itemCount === 0}
              aria-label={t.clipboard.selectMode}
              onClick={onToggleSelectMode}
              data-clipboard-confirm-control="true"
            >
              <SquareCheckBig size={14} strokeWidth={2.2} />
            </button>
            <button
              className={[
                "clipboard-clear-button",
                isConfirmingClear ? "clipboard-clear-button--confirming" : "",
              ]
                .filter(Boolean)
                .join(" ")}
              type="button"
              disabled={itemCount === 0}
              aria-label={
                isConfirmingClear ? t.clipboard.confirmClear : t.clipboard.clearHistory
              }
              onClick={onClear}
              data-clipboard-confirm-control="true"
            >
              {isConfirmingClear ? (
                <Check className="save-check-icon" size={14} strokeWidth={2.7} />
              ) : (
                <Trash2 size={14} strokeWidth={2.2} />
              )}
            </button>
          </>
        )}
      </div>
    </header>
  );
}
