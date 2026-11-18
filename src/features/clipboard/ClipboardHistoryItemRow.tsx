import { Check, ClipboardList, Copy, ImageIcon, Pencil, Search, Star, Trash2, X } from "lucide-react";
import { useTranslation } from "../../i18n";
import type { ClipboardHistoryItem } from "./api";
import "./ClipboardHistoryItemRow.css";

export function ClipboardHistoryItemRow({
  item,
  elementRef,
  isEditingNote,
  noteDraft,
  isSavingNote,
  isConfirmingDelete,
  isCopied,
  onCopy,
  onToggleFavorite,
  onStartEditNote,
  onNoteDraftChange,
  onSaveNote,
  onCancelEditNote,
  onDelete,
  // E2: selection and original-image preview.
  multiSelect,
  selected,
  onToggleSelect,
  onOpenOriginal,
}: {
  item: ClipboardHistoryItem;
  elementRef: (node: HTMLElement | null) => void;
  isEditingNote: boolean;
  noteDraft: string;
  isSavingNote: boolean;
  isConfirmingDelete: boolean;
  isCopied: boolean;
  onCopy: (id: string) => void;
  onToggleFavorite: (id: string) => void;
  onStartEditNote: (item: ClipboardHistoryItem) => void;
  onNoteDraftChange: (value: string) => void;
  onSaveNote: (id: string) => void;
  onCancelEditNote: () => void;
  onDelete: (id: string) => void;
  multiSelect?: boolean;
  selected?: boolean;
  onToggleSelect?: (id: string) => void;
  onOpenOriginal?: (id: string) => void;
}) {
  const t = useTranslation();

  return (
    <article
      className={multiSelect ? "clipboard-item clipboard-item--selecting" : "clipboard-item"}
      role="listitem"
      ref={elementRef}
      data-clipboard-id={item.kind === "text" ? item.id : undefined}
    >
      {multiSelect ? (
        <button
          className={selected ? "clipboard-item__check clipboard-item__check--on" : "clipboard-item__check"}
          type="button"
          disabled={item.favorite}
          aria-label={t.clipboard.selectItem(item.preview)}
          aria-pressed={Boolean(selected)}
          onClick={() => onToggleSelect?.(item.id)}
          data-clipboard-confirm-control="true"
        >
          {selected ? <Check size={13} strokeWidth={2.8} /> : null}
        </button>
      ) : null}
      <button
        className="clipboard-item__main"
        type="button"
        onClick={() => onCopy(item.id)}
      >
        {item.kind === "image" ? (
          <span className="clipboard-item__thumb">
            {item.image?.thumbnailDataUrl ? (
              <img src={item.image.thumbnailDataUrl} alt="" />
            ) : (
              <ImageIcon size={15} strokeWidth={2} />
            )}
          </span>
        ) : (
          <span className="clipboard-item__text-icon">
            <ClipboardList size={14} strokeWidth={2} />
          </span>
        )}
        <span className="clipboard-item__body">
          <span className="clipboard-item__preview">{item.preview}</span>
        </span>
      </button>
      <div className="clipboard-item__note">
        {isEditingNote ? (
          <form
            className="clipboard-note-editor"
            onSubmit={(event) => {
              event.preventDefault();
              onSaveNote(item.id);
            }}
          >
            <input
              autoFocus
              value={noteDraft}
              maxLength={80}
              placeholder={t.clipboard.notePlaceholder}
              aria-label={t.clipboard.noteInputLabel}
              disabled={isSavingNote}
              onChange={(event) => onNoteDraftChange(event.currentTarget.value)}
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  onCancelEditNote();
                }
              }}
            />
            <button
              type="submit"
              aria-label={t.clipboard.saveNote}
              disabled={isSavingNote}
            >
              <Check size={13} strokeWidth={2.6} />
            </button>
            <button
              type="button"
              aria-label={t.clipboard.cancelEditNote}
              disabled={isSavingNote}
              onClick={onCancelEditNote}
            >
              <X size={13} strokeWidth={2.5} />
            </button>
          </form>
        ) : (
          <button
            className={item.note ? "clipboard-note clipboard-note--filled" : "clipboard-note"}
            type="button"
            aria-label={
              item.note ? t.clipboard.editNote(item.note) : t.clipboard.addNoteLabel
            }
            onClick={() => onStartEditNote(item)}
          >
            <Pencil size={12} strokeWidth={2.3} />
            <span>{item.note || t.clipboard.noteFallback}</span>
          </button>
        )}
      </div>
      <div className="clipboard-item__actions">
        {item.kind === "image" ? (
          <button
            className="clipboard-original-button"
            type="button"
            aria-label={t.clipboard.originalImage}
            onClick={() => onOpenOriginal?.(item.id)}
            data-clipboard-confirm-control="true"
          >
            <Search size={14} strokeWidth={2.3} />
          </button>
        ) : null}
        <button
          className={[
            "clipboard-favorite-button",
            item.favorite ? "clipboard-favorite-button--active" : "",
          ]
            .filter(Boolean)
            .join(" ")}
          type="button"
          aria-label={
            item.favorite
              ? t.clipboard.unfavoriteItem
              : t.clipboard.favoriteItem
          }
          aria-pressed={item.favorite}
          onClick={() => onToggleFavorite(item.id)}
        >
          <Star
            size={14}
            strokeWidth={2.3}
            fill={item.favorite ? "currentColor" : "none"}
          />
        </button>
        <button
          className={[
            "clipboard-delete-button",
            isConfirmingDelete ? "clipboard-delete-button--confirming" : "",
          ]
            .filter(Boolean)
            .join(" ")}
          type="button"
          aria-label={t.clipboard.deleteItem}
          onClick={() => onDelete(item.id)}
          data-clipboard-confirm-control="true"
        >
          {isConfirmingDelete ? (
            <Check className="save-check-icon" size={14} strokeWidth={2.7} />
          ) : (
            <Trash2 size={14} strokeWidth={2.3} />
          )}
        </button>
        <button
          className={[
            "clipboard-copy-button",
            isCopied ? "clipboard-copy-button--copied" : "",
          ]
            .filter(Boolean)
            .join(" ")}
          type="button"
          aria-label={t.clipboard.copyBack}
          onClick={() => onCopy(item.id)}
        >
          {isCopied ? (
            <Check className="save-check-icon" size={14} strokeWidth={2.7} />
          ) : (
            <Copy size={14} strokeWidth={2.3} />
          )}
        </button>
      </div>
    </article>
  );
}
