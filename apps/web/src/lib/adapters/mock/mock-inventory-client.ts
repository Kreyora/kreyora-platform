import type { InventoryClient } from "@/lib/ports/inventory-client";
import type { PaginatedResult, StockMovement } from "@/lib/types";
import {
  inventoryItems,
  stockMovements,
  inventoryReservations,
} from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockInventoryClient: InventoryClient = {
  async getInventory(variantId: string) {
    await delay();
    const item = inventoryItems.find((i) => i.variantId === variantId);
    if (!item) {
      throw new Error(`Inventory item not found for variant: ${variantId}`);
    }
    return item;
  },

  async getStockMovements(variantId: string) {
    await delay();
    const item = inventoryItems.find((i) => i.variantId === variantId);
    if (!item) {
      return toPaginated<StockMovement>([]);
    }
    const movements = stockMovements.filter((m) => m.inventoryItemId === item.id);
    return toPaginated(movements);
  },

  async getReservations(variantId: string) {
    await delay();
    return inventoryReservations.filter((r) => r.variantId === variantId);
  },

  async getLowStock() {
    await delay();
    return inventoryItems.filter((i) => i.isLowStock);
  },

  async adjustStock(input) {
    await delay();
    const current = await this.getInventory(input.variantId);
    const change = input.type === "damage" || input.type === "correctionDecrease"
      ? -Math.abs(input.quantity)
      : Math.abs(input.quantity);
    return { ...current, onHand: current.onHand + change, available: current.available + change };
  },

  async setLowStockThreshold(current, threshold) {
    await delay();
    return { ...current, lowStockThreshold: threshold, isLowStock: current.available <= threshold };
  },
};
