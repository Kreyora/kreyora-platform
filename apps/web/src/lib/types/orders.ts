import type { TenantId, Timestamp, Money, Address } from "./common";

export type OrderStatus =
  | "draft"
  | "awaiting_customer"
  | "pending_confirmation"
  | "confirmed"
  | "processing"
  | "fulfilled"
  | "cancelled";

export type PaymentStatus =
  | "not_required"
  | "pending"
  | "awaiting_verification"
  | "authorized"
  | "paid"
  | "failed"
  | "refunded"
  | "partially_refunded";

export type FulfilmentStatus =
  | "unfulfilled"
  | "ready"
  | "dispatched"
  | "delivered"
  | "failed"
  | "cancelled";

export type OrderSource = "storefront" | "conversation" | "manual";

export type OrderAction =
  | "confirm"
  | "cancel"
  | "prepare"
  | "dispatch"
  | "deliver"
  | "mark_delivery_failed"
  | "verify_payment"
  | "reject_payment"
  | "mark_cod_collected";

export interface OrderActionEvaluation {
  action: OrderAction;
  isAllowed: boolean;
  denialReason?: string;
  requiresReason: boolean;
  isDestructive: boolean;
}

export interface ExecuteOrderActionParams {
  action: OrderAction;
  reason?: string;
  expectedVersion: number;
  paymentAttemptId?: string;
  providerReference?: string;
}

export interface OrderOperationResult {
  orderId: string;
  orderNumber: string;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  fulfilmentStatus: FulfilmentStatus;
  rowVersion: number;
  wasReplayed: boolean;
}

export interface OrderNotification {
  id: string;
  templateCode: string;
  channel: "email" | "sms" | "in_app";
  status: "pending" | "delivering" | "delivered" | "failed" | "dead_lettered";
  recipientContactRedacted: string;
  attemptCount: number;
  deliveredAt?: Timestamp;
  deadLetteredAt?: Timestamp;
  createdAt: Timestamp;
}

export interface OrderItem {
  id: string;
  variantId: string;
  productTitle: string;
  variantName: string;
  sku: string;
  unitPrice: Money;
  quantity: number;
  lineTotal: Money;
}

export interface OrderActivity {
  id: string;
  orderId: string;
  action: string;
  actorId: string;
  actorName: string;
  reason?: string;
  details?: Record<string, string>;
  createdAt: Timestamp;
}

export interface Order {
  id: string;
  tenantId: TenantId;
  orderNumber: string;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  fulfilmentStatus: FulfilmentStatus;
  source: OrderSource;
  items: OrderItem[];
  subtotal: Money;
  deliveryFee: Money;
  total: Money;
  currency: "NPR";
  customerName: string;
  customerPhone: string;
  customerEmail?: string;
  deliveryAddress: Address;
  paymentMethod: "cod" | "merchant_qr";
  activity: OrderActivity[];
  rowVersion?: number;
  createdAt: Timestamp;
  updatedAt: Timestamp;
}
