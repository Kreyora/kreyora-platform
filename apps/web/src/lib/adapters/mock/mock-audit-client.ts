import type { AuditClient } from "@/lib/ports/audit-client";
import type { PaginatedResult, AuditEvent } from "@/lib/types";
import { auditEvents } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockAuditClient: AuditClient = {
  async listAuditEvents(params) {
    await delay();

    let filtered = [...auditEvents];

    if (params?.resourceType) {
      filtered = filtered.filter((e) => e.resourceType === params.resourceType);
    }

    if (params?.action) {
      filtered = filtered.filter((e) => e.action === params.action);
    }

    return toPaginated<AuditEvent>(filtered);
  },
};
