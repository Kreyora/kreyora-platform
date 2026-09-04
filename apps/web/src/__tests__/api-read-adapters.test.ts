import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiAuditClient } from "@/lib/adapters/api/audit-client";
import { apiInventoryClient } from "@/lib/adapters/api/inventory-client";
import { clearSelectedWorkspace, selectWorkspace } from "@/lib/session/workspace-selection";

describe("API read adapters", () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030");
    selectWorkspace("tenant-1");
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    clearSelectedWorkspace();
    vi.unstubAllEnvs();
  });

  it("renders a commerce-system audit event without a user actor", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: () => Promise.resolve({
        items: [{
          id: "audit-1",
          actorUserId: null,
          actorKind: "commerceSystem",
          action: "order.created",
          targetType: "order",
          targetId: "order-1",
          occurredAt: "2026-09-05T00:00:00Z",
          correlationId: "corr-1",
          metadata: "{\"checkoutSessionId\":\"session-1\"}",
        }],
        nextCursor: null,
      }),
    });

    const result = await apiAuditClient.listAuditEvents();

    expect(result.items[0].actor).toEqual({
      id: "commerce-system",
      name: "Commerce system",
      role: "system",
      type: "system",
    });
  });

  it("maps a commerce-system stock movement to a stable internal actor id", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: () => Promise.resolve({
        items: [{
          id: "movement-1",
          inventoryItemId: "inventory-1",
          type: "commitment",
          quantityDelta: -2,
          reason: "Checkout reservation committed to order",
          actorUserId: null,
          actorKind: "commerceSystem",
          createdAt: "2026-09-05T00:00:00Z",
        }],
        nextCursor: null,
      }),
    });

    const result = await apiInventoryClient.getStockMovements("variant-1");

    expect(result.items[0].actorId).toBe("commerce-system");
  });
});
