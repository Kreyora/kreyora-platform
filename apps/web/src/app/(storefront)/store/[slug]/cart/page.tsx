"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCart } from "@/hooks/use-cart";
import { Button } from "@/components/ui/button";

export default function CartPage() {
  const { slug } = useParams<{ slug: string }>();
  const { items, itemCount, subtotal, removeItem, updateQuantity } = useCart();

  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <svg
          width="48"
          height="48"
          viewBox="0 0 24 24"
          fill="none"
          stroke="var(--color-ink-secondary)"
          strokeWidth="1.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z" />
          <line x1="3" x2="21" y1="6" y2="6" />
          <path d="M16 10a4 4 0 0 1-8 0" />
        </svg>
        <h1 className="mt-4 text-lg font-semibold text-[var(--color-ink-primary)]">
          Your cart is empty
        </h1>
        <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
          Browse products and add items to your cart.
        </p>
        <Link
          href={`/store/${slug}`}
          className="mt-4 inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors hover:bg-[var(--color-canvas-subtle)]"
        >
          Continue shopping
        </Link>
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-xl font-bold text-[var(--color-ink-primary)] sm:text-2xl">
        Cart
      </h1>

      <div className="mt-6 grid gap-8 lg:grid-cols-3">
        {/* Line items */}
        <div className="lg:col-span-2">
          <div className="flex flex-col divide-y divide-[var(--color-border)]">
            {items.map((item) => (
              <div key={item.variantId} className="flex gap-4 py-4 first:pt-0 last:pb-0">
                {/* Image placeholder */}
                <div className="flex h-20 w-20 shrink-0 items-center justify-center rounded-[var(--radius-md)] bg-[var(--color-canvas-subtle)]">
                  <svg
                    width="20"
                    height="20"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="var(--color-ink-secondary)"
                    strokeWidth="1.5"
                    aria-hidden="true"
                  >
                    <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                    <circle cx="8.5" cy="8.5" r="1.5" />
                    <path d="m21 15-5-5L5 21" />
                  </svg>
                </div>

                {/* Details */}
                <div className="flex flex-1 flex-col gap-1">
                  <p className="text-sm font-medium text-[var(--color-ink-primary)]">
                    {item.productTitle}
                  </p>
                  <p className="text-xs text-[var(--color-ink-secondary)]">
                    {item.variantName}
                  </p>
                  <p className="text-sm text-[var(--color-ink-primary)]">
                    Rs. {item.unitPrice.amount.toLocaleString("en-IN")}
                  </p>

                  <div className="mt-2 flex items-center gap-3">
                    {/* Quantity controls */}
                    <div className="flex items-center rounded-[var(--radius-md)] border border-[var(--color-border)]">
                      <button
                        type="button"
                        onClick={() => updateQuantity(item.variantId, item.quantity - 1)}
                        className="min-h-9 min-w-9 text-sm text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)] transition-colors"
                        aria-label="Decrease quantity"
                      >
                        −
                      </button>
                      <span className="min-w-8 text-center text-sm font-medium text-[var(--color-ink-primary)]">
                        {item.quantity}
                      </span>
                      <button
                        type="button"
                        onClick={() => updateQuantity(item.variantId, item.quantity + 1)}
                        className="min-h-9 min-w-9 text-sm text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)] transition-colors"
                        aria-label="Increase quantity"
                      >
                        +
                      </button>
                    </div>

                    <button
                      type="button"
                      onClick={() => removeItem(item.variantId)}
                      className="text-xs text-[var(--color-ink-secondary)] hover:text-[var(--color-danger)] transition-colors"
                    >
                      Remove
                    </button>
                  </div>
                </div>

                {/* Line total */}
                <div className="text-right">
                  <p className="text-sm font-medium text-[var(--color-ink-primary)]">
                    Rs. {(item.unitPrice.amount * item.quantity).toLocaleString("en-IN")}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Summary */}
        <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5 lg:self-start">
          <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">
            Order Summary
          </h2>
          <div className="mt-4 flex flex-col gap-2 text-sm">
            <div className="flex justify-between">
              <span className="text-[var(--color-ink-secondary)]">
                Items ({itemCount})
              </span>
              <span className="font-medium text-[var(--color-ink-primary)]">
                Rs. {subtotal.toLocaleString("en-IN")}
              </span>
            </div>
            <div className="flex justify-between text-xs text-[var(--color-ink-secondary)]">
              <span>Delivery</span>
              <span>Calculated at checkout</span>
            </div>
          </div>
          <div className="mt-4 border-t border-[var(--color-border)] pt-4">
            <div className="flex justify-between">
              <span className="text-sm font-semibold text-[var(--color-ink-primary)]">
                Subtotal
              </span>
              <span className="text-base font-bold text-[var(--color-ink-primary)]">
                Rs. {subtotal.toLocaleString("en-IN")}
              </span>
            </div>
          </div>
          <Link href={`/store/${slug}/checkout`} className="mt-4 block">
            <Button className="w-full">Proceed to checkout</Button>
          </Link>
          <Link
            href={`/store/${slug}`}
            className="mt-3 block text-center text-xs text-[var(--color-ink-secondary)] hover:underline"
          >
            Continue shopping
          </Link>
        </div>
      </div>

      <p className="mt-8 text-center text-[10px] text-[var(--color-ink-secondary)]">
        This is a demo cart. No real transactions occur.
      </p>
    </div>
  );
}
