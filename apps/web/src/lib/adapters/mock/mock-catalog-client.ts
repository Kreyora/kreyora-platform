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

  async createProduct(input) {
    await delay();
    return {
      id: `demo-product-${Date.now()}`,
      tenantId: "demo-tenant",
      title: input.title,
      description: input.description ?? "",
      slug: input.slug,
      publishState: "draft",
      variants: input.variants.map((variant, index) => ({
        id: `demo-variant-${index}`,
        productId: "demo-product",
        sku: variant.sku,
        name: variant.name,
        options: variant.options ?? {},
        price: { amount: variant.priceNpr, currency: "NPR" },
        compareAtPrice: variant.compareAtPriceNpr ? { amount: variant.compareAtPriceNpr, currency: "NPR" } : undefined,
        isPublished: variant.isPublished,
        createdAt: "",
        updatedAt: "",
      })),
      media: [], collections: [], tags: [], createdAt: "", updatedAt: "",
    };
  },

  async updateProduct(current, input) {
    await delay();
    return { ...current, ...input, description: input.description ?? "" };
  },

  async setPublication(current, state) {
    await delay();
    return { ...current, publishState: state };
  },

  async archiveProduct(current) {
    await delay();
    return { ...current, publishState: "archived" };
  },

  async addVariant(current, input) {
    await delay();
    return {
      ...current,
      variants: [...current.variants, {
        id: `demo-variant-${Date.now()}`, productId: current.id, sku: input.sku, name: input.name,
        options: input.options ?? {}, price: { amount: input.priceNpr, currency: "NPR" },
        compareAtPrice: input.compareAtPriceNpr ? { amount: input.compareAtPriceNpr, currency: "NPR" } : undefined,
        isPublished: input.isPublished, createdAt: "", updatedAt: "",
      }],
    };
  },

  async uploadMedia(productId, file, altText) {
    await delay();
    const current = await this.getProduct(productId);
    return { ...current, media: [...current.media, { id: `demo-media-${Date.now()}`, url: URL.createObjectURL(file), altText, width: 0, height: 0, mimeType: file.type, sortOrder: current.media.length }] };
  },

  async deleteMedia(productId, mediaId) {
    await delay();
    const current = await this.getProduct(productId);
    return { ...current, media: current.media.filter((item) => item.id !== mediaId) };
  },
};
