import { useEffect, useState } from "react";
import { Archive, Info, RotateCcw } from "lucide-react";
import { ReorderList } from "../../components/reorder/ReorderList";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { useTranslation } from "../../i18n";
import { getMediaState, type MediaState } from "./api";
import { useMusicSourcePriority } from "./useMusicSourcePriority";
import "./MusicSourcePrioritySection.css";

// Often enough for "playing" to follow a player that starts or stops.
const LIVE_SESSION_POLL_MS = 3_000;

/** Which media sessions exist now, read while the settings page is open. */
function useLiveMediaSessions() {
  const [sessions, setSessions] = useState<MediaState["sessions"]>([]);

  useEffect(() => {
    let didCancel = false;
    const read = () => {
      void getMediaState(undefined)
        .then((state) => {
          if (!didCancel) setSessions(state.sessions);
        })
        .catch((error) => console.error("Failed to read media sessions", error));
    };
    read();
    const timer = window.setInterval(read, LIVE_SESSION_POLL_MS);
    return () => {
      didCancel = true;
      window.clearInterval(timer);
    };
  }, []);

  return sessions;
}

/**
 * Settings → Music → Player order: which player the island follows when several have a media
 * session. A playing one comes before a paused one, and among each the list's order decides
 * (useMediaState). Players join the list the first time they are seen; the order is kept in
 * `focusd-island-music-sources`, as before Settings were slimmed down.
 */
export function MusicSourcePrioritySection() {
  const text = useTranslation().settings.musicSources;
  const live = useLiveMediaSessions();
  const {
    musicSourcePriority,
    archivedSources,
    setMusicSourceOrder,
    archiveSource,
    restoreSource,
  } = useMusicSourcePriority(live);

  return (
    <SettingsGroup
      title={text.title}
      footer={(
        <>
          {musicSourcePriority.length > 1 && <p>{text.footer}</p>}
          <p className="music-sources__cover-hint">
            <Info size={14} strokeWidth={1.8} aria-hidden="true" />
            <span>{text.coverHint}</span>
          </p>
        </>
      )}
    >
      {musicSourcePriority.length === 0 ? (
        <p className="music-sources__empty">{text.empty}</p>
      ) : (
        <ReorderList
          rows={musicSourcePriority.map((source) => {
            const session = live.find((candidate) => candidate.id === source.id);
            const state = session?.playing ? "playing" : session ? "open" : "notOpen";
            return {
              id: source.id,
              name: source.label,
              hint: text[state],
              icon: <span className={`music-sources__dot music-sources__dot--${state}`} aria-hidden="true" />,
              action: (
                <button
                  className="icon-button music-sources__archive"
                  type="button"
                  aria-label={text.archive(source.label)}
                  onClick={() => archiveSource(source.id)}
                >
                  <Archive size={15} strokeWidth={1.7} aria-hidden="true" />
                </button>
              ),
            };
          })}
          onReorder={setMusicSourceOrder}
          moveLabel={text.moveLabel}
        />
      )}
      {archivedSources.length > 0 && (
        <div className="music-sources__archived">
          <p className="music-sources__archived-title">{text.archivedTitle}</p>
          <ul className="music-sources__archived-list">
            {archivedSources.map((source) => (
              <li key={source.id} className="music-sources__archived-row">
                <span>{source.label}</span>
                <button
                  className="icon-button music-sources__restore"
                  type="button"
                  aria-label={text.restore(source.label)}
                  onClick={() => restoreSource(source.id)}
                >
                  <RotateCcw size={15} strokeWidth={1.7} aria-hidden="true" />
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </SettingsGroup>
  );
}
