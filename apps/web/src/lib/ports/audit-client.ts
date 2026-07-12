import type { AuditEvent, PaginatedResult } from "@/lib/types";

export interface AuditClient {
  listAuditEvents(params?: {
    resourceType?: string;
    action?: string;
    cursor?: string;
  }): Promise<PaginatedResult<AuditEvent>>;
}
