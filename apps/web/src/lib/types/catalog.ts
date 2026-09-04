import type { TenantId, Timestamp, Money } from "./common";

export type PublishState = "draft" | "published" | "unpublished" | "archived";

export interface MediaAsset {
  id: string;
  url: string;
  altText?: string;
  width: number;
  height: number;
  mimeType: string;
  sortOrder: number;
}

export interface ProductVariant {
  id: string;
  productId: string;
  sku: string;
  name: string;
  options: Record<string, string>;
  price: Money;
  compareAtPrice?: Money;
  isPublished: boolean;
  createdAt: Timestamp;
  updatedAt: Timestamp;
}

export interface Product {
  id: string;
  tenantId: TenantId;
  title: string;
  description: string;
  slug: string;
  publishState: PublishState;
  variants: ProductVariant[];
  media: MediaAsset[];
  collections: string[];
  tags: string[];
  createdAt: Timestamp;
  updatedAt: Timestamp;
  /** PostgreSQL concurrency token returned by the API; omitted by fixture data. */
  version?: number;
}

export interface Collection {
  id: string;
  tenantId: TenantId;
  name: string;
  slug: string;
  description?: string;
  productCount: number;
  sortOrder: number;
}
