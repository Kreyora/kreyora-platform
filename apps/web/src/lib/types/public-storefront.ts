export interface PublicStorefront {
  displayName: string;
  platformSlug: string;
  tagline?: string;
  contactEmail?: string;
  contactPhone?: string;
  contactWhatsApp?: string;
  socialLinks: Record<string, string>;
}

export interface PublicMediaAsset { id: string; contentType: string; altText?: string; sortOrder: number; }

export interface PublicCatalogVariant {
  id: string;
  name: string;
  options: Record<string, string>;
  priceNpr: number;
  compareAtPriceNpr?: number;
}

export interface PublicCatalogProduct {
  id: string;
  title: string;
  description?: string;
  slug: string;
  variants: PublicCatalogVariant[];
  media: PublicMediaAsset[];
}

export interface PublicCatalogPage { items: PublicCatalogProduct[]; nextCursor?: string; }

export interface PublicCartItem {
  variantId: string;
  productTitle: string;
  productSlug: string;
  variantName: string;
  imageId?: string;
  imageAlt?: string;
  unitPriceNpr: number;
  quantity: number;
}

export interface PublicDestination { countryCode: string; district: string; municipality?: string; locality?: string; }
export interface PublicCheckoutCustomer { displayName: string; phone: string; email?: string; saveContact: boolean; privacyAcknowledged: boolean; }
export interface PublicCheckoutAddress { addressLine1: string; addressLine2?: string; district: string; municipality?: string; locality?: string; landmark?: string; }

export interface PublicTotals {
  merchandiseSubtotalNpr: number;
  discountNpr: number;
  deliveryFeeNpr: number;
  taxNpr: number;
  totalNpr: number;
  currency: string;
}

export interface PublicDeliveryOption { name: string; feeNpr: number; estimatedEtaText?: string; codAvailable: boolean; }
export interface PublicDeliveryQuote { quoteToken: string; expiresAt: string; delivery: PublicDeliveryOption; totals: PublicTotals; }
export interface PublicCheckoutSession { id: string; expiresAt: string; delivery: PublicDeliveryOption; totals: PublicTotals; wasReplayed: boolean; }
export interface PublicOrderConfirmation { orderNumber: string; status: string | number; paymentStatus: string | number; fulfilmentStatus: string | number; paymentMethod: string | number; totalNpr: number; currency: string; wasReplayed: boolean; }
