"use client";

import { useId, useRef, useState, type KeyboardEvent } from "react";
import { Button } from "@/components/ui/button";

export const COMPOSER_MAX_LENGTH = 1000;

export interface MessageComposerProps {
  onSend: (text: string) => Promise<boolean>;
  /** When set, sending is blocked and the reason is shown. */
  disabledReason?: string;
  /** Informational, server decides (e.g. reply window probably closed). */
  hint?: string;
}

/**
 * Accessible reply composer: labelled textarea, Enter sends, Shift+Enter inserts a newline, character
 * counter, focus returns to the field after a send.
 */
export function MessageComposer({ onSend, disabledReason, hint }: MessageComposerProps) {
  const [text, setText] = useState("");
  const [isSending, setIsSending] = useState(false);
  const field = useRef<HTMLTextAreaElement>(null);
  const id = useId();
  const counterId = `${id}-counter`;
  const hintId = `${id}-hint`;
  const tooLong = text.length > COMPOSER_MAX_LENGTH;
  const blocked = Boolean(disabledReason);

  const submit = async () => {
    const value = text.trim();
    if (!value || tooLong || blocked || isSending) return;
    setIsSending(true);
    try {
      const accepted = await onSend(value);
      if (accepted) setText("");
    } finally {
      setIsSending(false);
      field.current?.focus();
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void submit();
    }
  };

  return (
    <form
      className="mt-4 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-3"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <label htmlFor={id} className="text-sm font-medium text-[var(--color-ink-primary)]">Reply</label>
      <textarea
        id={id}
        ref={field}
        value={text}
        onChange={(event) => setText(event.target.value)}
        onKeyDown={onKeyDown}
        disabled={blocked}
        rows={3}
        aria-describedby={`${counterId}${hint || disabledReason ? ` ${hintId}` : ""}`}
        aria-invalid={tooLong || undefined}
        placeholder={blocked ? undefined : "Write a reply… (Enter to send, Shift+Enter for a new line)"}
        className="mt-2 w-full resize-y rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 py-2 text-sm text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2 disabled:bg-[var(--color-canvas-subtle)] disabled:text-[var(--color-ink-secondary)]"
      />
      {(disabledReason || hint) && (
        <p id={hintId} className="mt-2 text-xs text-[var(--color-ink-secondary)]">{disabledReason ?? hint}</p>
      )}
      <div className="mt-2 flex items-center justify-between gap-3">
        <span id={counterId} className={`text-xs ${tooLong ? "text-[var(--color-danger)]" : "text-[var(--color-ink-secondary)]"}`}>
          {text.length}/{COMPOSER_MAX_LENGTH}
        </span>
        <Button type="submit" disabled={blocked || isSending || !text.trim() || tooLong}>
          {isSending ? "Sending…" : "Send"}
        </Button>
      </div>
    </form>
  );
}
