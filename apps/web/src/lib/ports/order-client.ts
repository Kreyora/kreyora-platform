import type {
  Order,
  OrderActivity,
  OrderActionEvaluation,
  ExecuteOrderActionParams,
  OrderOperationResult,
  OrderNotification,
  PaginatedResult,
} from "@/lib/types";

export interface OrderClient {
  listOrders(params?: {
    page?: number;
    pageSize?: number;
    status?: string;
    paymentStatus?: string;
    fulfilmentStatus?: string;
    paymentMethod?: string;
    source?: string;
    search?: string;
    cursor?: string;
  }): Promise<PaginatedResult<Order>>;
  getOrder(id: string): Promise<Order>;
  getAllowedActions(orderId: string): Promise<OrderActionEvaluation[]>;
  executeAction(orderId: string, params: ExecuteOrderActionParams): Promise<OrderOperationResult>;
  getOrderActivity(orderId: string): Promise<OrderActivity[]>;
  getOrderNotifications(orderId: string): Promise<OrderNotification[]>;
}
