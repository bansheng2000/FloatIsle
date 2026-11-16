import type { ReactNode } from "react";
import "./SettingsRow.css";

/**
 * One setting: its title, with an optional one-sentence consequence under it, on the
 * left and its control on the right. Children (a slider) span the row below them.
 * An indented row belongs to the switch above it and is only shown while that is on.
 */
export function SettingsRow({
  title,
  description,
  control,
  labelFor,
  indent = false,
  className,
  children,
}: {
  title: ReactNode;
  description?: ReactNode;
  control?: ReactNode;
  /** Id of the input the title names; clicking the title then focuses or toggles it. */
  labelFor?: string;
  indent?: boolean;
  /** Extra class for a group that lays its rows out differently. */
  className?: string;
  children?: ReactNode;
}) {
  const name = ["settings-row", indent && "settings-row--indent", className].filter(Boolean).join(" ");
  return (
    <div className={name}>
      <div className="settings-row__main">
        <div className="settings-row__text">
          {labelFor ? (
            <label className="settings-row__title" htmlFor={labelFor}>{title}</label>
          ) : (
            <span className="settings-row__title">{title}</span>
          )}
          {description ? <span className="settings-row__description">{description}</span> : null}
        </div>
        {control ? <div className="settings-row__control">{control}</div> : null}
      </div>
      {children}
    </div>
  );
}
