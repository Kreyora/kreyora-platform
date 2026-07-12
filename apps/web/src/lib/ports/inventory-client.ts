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
}
