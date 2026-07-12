"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, useCallback, useEffect } from "react";
import * as DialogPrimitive from "@radix-ui/react-dialog";

const NAV_LINKS = [
  { href: "/features", label: "Features" },
  { href: "/pricing", label: "Pricing" },
  { href: "/demo", label: "Demo" },
  { href: "/contact", label: "Contact" },
] as const;

function NavLink({
  href,
  label,
  active,
  onClick,
}: {
  href: string;
  label: string;
  active: boolean;
  onClick?: () => void;
}) {
  return (
    <Link
      href={href}
      onClick={onClick}
      className={[
        "inline-flex min-h-11 items-center text-sm transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)]",
        active
          ? "font-semibold text-[var(--color-ink-primary)]"
          : "text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]",
      ].join(" ")}
    >
      {label}
    </Link>
  );
}

function HamburgerIcon() {
  return (
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
  );
}

function CloseIcon() {
  return (
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
  );
}

export function MarketingHeader() {
  const pathname = usePathname();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const handler = () => setScrolled(window.scrollY > 8);
    handler();
    window.addEventListener("scroll", handler, { passive: true });
    return () => window.removeEventListener("scroll", handler);
  }, []);

  const closeMobile = useCallback(() => setMobileOpen(false), []);

  return (
    <header
      className={[
        "sticky top-0 z-40 bg-[var(--color-canvas)] transition-[border-color,box-shadow] duration-[var(--duration-hover)] ease-[var(--easing-default)]",
        scrolled
          ? "border-b border-[var(--color-border)] shadow-[var(--shadow-sm)]"
          : "border-b border-transparent",
      ].join(" ")}
    >
      <nav className="mx-auto flex max-w-[90rem] items-center justify-between px-5 py-3 md:px-8 lg:px-12">
        <Link
          href="/"
          className="text-lg font-bold text-[var(--color-ink-primary)]"
        >
          Kreyora
        </Link>

        {/* Desktop navigation */}
        <div className="hidden items-center gap-8 md:flex">
          {NAV_LINKS.map((link) => (
            <NavLink
              key={link.href}
              href={link.href}
              label={link.label}
              active={pathname === link.href}
            />
          ))}
          <Link
            href="/demo"
            className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-transparent bg-[var(--color-surface-dark)] px-[var(--space-4)] text-sm font-medium text-[var(--color-on-dark)] transition-opacity duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:opacity-90 active:opacity-80"
          >
            Try demo
          </Link>
        </div>

        {/* Mobile hamburger */}
        <DialogPrimitive.Root open={mobileOpen} onOpenChange={setMobileOpen}>
          <DialogPrimitive.Trigger asChild>
            <button
              type="button"
              className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-primary)] md:hidden"
              aria-label="Open menu"
            >
              <HamburgerIcon />
            </button>
          </DialogPrimitive.Trigger>

          <DialogPrimitive.Portal>
            <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 data-[state=open]:animate-fade-in" />
            <DialogPrimitive.Content className="fixed inset-y-0 right-0 z-50 flex w-full max-w-xs flex-col bg-[var(--color-canvas)] shadow-[var(--shadow-md)] data-[state=open]:animate-slide-in-right data-[state=closed]:animate-slide-out-right focus:outline-none">
              <div className="flex items-center justify-between border-b border-[var(--color-border)] px-5 py-3">
                <span className="text-lg font-bold text-[var(--color-ink-primary)]">
                  Kreyora
                </span>
                <DialogPrimitive.Close asChild>
                  <button
                    type="button"
                    className="inline-flex min-h-11 min-w-11 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]"
                    aria-label="Close menu"
                  >
                    <CloseIcon />
                  </button>
                </DialogPrimitive.Close>
              </div>
              <nav className="flex flex-col gap-1 p-5">
                {NAV_LINKS.map((link) => (
                  <NavLink
                    key={link.href}
                    href={link.href}
                    label={link.label}
                    active={pathname === link.href}
                    onClick={closeMobile}
                  />
                ))}
                <Link
                  href="/demo"
                  onClick={closeMobile}
                  className="mt-4 inline-flex min-h-11 items-center justify-center rounded-[var(--radius-md)] border border-transparent bg-[var(--color-surface-dark)] px-[var(--space-4)] text-sm font-medium text-[var(--color-on-dark)] transition-opacity duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:opacity-90"
                >
                  Try demo
                </Link>
              </nav>
            </DialogPrimitive.Content>
          </DialogPrimitive.Portal>
        </DialogPrimitive.Root>
      </nav>
    </header>
  );
}
