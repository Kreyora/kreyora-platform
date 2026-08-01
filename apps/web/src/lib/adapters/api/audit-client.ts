import { apiFetch } from "@/lib/api";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { AuditClient } from "@/lib/ports/audit-client";
import type { AuditEvent, PaginatedResult } from "@/lib/types";

export const apiAuditClient: AuditClient = {
  async listAuditEvents(params) {
    const query = new URLSearchParams(); if (params?.cursor) query.set("cursor", params.cursor);
    const page = await apiFetch<{ items: Array<{ id: string; actorUserId: string; action: string; targetType: string; targetId: string; occurredAt: string; correlationId: string; metadata: string | null }>; nextCursor: string | null }>(`/v1/audit-events${query.size ? `?${query}` : ""}`, { headers: { "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" } });
    const events: AuditEvent[] = page.items.map((item) => ({ id: item.id, tenantId: selectedWorkspaceId() ?? "", actor: { id: item.actorUserId, name: `User ${item.actorUserId.slice(0, 8)}`, role: "unknown", type: "user" }, action: item.action, resourceType: item.targetType, resourceId: item.targetId, details: item.metadata ? JSON.parse(item.metadata) as Record<string, string> : {}, correlationId: item.correlationId, createdAt: item.occurredAt }));
    const filtered = events.filter((item) => (!params?.action || item.action === params.action) && (!params?.resourceType || item.resourceType === params.resourceType));
    return { items: filtered, cursor: page.nextCursor, hasMore: page.nextCursor !== null, totalCount: filtered.length } as PaginatedResult<AuditEvent>;
  },
};
