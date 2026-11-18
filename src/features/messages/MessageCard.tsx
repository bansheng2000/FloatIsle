import { useTranslation } from "../../i18n";
import { X } from "lucide-react";
import { MessageSourceIcon } from "./MessageSourceIcon";
import { formatMessageCardTime, splitMessageSender } from "./messageCardText";
import "./MessageCard.css";

type MessageCardProps = {
  source: "qq" | "wechat";
  name: string;
  timestamp: number;
  body: string;
  messageId?: string;
  muted?: boolean;
  disabled?: boolean;
  ariaLabel?: string;
  onOpen?: () => void;
  /** (E5-2) Optional per-conversation clear affordance. */
  onClear?: () => void;
  clearLabel?: string;
};

export function MessageCard({ source, name, timestamp, body, messageId, muted, disabled, ariaLabel, onOpen, onClear, clearLabel }: MessageCardProps) {
  const t = useTranslation();
  const sender = muted ? null : splitMessageSender(body);
  const date = new Date(timestamp);
  const content = <>
    <div className="message-card__heading">
      <MessageSourceIcon source={source} size={18} />
      <strong className="message-card__name">{name}</strong>
      <time dateTime={Number.isFinite(date.getTime()) ? date.toISOString() : undefined}>{formatMessageCardTime(timestamp, Date.now(), t.messages.yesterday)}</time>
    </div>
    {body && <p className={`message-card__body${muted ? " message-card__body--muted" : ""}`}>
      {sender ? <><strong>{sender.sender}</strong>{sender.separator}{sender.content}</> : body}
    </p>}
  </>;
  const surface = onOpen
    ? <button className="message-card__surface" type="button" disabled={disabled} aria-label={ariaLabel} onClick={onOpen}>{content}</button>
    : <div className="message-card__surface">{content}</div>;
  return <article className="message-card" role="listitem" data-message-id={messageId}>
    {onClear
      ? <div className="message-card__row">
        {surface}
        <button className="icon-button message-card__clear" type="button" disabled={disabled} aria-label={clearLabel} onClick={onClear}><X size={14} strokeWidth={1.8} /></button>
      </div>
      : surface}
  </article>;
}
