"use client";

import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";

export type ButtonVariant = "solid" | "outline" | "ghost";
export type ButtonSize = "sm" | "md" | "lg";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  loadingText?: string;
}

const variantClasses: Record<ButtonVariant, string> = {
  solid:
    "border border-transparent bg-[var(--color-surface-dark)] text-[var(--color-on-dark)] hover:opacity-90 active:opacity-80",
  outline:
    "border border-[var(--color-border)] bg-[var(--color-canvas)] text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)] active:bg-[var(--color-canvas-subtle)]",
  ghost:
    "border border-transparent bg-transparent text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)] active:bg-[var(--color-canvas-subtle)]",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "min-h-11 px-[var(--space-3)] text-sm gap-[var(--space-2)]",
  md: "min-h-11 px-[var(--space-4)] text-sm gap-[var(--space-2)]",
  lg: "min-h-11 px-[var(--space-6)] text-base gap-[var(--space-3)]",
};

export const buttonBaseClasses =
  "inline-flex items-center justify-center font-medium rounded-[var(--radius-md)] transition-[opacity,background-color,border-color,transform] duration-[var(--duration-hover)] ease-[var(--easing-default)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2 active:scale-[0.97] disabled:opacity-50 disabled:pointer-events-none disabled:cursor-not-allowed";

function Spinner({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      xmlns="http://www.w3.org/2000/svg"
      fill="none"
      viewBox="0 0 24 24"
      aria-hidden="true"
    >
      <circle
        className="opacity-25"
        cx="12"
        cy="12"
        r="10"
        stroke="currentColor"
        strokeWidth="4"
      />
      <path
        className="opacity-75 animate-spin"
        fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
      />
    </svg>
  );
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      className,
      variant = "solid",
      size = "md",
      loading = false,
      loadingText = "Loading",
      disabled,
      children,
      type = "button",
      ...props
    },
    ref,
  ) => {
    const isDisabled = disabled || loading;

    return (
      <button
        ref={ref}
        type={type}
        disabled={isDisabled}
        aria-busy={loading || undefined}
        aria-disabled={isDisabled || undefined}
        className={[
          buttonBaseClasses,
          variantClasses[variant],
          sizeClasses[size],
          className,
        ]
          .filter(Boolean)
          .join(" ")}
        {...props}
      >
        {loading ? (
          <>
            <Spinner className="h-4 w-4 shrink-0" />
            <span className="sr-only">{loadingText}</span>
            {children ? <span aria-hidden="true">{children}</span> : null}
          </>
        ) : (
          (children as ReactNode)
        )}
      </button>
    );
  },
);

Button.displayName = "Button";
