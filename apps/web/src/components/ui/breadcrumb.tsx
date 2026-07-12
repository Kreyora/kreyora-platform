"use client";

import { forwardRef, type HTMLAttributes, type ReactNode } from "react";

export interface BreadcrumbItem {
  label: string;
  href?: string;
  current?: boolean;
}

export interface BreadcrumbProps extends HTMLAttributes<HTMLElement> {
  items: BreadcrumbItem[];
  separator?: ReactNode;
}

function DefaultSeparator() {
  return (
    <span className="text-[var(--color-ink-secondary)]" aria-hidden="true">
      /
    </span>
  );
}

export const Breadcrumb = forwardRef<HTMLElement, BreadcrumbProps>(
  ({ className, items, separator, ...props }, ref) => {
    return (
      <nav ref={ref} aria-label="Breadcrumb" className={className} {...props}>
        <ol className="flex flex-wrap items-center gap-[var(--space-2)] text-sm">
          {items.map((item, index) => {
            const isLast = index === items.length - 1;
            const isCurrent = item.current ?? isLast;

            return (
              <li key={`${item.label}-${index}`} className="inline-flex items-center gap-[var(--space-2)]">
                {index > 0 ? (separator ?? <DefaultSeparator />) : null}
                {item.href && !isCurrent ? (
                  <a
                    href={item.href}
                    className="text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
                  >
                    {item.label}
                  </a>
                ) : (
                  <span
                    className={[
                      isCurrent
                        ? "font-medium text-[var(--color-ink-primary)]"
                        : "text-[var(--color-ink-secondary)]",
                    ].join(" ")}
                    aria-current={isCurrent ? "page" : undefined}
                  >
                    {item.label}
                  </span>
                )}
              </li>
            );
          })}
        </ol>
      </nav>
    );
  },
);

Breadcrumb.displayName = "Breadcrumb";
