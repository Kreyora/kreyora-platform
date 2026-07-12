import type { TenantId, Timestamp } from "./common";

export type Role = "owner" | "admin" | "operator" | "viewer" | "platform_support";

export interface User {
  id: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
  createdAt: Timestamp;
}

export interface Tenant {
  id: TenantId;
  name: string;
  slug: string;
  createdAt: Timestamp;
}

export interface Membership {
  id: string;
  userId: string;
  tenantId: TenantId;
  role: Role;
  joinedAt: Timestamp;
}

export interface Session {
  user: User;
  tenant: Tenant;
  membership: Membership;
}

export type OnboardingStep =
  | "store_profile"
  | "catalog_readiness"
  | "delivery_rules"
  | "payment_setup"
  | "channel_connection"
  | "assistant_policy"
  | "activation_review";

export type OnboardingStepStatus = "completed" | "incomplete" | "blocked" | "permission_denied";

export interface OnboardingState {
  steps: Array<{
    step: OnboardingStep;
    status: OnboardingStepStatus;
    completedAt?: Timestamp;
  }>;
  isActivationReady: boolean;
}

export interface TeamMember {
  user: User;
  membership: Membership;
}
