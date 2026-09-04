import { apiFetch } from "@/lib/api";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { InventoryClient } from "@/lib/ports/inventory-client";
import type { InventoryItem, InventoryReservation, PaginatedResult, StockMovement } from "@/lib/types";
type Balance = { id: string; tenantId: string; variantId: string; onHandQuantity: number; reservedQuantity: number; availableQuantity: number; lowStockThreshold: number; isLowStock: boolean; version: number };
const headers = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });
const item = (value: Balance): InventoryItem => ({ id: value.id, tenantId: value.tenantId, variantId: value.variantId, productTitle: "", variantName: "", sku: "", onHand: value.onHandQuantity, committed: value.reservedQuantity, available: value.availableQuantity, lowStockThreshold: value.lowStockThreshold, isLowStock: value.isLowStock, updatedAt: "", version: value.version } as InventoryItem);
async function write<T>(path: string, method: "POST" | "PUT", body: unknown): Promise<T> { return apiFetch<T>(path, { method, body, headers: { ...headers(), "X-CSRF-Token": await getCsrfToken() } }); }
export const apiInventoryClient: InventoryClient = {
  async getInventory(variantId) { return item(await apiFetch<Balance>(`/v1/inventory/variants/${variantId}`, { headers: headers() })); },
  async getStockMovements(variantId) { const page = await apiFetch<{ items: Array<{ id: string; inventoryItemId: string; type: string; quantityDelta: number; reason: string; actorUserId: string; createdAt: string }>; nextCursor?: string }>(`/v1/inventory/variants/${variantId}/movements`, { headers: headers() }); return { items: page.items.map((value) => ({ id: value.id, inventoryItemId: value.inventoryItemId, type: value.type as StockMovement["type"], quantity: value.quantityDelta, reason: value.reason, actorId: value.actorUserId, createdAt: value.createdAt })), cursor: page.nextCursor ?? null, hasMore: Boolean(page.nextCursor) } as PaginatedResult<StockMovement>; },
  async getReservations(variantId) { const page = await apiFetch<{ items: Array<{ id: string; variantId: string; quantity: number; state: string; source: string; referenceId: string; expiresAt: string }> }>(`/v1/inventory/variants/${variantId}/reservations`, { headers: headers() }); return page.items.map((value) => ({ ...value, tenantId: selectedWorkspaceId() ?? "", state: value.state.toLowerCase() as InventoryReservation["state"], source: value.source.toLowerCase() as InventoryReservation["source"], idempotencyKey: "", createdAt: "" })); },
  async getLowStock() { return (await apiFetch<Balance[]>("/v1/inventory/low-stock", { headers: headers() })).map(item); },
  async adjustStock(input) { const result = await write<{ balance: Balance }>("/v1/inventory/adjustments", "POST", { ...input, type: input.type === "receipt" ? "Receipt" : input.type === "damage" ? "Damage" : input.type === "correctionIncrease" ? "CorrectionIncrease" : "CorrectionDecrease", idempotencyKey: crypto.randomUUID() }); return item(result.balance); },
  async setLowStockThreshold(current, threshold) { return item(await write<Balance>(`/v1/inventory/variants/${current.variantId}/threshold`, "PUT", { threshold, expectedVersion: (current as InventoryItem & { version?: number }).version ?? 0 })); },
};
