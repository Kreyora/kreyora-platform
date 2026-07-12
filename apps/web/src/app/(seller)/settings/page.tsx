"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { Session } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

export default function SettingsPage() {
  const { identity } = useClients();
  const [session, setSession] = useState<Session | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    identity.getCurrentSession().then((s) => {
      setSession(s);
      setIsLoading(false);
    });
  }, [identity]);

  if (isLoading || !session) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-48" />
        <Skeleton className="mt-6 h-64 w-full rounded-[var(--radius-lg)]" />
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-heading-page text-[var(--color-ink-primary)]">Settings</h1>

      <div className="mt-8 grid gap-8 lg:grid-cols-2">
        <section className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
          <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">Workspace</h2>
          <div className="mt-4 space-y-3 text-sm">
            <InfoRow label="Name" value={session.tenant.name} />
            <InfoRow label="Slug" value={session.tenant.slug} />
            <InfoRow label="Created" value={new Date(session.tenant.createdAt).toLocaleDateString()} />
            <div className="flex items-start gap-3 pt-1">
              <span className="w-28 shrink-0 text-xs text-[var(--color-ink-secondary)]">Plan</span>
              <Link href="/billing" className="text-sm text-[var(--color-ink-primary)] underline underline-offset-2 hover:text-[var(--color-ink-secondary)]">
                View billing &rarr;
              </Link>
            </div>
          </div>
        </section>

        <section className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
          <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">Session</h2>
          <div className="mt-4 space-y-3 text-sm">
            <InfoRow label="User" value={session.user.displayName} />
            <InfoRow label="Email" value={session.user.email} />
            <div className="flex items-start gap-3 pt-1">
              <span className="w-28 shrink-0 text-xs text-[var(--color-ink-secondary)]">Role</span>
              <Badge variant="neutral">{session.membership.role}</Badge>
            </div>
          </div>
        </section>
      </div>

      <section className="mt-8 rounded-[var(--radius-lg)] border border-[var(--color-danger)]/20 p-5">
        <h2 className="text-base font-semibold text-[var(--color-danger)]">Danger Zone</h2>
        <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
          Permanently delete this workspace and all associated data. This action cannot be undone.
        </p>
        <Button variant="outline" disabled className="mt-4 border-[var(--color-danger)]/40 text-[var(--color-danger)] opacity-50">
          Delete Workspace
        </Button>
      </section>

      <p className="mt-8 text-[10px] text-[var(--color-ink-secondary)]">
        Settings are simulated. Changes will not be persisted.
      </p>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start gap-3 border-b border-[var(--color-border)] pb-2 last:border-b-0">
      <span className="w-28 shrink-0 text-xs text-[var(--color-ink-secondary)]">{label}</span>
      <span className="text-sm text-[var(--color-ink-primary)]">{value}</span>
    </div>
  );
}
