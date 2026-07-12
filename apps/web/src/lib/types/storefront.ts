import type { TenantId, Timestamp, Money } from "./common";

export interface StoreProfile {
  name: string;
  tagline?: string;
  description?: string;
  logoUrl?: string;
  bannerUrl?: string;
  contactEmail?: string;
  contactPhone?: string;
  socialLinks: Record<string, string>;
}

export interface ThemeSettings {
  accentColor?: string;
  logoUrl?: string;
  bannerUrl?: string;
}

export interface DeliveryRule {
  id: string;
  tenantId: TenantId;
  name: string;
  zones: string[];
  feeType: "flat" | "threshold";
  flatFee?: Money;
  freeAbove?: Money;
  estimatedDays?: string;
  codAvailable: boolean;
  isActive: boolean;
}

export interface StoreReadiness {
  hasProfile: boolean;
  hasPublishedProducts: boolean;
  hasDeliveryRules: boolean;
  hasPaymentMethods: boolean;
  isReady: boolean;
  blockers: string[];
}

export interface Store {
  id: string;
  tenantId: TenantId;
  slug: string;
  profile: StoreProfile;
  theme: ThemeSettings;
  readiness: StoreReadiness;
  isPublished: boolean;
  publishedAt?: Timestamp;
  createdAt: Timestamp;
  updatedAt: Timestamp;
}
