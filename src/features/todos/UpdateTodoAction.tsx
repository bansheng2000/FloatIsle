import { useSyncExternalStore } from "react";
import { RefreshCcw } from "lucide-react";
import { useTranslation } from "../../i18n";
import { useUpdateAttention } from "../updater/useUpdateAttention";
import { getUpdateState, installUpdate, subscribeToUpdate } from "../updater/updaterController";
import "./UpdateTodoAction.css";

export function UpdateTodoAction() {
  const t = useTranslation().settings.project.update;
  const { stage, downloaded, total } = useSyncExternalStore(subscribeToUpdate, getUpdateState);
  const updateAttention = useUpdateAttention();
  const busy = stage === "downloading" || stage === "installing";
  const action = stage === "installing" ? t.installing : stage === "downloading"
    ? total > 0 ? t.updateDownloading(Math.min(100, Math.round(downloaded / total * 100))) : t.downloadingUnknown
    : t.updateNow;

  return (
    <button
      className={`todo-start todo-start--update${updateAttention ? " todo-start--attention" : ""}${busy ? " todo-start--busy" : ""}`}
      type="button"
      title={action}
      aria-label={action}
      disabled={busy}
      onClick={() => void installUpdate()}
    >
      <RefreshCcw size={13} strokeWidth={2.4} />
      <span>{action}</span>
      {updateAttention && <span className="island-message-button__dot island-update-dot" aria-hidden="true" />}
    </button>
  );
}
