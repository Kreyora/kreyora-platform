"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Skeleton } from "@/components/ui/skeleton";
import { Badge } from "@/components/ui/badge";
import { ViewerBadge } from "@/components/viewer-badge";
import type { DashboardMetrics, Order } from "@/lib/types";

function formatCurrency(amount: number): string {
  return `Rs. ${amount.toLocaleString("en-IN")}`;
}

function MetricCard({
  label,
  value,
  subtext,
}: {
  label: string;
  value: string;
  subtext?: string;
}) {
  return (
    <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-5">
      <p className="text-xs font-medium uppercase tracking-wider text-[var(--color-ink-secondary)]">
        {label}
      </p>
      <p className="mt-2 text-heading-page font-bold text-[var(--color-ink-primary)]">
        {value}
      </p>
      {subtext && (
        <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
          {subtext}
        </p>
      )}
    </div>
  );
}

const ORDER_STATUS_VARIANT: Record<string, "success" | "warning" | "info" | "neutral"> = {
  fulfilled: "success",
  confirmed: "info",
  processing: "warning",
  pending: "neutral",
};

export default function DashboardPage() {
  const { identity, reporting, order } = useClients();
  const { effectiveRole } = useSession();
  const [metrics, setMetrics] = useState<DashboardMetrics | null>(null);
  const [recentOrders, setRecentOrders] = useState<Order[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      reporting.getDashboardMetrics(""),
      order.listOrders(),
    ]).then(([m, o]) => {
      if (!cancelled) {
        setMetrics(m);
        setRecentOrders(o.items.slice(0, 3));
        setIsLoading(false);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [reporting, order]);

  if (isLoading || !metrics) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-40" />
        <Skeleton className="mb-8 h-4 w-64" />
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-28 rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="flex items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">
          Dashboard
        </h1>
        {effectiveRole === "viewer" && <ViewerBadge />}
      </div>
      <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
        Overview of your store performance and setup status.
      </p>

      {/* Metrics */}
      <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard
          label="Total orders"
          value={String(metrics.totalOrders)}
          subtext={`${metrics.ordersThisMonth}/${metrics.ordersLimit} this month`}
        />
        <MetricCard
          label="Total revenue"
          value={formatCurrency(metrics.totalRevenue.amount)}
        />
        <MetricCard
          label="Open conversations"
          value={String(metrics.openConversations)}
          subtext={`Avg reply: ${metrics.averageReplyTimeMinutes} min`}
        />
        <MetricCard
          label="AI credits"
          value={`${metrics.aiCreditsUsed}/${metrics.aiCreditsLimit}`}
          subtext={`${Math.round((metrics.aiCreditsUsed / metrics.aiCreditsLimit) * 100)}% used`}
        />
      </div>

      {/* Setup progress */}
      <div className="mt-8 rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-5">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-[var(--color-ink-primary)]">
              Setup progress
            </p>
            <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
              {metrics.setupProgress}% of your workspace setup is complete.
            </p>
          </div>
          <Link
            href="/onboarding"
            className="text-sm font-medium text-[var(--color-ink-primary)] underline underline-offset-2 hover:no-underline"
          >
            Continue setup
          </Link>
        </div>
        <div className="mt-3 h-2 overflow-hidden rounded-full bg-[var(--color-canvas-subtle)]">
          <div
            className="h-full rounded-full bg-[var(--color-success)] transition-[width] duration-[var(--duration-entrance)] ease-[var(--easing-default)]"
            style={{ width: `${metrics.setupProgress}%` }}
          />
        </div>
      </div>

      {/* Alerts */}
      {metrics.lowStockProducts > 0 && (
        <div className="mt-4 flex items-center justify-between rounded-[var(--radius-md)] border border-[var(--color-warning-subtle)] bg-[var(--color-warning-subtle)] px-4 py-3">
          <div className="flex items-center gap-2">
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="var(--color-warning)"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M12 9v4M12 17h.01M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
            </svg>
            <span className="text-sm text-[var(--color-warning)]">
              {metrics.lowStockProducts} product{metrics.lowStockProducts > 1 ? "s" : ""} with low stock
            </span>
          </div>
          <Link
            href="/inventory/low-stock"
            className="text-xs font-medium text-[var(--color-warning)] underline underline-offset-2"
          >
            View
          </Link>
        </div>
      )}

      {/* Recent orders */}
      <div className="mt-8">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">
            Recent orders
          </h2>
          <Link
            href="/orders"
            className="text-xs text-[var(--color-ink-secondary)] underline underline-offset-2 hover:text-[var(--color-ink-primary)]"
          >
            View all
          </Link>
        </div>
        <div className="mt-3 overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                  Order
                </th>
                <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                  Customer
                </th>
                <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                  Total
                </th>
                <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                  Status
                </th>
              </tr>
            </thead>
            <tbody>
              {recentOrders.map((o) => (
                <tr
                  key={o.id}
                  className="border-b border-[var(--color-border)] last:border-b-0"
                >
                  <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">
                    <Link
                      href={`/orders/${o.id}`}
                      className="hover:underline"
                    >
                      {o.orderNumber}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                    {o.customerName}
                  </td>
                  <td className="px-4 py-3 text-[var(--color-ink-primary)]">
                    {formatCurrency(o.total.amount)}
                  </td>
                  <td className="px-4 py-3">
                    <Badge
                      variant={ORDER_STATUS_VARIANT[o.status] ?? "neutral"}
                    >
                      {o.status}
                    </Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Quick actions */}
      {effectiveRole !== "viewer" && (
        <div className="mt-8">
          <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">
            Quick actions
          </h2>
          <div className="mt-3 flex flex-wrap gap-3">
            <Link
              href="/catalog/new"
              className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]"
            >
              Add product
            </Link>
            <Link
              href="/inbox"
              className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]"
            >
              View inbox
            </Link>
            <Link
              href="/inventory/low-stock"
              className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]"
            >
              Check inventory
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}
