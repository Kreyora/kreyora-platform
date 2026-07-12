"use client";

import * as ToastPrimitive from "@radix-ui/react-toast";
import {
  forwardRef,
  type ComponentPropsWithoutRef,
  type ElementRef,
  type ReactNode,
} from "react";

export type ToastVariant = "success" | "warning" | "danger" | "info";

const variantClasses: Record<ToastVariant, string> = {
  success: "border-[var(--color-success)] bg-[var(--color-success-subtle)]",
  warning: "border-[var(--color-warning)] bg-[var(--color-warning-subtle)]",
  danger: "border-[var(--color-danger)] bg-[var(--color-danger-subtle)]",
  info: "border-[var(--color-info)] bg-[var(--color-info-subtle)]",
};

export const ToastProvider = ToastPrimitive.Provider;

export const ToastViewport = forwardRef<
  ElementRef<typeof ToastPrimitive.Viewport>,
  ComponentPropsWithoutRef<typeof ToastPrimitive.Viewport>
>(({ className, ...props }, ref) => (
  <ToastPrimitive.Viewport
    ref={ref}
    className={[
      "fixed bottom-[var(--space-4)] right-[var(--space-4)] z-[100] flex max-h-screen w-full max-w-sm flex-col gap-[var(--space-3)] p-0 outline-none",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

ToastViewport.displayName = ToastPrimitive.Viewport.displayName;

export interface ToastProps extends ComponentPropsWithoutRef<typeof ToastPrimitive.Root> {
  variant?: ToastVariant;
}

export const Toast = forwardRef<ElementRef<typeof ToastPrimitive.Root>, ToastProps>(
  ({ className, variant = "info", ...props }, ref) => (
    <ToastPrimitive.Root
      ref={ref}
      className={[
        "pointer-events-auto w-full rounded-[var(--radius-md)] border-l-4 p-[var(--space-4)] shadow-[var(--shadow-sm)] data-[state=open]:animate-slide-in-right data-[swipe=cancel]:translate-x-0 data-[swipe=end]:translate-x-[var(--radix-toast-swipe-end-x)] data-[swipe=move]:translate-x-[var(--radix-toast-swipe-move-x)] data-[swipe=move]:transition-none",
        variantClasses[variant],
        className,
      ]
        .filter(Boolean)
        .join(" ")}
      {...props}
    />
  ),
);

Toast.displayName = ToastPrimitive.Root.displayName;

export const ToastTitle = forwardRef<
  ElementRef<typeof ToastPrimitive.Title>,
  ComponentPropsWithoutRef<typeof ToastPrimitive.Title>
>(({ className, ...props }, ref) => (
  <ToastPrimitive.Title
    ref={ref}
    className={["text-sm font-medium text-[var(--color-ink-primary)]", className]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

ToastTitle.displayName = ToastPrimitive.Title.displayName;

export const ToastDescription = forwardRef<
  ElementRef<typeof ToastPrimitive.Description>,
  ComponentPropsWithoutRef<typeof ToastPrimitive.Description>
>(({ className, ...props }, ref) => (
  <ToastPrimitive.Description
    ref={ref}
    className={["text-sm text-[var(--color-ink-secondary)]", className]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

ToastDescription.displayName = ToastPrimitive.Description.displayName;

export const ToastAction = forwardRef<
  ElementRef<typeof ToastPrimitive.Action>,
  ComponentPropsWithoutRef<typeof ToastPrimitive.Action>
>(({ className, ...props }, ref) => (
  <ToastPrimitive.Action
    ref={ref}
    className={[
      "inline-flex min-h-11 items-center justify-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-transparent px-[var(--space-3)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

ToastAction.displayName = ToastPrimitive.Action.displayName;

export const ToastClose = forwardRef<
  ElementRef<typeof ToastPrimitive.Close>,
  ComponentPropsWithoutRef<typeof ToastPrimitive.Close>
>(({ className, children, ...props }, ref) => (
  <ToastPrimitive.Close
    ref={ref}
    className={[
      "inline-flex min-h-11 min-w-11 shrink-0 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas)] hover:text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    toast-close=""
    {...props}
  >
    {children ?? (
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="M18 6 6 18" />
        <path d="m6 6 12 12" />
      </svg>
    )}
  </ToastPrimitive.Close>
));

ToastClose.displayName = ToastPrimitive.Close.displayName;

export interface ToastProviderProps extends ComponentPropsWithoutRef<typeof ToastPrimitive.Provider> {
  children: ReactNode;
}
