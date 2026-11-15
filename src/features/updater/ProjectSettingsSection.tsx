import { useSyncExternalStore } from "react";
import { GitBranch } from "lucide-react";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { useTranslation } from "../../i18n";
import markUrl from "../../../src-tauri/icons/mark.svg";
import { ProjectLink } from "./ProjectLink";
import { PROJECT_REPO_URL } from "./projectLinks";
import { getUpdateState, subscribeToUpdate } from "./updaterController";
import "./ProjectSettingsSection.css";

/** Settings → About, first: the island's mark, name, version and what it does. */
export function ProjectIdentitySection() {
  const t = useTranslation();
  const { version } = useSyncExternalStore(subscribeToUpdate, getUpdateState);

  return (
    <SettingsGroup>
      <div className="about-identity">
        <img className="about-identity__mark" src={markUrl} alt="" width={48} height={48} />
        <div className="about-identity__body">
          <div className="about-identity__heading">
            <p className="about-identity__name">{t.settings.project.appName}</p>
            {version && <span className="about-identity__version">{t.settings.project.versionChip(version)}</span>}
          </div>
          <p className="about-identity__description">{t.settings.project.description}</p>
        </div>
      </div>
    </SettingsGroup>
  );
}

/**
 * Settings → About → where the data comes from and where the project lives. All agent data is read
 * from local stores on this machine; nothing is uploaded, so there is no account and no telemetry.
 */
export function ProjectLinksSection() {
  const text = useTranslation().settings.project;

  return (
    <SettingsGroup>
      <ProjectLink
        href={PROJECT_REPO_URL}
        tone="star"
        title={text.repository}
        hint={text.repositoryHint}
        icon={<GitBranch className="project-link__icon" size={18} strokeWidth={1.5} absoluteStrokeWidth aria-hidden="true" />}
      />
    </SettingsGroup>
  );
}
