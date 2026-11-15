import { useState } from "react";
import { SettingsInput } from "../../components/settings/SettingsInput";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { useTranslation } from "../../i18n";
import { openTodoDataDirectory } from "./api";
import { relocateTodoDirectory } from "./fileSync";
import { useTodoFileStatus } from "./useTodoFileStatus";
import "./TodoFileSettingsSection.css";

/**
 * Todos → Saving: the JSON file of record and where it lives. One row inside the saving
 * group rather than a group of its own — the settings page is capped in height inside the
 * island, and the todo page is already close to that cap.
 */
export function TodoFileSettingsRows() {
  const t = useTranslation();
  const status = useTodoFileStatus();
  const [draft, setDraft] = useState("");
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const directory = status?.directory ?? "";

  const statusText = (() => {
    if (!status) return null;
    if (status.kind === "synced") return status.writable ? t.settings.todoFile.synced : t.settings.todoFile.readOnly;
    if (status.kind === "read-only") return t.settings.todoFile.readOnly;
    if (status.kind === "corrupt") return t.settings.todoFile.corrupt;
    if (status.kind === "migrated") return t.settings.todoFile.migrated;
    return t.settings.todoFile.failed;
  })();

  const tone =
    status?.kind === "synced" || status?.kind === "migrated"
      ? "ok"
      : status?.kind === "read-only"
        ? "warn"
        : status
          ? "bad"
          : "muted";

  const apply = async () => {
    setBusy(true);
    setError(null);
    try {
      await relocateTodoDirectory(draft);
      setEditing(false);
      setDraft("");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsRow
      title={t.settings.todoFile.folder}
      description={
        <>
          <span className="todo-file-path" title={directory}>
            {directory || "…"}
          </span>
          {statusText ? (
            <span className={`todo-file-status todo-file-status--${tone}`}>{statusText}</span>
          ) : null}
          {status?.kind === "error" || status?.kind === "corrupt" ? (
            <span className="todo-file-reason">{status.reason}</span>
          ) : null}
        </>
      }
      control={
        editing ? (
          <span className="todo-file-actions">
            <SettingsLinkButton onClick={() => void apply()}>{busy ? "…" : t.settings.todoFile.apply}</SettingsLinkButton>
            <SettingsLinkButton
              onClick={() => {
                setEditing(false);
                setDraft("");
                setError(null);
              }}
            >
              {t.settings.todoFile.cancel}
            </SettingsLinkButton>
          </span>
        ) : (
          <span className="todo-file-actions">
            <SettingsLinkButton onClick={() => void openTodoDataDirectory("")}>
              {t.settings.todoFile.openFolder}
            </SettingsLinkButton>
            <SettingsLinkButton
              onClick={() => {
                setDraft(directory);
                setEditing(true);
              }}
            >
              {t.settings.todoFile.change}
            </SettingsLinkButton>
          </span>
        )
      }
    >
      {editing ? (
        <>
          <SettingsInput
            monospace
            value={draft}
            placeholder={t.settings.todoFile.folderPlaceholder}
            aria-label={t.settings.todoFile.changeLabel}
            onChange={(event) => setDraft(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") void apply();
            }}
          />
          {error ? <span className="todo-file-error">{error}</span> : null}
        </>
      ) : null}
    </SettingsRow>
  );
}
