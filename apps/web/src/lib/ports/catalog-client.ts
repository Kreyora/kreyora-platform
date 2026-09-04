import type { Product, ProductVariant, Collection, PaginatedResult } from "@/lib/types";

export interface ProductVariantInput {
  sku: string;
  name: string;
  options?: Record<string, string>;
  priceNpr: number;
  compareAtPriceNpr?: number;
  isPublished: boolean;
}

export interface ProductInput {
  title: string;
  description?: string;
  slug: string;
}

export interface CatalogClient {
  listProducts(params?: {
    search?: string;
    collection?: string;
    publishState?: string;
    cursor?: string;
  }): Promise<PaginatedResult<Product>>;
  getProduct(id: string): Promise<Product>;
  getVariants(productId: string): Promise<ProductVariant[]>;
  getCollections(): Promise<Collection[]>;
  createProduct(input: ProductInput & { variants: ProductVariantInput[] }): Promise<Product>;
  updateProduct(product: Product, input: ProductInput): Promise<Product>;
  setPublication(product: Product, state: "published" | "unpublished"): Promise<Product>;
  archiveProduct(product: Product): Promise<Product>;
  addVariant(product: Product, input: ProductVariantInput): Promise<Product>;
  uploadMedia(productId: string, file: File, altText?: string): Promise<Product>;
  deleteMedia(productId: string, mediaId: string): Promise<Product>;
}
