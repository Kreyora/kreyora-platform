import type { Money } from "./common";

export interface DashboardMetrics {
  setupProgress: number;
  totalOrders: number;
  totalRevenue: Money;
  openConversations: number;
  averageReplyTimeMinutes: number;
  lowStockProducts: number;
  integrationHealthy: number;
  integrationTotal: number;
  aiCreditsUsed: number;
  aiCreditsLimit: number;
  ordersThisMonth: number;
  ordersLimit: number;
}

export interface AnalyticsSnapshot {
  period: "day" | "week" | "month";
  orderCount: number;
  revenue: Money;
  conversationCount: number;
  averageOrderValue: Money;
  conversionRate: number;
  topProducts: Array<{ productId: string; title: string; orderCount: number }>;
  ordersBySource: Record<string, number>;
  ordersByChannel: Record<string, number>;
}
