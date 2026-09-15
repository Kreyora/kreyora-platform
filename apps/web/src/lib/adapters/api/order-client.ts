import { apiFetch } from "@/lib/api";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { OrderClient } from "@/lib/ports/order-client";
import type {
  Order,
  OrderItem,
  OrderAction,
  ExecuteOrderActionParams,
  OrderNotification,
  PaginatedResult,
  OrderStatus,
  PaymentStatus,
  FulfilmentStatus,
} from "@/lib/types";

const headers = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });

interface ApiOrderSummary {
  id: string;
  orderNumber: string;
  status: string;
  paymentStatus: string;
  fulfilmentStatus: string;
  paymentMethod: string;
  source: string;
  customerName: string;
  customerPhone: string;
  customerEmail?: string;
  district: string;
  totalNpr: number;
  currency: string;
  itemCount: number;
  rowVersion: number;
  createdAt: string;
  modifiedAt?: string;
}

interface ApiOrderItemDetail {
  id: string;
  productId: string;
  productTitle: string;
  variantId: string;
  variantName: string;
  unitPriceNpr: number;
  quantity: number;
  lineTotalNpr: number;
  currency: string;
}

interface ApiOrderDetail {
  id: string;
  orderNumber: string;
  storeId: string;
  checkoutSessionId: string;
  status: string;
  paymentStatus: string;
  fulfilmentStatus: string;
  paymentMethod: string;
  source: string;
  customerName: string;
  customerPhone: string;
  customerEmail?: string;
  addressLine1: string;
  addressLine2?: string;
  district: string;
  municipality?: string;
  locality?: string;
  landmark?: string;
  merchandiseSubtotalNpr: number;
  discountNpr: number;
  deliveryFeeNpr: number;
  taxNpr: number;
  totalNpr: number;
  currency: string;
  deliveryRuleId: string;
  deliveryRuleName: string;
  estimatedEtaText?: string;
  codAvailable: boolean;
  rowVersion: number;
  createdAt: string;
  modifiedAt?: string;
  items: ApiOrderItemDetail[];
}

interface ApiActionEvaluation {
  action: string | number;
  isAllowed: boolean;
  denialReason?: string;
  requiresReason: boolean;
  isDestructive: boolean;
}

interface ApiOperationResult {
  orderId: string;
  orderNumber: string;
  status: string;
  paymentStatus: string;
  fulfilmentStatus: string;
  rowVersion: number;
  wasReplayed: boolean;
}

interface ApiActivityItem {
  id: string;
  action: string;
  actorUserId?: string;
  occurredAt: string;
  reason?: string;
  details?: string;
}

interface ApiNotificationItem {
  id: string;
  templateCode: string;
  channel: string;
  status: string;
  recipientContactRedacted: string;
  attemptCount: number;
  deliveredAt?: string;
  deadLetteredAt?: string;
  createdAt: string;
}

const ACTION_MAP_TO_API: Record<OrderAction, string> = {
  confirm: "Confirm",
  cancel: "Cancel",
  prepare: "Prepare",
  dispatch: "Dispatch",
  deliver: "Deliver",
  mark_delivery_failed: "MarkDeliveryFailed",
  verify_payment: "VerifyPayment",
  reject_payment: "RejectPayment",
  mark_cod_collected: "MarkCodCollected",
};

const ACTION_MAP_FROM_API: Record<string, OrderAction> = {
  confirm: "confirm",
  cancel: "cancel",
  prepare: "prepare",
  dispatch: "dispatch",
  deliver: "deliver",
  markdeliveryfailed: "mark_delivery_failed",
  verifypayment: "verify_payment",
  rejectpayment: "reject_payment",
  markcodcollected: "mark_cod_collected",
  // numeric fallback from enum
  "0": "confirm",
  "1": "cancel",
  "2": "prepare",
  "3": "dispatch",
  "4": "deliver",
  "5": "mark_delivery_failed",
  "6": "verify_payment",
  "7": "reject_payment",
  "8": "mark_cod_collected",
};

