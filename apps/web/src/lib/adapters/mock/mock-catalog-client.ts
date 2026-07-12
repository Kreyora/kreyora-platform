import type { CatalogClient } from "@/lib/ports/catalog-client";
import type { PaginatedResult, Product } from "@/lib/types";
import { products, collections } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockCatalogClient: CatalogClient = {
  async listProducts(params) {
    await delay();

    let filtered = [...products];

    if (params?.search) {
      const query = params.search.toLowerCase();
      filtered = filtered.filter(
        (p) =>
          p.title.toLowerCase().includes(query) ||
          p.description.toLowerCase().includes(query) ||
          p.tags.some((t) => t.toLowerCase().includes(query)),
      );
    }

    if (params?.collection) {
      filtered = filtered.filter((p) => p.collections.includes(params.collection!));
    }

    if (params?.publishState) {
      filtered = filtered.filter((p) => p.publishState === params.publishState);
    }

    return toPaginated<Product>(filtered);
  },

  async getProduct(id: string) {
    await delay();
    const product = products.find((p) => p.id === id);
    if (!product) {
      throw new Error(`Product not found: ${id}`);
    }
    return product;
  },

  async getVariants(productId: string) {
    await delay();
    const product = products.find((p) => p.id === productId);
    if (!product) {
      throw new Error(`Product not found: ${productId}`);
    }
    return product.variants;
  },

  async getCollections() {
    await delay();
    return collections;
  },
};
