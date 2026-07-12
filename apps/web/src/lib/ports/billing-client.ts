import type { Plan, Subscription, QuotaStatus, UsageEvent, PaginatedResult } from "@/lib/types";

export interface BillingClient {
  getPlan(tenantId: string): Promise<{ plan: Plan; subscription: Subscription }>;
  getUsage(tenantId: string): Promise<PaginatedResult<UsageEvent>>;
  getQuotaStatus(tenantId: string): Promise<QuotaStatus[]>;
}
