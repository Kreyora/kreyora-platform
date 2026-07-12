"use client";

import { useState, useCallback } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import type { Order } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const STATUS_BADGE: Record<string, { label: string; variant: BadgeVariant }> = {
  pending_confirmation: { label: "Pending", variant: "warning" },
  confirmed: { label: "Confirmed", variant: "info" },
  processing: { label: "Processing", variant: "info" },
  fulfilled: { label: "Fulfilled", variant: "success" },
  cancelled: { label: "Cancelled", variant: "danger" },
};

export default function OrderLookupPage() {
  const { slug } = useParams<{ slug: string }>();
  const { order: orderClient } = useClients();

  const [searchQuery, setSearchQuery] = useState("");
  const [result, setResult] = useState<Order | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [searching, setSearching] = useState(false);

  const handleSearch = useCallback(async () => {
    if (!searchQuery.trim()) return;
    setSearching(true);
    setNotFound(false);
    setResult(null);

    try {
      const orders = await orderClient.listOrders({ search: searchQuery.trim() });
      if (orders.items.length > 0) {
        setResult(orders.items[0]);
      } else {
        setNotFound(true);
      }
    } catch {
      setNotFound(true);
    }
    setSearching(false);
  }, [searchQuery, orderClient]);

  const statusInfo = result
    ? STATUS_BADGE[result.status] ?? { label: result.status, variant: "neutral" as const }
    : null;

  return (
    <div className="mx-auto max-w-lg">
      <h1 className="text-xl font-bold text-[var(--color-ink-primary)]">Track Your Order</h1>
      <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
        Enter your order number to check the status.
      </p>

      <div className="mt-6 flex gap-3">
        <div className="flex-1">
          <Input
            placeholder="e.g. NC-2025-0042"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            aria-label="Order number"
            onKeyDown={(e) => e.key === "Enter" && handleSearch()}
          />
        </div>
        <Button onClick={handleSearch} loading={searching} disabled={!searchQuery.trim()}>
          Search
        </Button>
      </div>

      {/* Not found */}
      {notFound && (
        <div className="mt-6 rounded-[var(--radius-lg)] border border-dashed border-[var(--color-border)] p-6 text-center">
          <p className="text-sm text-[var(--color-ink-secondary)]">
            Order not found. Please check the order number and try again.
          </p>
          <p className="mt-2 text-xs text-[var(--color-ink-secondary)]">
            Demo orders: NC-2025-0040, NC-2025-0041, NC-2025-0042
          </p>
        </div>
      )}

      {/* Result */}
      {result && statusInfo && (
        <div className="mt-6 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
          <div className="flex items-center justify-between">
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">
              {result.orderNumber}
            </h2>
            <Badge variant={statusInfo.variant}>{statusInfo.label}</Badge>
          </div>

          <div className="mt-4 text-sm text-[var(--color-ink-secondary)]">
            <p>Placed: {new Date(result.createdAt).toLocaleDateString()}</p>
            <p className="mt-1">Customer: {result.customerName}</p>
          </div>

          {/* Items */}
          <div className="mt-4 border-t border-[var(--color-border)] pt-4">
            <h3 className="text-sm font-medium text-[var(--color-ink-primary)]">Items</h3>
            <div className="mt-2 flex flex-col gap-2">
              {result.items.map((item) => (
                <div key={item.id} className="flex justify-between text-sm">
                  <span className="text-[var(--color-ink-secondary)]">
                    {item.productTitle} × {item.quantity}
                  </span>
                  <span className="text-[var(--color-ink-primary)]">
                    Rs. {item.lineTotal.amount.toLocaleString("en-IN")}
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* Total */}
          <div className="mt-3 border-t border-[var(--color-border)] pt-3">
            <div className="flex justify-between text-sm font-semibold">
              <span>Total</span>
              <span>Rs. {result.total.amount.toLocaleString("en-IN")}</span>
            </div>
          </div>

          {/* Delivery */}
          <div className="mt-3 border-t border-[var(--color-border)] pt-3 text-sm">
            <p className="text-[var(--color-ink-secondary)]">
              Delivery: {result.deliveryAddress.line1}, {result.deliveryAddress.city}
            </p>
            <p className="mt-1 text-[var(--color-ink-secondary)]">
              Payment: {result.paymentMethod === "cod" ? "Cash on Delivery" : "Fonepay / eSewa QR"}
            </p>
          </div>

          {/* Timeline */}
          {result.activity.length > 0 && (
            <div className="mt-4 border-t border-[var(--color-border)] pt-4">
              <h3 className="text-sm font-medium text-[var(--color-ink-primary)]">Timeline</h3>
              <div className="mt-2 flex flex-col gap-2">
                {result.activity.map((a) => (
                  <div key={a.id} className="flex items-start gap-2 text-xs">
                    <span className="mt-0.5 h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--color-ink-secondary)]" />
                    <div>
                      <span className="font-medium text-[var(--color-ink-primary)]">
                        {a.action.replace(/\./g, " ")}
                      </span>
                      <span className="text-[var(--color-ink-secondary)]">
                        {" "}by {a.actorName} · {new Date(a.createdAt).toLocaleDateString()}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      <div className="mt-6 text-center">
        <Link
          href={`/store/${slug}`}
          className="text-sm text-[var(--color-ink-secondary)] hover:underline"
        >
          Back to store
        </Link>
      </div>

      <p className="mt-6 text-center text-[10px] text-[var(--color-ink-secondary)]">
        This is a demo order lookup. Only fixture orders are searchable.
      </p>
    </div>
  );
}
