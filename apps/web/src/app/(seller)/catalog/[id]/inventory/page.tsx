"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type {
  Product,
  ProductVariant,
  InventoryItem,
  StockMovement,
  InventoryReservation,
  ReservationState,
  StockMovementType,
} from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const MOVEMENT_BADGE: Record<StockMovementType, { label: string; variant: BadgeVariant }> = {
  initial: { label: "Initial", variant: "info" },
  adjustment: { label: "Adjustment", variant: "neutral" },
  reservation: { label: "Reservation", variant: "warning" },
  commitment: { label: "Commitment", variant: "info" },
  release: { label: "Release", variant: "success" },
  expiry: { label: "Expiry", variant: "danger" },
  return: { label: "Return", variant: "success" },
};

const RESERVATION_BADGE: Record<ReservationState, { label: string; variant: BadgeVariant }> = {
  active: { label: "Active", variant: "info" },
  committed: { label: "Committed", variant: "success" },
  released: { label: "Released", variant: "neutral" },
  expired: { label: "Expired", variant: "warning" },
};

interface VariantInventoryData {
  variant: ProductVariant;
  inventory: InventoryItem | null;
  movements: StockMovement[];
  reservations: InventoryReservation[];
}

export default function ProductInventoryPage() {
  const { id } = useParams<{ id: string }>();
  const { catalog, inventory } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [product, setProduct] = useState<Product | null>(null);
  const [variantData, setVariantData] = useState<VariantInventoryData[]>([]);
  const [selectedVariantId, setSelectedVariantId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const [adjQty, setAdjQty] = useState("");
  const [adjReason, setAdjReason] = useState("");
  const [adjusting, setAdjusting] = useState(false);
  const [adjustSuccess, setAdjustSuccess] = useState(false);
  const [adjustError, setAdjustError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    catalog.getProduct(id).then(async (p) => {
      if (cancelled) return;
      setProduct(p);

      const data = await Promise.all(
        p.variants.map(async (v) => {
          try {
            const [inv, mov, res] = await Promise.all([
              inventory.getInventory(v.id),
              inventory.getStockMovements(v.id),
              inventory.getReservations(v.id),
            ]);
            return { variant: v, inventory: inv, movements: mov.items, reservations: res };
          } catch {
            return { variant: v, inventory: null, movements: [], reservations: [] };
          }
        }),
      );
      if (!cancelled) {
        setVariantData(data);
        if (data.length > 0) setSelectedVariantId(data[0].variant.id);
        setIsLoading(false);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [catalog, inventory, id]);

  const handleAdjust = useCallback(async () => {
    const quantity = Number(adjQty);
    if (!selectedVariantId || !Number.isInteger(quantity) || quantity === 0 || !adjReason.trim()) {
      setAdjustError("Enter a non-zero whole quantity and a reason.");
      return;
    }
    setAdjusting(true);
    setAdjustError(null);
    try {
      const updated = await inventory.adjustStock({
        variantId: selectedVariantId,
        type: quantity > 0 ? "receipt" : "correctionDecrease",
        quantity: Math.abs(quantity),
        reason: adjReason.trim(),
      });
      setVariantData((current) => current.map((entry) => entry.variant.id === selectedVariantId ? { ...entry, inventory: updated } : entry));
      setAdjusting(false);
      setAdjustSuccess(true);
      setAdjQty("");
      setAdjReason("");
      setTimeout(() => setAdjustSuccess(false), 3000);
    } catch (caught) {
      setAdjustError(caught instanceof Error ? caught.message : "We could not adjust stock. Please try again.");
      setAdjusting(false);
    }
  }, [adjQty, adjReason, inventory, selectedVariantId]);

  if (isLoading || !product) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-64" />
        <Skeleton className="mb-6 h-8 w-48" />
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  const selected = variantData.find((d) => d.variant.id === selectedVariantId);

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/catalog" className="hover:underline">Catalog</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <Link href={`/catalog/${id}`} className="hover:underline">{product.title}</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">Inventory</span>
      </nav>

      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">
          Inventory — {product.title}
        </h1>
        {isViewer && <ViewerBadge />}
      </div>

      {/* Variant inventory cards */}
      <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {variantData.map(({ variant, inventory: inv }) => (
          <button
            key={variant.id}
            type="button"
            onClick={() => setSelectedVariantId(variant.id)}
            className={[
              "rounded-[var(--radius-lg)] border p-4 text-left transition-colors",
              variant.id === selectedVariantId
                ? "border-[var(--color-surface-dark)] bg-[var(--color-canvas-subtle)]"
                : "border-[var(--color-border)] hover:bg-[var(--color-canvas-subtle)]",
            ].join(" ")}
          >
            <div className="flex items-start justify-between gap-2">
              <div>
                <p className="text-sm font-medium text-[var(--color-ink-primary)]">{variant.name}</p>
                <p className="text-xs text-[var(--color-ink-secondary)]">{variant.sku}</p>
              </div>
              {inv?.isLowStock && <Badge variant="danger">Low stock</Badge>}
            </div>
            {inv && (
              <div className="mt-3 grid grid-cols-3 gap-2 text-center text-xs">
                <div>
                  <p className="font-medium text-[var(--color-ink-primary)]">{inv.onHand}</p>
                  <p className="text-[var(--color-ink-secondary)]">On hand</p>
                </div>
                <div>
                  <p className="font-medium text-[var(--color-ink-primary)]">{inv.committed}</p>
                  <p className="text-[var(--color-ink-secondary)]">Committed</p>
                </div>
                <div>
                  <p className="font-medium text-[var(--color-ink-primary)]">{inv.available}</p>
                  <p className="text-[var(--color-ink-secondary)]">Available</p>
                </div>
              </div>
            )}
            {inv && (
              <p className="mt-2 text-[10px] text-[var(--color-ink-secondary)]">
                Threshold: {inv.lowStockThreshold}
              </p>
            )}
            {!inv && (
              <p className="mt-3 text-xs text-[var(--color-ink-secondary)]">
                No inventory data
              </p>
            )}
          </button>
        ))}
      </div>

      {/* Selected variant details */}
      {selected && selected.inventory && (
        <div className="mt-8 space-y-8">
          {/* Stock ledger */}
          <div>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
              Stock Ledger — {selected.variant.name}
            </h2>
            {selected.movements.length === 0 ? (
              <EmptyState title="No stock movements" description="No movements recorded for this variant." />
            ) : (
              <div className="overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead>
                      <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Date</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Type</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Qty</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Reason</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Actor</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selected.movements.map((m) => {
                        const movBadge = MOVEMENT_BADGE[m.type] ?? { label: m.type, variant: "neutral" as const };
                        return (
                          <tr key={m.id} className="border-b border-[var(--color-border)] last:border-b-0">
                            <td className="whitespace-nowrap px-4 py-3 text-[var(--color-ink-secondary)]">
                              {new Date(m.createdAt).toLocaleDateString()}
                            </td>
                            <td className="px-4 py-3">
                              <Badge variant={movBadge.variant}>{movBadge.label}</Badge>
                            </td>
                            <td className={`px-4 py-3 font-medium ${m.quantity >= 0 ? "text-[var(--color-success)]" : "text-[var(--color-danger)]"}`}>
                              {m.quantity > 0 ? `+${m.quantity}` : m.quantity}
                            </td>
                            <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                              {m.reason || "—"}
                            </td>
                            <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                              {m.actorId}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </div>

          {/* Reservations */}
          <div>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
              Reservations — {selected.variant.name}
            </h2>
            {selected.reservations.length === 0 ? (
              <EmptyState title="No reservations" description="No active or past reservations for this variant." />
            ) : (
              <div className="overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead>
                      <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Source</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Qty</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">State</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Reference</th>
                        <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Expires</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selected.reservations.map((r) => {
                        const resBadge = RESERVATION_BADGE[r.state];
                        return (
                          <tr key={r.id} className="border-b border-[var(--color-border)] last:border-b-0">
                            <td className="px-4 py-3 text-[var(--color-ink-primary)] capitalize">
                              {r.source}
                            </td>
                            <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">
                              {r.quantity}
                            </td>
                            <td className="px-4 py-3">
                              <Badge variant={resBadge.variant}>{resBadge.label}</Badge>
                            </td>
                            <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                              {r.referenceId}
                            </td>
                            <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                              {new Date(r.expiresAt).toLocaleDateString()}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </div>

          {/* Stock adjustment form */}
          {!isViewer && (
            <div className="max-w-md rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
                Stock Adjustment
              </h2>
              <div className="space-y-4">
                <Input
                  label="Quantity"
                  type="number"
                  value={adjQty}
                  onChange={(e) => setAdjQty(e.target.value)}
                  placeholder="e.g. +10 or -5"
                />
                <Textarea
                  label="Reason"
                  value={adjReason}
                  onChange={(e) => setAdjReason(e.target.value)}
                  rows={2}
                  placeholder="Reason for adjustment"
                />
                <Button onClick={() => void handleAdjust()} loading={adjusting} disabled={!adjQty || !adjReason.trim()}>
                  Adjust stock
                </Button>
                {adjustSuccess && (
                  <p className="text-sm text-[var(--color-success)]">
                    Stock adjustment saved successfully.
                  </p>
                )}
                {adjustError && <p role="alert" className="text-sm text-[var(--color-danger)]">{adjustError}</p>}
              </div>
              <p className="mt-3 text-xs text-[var(--color-ink-secondary)]">
                Every adjustment is recorded in the append-only stock ledger.
              </p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
