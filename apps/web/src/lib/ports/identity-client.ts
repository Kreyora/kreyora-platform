import type { Session, Tenant, OnboardingState, TeamMember, PaginatedResult } from "@/lib/types";

export interface IdentityClient {
  getCurrentSession(): Promise<Session>;
  getWorkspaces(): Promise<Tenant[]>;
  getOnboardingState(tenantId: string): Promise<OnboardingState>;
  getTeamMembers(tenantId: string): Promise<PaginatedResult<TeamMember>>;
}
