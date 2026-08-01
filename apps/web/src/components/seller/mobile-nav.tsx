"use client";

import { useState, useCallback } from "react";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { SidebarNav } from "./sidebar-nav";
import { useSession } from "@/hooks/use-session";

export function MobileNav() {
  const [open, setOpen] = useState(false);
  const { effectiveRole, permissions } = useSession();
  const close = useCallback(() => setOpen(false), []);

  return (
    <DialogPrimitive.Root open={open} onOpenChange={setOpen}>
      <DialogPrimitive.Trigger asChild>
        <button
          type="button"
          className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-primary)] md:hidden"
          aria-label="Open navigation"
        >
          <svg
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <line x1="4" y1="6" x2="20" y2="6" />
            <line x1="4" y1="12" x2="20" y2="12" />
            <line x1="4" y1="18" x2="20" y2="18" />
          </svg>
        </button>
      </DialogPrimitive.Trigger>

      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 data-[state=open]:animate-fade-in" />
        <DialogPrimitive.Content className="fixed inset-y-0 left-0 z-50 flex w-full max-w-[280px] flex-col bg-[var(--color-canvas)] shadow-[var(--shadow-md)] data-[state=open]:animate-slide-in-right focus:outline-none">
          <div className="flex items-center justify-between border-b border-[var(--color-border)] px-4 py-3">
            <span className="text-sm font-bold text-[var(--color-ink-primary)]">
              Kreyora
            </span>
            <DialogPrimitive.Close asChild>
              <button
                type="button"
                className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]"
                aria-label="Close navigation"
              >
                <svg
                  width="20"
                  height="20"
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
              </button>
            </DialogPrimitive.Close>
          </div>
          <div className="flex-1 overflow-y-auto p-4">
            <SidebarNav role={effectiveRole} permissions={permissions} onLinkClick={close} />
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
