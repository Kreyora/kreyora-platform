import type { PublicCheckoutClient, PublicStorefrontClient } from "@/lib/ports/public-storefront-client";
import type { PublicCatalogProduct, PublicOrderConfirmation } from "@/lib/types/public-storefront";
import { allVariants, deliveryRules, products, store, TENANT_SLUG } from "../fixtures/data";

const delay = () => new Promise<void>((resolve) => setTimeout(resolve, 50));
const asPublicProduct = (value: typeof products[number]): PublicCatalogProduct => ({
  id: value.id,
  title: value.title,
  description: value.description,
  slug: value.slug,
  variants: value.variants.filter((variant) => variant.isPublished).map((variant) => ({ id: variant.id, name: variant.name, options: variant.options, priceNpr: variant.price.amount, compareAtPriceNpr: variant.compareAtPrice?.amount })),
  media: value.media.map((media) => ({ id: media.id, contentType: media.mimeType, altText: media.altText, sortOrder: media.sortOrder })),
});

function ensureStore(slug: string): void {
  if (slug !== TENANT_SLUG) throw new Error("Store not found");
}

export const mockPublicStorefrontClient: PublicStorefrontClient = {
  async getStore(slug) {
    await delay(); ensureStore(slug);
    return { displayName: store.profile.name, platformSlug: store.slug, tagline: store.profile.tagline, contactEmail: store.profile.contactEmail, contactPhone: store.profile.contactPhone, socialLinks: store.profile.socialLinks };
  },
  async listProducts(slug, query) {
    await delay(); ensureStore(slug);
    const q = query?.q?.toLowerCase();
    const items = products.filter((product) => product.publishState === "published" && (!q || product.title.toLowerCase().includes(q) || product.description.toLowerCase().includes(q))).map(asPublicProduct);
    return { items };
  },
  async getProduct(slug, productSlug) {
    await delay(); ensureStore(slug);
    const product = products.find((item) => item.slug === productSlug || item.id === productSlug);
    if (!product) throw new Error("Product not found");
    return asPublicProduct(product);
  },
  getMediaUrl() { return "/fixtures/product-placeholder.svg"; },
};

export const mockPublicCheckoutClient: PublicCheckoutClient = {
  async createQuote(input) {
    await delay(); ensureStore(input.slug);
    const subtotal = input.lines.reduce((total, line) => total + (allVariants.find((variant) => variant.id === line.variantId)?.price.amount ?? 0) * line.quantity, 0);
    const rule = deliveryRules.find((item) => item.isActive && item.codAvailable);
    const deliveryFee = rule?.feeType === "threshold" && rule.freeAbove && subtotal >= rule.freeAbove.amount ? 0 : rule?.flatFee?.amount ?? 150;
    return { quoteToken: `demo-quote-${Date.now()}`, expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(), delivery: { name: rule?.name ?? "Delivery", feeNpr: deliveryFee, estimatedEtaText: rule?.estimatedDays, codAvailable: Boolean(rule) }, totals: { merchandiseSubtotalNpr: subtotal, discountNpr: 0, deliveryFeeNpr: deliveryFee, taxNpr: 0, totalNpr: subtotal + deliveryFee, currency: "NPR" } };
  },
  async createSession(input) {
    await delay(); ensureStore(input.slug);
    if (!input.customer.privacyAcknowledged) throw new Error("Privacy acknowledgement is required");
    return { id: `demo-session-${input.idempotencyKey}`, expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(), delivery: { name: "Demo delivery", feeNpr: 150, codAvailable: true }, totals: { merchandiseSubtotalNpr: 0, discountNpr: 0, deliveryFeeNpr: 150, taxNpr: 0, totalNpr: 150, currency: "NPR" }, wasReplayed: false };
  },
  async createCodOrder(input) {
    await delay(); ensureStore(input.slug);
    return { orderNumber: "NC-2025-0099", status: "pending_confirmation", paymentStatus: "pending", fulfilmentStatus: "unfulfilled", paymentMethod: "cod", totalNpr: 0, currency: "NPR", wasReplayed: false } satisfies PublicOrderConfirmation;
  },
};