function normalizeStatus(val: string): OrderStatus {
  const lower = val.toLowerCase().replace(/[\s-]/g, "_");
  if (lower === "pendingconfirmation") return "pending_confirmation";
  if (lower === "awaitingcustomer") return "awaiting_customer";
  return lower as OrderStatus;
}

function normalizePaymentStatus(val: string): PaymentStatus {
  const lower = val.toLowerCase().replace(/[\s-]/g, "_");
  if (lower === "awaitingverification") return "awaiting_verification";
  if (lower === "notrequired") return "not_required";
  if (lower === "partiallyrefunded") return "partially_refunded";
  return lower as PaymentStatus;
}

function normalizeFulfilmentStatus(val: string): FulfilmentStatus {
  return val.toLowerCase().replace(/[\s-]/g, "_") as FulfilmentStatus;
}

function normalizePaymentMethod(val: string): "cod" | "merchant_qr" {
  const lower = val.toLowerCase();
  return lower.includes("qr") ? "merchant_qr" : "cod";
}

function mapSummaryToOrder(api: ApiOrderSummary): Order {
  return {
    id: api.id,
    tenantId: selectedWorkspaceId() ?? "",
    orderNumber: api.orderNumber,
    status: normalizeStatus(api.status),
    paymentStatus: normalizePaymentStatus(api.paymentStatus),
    fulfilmentStatus: normalizeFulfilmentStatus(api.fulfilmentStatus),
    source: (api.source.toLowerCase() as Order["source"]) || "storefront",
    items: [],
    subtotal: { amount: api.totalNpr, currency: "NPR" },
    deliveryFee: { amount: 0, currency: "NPR" },
    total: { amount: api.totalNpr, currency: "NPR" },
    currency: "NPR",
    customerName: api.customerName,
    customerPhone: api.customerPhone,
    customerEmail: api.customerEmail,
    deliveryAddress: {
      line1: api.district,
      city: api.district,
      district: api.district,
      country: "NP",
      contactName: api.customerName,
      contactPhone: api.customerPhone,
    },
    paymentMethod: normalizePaymentMethod(api.paymentMethod),
    activity: [],
    rowVersion: api.rowVersion,
    createdAt: api.createdAt,
    updatedAt: api.modifiedAt ?? api.createdAt,
  };
}

function mapDetailToOrder(api: ApiOrderDetail): Order {
  const items: OrderItem[] = api.items.map((i) => ({
    id: i.id,
    variantId: i.variantId,
    productTitle: i.productTitle,
    variantName: i.variantName,
    sku: "",
    unitPrice: { amount: i.unitPriceNpr, currency: "NPR" },
    quantity: i.quantity,
    lineTotal: { amount: i.lineTotalNpr, currency: "NPR" },
  }));

  return {
    id: api.id,
    tenantId: selectedWorkspaceId() ?? "",
    orderNumber: api.orderNumber,
    status: normalizeStatus(api.status),
    paymentStatus: normalizePaymentStatus(api.paymentStatus),
    fulfilmentStatus: normalizeFulfilmentStatus(api.fulfilmentStatus),
    source: (api.source.toLowerCase() as Order["source"]) || "storefront",
    items,
    subtotal: { amount: api.merchandiseSubtotalNpr, currency: "NPR" },
    deliveryFee: { amount: api.deliveryFeeNpr, currency: "NPR" },
    total: { amount: api.totalNpr, currency: "NPR" },
    currency: "NPR",
    customerName: api.customerName,
    customerPhone: api.customerPhone,
    customerEmail: api.customerEmail,
    deliveryAddress: {
      line1: api.addressLine1,
      line2: api.addressLine2,
      city: api.municipality ?? api.locality ?? api.district,
      district: api.district,
      country: "NP",
      contactName: api.customerName,
      contactPhone: api.customerPhone,
    },
    paymentMethod: normalizePaymentMethod(api.paymentMethod),
    activity: [],
    rowVersion: api.rowVersion,
    createdAt: api.createdAt,
    updatedAt: api.modifiedAt ?? api.createdAt,
  };
}

