"use client";

import { createContext, useContext, useState, useEffect, useCallback } from "react";
import type { Session, Role } from "@/lib/types";
import { useIdentityClient } from "@/lib/providers/client-provider";

export interface SessionState {
  session: Session | null;
  isLoading: boolean;
  effectiveRole: Role;
  demoRoleOverride: Role | null;
  setDemoRole: (role: Role | null) => void;
}

const SessionContext = createContext<SessionState | null>(null);

export const SessionProvider = SessionContext.Provider;

export function useSessionLoader(): SessionState {
  const identityClient = useIdentityClient();
  const [session, setSession] = useState<Session | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [demoRoleOverride, setDemoRoleOverride] = useState<Role | null>(null);

  useEffect(() => {
    let cancelled = false;
    identityClient.getCurrentSession().then((s) => {
      if (!cancelled) {
        setSession(s);
        setIsLoading(false);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [identityClient]);

  const effectiveRole = demoRoleOverride ?? session?.membership.role ?? "viewer";

  const setDemoRole = useCallback((role: Role | null) => {
    setDemoRoleOverride(role);
  }, []);

  return { session, isLoading, effectiveRole, demoRoleOverride, setDemoRole };
}

export function useSession(): SessionState {
  const ctx = useContext(SessionContext);
  if (!ctx) {
    throw new Error("useSession must be used within a SessionProvider");
  }
  return ctx;
}
