"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Order } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const ORDER_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  draft: { label: "Draft", variant: "neutral" },
  awaiting_customer: { label: "Awaiting Customer", variant: "warning" },
  pending_confirmation: { label: "Pending", variant: "warning" },
  confirmed: { label: "Confirmed", variant: "info" },
  processing: { label: "Processing", variant: "info" },
  fulfilled: { label: "Fulfilled", variant: "success" },
  cancelled: { label: "Cancelled", variant: "danger" },
};

const PAYMENT_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  not_required: { label: "N/A", variant: "neutral" },
  pending: { label: "Pending", variant: "warning" },
  awaiting_verification: { label: "Awaiting Verification", variant: "warning" },
  authorized: { label: "Authorized", variant: "info" },
  paid: { label: "Paid", variant: "success" },
  failed: { label: "Failed", variant: "danger" },
  refunded: { label: "Refunded", variant: "neutral" },
  partially_refunded: { label: "Partial Refund", variant: "neutral" },
};

const FULFILMENT_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  unfulfilled: { label: "Unfulfilled", variant: "neutral" },
  ready: { label: "Ready", variant: "info" },
  dispatched: { label: "Dispatched", variant: "info" },
  delivered: { label: "Delivered", variant: "success" },
  failed: { label: "Failed", variant: "danger" },
  cancelled: { label: "Cancelled", variant: "danger" },
};

const SOURCE_LABEL: Record<string, string> = {
  storefront: "Storefront",
  conversation: "Conversation",
  manual: "Manual",
};

