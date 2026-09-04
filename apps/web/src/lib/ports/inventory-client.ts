import type {
  InventoryItem,
  StockMovement,
  InventoryReservation,
  PaginatedResult,
} from "@/lib/types";

export interface InventoryClient {
  getInventory(variantId: string): Promise<InventoryItem>;
  getStockMovements(variantId: string): Promise<PaginatedResult<StockMovement>>;
  getReservations(variantId: string): Promise<InventoryReservation[]>;
  getLowStock(): Promise<InventoryItem[]>;
  adjustStock(input: {
    variantId: string;
    type: "receipt" | "correctionIncrease" | "correctionDecrease" | "damage";
    quantity: number;
    reason: string;
  }): Promise<InventoryItem>;
  setLowStockThreshold(item: InventoryItem, threshold: number): Promise<InventoryItem>;
}
