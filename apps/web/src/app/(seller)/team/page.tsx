"use client";

import { useEffect, useState } from "react";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { TeamMember, Role } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const ROLE_VARIANTS: Record<Role, "info" | "success" | "warning" | "neutral" | "danger"> = {
  owner: "info",
  admin: "success",
  operator: "warning",
  viewer: "neutral",
  platform_support: "danger",
};

const ROLE_DESCRIPTIONS: Record<Role, string> = {
  owner: "Full access. Can manage billing, team, and all settings.",
  admin: "Can manage catalog, orders, and team members.",
  operator: "Can manage catalog, orders, and conversations.",
  viewer: "Read-only access to all surfaces.",
  platform_support: "Platform-level support access.",
};

export default function TeamPage() {
  const { identity } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [members, setMembers] = useState<TeamMember[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    identity.getTeamMembers(DEMO_TENANT_ID).then((res) => {
      setMembers(res.items);
      setIsLoading(false);
    });
  }, [identity]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-48" />
        <div className="mt-6 space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Team</h1>
          {isViewer && <ViewerBadge />}
        </div>
        {!isViewer && (
          <Button variant="outline" disabled>Invite Member</Button>
        )}
      </div>

      {members.length === 0 ? (
        <div className="mt-6"><EmptyState title="No team members" description="Your team is empty." /></div>
      ) : (
        <div className="mt-6 flex flex-col gap-3">
          {members.map((m) => (
            <div key={m.membership.id} className="flex items-center gap-4 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4">
              <Avatar name={m.user.displayName} src={m.user.avatarUrl} size="md" />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="truncate text-sm font-medium text-[var(--color-ink-primary)]">{m.user.displayName}</p>
                  <Badge variant={ROLE_VARIANTS[m.membership.role]}>{m.membership.role}</Badge>
                </div>
                <p className="truncate text-xs text-[var(--color-ink-secondary)]">{m.user.email}</p>
              </div>
              <span className="hidden text-xs text-[var(--color-ink-secondary)] sm:block">
                Joined {new Date(m.membership.joinedAt).toLocaleDateString()}
              </span>
            </div>
          ))}
        </div>
      )}

      <section className="mt-8">
        <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">Roles</h2>
        <div className="mt-3 space-y-2 text-sm">
          {(Object.keys(ROLE_DESCRIPTIONS) as Role[]).map((role) => (
            <div key={role} className="flex items-start gap-2 border-b border-[var(--color-border)] pb-2 last:border-b-0">
              <Badge variant={ROLE_VARIANTS[role]} className="mt-0.5 shrink-0">{role}</Badge>
              <span className="text-[var(--color-ink-secondary)]">{ROLE_DESCRIPTIONS[role]}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
