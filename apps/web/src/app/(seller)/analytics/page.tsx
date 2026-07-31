"use client";

import { useEffect, useState, useCallback } from "react";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import type { AnalyticsSnapshot } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";
type Period = "day" | "week" | "month";

const PERIOD_LABELS: Record<Period, string> = {
  day: "Today",
  week: "This Week",
  month: "This Month",
};

export default function AnalyticsPage() {
  const { reporting } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [period, setPeriod] = useState<Period>("month");
  const [data, setData] = useState<AnalyticsSnapshot | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const load = useCallback(
    (p: Period) => {
      setIsLoading(true);
      reporting.getAnalytics(DEMO_TENANT_ID, p).then((s) => {
        setData(s);
        setIsLoading(false);
      });
    },
    [reporting],
  );

  useEffect(() => {
    void Promise.resolve().then(() => load(period));
  }, [period, load]);

  const handlePeriodChange = (p: Period) => {
    setPeriod(p);
  };

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Analytics</h1>
          {isViewer && <ViewerBadge />}
        </div>
        <div className="flex gap-1 rounded-[var(--radius-md)] border border-[var(--color-border)] p-0.5" role="group" aria-label="Period selector">
          {(Object.keys(PERIOD_LABELS) as Period[]).map((p) => (
            <button
              key={p}
              onClick={() => handlePeriodChange(p)}
              className={`min-h-[36px] rounded-[var(--radius-sm)] px-3 text-sm font-medium transition-colors ${p === period ? "bg-[var(--color-ink-primary)] text-white" : "text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]"}`}
            >
              {PERIOD_LABELS[p]}
            </button>
          ))}
        </div>
      </div>

      {isLoading || !data ? (
        <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-20 rounded-[var(--radius-lg)]" />
          ))}
        </div>
      ) : (
        <>
          <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <MetricCard label="Orders" value={data.orderCount.toLocaleString()} />
            <MetricCard label="Revenue" value={`Rs. ${data.revenue.amount.toLocaleString("en-IN")}`} />
            <MetricCard label="Conversations" value={data.conversationCount.toLocaleString()} />
            <MetricCard label="Avg. Order" value={`Rs. ${data.averageOrderValue.amount.toLocaleString("en-IN")}`} />
            <MetricCard label="Conversion" value={`${data.conversionRate}%`} />
          </div>

          <div className="mt-8 grid gap-8 lg:grid-cols-2">
            <section>
              <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">Top Products</h2>
              {data.topProducts.length === 0 ? (
                <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">No products this period.</p>
              ) : (
                <div className="mt-3 overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-[var(--color-border)]">
                        <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Product</th>
                        <th className="pb-2 text-right font-medium text-[var(--color-ink-secondary)]">Orders</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.topProducts.map((p) => (
                        <tr key={p.productId} className="border-b border-[var(--color-border)] last:border-b-0">
                          <td className="py-2 text-[var(--color-ink-primary)]">{p.title}</td>
                          <td className="py-2 text-right text-[var(--color-ink-primary)]">{p.orderCount}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <div className="space-y-8">
              <section>
                <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">Orders by Source</h2>
                <div className="mt-3 space-y-2">
                  {Object.entries(data.ordersBySource).map(([source, count]) => (
                    <div key={source} className="flex items-center justify-between border-b border-[var(--color-border)] pb-2 last:border-b-0">
                      <span className="text-sm capitalize text-[var(--color-ink-primary)]">{source}</span>
                      <Badge variant="neutral">{count}</Badge>
                    </div>
                  ))}
                </div>
              </section>

              <section>
                <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">Orders by Channel</h2>
                <div className="mt-3 space-y-2">
                  {Object.entries(data.ordersByChannel).map(([channel, count]) => (
                    <div key={channel} className="flex items-center justify-between border-b border-[var(--color-border)] pb-2 last:border-b-0">
                      <span className="text-sm capitalize text-[var(--color-ink-primary)]">{channel}</span>
                      <Badge variant="neutral">{count}</Badge>
                    </div>
                  ))}
                </div>
              </section>
            </div>
          </div>
        </>
      )}

      <p className="mt-8 text-[10px] text-[var(--color-ink-secondary)]">
        Analytics data is simulated. Numbers are derived from mock fixtures.
      </p>
    </div>
  );
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4">
      <p className="text-xs text-[var(--color-ink-secondary)]">{label}</p>
      <p className="mt-1 text-xl font-semibold text-[var(--color-ink-primary)]">{value}</p>
    </div>
  );
}
