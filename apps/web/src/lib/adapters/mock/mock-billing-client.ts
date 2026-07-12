import type { BillingClient } from "@/lib/ports/billing-client";
import type { PaginatedResult, UsageEvent } from "@/lib/types";
import { growPlan, subscription, usageEvents, quotaStatuses } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockBillingClient: BillingClient = {
  async getPlan(_tenantId: string) {
    await delay();
    return { plan: growPlan, subscription };
  },

  async getUsage(_tenantId: string) {
    await delay();
    return toPaginated<UsageEvent>(usageEvents);
  },

  async getQuotaStatus(_tenantId: string) {
    await delay();
    return quotaStatuses;
  },
};
