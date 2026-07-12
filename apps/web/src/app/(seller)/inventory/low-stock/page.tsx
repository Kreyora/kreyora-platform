"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { InventoryItem, Product } from "@/lib/types";

export default function LowStockPage() {
  const { inventory, catalog } = useClients();
  const { effectiveRole } = useSession();
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [variantToProduct, setVariantToProduct] = useState<Record<string, string>>({});
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    Promise.all([inventory.getLowStock(), catalog.listProducts()]).then(
      ([lowStock, productResult]) => {
        setItems(lowStock);
        const map: Record<string, string> = {};
        for (const p of productResult.items) {
          for (const v of p.variants) {
            map[v.id] = p.id;
          }
        }
        setVariantToProduct(map);
        setIsLoading(false);
      },
    );
  }, [inventory, catalog]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-48" />
        <Skeleton className="mb-6 h-4 w-64" />
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-md)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      {/* Header */}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">
          Low Stock Alerts
        </h1>
        {effectiveRole === "viewer" && <ViewerBadge />}
      </div>
      <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
        Products with stock at or below their low-stock threshold.
      </p>

      {items.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="No low-stock items"
            description="All products have adequate stock levels."
          />
        </div>
      ) : (
        <>
          {/* Desktop table */}
          <div className="mt-6 hidden overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)] md:block">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Product
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Variant
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    SKU
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    On Hand / Threshold
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Available
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]" />
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr
                    key={item.id}
                    className="border-b border-[var(--color-border)] last:border-b-0"
                  >
                    <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">
                      {item.productTitle}
                    </td>
                    <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                      {item.variantName}
                    </td>
                    <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                      {item.sku}
                    </td>
                    <td className="px-4 py-3">
                      <span className="inline-flex items-center gap-2">
                        <span className="font-medium text-[var(--color-ink-primary)]">
                          {item.onHand}
                        </span>
                        <span className="text-[var(--color-ink-secondary)]">/</span>
                        <span className="text-[var(--color-ink-secondary)]">
                          {item.lowStockThreshold}
                        </span>
                        {item.onHand <= item.lowStockThreshold && (
                          <Badge variant="danger">Low</Badge>
                        )}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-[var(--color-ink-primary)]">
                      {item.available}
                    </td>
                    <td className="px-4 py-3">
                      <Link
                        href={`/catalog/${variantToProduct[item.variantId] ?? item.variantId}/inventory`}
                        className="text-sm font-medium text-[var(--color-ink-primary)] hover:underline"
                      >
                        View inventory
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile cards */}
          <div className="mt-6 flex flex-col gap-3 md:hidden">
            {items.map((item) => (
              <div
                key={item.id}
                className="rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-4"
              >
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <p className="text-sm font-medium text-[var(--color-ink-primary)]">
                      {item.productTitle}
                    </p>
                    <p className="text-xs text-[var(--color-ink-secondary)]">
                      {item.variantName} · {item.sku}
                    </p>
                  </div>
                  <Badge variant="danger">Low</Badge>
                </div>
                <div className="mt-3 flex gap-4 text-xs">
                  <div>
                    <span className="font-medium text-[var(--color-ink-primary)]">
                      {item.onHand}
                    </span>
                    <span className="text-[var(--color-ink-secondary)]"> on hand</span>
                  </div>
                  <div>
                    <span className="font-medium text-[var(--color-ink-primary)]">
                      {item.available}
                    </span>
                    <span className="text-[var(--color-ink-secondary)]"> available</span>
                  </div>
                </div>
                <Link
                  href={`/catalog/${variantToProduct[item.variantId] ?? item.variantId}/inventory`}
                  className="mt-3 inline-block text-xs font-medium text-[var(--color-ink-primary)] hover:underline"
                >
                  View inventory →
                </Link>
              </div>
            ))}
          </div>
        </>
      )}

      <p className="mt-6 text-xs text-[var(--color-ink-secondary)]">
        {items.length} low-stock item{items.length !== 1 ? "s" : ""}
      </p>
    </div>
  );
}
