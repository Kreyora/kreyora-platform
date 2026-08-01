import { apiFetch } from "@/lib/api";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { IdentityClient } from "@/lib/ports/identity-client";
import type { PaginatedResult, Role, TeamMember, Tenant } from "@/lib/types";

type Workspace = { tenantId: string; displayName: string; slug: string; role: string };
type Member = { id: string; userId: string; displayName: string; email: string; role: string; status: string; createdAt: string };
const tenantHeaders = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });
const role = (value: string): Role => value.toLowerCase() as Role;
const tenant = (value: Workspace): Tenant => ({ id: value.tenantId, name: value.displayName, slug: value.slug, createdAt: "", role: role(value.role) });
const member = (value: Member): TeamMember => ({ user: { id: value.userId, displayName: value.displayName, email: value.email, createdAt: value.createdAt }, membership: { id: value.id, userId: value.userId, tenantId: selectedWorkspaceId() ?? "", role: role(value.role), joinedAt: value.createdAt, status: value.status.toLowerCase() as "active" | "suspended" | "revoked" } });

async function mutate(path: string, method: string, body?: unknown) { await apiFetch<void>(path, { method, body, headers: { ...tenantHeaders(), "X-CSRF-Token": await getCsrfToken() } }); }

export const apiIdentityClient: IdentityClient = {
  async getCurrentSession() { throw new Error("Session is assembled by the seller session loader."); },
  async getWorkspaces() { return (await apiFetch<Workspace[]>("/v1/workspaces")).map(tenant); },
  async getOnboardingState() { throw new Error("Onboarding remains fixture-backed."); },
  async getTeamMembers() { const items = await apiFetch<Member[]>("/v1/memberships", { headers: tenantHeaders() }); return { items: items.map(member), cursor: null, hasMore: false, totalCount: items.length } as PaginatedResult<TeamMember>; },
  async grantMember(email, memberRole) { await mutate("/v1/memberships", "POST", { email, role: memberRole }); },
  async changeMemberRole(id, memberRole) { await mutate(`/v1/memberships/${id}/role`, "PATCH", { role: memberRole }); },
  async suspendMember(id) { await mutate(`/v1/memberships/${id}/suspend`, "POST"); },
  async reactivateMember(id) { await mutate(`/v1/memberships/${id}/reactivate`, "POST"); },
  async revokeMember(id) { await mutate(`/v1/memberships/${id}`, "DELETE"); },
  async getPermissions() { return (await apiFetch<{ permissions: string[] }>("/v1/permissions", { headers: tenantHeaders() })).permissions; },
};