export default function OrdersPage() {
  const { order: orderClient } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [orders, setOrders] = useState<Order[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [sourceFilter, setSourceFilter] = useState("");
  const [paymentFilter, setPaymentFilter] = useState("");

  const loadOrders = useCallback(async () => {
    setIsLoading(true);
    try {
      const result = await orderClient.listOrders({
        search: search.trim() || undefined,
        status: statusFilter || undefined,
        source: sourceFilter || undefined,
        paymentStatus: paymentFilter || undefined,
        pageSize: 50,
      });
      setOrders(result.items);
      setTotalCount(result.totalCount ?? result.items.length);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load orders");
    } finally {
      setIsLoading(false);
    }
  }, [orderClient, search, statusFilter, sourceFilter, paymentFilter]);

  useEffect(() => {
    let cancelled = false;
    orderClient
      .listOrders({
        search: search.trim() || undefined,
        status: statusFilter || undefined,
        source: sourceFilter || undefined,
        paymentStatus: paymentFilter || undefined,
        pageSize: 50,
      })
      .then((result) => {
        if (!cancelled) {
          setOrders(result.items);
          setTotalCount(result.totalCount ?? result.items.length);
          setError(null);
          setIsLoading(false);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load orders");
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [orderClient, search, statusFilter, sourceFilter, paymentFilter]);

  const clearFilters = () => {
    setSearch("");
    setStatusFilter("");
    setSourceFilter("");
    setPaymentFilter("");
  };

  const hasActiveFilters = Boolean(search || statusFilter || sourceFilter || paymentFilter);

  return (
    <div>
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Orders</h1>
          {isViewer && <ViewerBadge />}
        </div>
      </div>

      {/* Error state */}
      {error && (
        <div className="mt-4 flex items-center justify-between rounded-[var(--radius-md)] border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/50 dark:bg-red-950/50 dark:text-red-300" role="alert">
          <p>{error}</p>
          <Button variant="outline" size="sm" onClick={() => loadOrders()}>
            Retry
          </Button>
        </div>
      )}

      {/* Search and filters */}
      <div className="mt-6 flex flex-wrap items-end gap-3">
        <div className="min-w-[200px] flex-1">
          <Input
            placeholder="Search by order #, name, phone..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search orders"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          <option value="pending_confirmation">Pending</option>
          <option value="confirmed">Confirmed</option>
          <option value="processing">Processing</option>
          <option value="fulfilled">Fulfilled</option>
          <option value="cancelled">Cancelled</option>
        </select>
        <select
          value={sourceFilter}
          onChange={(e) => setSourceFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by source"
        >
          <option value="">All sources</option>
          <option value="storefront">Storefront</option>
          <option value="conversation">Conversation</option>
          <option value="manual">Manual</option>
        </select>
        <select
          value={paymentFilter}
          onChange={(e) => setPaymentFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by payment"
        >
          <option value="">All payments</option>
          <option value="pending">Pending</option>
          <option value="awaiting_verification">Awaiting Verification</option>
          <option value="paid">Paid</option>
          <option value="failed">Failed</option>
        </select>
        {hasActiveFilters && (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            Clear filters
          </Button>
        )}
      </div>

      {/* Loading state */}
      {isLoading ? (
        <div className="mt-6 space-y-3">
          <Skeleton className="h-12 w-full rounded-[var(--radius-md)]" />
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-md)]" />
          ))}
        </div>
      ) : orders.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="No orders found"
            description={hasActiveFilters ? "Try adjusting your filters." : "No orders have been placed yet."}
          />
        </div>
      ) : (
        <>
          {/* Desktop table */}
          <div className="mt-6 hidden overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)] md:block">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Order</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Customer</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Status</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Payment</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Fulfilment</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Source</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Total</th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Date</th>
                </tr>
              </thead>
              <tbody>
                {orders.map((o) => {
                  const os = ORDER_STATUS[o.status] ?? { label: o.status, variant: "neutral" as const };
                  const ps = PAYMENT_STATUS[o.paymentStatus] ?? { label: o.paymentStatus, variant: "neutral" as const };
                  const fs = FULFILMENT_STATUS[o.fulfilmentStatus] ?? { label: o.fulfilmentStatus, variant: "neutral" as const };
                  return (
                    <tr
                      key={o.id}
                      className="border-b border-[var(--color-border)] last:border-b-0 transition-colors hover:bg-[var(--color-canvas-subtle)]"
                    >
                      <td className="px-4 py-3">
                        <Link href={`/orders/${o.id}`} className="font-medium text-[var(--color-ink-primary)] hover:underline">
                          {o.orderNumber}
                        </Link>
                      </td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">{o.customerName}</td>
                      <td className="px-4 py-3"><Badge variant={os.variant}>{os.label}</Badge></td>
                      <td className="px-4 py-3"><Badge variant={ps.variant}>{ps.label}</Badge></td>
                      <td className="px-4 py-3"><Badge variant={fs.variant}>{fs.label}</Badge></td>
                      <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{SOURCE_LABEL[o.source] ?? o.source}</td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">Rs. {o.total.amount.toLocaleString("en-IN")}</td>
                      <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{new Date(o.createdAt).toLocaleDateString()}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Mobile cards */}
          <div className="mt-6 flex flex-col gap-3 md:hidden">
            {orders.map((o) => {
              const os = ORDER_STATUS[o.status] ?? { label: o.status, variant: "neutral" as const };
              const ps = PAYMENT_STATUS[o.paymentStatus] ?? { label: o.paymentStatus, variant: "neutral" as const };
              return (
                <Link
                  key={o.id}
                  href={`/orders/${o.id}`}
                  className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 transition-colors hover:bg-[var(--color-canvas-subtle)]"
                >
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="text-sm font-medium text-[var(--color-ink-primary)]">{o.orderNumber}</p>
                      <p className="text-xs text-[var(--color-ink-secondary)]">{o.customerName}</p>
                    </div>
                    <Badge variant={os.variant}>{os.label}</Badge>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Badge variant={ps.variant}>{ps.label}</Badge>
                    <span className="text-xs text-[var(--color-ink-secondary)]">{SOURCE_LABEL[o.source]}</span>
                  </div>
                  <div className="mt-2 flex justify-between text-xs">
                    <span className="font-medium text-[var(--color-ink-primary)]">Rs. {o.total.amount.toLocaleString("en-IN")}</span>
                    <span className="text-[var(--color-ink-secondary)]">{new Date(o.createdAt).toLocaleDateString()}</span>
                  </div>
                </Link>
              );
            })}
          </div>
        </>
      )}

      <p className="mt-6 text-xs text-[var(--color-ink-secondary)]">
        {totalCount} order{totalCount !== 1 ? "s" : ""} total
      </p>
    </div>
  );
}
