import type { Product, ProductVariant, Collection, PaginatedResult } from "@/lib/types";

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
}
