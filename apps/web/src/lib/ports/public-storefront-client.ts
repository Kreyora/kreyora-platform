import type { PublicCatalogPage, PublicCatalogProduct, PublicCheckoutAddress, PublicCheckoutCustomer, PublicCheckoutSession, PublicDeliveryQuote, PublicDestination, PublicOrderConfirmation, PublicStorefront } from "@/lib/types/public-storefront";

export interface PublicStorefrontClient {
  getStore(slug: string): Promise<PublicStorefront>;
  listProducts(slug: string, query?: { q?: string; cursor?: string }): Promise<PublicCatalogPage>;
  getProduct(slug: string, productSlug: string): Promise<PublicCatalogProduct>;
  getMediaUrl(slug: string, mediaId: string): string;
}

export interface PublicCheckoutClient {
  createQuote(input: { slug: string; lines: Array<{ variantId: string; quantity: number }>; destination: PublicDestination }): Promise<PublicDeliveryQuote>;
  createSession(input: { slug: string; quoteToken: string; customer: PublicCheckoutCustomer; address: PublicCheckoutAddress; idempotencyKey: string }): Promise<PublicCheckoutSession>;
  createCodOrder(input: { slug: string; checkoutSessionId: string; idempotencyKey: string }): Promise<PublicOrderConfirmation>;
}
