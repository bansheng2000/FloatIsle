import { useMemo, useState } from "react";
import { SettingsGroup } from "../../components/settings/SettingsGroup";
import { SettingsLinkButton } from "../../components/settings/SettingsLinkButton";
import { useLocale, useTranslation } from "../../i18n";
import { buildChangelog, inlineParts, type ChangelogEntry } from "./changelogParse";
import { versionLabel } from "./versionLabel";
import "./ChangelogSection.css";

// 构建时把每个版本的发布说明原文打进前端：发布说明就是更新日志，不需要另写一份。
const RELEASE_NOTES = import.meta.glob<string>("../../../docs/releases/v*.md", { query: "?raw", import: "default", eager: true });
// 上游（MIT 期的 fly-island 3.0.8 及更早）原文单独放一处，只作历史留存，不混进我们的时间线。
const UPSTREAM_NOTES = import.meta.glob<string>("../../../docs/releases/upstream/v*.md", { query: "?raw", import: "default", eager: true });
const COLLAPSED_VERSIONS = 3;

function Inline({ text }: { text: string }) {
  return <>{inlineParts(text).map((part, index) => part.kind === "strong" ? <strong key={index}>{part.value}</strong>
    : part.kind === "code" ? <code key={index}>{part.value}</code> : <span key={index}>{part.value}</span>)}</>;
}

function Entries({ entries }: { entries: ChangelogEntry[] }) {
  return <>{entries.map((entry) => (
    <section key={entry.version} className="changelog-entry" aria-label={versionLabel(entry.version)}>
      <h3 className="changelog-entry__version">{versionLabel(entry.version)}</h3>
      <ul className="changelog-entry__list">
        {entry.items.map((item, index) => (
          <li key={index} className={`changelog-entry__item changelog-entry__item--depth-${item.depth}`}><Inline text={item.text} /></li>
        ))}
      </ul>
    </section>
  ))}</>;
}

/** 关于页的更新日志：默认展开最近几个版本，其余点“显示全部”；上游历史单独折叠。 */
export function ChangelogSection() {
  const text = useTranslation().settings.project;
  const { locale } = useLocale();
  const tag = locale === "en" ? "en" as const : "zh-CN" as const;
  const entries = useMemo(() => buildChangelog(RELEASE_NOTES, tag), [tag]);
  const upstream = useMemo(() => buildChangelog(UPSTREAM_NOTES, tag), [tag]);
  const [expanded, setExpanded] = useState(false);
  const [upstreamOpen, setUpstreamOpen] = useState(false);
  if (!entries.length) return null;
  const shown = expanded ? entries : entries.slice(0, COLLAPSED_VERSIONS);
  const more = entries.length > COLLAPSED_VERSIONS;
  if (!more && !upstream.length) {
    return <SettingsGroup title={text.changelog}><Entries entries={shown} /></SettingsGroup>;
  }
  return (
    <>
      <SettingsGroup title={text.changelog} footer={<div className="changelog-footer">
        {more ? (
          <SettingsLinkButton onClick={() => setExpanded((value) => !value)}>
            {expanded ? text.changelogLess : text.changelogAll(entries.length)}
          </SettingsLinkButton>
        ) : null}
        {upstream.length ? (
          <SettingsLinkButton onClick={() => setUpstreamOpen((value) => !value)}>
            {upstreamOpen ? text.upstreamHide : text.upstreamShow(upstream.length)}
          </SettingsLinkButton>
        ) : null}
      </div>}>
        <Entries entries={shown} />
      </SettingsGroup>
      {upstreamOpen && upstream.length ? (
        <SettingsGroup title={text.upstreamTitle} footer={text.upstreamAttribution}>
          <Entries entries={upstream} />
        </SettingsGroup>
      ) : null}
    </>
  );
}
