import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicAssistantLink } from "@/lib/types/public-storefront";
import { apiPublicCheckoutClient } from "@/lib/adapters/api/public-storefront-client";
import { clearAssistantLink, readAssistantLink, saveAssistantLink } from "@/lib/storefront/assistant-link";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useParams: () => ({ slug: "demo", token: "tok_123" }),
  useRouter: () => ({ replace, push: vi.fn() }),
  usePathname: () => "/store/demo/link/tok_123",
}));

const getAssistantLink = vi.fn<(input: { slug: string; token: string }) => Promise<PublicAssistantLink>>();
vi.mock("@/lib/providers/client-provider", () => ({ usePublicCheckoutClient: () => ({ getAssistantLink }) }));

const replaceItems = vi.fn();
vi.mock("@/hooks/use-cart", () => ({ useCart: () => ({ replaceItems }) }));

import AssistantLinkPage from "@/app/(storefront)/store/[slug]/link/[token]/page";

const line = (overrides: Partial<PublicAssistantLink["items"][number]> = {}) => ({
  productId: "p1", productSlug: "red-kurta", productTitle: "Red Kurta", variantId: "v1", variantName: "Small", quantity: 2, unitPriceNpr: 2500, available: true, ...overrides,
});

describe("M09-S05 assistant checkout link landing page", () => {
  beforeEach(() => { replace.mockReset(); replaceItems.mockReset(); getAssistantLink.mockReset(); sessionStorage.clear(); });

  it("fills the cart with the link's items at server prices and opens the normal checkout", async () => {
    getAssistantLink.mockResolvedValue({ expiresAt: new Date(Date.now() + 3_600_000).toISOString(), items: [line()] });
    render(<AssistantLinkPage />);

    expect(screen.getByRole("status")).toHaveTextContent("Preparing your cart");
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/store/demo/checkout"));
    expect(getAssistantLink).toHaveBeenCalledWith({ slug: "demo", token: "tok_123" });
    expect(replaceItems).toHaveBeenCalledWith([{ variantId: "v1", productTitle: "Red Kurta", productSlug: "red-kurta", variantName: "Small", unitPriceNpr: 2500, quantity: 2 }]);
    expect(readAssistantLink("demo")).toBe("tok_123");
  });

  it("lists unavailable items and lets the customer continue with the rest", async () => {
    getAssistantLink.mockResolvedValue({ expiresAt: new Date(Date.now() + 3_600_000).toISOString(), items: [line(), line({ variantId: "v2", variantName: "Large", available: false })] });
    render(<AssistantLinkPage />);

    expect(await screen.findByText("Some items are no longer available")).toBeInTheDocument();
    expect(screen.getByText("Red Kurta · Large")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Continue with available items" }));
    expect(replaceItems).toHaveBeenCalledWith([expect.objectContaining({ variantId: "v1" })]);
    expect(replace).toHaveBeenCalledWith("/store/demo/checkout");
  });

  it("explains an expired or invalid link without touching the cart", async () => {
    getAssistantLink.mockRejectedValue(new Error("404"));
    render(<AssistantLinkPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent("expired or is not valid");
    expect(replaceItems).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "Browse the store" })).toHaveAttribute("href", "/store/demo");
  });

  it("refuses a link whose items are all unavailable", async () => {
    getAssistantLink.mockResolvedValue({ expiresAt: new Date(Date.now() + 3_600_000).toISOString(), items: [line({ available: false })] });
    render(<AssistantLinkPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent("no longer available");
    expect(replaceItems).not.toHaveBeenCalled();
  });
});

describe("M09-S05 assistant link storage", () => {
  beforeEach(() => sessionStorage.clear());

  it("keeps the token only until it expires and clears it after ordering", () => {
    saveAssistantLink("demo", "tok", new Date(Date.now() + 60_000).toISOString());
    expect(readAssistantLink("demo")).toBe("tok");
    expect(readAssistantLink("demo", new Date(Date.now() + 120_000))).toBeUndefined();
    expect(readAssistantLink("other")).toBeUndefined();
    clearAssistantLink("demo");
    expect(readAssistantLink("demo")).toBeUndefined();
  });
});

describe("M09-S05 public checkout adapter", () => {
  const originalFetch = globalThis.fetch;
  beforeEach(() => { vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030"); });
  afterEach(() => { globalThis.fetch = originalFetch; vi.unstubAllEnvs(); });

  it("reads a link anonymously from the store's link route", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve({ expiresAt: "2026-10-08T00:00:00Z", items: [line()] }), headers: new Headers() });
    const link = await apiPublicCheckoutClient.getAssistantLink({ slug: "demo", token: "tok/../x" });
    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[0]).toBe("http://localhost:5030/public/v1/dev/stores/demo/assistant-links/tok%2F..%2Fx");
    expect(call[1].credentials).toBe("omit");
    expect(link.items[0].unitPriceNpr).toBe(2500);
  });

  it("sends the link token with quote and session only when there is one", async () => {
    const quoteResponse = { quoteToken: "q", expiresAt: "2026-10-08T00:00:00Z", delivery: { name: "D", feeNpr: 100, estimatedEtaText: null, codAvailable: true }, totals: { merchandiseSubtotalNpr: 100, discountNpr: 0, deliveryFeeNpr: 100, taxNpr: 0, totalNpr: 200, currency: "NPR" } };
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(quoteResponse), headers: new Headers() });
    await apiPublicCheckoutClient.createQuote({ slug: "demo", lines: [{ variantId: "v1", quantity: 1 }], destination: { countryCode: "NP", district: "Kathmandu" }, assistantLinkToken: "tok" });
    await apiPublicCheckoutClient.createQuote({ slug: "demo", lines: [{ variantId: "v1", quantity: 1 }], destination: { countryCode: "NP", district: "Kathmandu" } });
    const bodies = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls.map((call) => JSON.parse(call[1].body));
    expect(bodies[0].assistantLinkToken).toBe("tok");
    expect("assistantLinkToken" in bodies[1]).toBe(false);
  });
});
