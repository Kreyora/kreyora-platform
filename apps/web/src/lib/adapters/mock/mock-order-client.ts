import type { OrderClient } from "@/lib/ports/order-client";
import type { PaginatedResult, Order } from "@/lib/types";
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
    return order;
  },

  async getOrderActivity(orderId: string) {
    await delay();
    return orderActivitiesByOrderId[orderId] ?? [];
  },
};
