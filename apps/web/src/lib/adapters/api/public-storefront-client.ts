import { apiFetch } from "@/lib/api";
import type { components } from "@/lib/api/generated/v1";
import type { PublicCheckoutClient, PublicStorefrontClient } from "@/lib/ports/public-storefront-client";
import type { PublicCatalogPage, PublicCatalogProduct, PublicCheckoutSession, PublicDeliveryQuote, PublicOrderConfirmation, PublicStorefront, PublicTotals } from "@/lib/types/public-storefront";

type Schemas = components["schemas"];
const asNumber = (value: number | string | null | undefined) => typeof value === "number" ? value : Number(value ?? 0);

function target(slug: string, suffix: string): { baseUrl: string; path: string } {
  const apiUrl = process.env.NEXT_PUBLIC_API_URL;
  return apiUrl
    ? { baseUrl: apiUrl, path: `/public/v1/dev/stores/${encodeURIComponent(slug)}${suffix}` }
    : { baseUrl: "", path: `/public/v1/store${suffix}` };
}

function mapTotals(value: Schemas["StorefrontQuoteTotals"]): PublicTotals {
  return { merchandiseSubtotalNpr: asNumber(value.merchandiseSubtotalNpr), discountNpr: asNumber(value.discountNpr), deliveryFeeNpr: asNumber(value.deliveryFeeNpr), taxNpr: asNumber(value.taxNpr), totalNpr: asNumber(value.totalNpr), currency: value.currency };
}

function mapProduct(value: Schemas["PublicCatalogProduct"]): PublicCatalogProduct {
  return { id: value.id, title: value.title, description: value.description ?? undefined, slug: value.slug, variants: value.variants.map((variant) => ({ id: variant.id, name: variant.name, options: variant.options, priceNpr: asNumber(variant.priceNpr), compareAtPriceNpr: variant.compareAtPriceNpr == null ? undefined : asNumber(variant.compareAtPriceNpr) })), media: value.media.map((media) => ({ id: media.id, contentType: media.contentType, altText: media.altText ?? undefined, sortOrder: asNumber(media.sortOrder) })) };
}

function request<T>(slug: string, suffix: string, options?: Parameters<typeof apiFetch<T>>[1]): Promise<T> {
  const resolved = target(slug, suffix);
  return apiFetch<T>(resolved.path, { ...options, baseUrl: resolved.baseUrl, credentials: "omit" });
}

export const apiPublicStorefrontClient: PublicStorefrontClient = {
  async getStore(slug) {
    const value = await request<Schemas["PublicStorefront"]>(slug, "");
    return { displayName: value.displayName, platformSlug: value.platformSlug, tagline: value.tagline ?? undefined, contactEmail: value.contactEmail ?? undefined, contactPhone: value.contactPhone ?? undefined, contactWhatsApp: value.contactWhatsApp ?? undefined, socialLinks: Object.fromEntries([["facebook", value.facebookUrl], ["instagram", value.instagramUrl], ["tiktok", value.tikTokUrl]].filter((entry): entry is [string, string] => Boolean(entry[1]))) } satisfies PublicStorefront;
  },
  async listProducts(slug, query) {
    const params = new URLSearchParams();
    if (query?.q) params.set("q", query.q);
    if (query?.cursor) params.set("cursor", query.cursor);
    const value = await request<Schemas["PublicCatalogPage"]>(slug, `/products${params.size > 0 ? `?${params}` : ""}`);
    return { items: value.items.map(mapProduct), nextCursor: value.nextCursor ?? undefined } satisfies PublicCatalogPage;
  },
  async getProduct(slug, productSlug) { return mapProduct(await request<Schemas["PublicCatalogProduct"]>(slug, `/products/${encodeURIComponent(productSlug)}`)); },
  getMediaUrl(slug, mediaId) { const resolved = target(slug, `/media/${encodeURIComponent(mediaId)}`); return `${resolved.baseUrl}${resolved.path}`; },
};

export const apiPublicCheckoutClient: PublicCheckoutClient = {
  async createQuote(input) {
    const value = await request<Schemas["PublicDeliveryQuote"]>(input.slug, "/checkout/quotes", { method: "POST", body: { lines: input.lines, destination: input.destination } });
    return { quoteToken: value.quoteToken, expiresAt: value.expiresAt, delivery: { name: value.delivery.name, feeNpr: asNumber(value.delivery.feeNpr), estimatedEtaText: value.delivery.estimatedEtaText ?? undefined, codAvailable: value.delivery.codAvailable }, totals: mapTotals(value.totals) } satisfies PublicDeliveryQuote;
  },
  async createSession(input) {
    const value = await request<Schemas["PublicCheckoutSession"]>(input.slug, "/checkout/sessions", { method: "POST", headers: { "Idempotency-Key": input.idempotencyKey }, body: { quoteToken: input.quoteToken, customer: input.customer, address: input.address } });
    return { id: value.id, expiresAt: value.expiresAt, delivery: { name: value.delivery.name, feeNpr: asNumber(value.delivery.feeNpr), estimatedEtaText: value.delivery.estimatedEtaText ?? undefined, codAvailable: value.delivery.codAvailable }, totals: mapTotals(value.totals), wasReplayed: value.wasReplayed } satisfies PublicCheckoutSession;
  },
  async createCodOrder(input) {
    const value = await request<Schemas["PublicOrderConfirmation"]>(input.slug, "/checkout/orders", { method: "POST", headers: { "Idempotency-Key": input.idempotencyKey }, body: { checkoutSessionId: input.checkoutSessionId } });
    return { orderNumber: value.orderNumber, status: value.status, paymentStatus: value.paymentStatus, fulfilmentStatus: value.fulfilmentStatus, paymentMethod: value.paymentMethod, totalNpr: asNumber(value.totalNpr), currency: value.currency, wasReplayed: value.wasReplayed } satisfies PublicOrderConfirmation;
  },
};
