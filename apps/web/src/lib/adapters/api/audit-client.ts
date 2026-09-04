import { apiFetch } from "@/lib/api";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { AuditClient } from "@/lib/ports/audit-client";
import type { AuditEvent, PaginatedResult } from "@/lib/types";

export const apiAuditClient: AuditClient = {
  async listAuditEvents(params) {
    const query = new URLSearchParams(); if (params?.cursor) query.set("cursor", params.cursor);
    const page = await apiFetch<{ items: Array<{ id: string; actorUserId: string | null; actorKind: "member" | "commerceSystem"; action: string; targetType: string; targetId: string; occurredAt: string; correlationId: string; metadata: string | null }>; nextCursor: string | null }>(`/v1/audit-events${query.size ? `?${query}` : ""}`, { headers: { "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" } });
    const events: AuditEvent[] = page.items.map((item) => {
      const isCommerceSystem = item.actorKind === "commerceSystem";
      const actorId = item.actorUserId ?? "commerce-system";

      return {
        id: item.id,
        tenantId: selectedWorkspaceId() ?? "",
        actor: {
          id: actorId,
          name: isCommerceSystem ? "Commerce system" : `User ${actorId.slice(0, 8)}`,
          role: isCommerceSystem ? "system" : "unknown",
          type: isCommerceSystem ? "system" : "user",
        },
        action: item.action,
        resourceType: item.targetType,
        resourceId: item.targetId,
        details: item.metadata ? JSON.parse(item.metadata) as Record<string, string> : {},
        correlationId: item.correlationId,
        createdAt: item.occurredAt,
      };
    });
    const filtered = events.filter((item) => (!params?.action || item.action === params.action) && (!params?.resourceType || item.resourceType === params.resourceType));
    return { items: filtered, cursor: page.nextCursor, hasMore: page.nextCursor !== null, totalCount: filtered.length } as PaginatedResult<AuditEvent>;
  },
};
