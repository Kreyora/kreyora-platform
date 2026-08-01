import type { IdentityClient } from "@/lib/ports/identity-client";
import type { PaginatedResult, TeamMember } from "@/lib/types";
import { session, tenant, onboardingState, teamMembers } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockIdentityClient: IdentityClient = {
  async getCurrentSession() {
    await delay();
    return session;
  },

  async getWorkspaces() {
    await delay();
    return [tenant];
  },

  async getOnboardingState(_tenantId: string) {
    await delay();
    return onboardingState;
  },

  async getTeamMembers(_tenantId: string) {
    await delay();
    return toPaginated<TeamMember>(teamMembers);
  },
  async grantMember() {}, async changeMemberRole() {}, async suspendMember() {}, async reactivateMember() {}, async revokeMember() {},
  async getPermissions() { return ["memberships.manage", "audit.read", "billing.manage", "settings.write"]; },
};
