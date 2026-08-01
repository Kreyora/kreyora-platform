"use client";

import { createContext, useContext, useState, useEffect, useCallback } from "react";
import type { Session, Role } from "@/lib/types";
import { useAuthClient, useIdentityClient, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { clearSelectedWorkspace, selectedWorkspaceId, selectWorkspace } from "@/lib/session/workspace-selection";

export interface SessionState {
  session: Session | null;
  isLoading: boolean;
  effectiveRole: Role;
  demoRoleOverride: Role | null;
  setDemoRole: (role: Role | null) => void;
  permissions: string[];
  selectWorkspace: (id: string) => void;
  clearWorkspace: () => void;
  refresh: () => Promise<void>;
}

const SessionContext = createContext<SessionState | null>(null);

export const SessionProvider = SessionContext.Provider;

export function useSessionLoader(): SessionState {
  const identityClient = useIdentityClient();
  const authClient = useAuthClient();
  const [session, setSession] = useState<Session | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [demoRoleOverride, setDemoRoleOverride] = useState<Role | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    try {
      if (USING_FIXTURE_ADAPTERS) { setSession(await identityClient.getCurrentSession()); setPermissions(await identityClient.getPermissions()); return; }
      const user = await authClient.getCurrentUser();
      const tenantId = selectedWorkspaceId();
      if (!tenantId) { setSession(null); setPermissions([]); return; }
      const workspaces = await identityClient.getWorkspaces();
      const workspace = workspaces.find((item) => item.id === tenantId);
      if (!workspace) { clearSelectedWorkspace(); setSession(null); setPermissions([]); return; }
      const permissions = await identityClient.getPermissions();
      setPermissions(permissions);
      setSession({ user: { ...user, createdAt: "" }, tenant: workspace, membership: { id: tenantId, userId: user.id, tenantId, role: workspace.role ?? "viewer", joinedAt: "" } });
    } finally { setIsLoading(false); }
  }, [authClient, identityClient]);

  useEffect(() => {
    let cancelled = false;
    void Promise.resolve().then(refresh).catch(() => { if (!cancelled) { setSession(null); setPermissions([]); setIsLoading(false); } });
    return () => {
      cancelled = true;
    };
  }, [refresh]);

  const effectiveRole = demoRoleOverride ?? session?.membership.role ?? "viewer";

  const setDemoRole = useCallback((role: Role | null) => {
    setDemoRoleOverride(role);
  }, []);

  return { session, isLoading, effectiveRole, demoRoleOverride, setDemoRole, permissions, selectWorkspace, clearWorkspace: clearSelectedWorkspace, refresh };
}

export function useSession(): SessionState {
  const ctx = useContext(SessionContext);
  if (!ctx) {
    throw new Error("useSession must be used within a SessionProvider");
  }
  return ctx;
}
