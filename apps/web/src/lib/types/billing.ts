import type { TenantId, Timestamp, Money } from "./common";

export interface Plan {
  id: string;
  name: string;
  monthlyPrice: Money;
  limits: {
    products: number;
    aiCredits: number;
    ordersPerMonth: number;
    socialIntegrations: number;
    teamSeats: number;
  };
  platformFeePercent: number;
}

export interface Subscription {
  id: string;
  tenantId: TenantId;
  planId: string;
  planName: string;
  status: "active" | "cancelled" | "past_due";
  currentPeriodStart: Timestamp;
  currentPeriodEnd: Timestamp;
}

export interface Entitlement {
  feature: string;
  limit: number;
  used: number;
  remaining: number;
}

export type QuotaLevel = "normal" | "warning_70" | "warning_90" | "exceeded";

export interface QuotaStatus {
  metric: string;
  limit: number;
  used: number;
  level: QuotaLevel;
  percentUsed: number;
}

export interface UsageEvent {
  id: string;
  tenantId: TenantId;
  metric: string;
  quantity: number;
  source: string;
  idempotencyKey: string;
  createdAt: Timestamp;
}
