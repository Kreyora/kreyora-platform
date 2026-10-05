"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { Message } from "@/lib/types";
import { describeFailureCode } from "@/lib/utils/conversation-errors";
import { DELIVERY_LABEL } from "./inbox-display";

/** A server message, or an optimistic local reply that hasn't been accepted yet. */
export type TimelineMessage = Message & {
  local?: { status: "sending" | "failed"; error?: string };
};

export interface ConversationTimelineProps {
  messages: TimelineMessage[];
  customerName: string;
  canWrite: boolean;
  hasOlder: boolean;
  isLoadingOlder: boolean;
  onLoadOlder: () => void;
  /** Resend a failed message as a NEW reply (new idempotency key). */
  onSendAgain: (text: string) => void;
  /** Retry the SAME local attempt (same idempotency key). */
  onRetryLocal: (localId: string) => void;
}

function senderLabel(message: TimelineMessage, customerName: string): string {
  return message.senderType === "customer" ? customerName : message.senderName;
}

function FailedActions({ message, onSendAgain }: { message: TimelineMessage; onSendAgain: (text: string) => void }) {
  const [confirming, setConfirming] = useState(false);
  const unconfirmed = message.failureCode === "delivery_unconfirmed";

  return (
    <div className="mt-2 flex flex-col gap-2">
      <p className="text-xs text-[var(--color-danger)]">{describeFailureCode(message.failureCode)}</p>
      {confirming ? (
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Confirm resend">
          <span className="text-xs text-[var(--color-ink-primary)]">The customer may already have this message. Send again anyway?</span>
          <Button size="sm" onClick={() => { setConfirming(false); onSendAgain(message.content); }}>Send again</Button>
          <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>Cancel</Button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="outline" onClick={() => (unconfirmed ? setConfirming(true) : onSendAgain(message.content))}>
            Send again
          </Button>
          <Button size="sm" variant="ghost" onClick={() => void navigator.clipboard?.writeText(message.content)}>Copy text</Button>
        </div>
      )}
    </div>
  );
}

export function ConversationTimeline({
  messages,
  customerName,
  canWrite,
  hasOlder,
  isLoadingOlder,
  onLoadOlder,
  onSendAgain,
  onRetryLocal,
}: ConversationTimelineProps) {
  return (
    <section aria-label="Messages">
      {hasOlder && (
        <div className="mb-3 flex justify-center">
          <Button variant="ghost" size="sm" onClick={onLoadOlder} disabled={isLoadingOlder}>
            {isLoadingOlder ? "Loading…" : "Load earlier messages"}
          </Button>
        </div>
      )}
      {messages.length === 0 && (
        <p className="text-sm text-[var(--color-ink-secondary)]">No messages yet.</p>
      )}
      <ol className="flex flex-col gap-3">
        {messages.map((m) => {
          const isOutbound = m.direction === "outbound";
          const delivery = m.local
            ? m.local.status === "sending"
              ? DELIVERY_LABEL.pending
              : DELIVERY_LABEL.failed
            : DELIVERY_LABEL[m.deliveryState];
          return (
            <li
              key={m.id}
              className={`flex flex-col gap-1 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 ${isOutbound ? "ml-6 bg-[var(--color-canvas-subtle)] sm:ml-12" : "mr-6 sm:mr-12"}`}
            >
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-xs font-medium text-[var(--color-ink-primary)]">{senderLabel(m, customerName)}</span>
                {isOutbound && <Badge variant={delivery.variant}>{delivery.label}</Badge>}
              </div>
              <p className={`whitespace-pre-wrap break-words text-sm ${m.isRedacted ? "italic text-[var(--color-ink-secondary)]" : "text-[var(--color-ink-primary)]"}`}>
                {m.content}
              </p>
              {m.attachments.map((att) => (
                <a
                  key={att.url}
                  href={att.url}
                  target="_blank"
                  rel="noopener noreferrer nofollow"
                  className="inline-flex min-h-11 items-center text-xs text-[var(--color-ink-secondary)] underline"
                >
                  Open {att.type} attachment
                </a>
              ))}
              {m.reactions && m.reactions.length > 0 && (
                <span className="text-xs text-[var(--color-ink-secondary)]" aria-label="Reactions">
                  {m.reactions.map((r) => `${r.emoji} ${r.count}`).join("  ")}
                </span>
              )}
              <time dateTime={m.createdAt} className="text-[10px] text-[var(--color-ink-secondary)]">
                {new Date(m.createdAt).toLocaleString()}
              </time>
              {m.local?.status === "failed" && (
                <div className="mt-2 flex flex-col gap-2">
                  <p className="text-xs text-[var(--color-danger)]">{m.local.error}</p>
                  {canWrite && (
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" onClick={() => onRetryLocal(m.id)}>Try again</Button>
                    </div>
                  )}
                </div>
              )}
              {!m.local && canWrite && isOutbound && m.deliveryState === "failed" && (
                <FailedActions message={m} onSendAgain={onSendAgain} />
              )}
            </li>
          );
        })}
      </ol>
    </section>
  );
}
