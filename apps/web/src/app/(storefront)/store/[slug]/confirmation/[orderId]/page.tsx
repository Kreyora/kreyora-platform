"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import type { Order } from "@/lib/types";

export default function ConfirmationPage() {
  const { slug, orderId } = useParams<{ slug: string; orderId: string }>();
  const { order: orderClient } = useClients();
  const [order, setOrder] = useState<Order | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    orderClient
      .getOrder(orderId)
      .then((o) => {
        setOrder(o);
        setIsLoading(false);
      })
      .catch(() => {
        setError(true);
        setIsLoading(false);
      });
  }, [orderClient, orderId]);

  if (isLoading) {
    return (
      <div className="flex flex-col items-center py-12">
        <Skeleton className="mb-4 h-12 w-12 rounded-full" />
        <Skeleton className="mb-2 h-6 w-48" />
        <Skeleton className="h-4 w-64" />
      </div>
    );
  }

  if (error || !order) {
    return (
      <div className="flex flex-col items-center py-16 text-center">
        <h1 className="text-lg font-semibold text-[var(--color-ink-primary)]">
          Order not found
        </h1>
        <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
          We couldn&apos;t find this order. It may not exist yet in the demo.
        </p>
        <Link
          href={`/store/${slug}`}
          className="mt-4 inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)]"
        >
          Back to store
        </Link>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-lg">
      {/* Success header */}
      <div className="flex flex-col items-center py-8 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-full bg-[var(--color-canvas-subtle)]">
          <svg
            width="28"
            height="28"
            viewBox="0 0 24 24"
            fill="none"
            stroke="var(--color-success)"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M20 6 9 17l-5-5" />
          </svg>
        </div>
        <h1 className="mt-4 text-xl font-bold text-[var(--color-ink-primary)]">
          Order confirmed!
        </h1>
        <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
          Order number: <span className="font-semibold">{order.orderNumber}</span>
        </p>
      </div>

      {/* Order details */}
      <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium text-[var(--color-ink-primary)]">Status</span>
          <Badge variant="info">{order.status.replace(/_/g, " ")}</Badge>
        </div>

        <div className="mt-4 border-t border-[var(--color-border)] pt-4">
          <h3 className="text-sm font-medium text-[var(--color-ink-primary)]">Items</h3>
          <div className="mt-2 flex flex-col gap-2">
            {order.items.map((item) => (
              <div key={item.id} className="flex justify-between text-sm">
                <span className="text-[var(--color-ink-secondary)]">
                  {item.productTitle} ({item.variantName}) × {item.quantity}
                </span>
                <span className="text-[var(--color-ink-primary)]">
                  Rs. {item.lineTotal.amount.toLocaleString("en-IN")}
                </span>
              </div>
            ))}
          </div>
        </div>

        <div className="mt-4 space-y-1 border-t border-[var(--color-border)] pt-4 text-sm">
          <div className="flex justify-between">
            <span className="text-[var(--color-ink-secondary)]">Subtotal</span>
            <span>Rs. {order.subtotal.amount.toLocaleString("en-IN")}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-[var(--color-ink-secondary)]">Delivery</span>
            <span>
              {order.deliveryFee.amount === 0
                ? "Free"
                : `Rs. ${order.deliveryFee.amount.toLocaleString("en-IN")}`}
            </span>
          </div>
          <div className="flex justify-between font-semibold">
            <span>Total</span>
            <span>Rs. {order.total.amount.toLocaleString("en-IN")}</span>
          </div>
        </div>

        <div className="mt-4 border-t border-[var(--color-border)] pt-4 text-sm">
          <p className="text-[var(--color-ink-secondary)]">
            <span className="font-medium">Delivery to:</span> {order.deliveryAddress.line1}
            {order.deliveryAddress.line2 && `, ${order.deliveryAddress.line2}`}, {order.deliveryAddress.city}
          </p>
          <p className="mt-1 text-[var(--color-ink-secondary)]">
            <span className="font-medium">Payment:</span>{" "}
            {order.paymentMethod === "cod" ? "Cash on Delivery" : "Fonepay / eSewa QR"}
          </p>
        </div>
      </div>

      {/* Actions */}
      <div className="mt-6 flex flex-col items-center gap-3">
        <Link
          href={`/store/${slug}/order-lookup`}
          className="text-sm font-medium text-[var(--color-ink-primary)] hover:underline"
        >
          Track your order
        </Link>
        <Link
          href={`/store/${slug}`}
          className="text-sm text-[var(--color-ink-secondary)] hover:underline"
        >
          Continue shopping
        </Link>
      </div>

      <p className="mt-8 text-center text-[10px] text-[var(--color-ink-secondary)]">
        This is a simulated order confirmation. No real order has been placed.
      </p>
    </div>
  );
}
