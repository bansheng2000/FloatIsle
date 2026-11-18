import { useSyncExternalStore } from "react";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsRow } from "../../components/settings/SettingsRow";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { useTranslation } from "../../i18n";
import {
  checkForUpdate,
  getUpdateState,
  installUpdate,
  subscribeToUpdate,
  type UpdateStage,
} from "./updaterController";

type UpdateText = ReturnType<typeof useTranslation>["settings"]["project"]["update"];

function statusText(stage: UpdateStage, update: UpdateText): string {
  switch (stage) {
    case "checking":
      return update.statusChecking;
    case "available":
      return update.statusFound;
    case "downloading":
      return update.statusDownloading;
    case "ready":
      return update.statusReady;
    case "installing":
    case "done":
      return update.statusInstalling;
    case "error":
      return update.statusError;
    case "disabled":
    case "idle":
    case "latest":
    default:
      return update.statusLatest;
  }
}

/** About → the always-visible update group: current status plus check/install controls. */
export function UpdateSettingsSection() {
  const update = useTranslation().settings.project.update;
  const { stage, found } = useSyncExternalStore(subscribeToUpdate, getUpdateState);
  const busy = stage === "checking" || stage === "downloading" || stage === "installing";
  const canInstall = found && (stage === "available" || stage === "ready" || stage === "error");

  return (
    <SettingsGroup>
      <SettingsRow
        title={update.title}
        description={statusText(stage, update)}
        control={
          <SettingsLinkButton disabled={busy} onClick={() => void checkForUpdate()}>
            {update.checkAction}
          </SettingsLinkButton>
        }
      />
      {canInstall && (
        <SettingsRow
          title={update.updateNow}
          control={
            <SettingsLinkButton disabled={busy} onClick={() => void installUpdate()}>
              {update.updateNow}
            </SettingsLinkButton>
          }
        />
      )}
    </SettingsGroup>
  );
}
