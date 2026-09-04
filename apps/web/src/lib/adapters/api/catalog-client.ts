import { apiFetch } from "@/lib/api";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { CatalogClient, ProductVariantInput } from "@/lib/ports/catalog-client";
import type { MediaAsset, PaginatedResult, Product } from "@/lib/types";

type ApiProduct = { id: string; tenantId: string; title: string; description?: string; slug: string; publishState: string; version: number; variants: Array<{ id: string; sku: string; name: string; options: Record<string, string>; priceNpr: number; compareAtPriceNpr?: number; isPublished: boolean }> };
type ApiMedia = { id: string; productId?: string; contentType: string; state: string; sortOrder?: number; altText?: string };
const headers = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });

async function write<T>(path: string, method: "POST" | "PUT" | "DELETE", body?: unknown): Promise<T> {
  return apiFetch<T>(path, { method, body, headers: { ...headers(), "X-CSRF-Token": await getCsrfToken() } });
}

function media(value: ApiMedia): MediaAsset {
  return { id: value.id, url: `${process.env.NEXT_PUBLIC_API_URL ?? ""}/v1/media/${value.id}/content`, altText: value.altText, width: 0, height: 0, mimeType: value.contentType, sortOrder: value.sortOrder ?? 0 };
}

function product(value: ApiProduct, mediaItems: MediaAsset[] = []): Product {
  return {
    id: value.id, tenantId: value.tenantId, title: value.title, description: value.description ?? "", slug: value.slug,
    publishState: value.publishState.toLowerCase() as Product["publishState"],
    variants: value.variants.map((item) => ({ id: item.id, productId: value.id, sku: item.sku, name: item.name, options: item.options, price: { amount: item.priceNpr, currency: "NPR" }, compareAtPrice: item.compareAtPriceNpr ? { amount: item.compareAtPriceNpr, currency: "NPR" } : undefined, isPublished: item.isPublished, createdAt: "", updatedAt: "" })),
    media: mediaItems, collections: [], tags: [], createdAt: "", updatedAt: "", version: value.version,
  } as Product;
}

async function loadMedia(productId: string): Promise<MediaAsset[]> {
  const items = await apiFetch<ApiMedia[]>(`/v1/media/products/${productId}`, { headers: headers() });
  return items.filter((item) => item.state.toLowerCase() === "ready").map(media).sort((left, right) => left.sortOrder - right.sortOrder);
}

async function loadProduct(id: string): Promise<Product> {
  const [catalogProduct, mediaItems] = await Promise.all([apiFetch<ApiProduct>(`/v1/catalog/products/${id}`, { headers: headers() }), loadMedia(id)]);
  return product(catalogProduct, mediaItems);
}

function versionOf(current: Product): number { return (current as Product & { version?: number }).version ?? 0; }

export const apiCatalogClient: CatalogClient = {
  async listProducts(params) {
    const query = new URLSearchParams();
    if (params?.search) query.set("search", params.search);
    if (params?.publishState) query.set("publishState", params.publishState);
    if (params?.cursor) query.set("cursor", params.cursor);
    const suffix = query.size > 0 ? `?${query}` : "";
    const page = await apiFetch<{ items: ApiProduct[]; nextCursor?: string }>(`/v1/catalog/products${suffix}`, { headers: headers() });
    return { items: page.items.map((item) => product(item)), cursor: page.nextCursor ?? null, hasMore: Boolean(page.nextCursor), totalCount: page.items.length } as PaginatedResult<Product>;
  },
  getProduct: loadProduct,
  async getVariants(productId) { return (await loadProduct(productId)).variants; },
  async getCollections() { return []; },
  async createProduct(input) { const created = await write<ApiProduct>("/v1/catalog/products", "POST", { ...input, idempotencyKey: crypto.randomUUID() }); return loadProduct(created.id); },
  async updateProduct(current, input) { const updated = await write<ApiProduct>(`/v1/catalog/products/${current.id}`, "PUT", { ...input, expectedVersion: versionOf(current) }); return loadProduct(updated.id); },
  async setPublication(current, state) { const updated = await write<ApiProduct>(`/v1/catalog/products/${current.id}/publication`, "POST", { state, expectedVersion: versionOf(current) }); return loadProduct(updated.id); },
  async archiveProduct(current) { const updated = await write<ApiProduct>(`/v1/catalog/products/${current.id}/archive`, "POST", { expectedVersion: versionOf(current) }); return loadProduct(updated.id); },
  async addVariant(current, input: ProductVariantInput) { const updated = await write<ApiProduct>(`/v1/catalog/products/${current.id}/variants`, "POST", { ...input, expectedVersion: versionOf(current) }); return loadProduct(updated.id); },
  async uploadMedia(productId, file, altText) {
    const initiated = await write<ApiMedia>("/v1/media/initiate", "POST", { contentType: file.type, byteSize: file.size });
    const form = new FormData(); form.append("file", file);
    await apiFetch<ApiMedia>(`/v1/media/${initiated.id}/complete`, { method: "POST", body: form, headers: { ...headers(), "X-CSRF-Token": await getCsrfToken() } });
    await write<ApiMedia>(`/v1/media/${initiated.id}/attach`, "POST", { productId, sortOrder: 0, altText });
    return loadProduct(productId);
  },
  async deleteMedia(productId, mediaId) { await write<ApiMedia>(`/v1/media/${mediaId}`, "DELETE"); return loadProduct(productId); },
};
