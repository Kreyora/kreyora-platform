"use client";

import { forwardRef, type HTMLAttributes, type ReactNode } from "react";

export interface EmptyStateProps extends HTMLAttributes<HTMLDivElement> {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
}

function DefaultIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="32"
      height="32"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <path d="M3 9h18" />
      <path d="M9 21V9" />
    </svg>
  );
}

export const EmptyState = forwardRef<HTMLDivElement, EmptyStateProps>(
  ({ className, icon, title, description, action, ...props }, ref) => {
    return (
      <div
        ref={ref}
        role="status"
        className={[
          "flex flex-col items-center justify-center gap-[var(--space-4)] px-[var(--space-6)] py-[var(--space-12)] text-center",
          className,
        ]
          .filter(Boolean)
          .join(" ")}
        {...props}
      >
        <div
          className="flex h-16 w-16 items-center justify-center rounded-[var(--radius-lg)] bg-[var(--color-canvas-subtle)] text-[var(--color-ink-secondary)]"
          aria-hidden="true"
        >
          {icon ?? <DefaultIcon />}
        </div>
        <div className="flex max-w-sm flex-col gap-[var(--space-2)]">
          <h3 className="text-base font-semibold text-[var(--color-ink-primary)]">{title}</h3>
          {description ? (
            <p className="text-sm text-[var(--color-ink-secondary)]">{description}</p>
          ) : null}
        </div>
        {action ? <div className="mt-[var(--space-2)]">{action}</div> : null}
      </div>
    );
  },
);

EmptyState.displayName = "EmptyState";
