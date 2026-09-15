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

  it("maps apiOrderClient list and detail responses with rowVersion", async () => {
    const { apiOrderClient } = await import("@/lib/adapters/api/order-client");

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: () => Promise.resolve({
        id: "order-123",
        orderNumber: "ORD-001",
        storeId: "store-1",
        checkoutSessionId: "cs-1",
        status: "PendingConfirmation",
        paymentStatus: "Pending",
        fulfilmentStatus: "Unfulfilled",
        paymentMethod: "CashOnDelivery",
        source: "Storefront",
        customerName: "Binod Basnet",
        customerPhone: "+9779801234567",
        customerEmail: "binod@example.com",
        addressLine1: "Kathmandu",
        district: "Kathmandu",
        merchandiseSubtotalNpr: 1500,
        discountNpr: 0,
        deliveryFeeNpr: 100,
        taxNpr: 0,
        totalNpr: 1600,
        currency: "NPR",
        deliveryRuleId: "dr-1",
        deliveryRuleName: "Standard",
        codAvailable: true,
        rowVersion: 42,
        createdAt: "2026-09-15T00:00:00Z",
        items: [{
          id: "item-1",
          productId: "prod-1",
          productTitle: "Test Product",
          variantId: "var-1",
          variantName: "Default",
          unitPriceNpr: 1500,
          quantity: 1,
          lineTotalNpr: 1500,
          currency: "NPR",
        }],
      }),
    });

    const order = await apiOrderClient.getOrder("order-123");
    expect(order.id).toBe("order-123");
    expect(order.status).toBe("pending_confirmation");
    expect(order.rowVersion).toBe(42);
    expect(order.items.length).toBe(1);
    expect(order.items[0].productTitle).toBe("Test Product");
  });

  it("constructs correct proof content URL via apiPaymentClient", async () => {
    const { apiPaymentClient } = await import("@/lib/adapters/api/payment-client");

    const url = apiPaymentClient.getProofContentUrl("proof-abc-123");
    expect(url).toBe("http://localhost:5030/v1/payments/proofs/proof-abc-123/content");
  });
});
