import type { ReactNode } from "react";
import { ClientProvider, useClients, type ClientSet } from "@/lib/providers/client-provider";
import { SessionProvider, type SessionState } from "@/hooks/use-session";
import type { Role } from "@/lib/types";
import { renderHook } from "@testing-library/react";

export function session(role: Role, userId = "u-me"): SessionState {
  return {
    session: {
      user: { id: userId, email: "me@kreyora.test", displayName: "Me", createdAt: "" },
      tenant: { id: "tenant-1", name: "Test Store", slug: "test-store", role, createdAt: "" },
      membership: { id: "m-1", userId, tenantId: "tenant-1", role, joinedAt: "" },
    } as unknown as SessionState["session"],
    isLoading: false,
    effectiveRole: role,
    demoRoleOverride: null,
    setDemoRole: () => undefined,
    permissions: [],
    selectWorkspace: () => undefined,
    clearWorkspace: () => undefined,
    refresh: async () => undefined,
  };
}

export function Wrapper({ role, clients, children }: { role: Role; clients: Partial<ClientSet>; children: ReactNode }) {
  return (
    <ClientProvider clients={clients}>
      <SessionProvider value={session(role)}>{children}</SessionProvider>
    </ClientProvider>
  );
}

export function defaultClientsForTest(): ClientSet {
  return renderHook(() => useClients()).result.current;
}
