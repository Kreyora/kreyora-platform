"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useIdentityClient } from "@/lib/providers/client-provider";
import { Skeleton } from "@/components/ui/skeleton";
import { Badge } from "@/components/ui/badge";
import type { Tenant } from "@/lib/types";

export default function WorkspacesPage() {
  const identityClient = useIdentityClient();
  const [workspaces, setWorkspaces] = useState<Tenant[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    identityClient.getWorkspaces().then((ws) => {
      if (!cancelled) {
        setWorkspaces(ws);
        setIsLoading(false);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [identityClient]);

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="text-heading-page text-[var(--color-ink-primary)]">
        Choose a workspace
      </h1>
      <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
        Select a workspace to manage. Each workspace is an independent seller
        account with its own catalog, orders, and team.
      </p>

      <div className="mt-8 flex flex-col gap-3">
        {isLoading ? (
          <>
            <Skeleton className="h-24 w-full rounded-[var(--radius-lg)]" />
            <Skeleton className="h-24 w-full rounded-[var(--radius-lg)]" />
          </>
        ) : (
          workspaces.map((ws) => (
            <Link
              key={ws.id}
              href="/dashboard"
              className="group flex items-center justify-between rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-6 transition-[border-color,box-shadow] duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:border-[var(--color-ink-secondary)] hover:shadow-[var(--shadow-sm)]"
            >
              <div>
                <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">
                  {ws.name}
                </h2>
                <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
                  {ws.slug}
                </p>
              </div>
              <div className="flex items-center gap-3">
                <Badge variant="neutral">Owner</Badge>
                <svg
                  width="16"
                  height="16"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className="text-[var(--color-ink-secondary)] transition-transform duration-[var(--duration-hover)] group-hover:translate-x-0.5"
                  aria-hidden="true"
                >
                  <path d="m9 18 6-6-6-6" />
                </svg>
              </div>
            </Link>
          ))
        )}
      </div>

      <p className="mt-6 text-center text-xs text-[var(--color-ink-secondary)]">
        Simulated workspace selection — the demo has one workspace with fixture
        data.
      </p>
    </div>
  );
}
