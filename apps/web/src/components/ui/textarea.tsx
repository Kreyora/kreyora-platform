"use client";

import { forwardRef, useId, type TextareaHTMLAttributes } from "react";
import { Label } from "./label";

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  error?: string;
  hint?: string;
}

const fieldClasses =
  "w-full min-h-[6rem] rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] py-[var(--space-3)] text-sm text-[var(--color-ink-primary)] transition-[border-color,box-shadow,background-color] duration-[var(--duration-hover)] ease-[var(--easing-default)] placeholder:text-[var(--color-ink-secondary)] hover:border-[var(--color-ink-secondary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:bg-[var(--color-canvas-subtle)] disabled:text-[var(--color-ink-secondary)] disabled:opacity-60 resize-y";

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ className, label, error, hint, id, required, disabled, ...props }, ref) => {
    const generatedId = useId();
    const textareaId = id ?? generatedId;
    const errorId = error ? `${textareaId}-error` : undefined;
    const hintId = hint ? `${textareaId}-hint` : undefined;

    return (
      <div className="flex w-full flex-col gap-[var(--space-2)]">
        {label ? (
          <Label htmlFor={textareaId} required={required}>
            {label}
          </Label>
        ) : null}
        <textarea
          ref={ref}
          id={textareaId}
          required={required}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={[hintId, errorId].filter(Boolean).join(" ") || undefined}
          className={[
            fieldClasses,
            error
              ? "border-[var(--color-danger)] focus-visible:outline-[var(--color-danger)]"
              : undefined,
            className,
          ]
            .filter(Boolean)
            .join(" ")}
          {...props}
        />
        {hint && !error ? (
          <p id={hintId} className="text-xs text-[var(--color-ink-secondary)]">
            {hint}
          </p>
        ) : null}
        {error ? (
          <p id={errorId} role="alert" className="text-xs text-[var(--color-danger)]">
            {error}
          </p>
        ) : null}
      </div>
    );
  },
);

Textarea.displayName = "Textarea";
