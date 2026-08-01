"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { TeamMember, Role } from "@/lib/types";


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
  const { effectiveRole, session, permissions } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [members, setMembers] = useState<TeamMember[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    if (!session) return;
    const result = await identity.getTeamMembers(session.tenant.id);
    setMembers(result.items);
  }, [identity, session]);

  useEffect(() => {
    if (!session) return;
    void Promise.resolve().then(reload).catch(() => setError("We could not load this workspace's team.")).finally(() => setIsLoading(false));
  }, [reload, session]);
  const canManage = permissions.includes("memberships.manage") || effectiveRole === "owner" || effectiveRole === "admin";
  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setError(null);
    try {
      await identity.grantMember(String(data.get("email")), String(data.get("role")) as Role);
      await reload();
      event.currentTarget.reset();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "We could not add that member.");
    }
  }

  async function changeMembership(id: string, action: "suspend" | "reactivate" | "revoke") {
    setError(null);
    try {
      if (action === "suspend") await identity.suspendMember(id);
      if (action === "reactivate") await identity.reactivateMember(id);
      if (action === "revoke") await identity.revokeMember(id);
      await reload();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "We could not change this membership.");
    }
  }

  async function updateRole(id: string, role: Role) {
    setError(null);
    try {
      await identity.changeMemberRole(id, role);
      await reload();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "We could not change this role.");
    }
  }

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
        {canManage && <Button variant="outline" disabled={!session}>Add member below</Button>}
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
                {m.membership.status && m.membership.status !== "active" && <p className="text-xs text-[var(--color-danger)]">{m.membership.status}</p>}
              </div>
              <span className="hidden text-xs text-[var(--color-ink-secondary)] sm:block">
                Joined {new Date(m.membership.joinedAt).toLocaleDateString()}
              </span>
              {canManage && (effectiveRole === "owner" || m.membership.role !== "owner") && (
                <div className="flex gap-2">
                  <select aria-label={`Change role for ${m.user.email}`} value={m.membership.role} onChange={(event) => void updateRole(m.membership.id, event.target.value as Role)} className="min-h-11 rounded border px-2 text-sm">
                    {effectiveRole === "owner" && <option value="owner">Owner</option>}
                    <option value="admin">Admin</option><option value="operator">Operator</option><option value="viewer">Viewer</option>
                  </select>
                  {m.membership.status === "suspended" ? <Button size="sm" variant="outline" onClick={() => changeMembership(m.membership.id, "reactivate")}>Reactivate</Button> : <Button size="sm" variant="outline" onClick={() => changeMembership(m.membership.id, "suspend")}>Suspend</Button>}
                  <Button size="sm" variant="ghost" onClick={() => changeMembership(m.membership.id, "revoke")}>Revoke</Button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {canManage && <form onSubmit={add} className="mt-6 flex flex-wrap gap-3"><input required name="email" type="email" placeholder="Registered account email" className="min-h-11 rounded border px-3" /><select name="role" className="min-h-11 rounded border px-3"><option value="viewer">Viewer</option><option value="operator">Operator</option><option value="admin">Admin</option></select><Button type="submit">Add member</Button></form>}
      {error && <p className="mt-3 text-sm text-[var(--color-danger)]">{error}</p>}

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
