"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { SessionProvider, useSessionLoader } from "@/hooks/use-session";
import { SidebarNav } from "@/components/seller/sidebar-nav";
import { ProfileMenu } from "@/components/seller/profile-menu";
import { MobileNav } from "@/components/seller/mobile-nav";
import { RoleSwitcher } from "@/components/seller/role-switcher";
import { useAuthClient, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";

function NotificationBell() {
  return (
    <button
      type="button"
      className="relative inline-flex min-h-11 min-w-11 items-center justify-center rounded-[var(--radius-md)] text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)] hover:text-[var(--color-ink-primary)]"
      aria-label="Notifications"
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
        <path d="M13.73 21a2 2 0 0 1-3.46 0" />
      </svg>
      <span className="absolute right-2 top-2 h-2 w-2 rounded-full bg-[var(--color-danger)]" />
    </button>
  );
}

function SellerShellInner({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const auth = useAuthClient();
  const [isAuthenticated, setIsAuthenticated] = useState(USING_FIXTURE_ADAPTERS);
  const sessionState = useSessionLoader();
  const { session, isLoading, effectiveRole } = sessionState;

  useEffect(() => {
    if (USING_FIXTURE_ADAPTERS) return;
    auth.getCurrentUser().then(() => setIsAuthenticated(true)).catch(() => router.replace("/signin"));
  }, [auth, router]);

  if (isLoading || !isAuthenticated) {
    return (
      <div className="flex min-h-full items-center justify-center">
        <div className="text-sm text-[var(--color-ink-secondary)]">
          Loading workspace...
        </div>
      </div>
    );
  }

  return (
    <SessionProvider value={sessionState}>
      <div className="flex min-h-full">
        {/* Desktop sidebar */}
        <aside className="hidden w-60 shrink-0 flex-col border-r border-[var(--color-border)] bg-[var(--color-canvas)] md:flex">
          <div className="border-b border-[var(--color-border)] px-4 py-4">
            <Link
              href="/dashboard"
              className="text-sm font-bold text-[var(--color-ink-primary)]"
            >
              Kreyora
            </Link>
            <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
              {session?.tenant.name ?? "Workspace"}
            </p>
          </div>

          <div className="flex-1 overflow-y-auto px-3 py-4">
            <SidebarNav role={effectiveRole} />
          </div>

          <div className="border-t border-[var(--color-border)] px-3 py-3">
            <RoleSwitcher />
          </div>
        </aside>

        {/* Main area */}
        <div className="flex flex-1 flex-col">
          {/* Top bar */}
          <header className="flex items-center justify-between border-b border-[var(--color-border)] bg-[var(--color-canvas)] px-4 py-2 md:px-6">
            <div className="flex items-center gap-2">
              <MobileNav />
              <span className="text-sm font-semibold text-[var(--color-ink-primary)] md:hidden">
                {session?.tenant.name ?? "Workspace"}
              </span>
            </div>
            <div className="flex items-center gap-1">
              <NotificationBell />
              <ProfileMenu />
            </div>
          </header>

          <main className="flex-1 px-5 py-6 md:px-8">{children}</main>
        </div>
      </div>
    </SessionProvider>
  );
}

export default function SellerLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <SellerShellInner>{children}</SellerShellInner>;
}
