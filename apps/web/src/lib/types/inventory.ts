import type { TenantId, Timestamp } from "./common";

export type ReservationState = "active" | "committed" | "released" | "expired";

export type StockMovementType =
  | "initial"
  | "adjustment"
  | "reservation"
  | "commitment"
  | "release"
  | "expiry"
  | "return";

export interface InventoryItem {
  id: string;
  tenantId: TenantId;
  variantId: string;
  productTitle: string;
  variantName: string;
  sku: string;
  onHand: number;
  committed: number;
  available: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  updatedAt: Timestamp;
  /** PostgreSQL concurrency token returned by the API; omitted by fixture data. */
  version?: number;
}

export interface StockMovement {
  id: string;
  inventoryItemId: string;
  type: StockMovementType;
  quantity: number;
  reason?: string;
  referenceId?: string;
  actorId: string;
  createdAt: Timestamp;
}

export interface InventoryReservation {
  id: string;
  tenantId: TenantId;
  variantId: string;
  quantity: number;
  state: ReservationState;
  source: "checkout" | "conversation" | "manual";
  referenceId: string;
  idempotencyKey: string;
  expiresAt: Timestamp;
  createdAt: Timestamp;
}
