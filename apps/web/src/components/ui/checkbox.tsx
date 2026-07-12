"use client";

import { forwardRef, useId, type InputHTMLAttributes } from "react";
import { Label } from "./label";

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type"> {
  label?: string;
  error?: string;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(
  ({ className, label, error, id, required, disabled, ...props }, ref) => {
    const generatedId = useId();
    const checkboxId = id ?? generatedId;
    const errorId = error ? `${checkboxId}-error` : undefined;

    return (
      <div className="flex flex-col gap-[var(--space-1)]">
        <div className="flex items-start gap-[var(--space-3)]">
          <span className="relative inline-flex min-h-11 min-w-11 shrink-0 items-center justify-center">
            <input
              ref={ref}
              type="checkbox"
              id={checkboxId}
              required={required}
              disabled={disabled}
              aria-invalid={error ? true : undefined}
              aria-describedby={errorId}
              className={[
                "peer h-5 w-5 shrink-0 cursor-pointer appearance-none rounded-[var(--radius-sm)] border border-[var(--color-border)] bg-[var(--color-canvas)] transition-[border-color,background-color] duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:border-[var(--color-ink-secondary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2 checked:border-[var(--color-surface-dark)] checked:bg-[var(--color-surface-dark)] disabled:cursor-not-allowed disabled:opacity-60",
                error ? "border-[var(--color-danger)]" : undefined,
                className,
              ]
                .filter(Boolean)
                .join(" ")}
              {...props}
            />
            <svg
              className="pointer-events-none absolute h-3 w-3 text-[var(--color-on-dark)] opacity-0 peer-checked:opacity-100"
              viewBox="0 0 12 12"
              fill="none"
              aria-hidden="true"
            >
              <path
                d="M2.5 6L5 8.5L9.5 3.5"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </span>
          {label ? (
            <Label
              htmlFor={checkboxId}
              required={required}
              className="min-h-11 cursor-pointer pt-[var(--space-3)] leading-none"
            >
              {label}
            </Label>
          ) : null}
        </div>
        {error ? (
          <p id={errorId} role="alert" className="pl-[calc(var(--space-3)+2.75rem)] text-xs text-[var(--color-danger)]">
            {error}
          </p>
        ) : null}
      </div>
    );
  },
);

Checkbox.displayName = "Checkbox";
