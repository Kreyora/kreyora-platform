import type { OrderClient } from "@/lib/ports/order-client";
import type {
  PaginatedResult,
  Order,
  OrderActionEvaluation,
  ExecuteOrderActionParams,
  OrderOperationResult,
  OrderNotification,
  OrderAction,
} from "@/lib/types";
import { orders, orderActivitiesByOrderId } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockOrderClient: OrderClient = {
  async listOrders(params) {
    await delay();

    let filtered = [...orders];

    if (params?.status) {
      filtered = filtered.filter((o) => o.status === params.status);
    }

    if (params?.paymentStatus) {
      filtered = filtered.filter((o) => o.paymentStatus === params.paymentStatus);
    }

    if (params?.fulfilmentStatus) {
      filtered = filtered.filter((o) => o.fulfilmentStatus === params.fulfilmentStatus);
    }

    if (params?.source) {
      filtered = filtered.filter((o) => o.source === params.source);
    }

    if (params?.search) {
      const query = params.search.toLowerCase();
      filtered = filtered.filter(
        (o) =>
          o.orderNumber.toLowerCase().includes(query) ||
          o.customerName.toLowerCase().includes(query) ||
          o.customerPhone.includes(query),
      );
    }

    return toPaginated<Order>(filtered);
  },

  async getOrder(id: string) {
    await delay();
    const order = orders.find((o) => o.id === id);
    if (!order) {
      throw new Error(`Order not found: ${id}`);
    }
    return { ...order, rowVersion: 1 };
  },

  async getAllowedActions(orderId: string): Promise<OrderActionEvaluation[]> {
    await delay();
    const order = orders.find((o) => o.id === orderId);
    if (!order) return [];

    const actions: OrderActionEvaluation[] = [];

    if (order.status === "pending_confirmation") {
      actions.push(
        { action: "confirm", isAllowed: true, requiresReason: false, isDestructive: false },
        { action: "cancel", isAllowed: true, requiresReason: true, isDestructive: true },
      );
    } else if (order.status === "confirmed" || order.status === "processing") {
      if (order.paymentMethod === "merchant_qr" && order.paymentStatus === "awaiting_verification") {
        actions.push(
          { action: "verify_payment", isAllowed: true, requiresReason: false, isDestructive: false },
          { action: "reject_payment", isAllowed: true, requiresReason: true, isDestructive: true },
        );
      }
      if (order.paymentMethod === "cod" && (order.fulfilmentStatus === "dispatched" || order.fulfilmentStatus === "delivered") && order.paymentStatus === "pending") {
        actions.push({ action: "mark_cod_collected", isAllowed: true, requiresReason: false, isDestructive: false });
      }
      if (order.fulfilmentStatus === "unfulfilled") {
        actions.push({ action: "prepare", isAllowed: true, requiresReason: false, isDestructive: false });
      }
      if (order.fulfilmentStatus === "ready") {
        actions.push({ action: "dispatch", isAllowed: true, requiresReason: false, isDestructive: false });
      }
      if (order.fulfilmentStatus === "dispatched") {
        actions.push({ action: "deliver", isAllowed: true, requiresReason: false, isDestructive: false });
      }
      actions.push({ action: "cancel", isAllowed: true, requiresReason: true, isDestructive: true });
    }

    return actions;
  },

  async executeAction(orderId: string, params: ExecuteOrderActionParams): Promise<OrderOperationResult> {
    await delay();
    const order = orders.find((o) => o.id === orderId);
    if (!order) throw new Error(`Order not found: ${orderId}`);

    if (params.action === "confirm") {
      order.status = "confirmed";
    } else if (params.action === "cancel") {
      order.status = "cancelled";
    } else if (params.action === "prepare") {
      order.fulfilmentStatus = "ready";
    } else if (params.action === "dispatch") {
      order.fulfilmentStatus = "dispatched";
    } else if (params.action === "deliver") {
      order.fulfilmentStatus = "delivered";
      order.status = "fulfilled";
    } else if (params.action === "verify_payment" || params.action === "mark_cod_collected") {
      order.paymentStatus = "paid";
    } else if (params.action === "reject_payment") {
      order.paymentStatus = "failed";
    }

    return {
      orderId,
      orderNumber: order.orderNumber,
      status: order.status,
      paymentStatus: order.paymentStatus,
      fulfilmentStatus: order.fulfilmentStatus,
      rowVersion: (order.rowVersion ?? 1) + 1,
      wasReplayed: false,
    };
  },

  async getOrderActivity(orderId: string) {
    await delay();
    return orderActivitiesByOrderId[orderId] ?? [];
  },

  async getOrderNotifications(orderId: string): Promise<OrderNotification[]> {
    await delay();
    const order = orders.find((o) => o.id === orderId);
    if (!order) return [];

    return [
      {
        id: `notif-${orderId}-1`,
        templateCode: "order_created",
        channel: "email",
        status: "delivered",
        recipientContactRedacted: "c***@example.com",
        attemptCount: 1,
        deliveredAt: order.createdAt,
        createdAt: order.createdAt,
      },
    ];
  },
};
