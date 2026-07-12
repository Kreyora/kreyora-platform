import { forwardRef, type HTMLAttributes } from "react";

export type BadgeVariant = "success" | "warning" | "danger" | "info" | "neutral";

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: BadgeVariant;
}

const variantClasses: Record<BadgeVariant, string> = {
  success:
    "bg-[var(--color-success-subtle)] text-[var(--color-success)]",
  warning:
    "bg-[var(--color-warning-subtle)] text-[var(--color-warning)]",
  danger:
    "bg-[var(--color-danger-subtle)] text-[var(--color-danger)]",
  info:
    "bg-[var(--color-info-subtle)] text-[var(--color-info)]",
  neutral:
    "bg-[var(--color-canvas-subtle)] text-[var(--color-ink-secondary)]",
};

export const Badge = forwardRef<HTMLSpanElement, BadgeProps>(
  ({ className, variant = "neutral", children, ...props }, ref) => {
    return (
      <span
        ref={ref}
        className={[
          "inline-flex items-center rounded-[var(--radius-full)] px-[var(--space-3)] py-[var(--space-1)] text-xs font-medium leading-none",
          variantClasses[variant],
          className,
        ]
          .filter(Boolean)
          .join(" ")}
        {...props}
      >
        {children}
      </span>
    );
  },
);

Badge.displayName = "Badge";
