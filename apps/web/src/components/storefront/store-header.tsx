"use client";

import Link from "next/link";
import { useCart } from "@/hooks/use-cart";
import type { Store } from "@/lib/types";

interface StoreHeaderProps {
  store: Store;
}

export function StoreHeader({ store }: StoreHeaderProps) {
  const { itemCount } = useCart();
  const slug = store.slug;

  return (
    <header className="sticky top-0 z-30 border-b border-[var(--color-border)] bg-[var(--color-canvas)]">
      <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-3">
        <Link
          href={`/store/${slug}`}
          className="text-base font-bold text-[var(--color-ink-primary)] hover:opacity-80 transition-opacity duration-[var(--duration-hover)]"
        >
          {store.profile.name}
        </Link>

        <nav className="flex items-center gap-4">
          <Link
            href={`/store/${slug}/order-lookup`}
            className="hidden text-sm text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] sm:block"
          >
            Track order
          </Link>
          <Link
            href={`/store/${slug}/cart`}
            className="relative inline-flex min-h-11 min-w-11 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] hover:bg-[var(--color-canvas-subtle)]"
            aria-label={`Cart (${itemCount} items)`}
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
              <path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z" />
              <line x1="3" x2="21" y1="6" y2="6" />
              <path d="M16 10a4 4 0 0 1-8 0" />
            </svg>
            {itemCount > 0 && (
              <span className="absolute -top-0.5 -right-0.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-[var(--color-surface-dark)] px-1 text-[10px] font-bold text-[var(--color-on-dark)]">
                {itemCount}
              </span>
            )}
          </Link>
        </nav>
      </div>
    </header>
  );
}
