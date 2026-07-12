import type { Order, OrderActivity, PaginatedResult } from "@/lib/types";

export interface OrderClient {
  listOrders(params?: {
    status?: string;
    source?: string;
    search?: string;
    cursor?: string;
  }): Promise<PaginatedResult<Order>>;
  getOrder(id: string): Promise<Order>;
  getOrderActivity(orderId: string): Promise<OrderActivity[]>;
}
