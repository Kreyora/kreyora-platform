import type { ReportingClient } from "@/lib/ports/reporting-client";
import { dashboardMetrics, analyticsSnapshots } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

export const mockReportingClient: ReportingClient = {
  async getDashboardMetrics(_tenantId: string) {
    await delay();
    return dashboardMetrics;
  },

  async getAnalytics(_tenantId: string, period: "day" | "week" | "month") {
    await delay();
    return analyticsSnapshots[period];
  },
};
