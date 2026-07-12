import type { DashboardMetrics, AnalyticsSnapshot } from "@/lib/types";

export interface ReportingClient {
  getDashboardMetrics(tenantId: string): Promise<DashboardMetrics>;
  getAnalytics(tenantId: string, period: "day" | "week" | "month"): Promise<AnalyticsSnapshot>;
}
