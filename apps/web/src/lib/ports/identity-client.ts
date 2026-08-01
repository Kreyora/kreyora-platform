import type { Session, Tenant, OnboardingState, TeamMember, PaginatedResult, Role } from "@/lib/types";

export interface IdentityClient {
  getCurrentSession(): Promise<Session>;
  getWorkspaces(): Promise<Tenant[]>;
  getOnboardingState(tenantId: string): Promise<OnboardingState>;
  getTeamMembers(tenantId: string): Promise<PaginatedResult<TeamMember>>;
  grantMember(email: string, role: Role): Promise<void>;
  changeMemberRole(id: string, role: Role): Promise<void>;
  suspendMember(id: string): Promise<void>;
  reactivateMember(id: string): Promise<void>;
  revokeMember(id: string): Promise<void>;
  getPermissions(): Promise<string[]>;
}
