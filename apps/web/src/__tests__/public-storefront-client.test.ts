import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiPublicCheckoutClient, apiPublicStorefrontClient } from "@/lib/adapters/api/public-storefront-client";

describe("public storefront API adapters", () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => { vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030"); });
  afterEach(() => { globalThis.fetch = originalFetch; vi.unstubAllEnvs(); });

  it("uses the sanctioned development-slug route and anonymous credentials", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({ displayName: "Demo", platformSlug: "demo", tagline: null, contactEmail: null, contactPhone: null, contactWhatsApp: null, facebookUrl: null, instagramUrl: null, tikTokUrl: null }), headers: new Headers() });
    await apiPublicStorefrontClient.getStore("demo");
    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[0]).toBe("http://localhost:5030/public/v1/dev/stores/demo");
    expect(call[1].credentials).toBe("omit");
  });

  it("uses the host-bound route when no API origin is configured", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "");
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({ displayName: "Demo", platformSlug: "demo", tagline: null, contactEmail: null, contactPhone: null, contactWhatsApp: null, facebookUrl: null, instagramUrl: null, tikTokUrl: null }), headers: new Headers() });
    await apiPublicStorefrontClient.getStore("ignored-on-host");
    expect((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0]).toBe("/public/v1/store");
  });

  it("sends idempotency keys for public checkout writes", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({ id: "session-1", expiresAt: "2026-09-07T00:00:00Z", items: [], delivery: { name: "Delivery", feeNpr: 150, estimatedEtaText: null, codAvailable: true }, totals: { merchandiseSubtotalNpr: 100, discountNpr: 0, deliveryFeeNpr: 150, taxNpr: 0, totalNpr: 250, currency: "NPR" }, wasReplayed: false }), headers: new Headers() });
    await apiPublicCheckoutClient.createSession({ slug: "demo", quoteToken: "quote", idempotencyKey: "session-key", customer: { displayName: "Sita", phone: "9800000000", saveContact: false, privacyAcknowledged: true }, address: { addressLine1: "Ward 1", district: "Kathmandu" } });
    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[0]).toBe("http://localhost:5030/public/v1/dev/stores/demo/checkout/sessions");
    expect(call[1].headers["Idempotency-Key"]).toBe("session-key");
    expect(call[1].credentials).toBe("omit");
  });

  it("serializes only customer checkout intent and never browser-owned commerce facts", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({ quoteToken: "quote", expiresAt: "2026-09-07T00:00:00Z", delivery: { name: "Delivery", feeNpr: 150, estimatedEtaText: null, codAvailable: true }, totals: { merchandiseSubtotalNpr: 100, discountNpr: 0, deliveryFeeNpr: 150, taxNpr: 0, totalNpr: 250, currency: "NPR" } }), headers: new Headers() });

    await apiPublicCheckoutClient.createQuote({ slug: "demo", lines: [{ variantId: "variant-1", quantity: 2 }], destination: { countryCode: "NP", district: "Kathmandu" } });
    const quoteBody = JSON.parse((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][1].body);
    expect(quoteBody).toEqual({ lines: [{ variantId: "variant-1", quantity: 2 }], destination: { countryCode: "NP", district: "Kathmandu" } });

    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 201, json: () => Promise.resolve({ orderNumber: "NC-001", status: "pendingConfirmation", paymentStatus: "pending", fulfilmentStatus: "unfulfilled", paymentMethod: "cashOnDelivery", totalNpr: 250, currency: "NPR", wasReplayed: false }), headers: new Headers() });
    await apiPublicCheckoutClient.createCodOrder({ slug: "demo", checkoutSessionId: "session-1", idempotencyKey: "order-key" });
    const orderBody = JSON.parse((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][1].body);
    expect(orderBody).toEqual({ checkoutSessionId: "session-1" });
    expect(JSON.stringify({ quoteBody, orderBody })).not.toMatch(/tenant|store|price|total|delivery|payment|publication/i);
  });
});
