"use client";

import * as SelectPrimitive from "@radix-ui/react-select";
import { forwardRef, type ComponentPropsWithoutRef } from "react";

export type SelectProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Root>;

export const Select = SelectPrimitive.Root;

export const SelectGroup = SelectPrimitive.Group;

export const SelectValue = SelectPrimitive.Value;

function ChevronDownIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="shrink-0 text-[var(--color-ink-secondary)]"
    >
      <path d="m6 9 6 6 6-6" />
    </svg>
  );
}

function ChevronUpIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="shrink-0 text-[var(--color-ink-secondary)]"
    >
      <path d="m18 15-6-6-6 6" />
    </svg>
  );
}

export type SelectTriggerProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Trigger>;

export const SelectTrigger = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.Trigger>,
  SelectTriggerProps
>(({ className, children, ...props }, ref) => (
  <SelectPrimitive.Trigger
    ref={ref}
    className={[
      "flex h-11 w-full items-center justify-between gap-[var(--space-2)] rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-left text-sm text-[var(--color-ink-primary)] transition-[border-color,box-shadow,background-color] duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:border-[var(--color-ink-secondary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50 data-[error]:border-[var(--color-danger)] data-[placeholder]:text-[var(--color-ink-secondary)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  >
    {children}
    <SelectPrimitive.Icon asChild>
      <ChevronDownIcon />
    </SelectPrimitive.Icon>
  </SelectPrimitive.Trigger>
));

SelectTrigger.displayName = SelectPrimitive.Trigger.displayName;

const SelectScrollUpButton = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.ScrollUpButton>,
  ComponentPropsWithoutRef<typeof SelectPrimitive.ScrollUpButton>
>(({ className, ...props }, ref) => (
  <SelectPrimitive.ScrollUpButton
    ref={ref}
    className={[
      "flex cursor-default items-center justify-center py-[var(--space-1)] text-[var(--color-ink-secondary)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  >
    <ChevronUpIcon />
  </SelectPrimitive.ScrollUpButton>
));

SelectScrollUpButton.displayName = SelectPrimitive.ScrollUpButton.displayName;

const SelectScrollDownButton = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.ScrollDownButton>,
  ComponentPropsWithoutRef<typeof SelectPrimitive.ScrollDownButton>
>(({ className, ...props }, ref) => (
  <SelectPrimitive.ScrollDownButton
    ref={ref}
    className={[
      "flex cursor-default items-center justify-center py-[var(--space-1)] text-[var(--color-ink-secondary)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  >
    <ChevronDownIcon />
  </SelectPrimitive.ScrollDownButton>
));

SelectScrollDownButton.displayName = SelectPrimitive.ScrollDownButton.displayName;

export type SelectContentProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Content>;

export const SelectContent = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.Content>,
  SelectContentProps
>(({ className, children, position = "popper", ...props }, ref) => (
  <SelectPrimitive.Portal>
    <SelectPrimitive.Content
      ref={ref}
      position={position}
      className={[
        "relative z-50 max-h-96 overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] shadow-[var(--shadow-md)] data-[side=bottom]:animate-fade-in data-[side=left]:animate-fade-in data-[side=right]:animate-fade-in data-[side=top]:animate-fade-in",
        position === "popper"
          ? "min-w-[var(--radix-select-trigger-width)] data-[side=bottom]:translate-y-1 data-[side=left]:-translate-x-1 data-[side=right]:translate-x-1 data-[side=top]:-translate-y-1"
          : undefined,
        className,
      ]
        .filter(Boolean)
        .join(" ")}
      {...props}
    >
      <SelectScrollUpButton />
      <SelectPrimitive.Viewport
        className={[
          "p-[var(--space-1)]",
          position === "popper"
            ? "h-[var(--radix-select-trigger-height)] w-full min-w-[var(--radix-select-trigger-width)]"
            : undefined,
        ]
          .filter(Boolean)
          .join(" ")}
      >
        {children}
      </SelectPrimitive.Viewport>
      <SelectScrollDownButton />
    </SelectPrimitive.Content>
  </SelectPrimitive.Portal>
));

SelectContent.displayName = SelectPrimitive.Content.displayName;

export type SelectLabelProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Label>;

export const SelectLabel = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.Label>,
  SelectLabelProps
>(({ className, ...props }, ref) => (
  <SelectPrimitive.Label
    ref={ref}
    className={[
      "px-[var(--space-3)] py-[var(--space-2)] text-xs font-medium text-[var(--color-ink-secondary)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

SelectLabel.displayName = SelectPrimitive.Label.displayName;

export type SelectItemProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Item>;

export const SelectItem = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.Item>,
  SelectItemProps
>(({ className, children, ...props }, ref) => (
  <SelectPrimitive.Item
    ref={ref}
    className={[
      "relative flex min-h-11 w-full cursor-default select-none items-center rounded-[var(--radius-sm)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] outline-none transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] focus:bg-[var(--color-canvas-subtle)] data-[disabled]:pointer-events-none data-[disabled]:cursor-not-allowed data-[disabled]:opacity-50 data-[highlighted]:bg-[var(--color-canvas-subtle)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  >
    <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
  </SelectPrimitive.Item>
));

SelectItem.displayName = SelectPrimitive.Item.displayName;

export type SelectSeparatorProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Separator>;

export const SelectSeparator = forwardRef<
  React.ComponentRef<typeof SelectPrimitive.Separator>,
  SelectSeparatorProps
>(({ className, ...props }, ref) => (
  <SelectPrimitive.Separator
    ref={ref}
    className={[
      "-mx-[var(--space-1)] my-[var(--space-1)] h-px bg-[var(--color-border)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

SelectSeparator.displayName = SelectPrimitive.Separator.displayName;