export const apiOrderClient: OrderClient = {
  async listOrders(params) {
    const query = new URLSearchParams();
    if (params?.page) query.set("page", params.page.toString());
    if (params?.pageSize) query.set("pageSize", params.pageSize.toString());
    if (params?.status) query.set("status", params.status);
    if (params?.paymentStatus) query.set("paymentStatus", params.paymentStatus);
    if (params?.fulfilmentStatus) query.set("fulfilmentStatus", params.fulfilmentStatus);
    if (params?.paymentMethod) query.set("paymentMethod", params.paymentMethod);
    if (params?.source) query.set("source", params.source);
    if (params?.search) query.set("search", params.search);

    const qs = query.size > 0 ? `?${query}` : "";
    const page = await apiFetch<{
      items: ApiOrderSummary[];
      page: number;
      pageSize: number;
      totalCount: number;
    }>(`/v1/orders${qs}`, { headers: headers() });

    return {
      items: page.items.map(mapSummaryToOrder),
      cursor: null,
      hasMore: page.page * page.pageSize < page.totalCount,
      totalCount: page.totalCount,
    } as PaginatedResult<Order>;
  },

  async getOrder(id: string) {
    const detail = await apiFetch<ApiOrderDetail>(`/v1/orders/${id}`, { headers: headers() });
    return mapDetailToOrder(detail);
  },

  async getAllowedActions(orderId: string) {
    const evals = await apiFetch<ApiActionEvaluation[]>(`/v1/orders/${orderId}/actions`, {
      headers: headers(),
    });

    return evals.map((e) => {
      const rawKey = e.action.toString().toLowerCase().replace(/_/g, "");
      const action = ACTION_MAP_FROM_API[rawKey] ?? (e.action.toString().toLowerCase() as OrderAction);
      return {
        action,
        isAllowed: e.isAllowed,
        denialReason: e.denialReason,
        requiresReason: e.requiresReason,
        isDestructive: e.isDestructive,
      };
    });
  },

  async executeAction(orderId: string, params: ExecuteOrderActionParams) {
    const apiAction = ACTION_MAP_TO_API[params.action] ?? params.action;
    const idempotencyKey = crypto.randomUUID();
    const csrfToken = await getCsrfToken();

    const result = await apiFetch<ApiOperationResult>(`/v1/orders/${orderId}/actions`, {
      method: "POST",
      body: {
        action: apiAction,
        reason: params.reason,
        expectedVersion: params.expectedVersion,
        paymentAttemptId: params.paymentAttemptId,
        providerReference: params.providerReference,
      },
      headers: {
        ...headers(),
        "X-CSRF-Token": csrfToken,
        "Idempotency-Key": idempotencyKey,
      },
    });

    return {
      orderId: result.orderId,
      orderNumber: result.orderNumber,
      status: normalizeStatus(result.status),
      paymentStatus: normalizePaymentStatus(result.paymentStatus),
      fulfilmentStatus: normalizeFulfilmentStatus(result.fulfilmentStatus),
      rowVersion: result.rowVersion,
      wasReplayed: result.wasReplayed,
    };
  },

  async getOrderActivity(orderId: string) {
    const items = await apiFetch<ApiActivityItem[]>(`/v1/orders/${orderId}/activity`, {
      headers: headers(),
    });

    return items.map((a) => ({
      id: a.id,
      orderId,
      action: a.action,
      actorId: a.actorUserId ?? "system",
      actorName: a.actorUserId ? `User ${a.actorUserId.slice(0, 8)}` : "System",
      reason: a.reason,
      createdAt: a.occurredAt,
    }));
  },

  async getOrderNotifications(orderId: string) {
    const items = await apiFetch<ApiNotificationItem[]>(`/v1/orders/${orderId}/notifications`, {
      headers: headers(),
    });

    return items.map((n) => ({
      id: n.id,
      templateCode: n.templateCode,
      channel: n.channel.toLowerCase() as OrderNotification["channel"],
      status: n.status.toLowerCase().replace(/[\s-]/g, "_") as OrderNotification["status"],
      recipientContactRedacted: n.recipientContactRedacted,
      attemptCount: n.attemptCount,
      deliveredAt: n.deliveredAt,
      deadLetteredAt: n.deadLetteredAt,
      createdAt: n.createdAt,
    }));
  },
};
