// E2: full-size clipboard image preview. The thumbnail (already in the snapshot)
// holds the original's aspect ratio while the native command reads the original
// PNG on demand, so opening the preview never jumps the layout.
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { useTranslation } from "../../i18n";
import type { ClipboardHistoryItem } from "./api";
import "./ClipboardImageLightbox.css";

export function ClipboardImageLightbox({
  item,
  onLoadOriginal,
  onClose,
}: {
  item: ClipboardHistoryItem;
  onLoadOriginal: (id: string) => Promise<string | null>;
  onClose: () => void;
}) {
  const t = useTranslation();
  const closeButton = useRef<HTMLButtonElement>(null);
  const [original, setOriginal] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  const width = item.image?.width ?? 0;
  const height = item.image?.height ?? 0;
  const ratio = width > 0 && height > 0 ? width / height : undefined;
  const thumbnail = item.image?.thumbnailDataUrl ?? null;

  useEffect(() => {
    let cancelled = false;
    setOriginal(null);
    setFailed(false);
    void onLoadOriginal(item.id).then((result) => {
      if (cancelled) {
        return;
      }
      if (result) {
        setOriginal(result);
      } else {
        setFailed(true);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [item.id, onLoadOriginal]);

  useEffect(() => {
    closeButton.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        closeRef.current();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  return createPortal(
    <div
      className="clipboard-lightbox"
      role="dialog"
      aria-modal="true"
      aria-label={t.clipboard.originalImage}
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <button
        ref={closeButton}
        className="clipboard-lightbox__close"
        type="button"
        aria-label={t.clipboard.closePreview}
        onClick={onClose}
      >
        <X size={16} strokeWidth={2.4} />
      </button>
      <div
        className="clipboard-lightbox__frame"
        style={ratio ? { aspectRatio: `${ratio}` } : undefined}
      >
        {original ? (
          <img className="clipboard-lightbox__image" src={original} alt={item.preview} />
        ) : thumbnail ? (
          <img
            className="clipboard-lightbox__image clipboard-lightbox__image--thumb"
            src={thumbnail}
            alt={item.preview}
          />
        ) : null}
        {!original && !failed ? (
          <span className="clipboard-lightbox__hint">{t.clipboard.loadingOriginal}</span>
        ) : null}
        {failed ? (
          <span className="clipboard-lightbox__hint">{t.clipboard.originalUnavailable}</span>
        ) : null}
      </div>
    </div>,
    document.body,
  );
}
