"use client";

import * as TabsPrimitive from "@radix-ui/react-tabs";
import { forwardRef, type ComponentPropsWithoutRef, type ElementRef } from "react";

export const Tabs = TabsPrimitive.Root;

export const TabsList = forwardRef<
  ElementRef<typeof TabsPrimitive.List>,
  ComponentPropsWithoutRef<typeof TabsPrimitive.List>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.List
    ref={ref}
    className={[
      "flex gap-[var(--space-1)] border-b border-[var(--color-border)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

TabsList.displayName = TabsPrimitive.List.displayName;

export const TabsTrigger = forwardRef<
  ElementRef<typeof TabsPrimitive.Trigger>,
  ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Trigger
    ref={ref}
    className={[
      "inline-flex min-h-11 items-center border-b-2 border-transparent px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50 data-[state=active]:border-[var(--color-ink-primary)] data-[state=active]:font-semibold data-[state=active]:text-[var(--color-ink-primary)]",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

TabsTrigger.displayName = TabsPrimitive.Trigger.displayName;

export const TabsContent = forwardRef<
  ElementRef<typeof TabsPrimitive.Content>,
  ComponentPropsWithoutRef<typeof TabsPrimitive.Content>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Content
    ref={ref}
    className={[
      "py-[var(--space-4)] focus-visible:outline-none",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
    {...props}
  />
));

TabsContent.displayName = TabsPrimitive.Content.displayName;
