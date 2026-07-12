"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import { getAllowedActions, type ActionDef } from "@/lib/utils/order-actions";
import type { Order, OrderActivity, InventoryItem } from "@/lib/types";
import type { PaymentAttempt } from "@/lib/types/payments";
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

const ATTEMPT_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  pending: { label: "Pending", variant: "warning" },
  awaiting_verification: { label: "Awaiting Verification", variant: "warning" },
  verified: { label: "Verified", variant: "success" },
  rejected: { label: "Rejected", variant: "danger" },
  failed: { label: "Failed", variant: "danger" },
};

interface NotificationEntry {
  id: string;
  type: string;
  channel: string;
  status: "delivered" | "pending" | "failed";
  sentAt: string;
}

function getSimulatedNotifications(order: Order): NotificationEntry[] {
  const entries: NotificationEntry[] = [
    {
      id: "notif-1",
      type: "Order confirmation",
      channel: "SMS",
      status: order.status !== "pending_confirmation" ? "delivered" : "pending",
      sentAt: order.createdAt,
    },
  ];
  if (order.paymentMethod === "merchant_qr" && order.paymentStatus === "awaiting_verification") {
    entries.push({
      id: "notif-2",
      type: "Payment reminder",
      channel: "SMS",
      status: "pending",
      sentAt: order.updatedAt,
    });
  }
  if (order.fulfilmentStatus === "dispatched" || order.fulfilmentStatus === "delivered") {
    entries.push({
      id: "notif-3",
      type: "Dispatch notification",
      channel: "SMS",
      status: "delivered",
      sentAt: order.updatedAt,
    });
  }
  return entries;
}

const NOTIF_BADGE: Record<string, { label: string; variant: BadgeVariant }> = {
  delivered: { label: "Delivered", variant: "success" },
  pending: { label: "Pending", variant: "warning" },
  failed: { label: "Failed", variant: "danger" },
};

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { order: orderClient, payment, inventory } = useClients();
  const { effectiveRole, session } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [order, setOrder] = useState<Order | null>(null);
  const [activities, setActivities] = useState<OrderActivity[]>([]);
  const [attempts, setAttempts] = useState<PaymentAttempt[]>([]);
  const [inventoryMap, setInventoryMap] = useState<Record<string, InventoryItem>>({});
  const [isLoading, setIsLoading] = useState(true);

  const [activeAction, setActiveAction] = useState<ActionDef | null>(null);
  const [actionReason, setActionReason] = useState("");
  const [executing, setExecuting] = useState(false);
  const [localActivities, setLocalActivities] = useState<OrderActivity[]>([]);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      orderClient.getOrder(id),
      orderClient.getOrderActivity(id),
      payment.getPaymentAttempts(id),
    ]).then(async ([o, acts, pa]) => {
      if (cancelled) return;
      setOrder(o);
      setActivities(acts);
      setAttempts(pa);

      const invMap: Record<string, InventoryItem> = {};
      await Promise.all(
        o.items.map(async (item) => {
          try {
            const inv = await inventory.getInventory(item.variantId);
            invMap[item.variantId] = inv;
          } catch { /* variant may lack inventory */ }
        }),
      );
      if (!cancelled) {
        setInventoryMap(invMap);
        setIsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, [orderClient, payment, inventory, id]);

  const handleExecuteAction = useCallback(() => {
    if (!activeAction || !order) return;
    setExecuting(true);
    setTimeout(() => {
      const newActivity: OrderActivity = {
        id: `oa-sim-${Date.now()}`,
        orderId: order.id,
        action: `order.${activeAction.action}`,
        actorId: session?.user.id ?? "system",
        actorName: session?.user.displayName ?? "System",
        reason: actionReason || undefined,
        createdAt: new Date().toISOString(),
      };
      setLocalActivities((prev) => [...prev, newActivity]);
      setExecuting(false);
      setActiveAction(null);
      setActionReason("");
    }, 600);
  }, [activeAction, order, session, actionReason]);

  if (isLoading || !order) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-48" />
        <Skeleton className="mb-6 h-8 w-64" />
        <div className="space-y-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  const os = ORDER_STATUS[order.status] ?? { label: order.status, variant: "neutral" as const };
  const ps = PAYMENT_STATUS[order.paymentStatus] ?? { label: order.paymentStatus, variant: "neutral" as const };
  const fs = FULFILMENT_STATUS[order.fulfilmentStatus] ?? { label: order.fulfilmentStatus, variant: "neutral" as const };
  const allowedActions = getAllowedActions(order.status, order.paymentStatus, order.fulfilmentStatus, order.paymentMethod, effectiveRole);
  const allActivities = [...activities, ...localActivities].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
  );
  const notifications = getSimulatedNotifications(order);

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/orders" className="hover:underline">Orders</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">{order.orderNumber}</span>
      </nav>

      {/* Header */}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">{order.orderNumber}</h1>
        {isViewer && <ViewerBadge />}
      </div>
      <div className="mt-2 flex flex-wrap gap-2">
        <Badge variant={os.variant}>{os.label}</Badge>
        <Badge variant={ps.variant}>Pay: {ps.label}</Badge>
        <Badge variant={fs.variant}>Ship: {fs.label}</Badge>
        <Badge variant="neutral">{order.source}</Badge>
      </div>

      <div className="mt-8 grid gap-8 lg:grid-cols-3">
        {/* Main content */}
        <div className="space-y-8 lg:col-span-2">
          {/* Financial snapshot */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Items</h2>
            <div className="overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
              <table className="w-full text-left text-sm">
                <thead>
                  <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                    <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Product</th>
                    <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">SKU</th>
                    <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Qty</th>
                    <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Price</th>
                    <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Total</th>
                  </tr>
                </thead>
                <tbody>
                  {order.items.map((item) => (
                    <tr key={item.id} className="border-b border-[var(--color-border)] last:border-b-0">
                      <td className="px-4 py-3">
                        <p className="font-medium text-[var(--color-ink-primary)]">{item.productTitle}</p>
                        <p className="text-xs text-[var(--color-ink-secondary)]">{item.variantName}</p>
                      </td>
                      <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{item.sku}</td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">{item.quantity}</td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">Rs. {item.unitPrice.amount.toLocaleString("en-IN")}</td>
                      <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">Rs. {item.lineTotal.amount.toLocaleString("en-IN")}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="mt-3 flex flex-col items-end gap-1 text-sm">
              <div className="flex gap-8"><span className="text-[var(--color-ink-secondary)]">Subtotal</span><span>Rs. {order.subtotal.amount.toLocaleString("en-IN")}</span></div>
              <div className="flex gap-8"><span className="text-[var(--color-ink-secondary)]">Delivery</span><span>{order.deliveryFee.amount === 0 ? "Free" : `Rs. ${order.deliveryFee.amount.toLocaleString("en-IN")}`}</span></div>
              <div className="flex gap-8 font-semibold"><span>Total</span><span>Rs. {order.total.amount.toLocaleString("en-IN")}</span></div>
            </div>
          </section>

          {/* Inventory allocation */}
          {Object.keys(inventoryMap).length > 0 && (
            <section>
              <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Inventory Allocation</h2>
              <div className="flex flex-col gap-2">
                {order.items.map((item) => {
                  const inv = inventoryMap[item.variantId];
                  return (
                    <div key={item.variantId} className="flex items-center justify-between rounded-[var(--radius-md)] border border-[var(--color-border)] px-4 py-2.5 text-sm">
                      <span className="text-[var(--color-ink-primary)]">{item.variantName} ({item.sku})</span>
                      {inv ? (
                        <span className="text-[var(--color-ink-secondary)]">
                          {inv.available} available / {inv.onHand} on hand
                        </span>
                      ) : (
                        <span className="text-[var(--color-ink-secondary)]">No data</span>
                      )}
                    </div>
                  );
                })}
              </div>
            </section>
          )}

          {/* Payment */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Payment</h2>
            <p className="text-sm text-[var(--color-ink-secondary)]">
              Method: <span className="font-medium text-[var(--color-ink-primary)]">{order.paymentMethod === "cod" ? "Cash on Delivery" : "Fonepay / eSewa QR"}</span>
            </p>
            {attempts.length > 0 && (
              <div className="mt-3 overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
                <table className="w-full text-left text-sm">
                  <thead>
                    <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Method</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Amount</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Status</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Proof</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Date</th>
                    </tr>
                  </thead>
                  <tbody>
                    {attempts.map((a) => {
                      const as = ATTEMPT_STATUS[a.status] ?? { label: a.status, variant: "neutral" as const };
                      return (
                        <tr key={a.id} className="border-b border-[var(--color-border)] last:border-b-0">
                          <td className="px-4 py-3 text-[var(--color-ink-primary)] capitalize">{a.method.replace("_", " ")}</td>
                          <td className="px-4 py-3">Rs. {a.amount.amount.toLocaleString("en-IN")}</td>
                          <td className="px-4 py-3"><Badge variant={as.variant}>{as.label}</Badge></td>
                          <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                            {a.proofUrl ? (
                              <span className="text-xs">Screenshot uploaded</span>
                            ) : (
                              <span className="text-xs">—</span>
                            )}
                          </td>
                          <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{new Date(a.createdAt).toLocaleDateString()}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {/* Activity timeline */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Activity Timeline</h2>
            <div className="flex flex-col gap-3">
              {allActivities.map((a) => (
                <div key={a.id} className="flex items-start gap-3 text-sm">
                  <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-[var(--color-ink-secondary)]" />
                  <div>
                    <span className="font-medium text-[var(--color-ink-primary)]">
                      {a.action.replace(/\./g, " ")}
                    </span>
                    <span className="text-[var(--color-ink-secondary)]">
                      {" "}by {a.actorName} · {new Date(a.createdAt).toLocaleString()}
                    </span>
                    {a.reason && (
                      <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
                        Reason: {a.reason}
                      </p>
                    )}
                    {a.details && Object.keys(a.details).length > 0 && (
                      <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
                        {Object.entries(a.details).map(([k, v]) => `${k}: ${v}`).join(", ")}
                      </p>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </section>

          {/* Notification delivery status */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Notification Delivery</h2>
            <div className="flex flex-col gap-2">
              {notifications.map((n) => {
                const nb = NOTIF_BADGE[n.status];
                return (
                  <div key={n.id} className="flex items-center justify-between rounded-[var(--radius-md)] border border-[var(--color-border)] px-4 py-2.5 text-sm">
                    <div>
                      <span className="text-[var(--color-ink-primary)]">{n.type}</span>
                      <span className="ml-2 text-xs text-[var(--color-ink-secondary)]">via {n.channel}</span>
                    </div>
                    <Badge variant={nb.variant}>{nb.label}</Badge>
                  </div>
                );
              })}
            </div>
            <p className="mt-2 text-[10px] text-[var(--color-ink-secondary)]">
              Notification delivery is simulated. No real messages are sent.
            </p>
          </section>
        </div>

        {/* Sidebar */}
        <div className="space-y-6 lg:self-start">
          {/* Customer snapshot */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Customer</h3>
            <div className="mt-3 space-y-1 text-sm">
              <p className="text-[var(--color-ink-primary)]">{order.customerName}</p>
              <p className="text-[var(--color-ink-secondary)]">{order.customerPhone}</p>
              {order.customerEmail && <p className="text-[var(--color-ink-secondary)]">{order.customerEmail}</p>}
            </div>
          </div>

          {/* Delivery snapshot */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Delivery</h3>
            <div className="mt-3 space-y-1 text-sm text-[var(--color-ink-secondary)]">
              <p>{order.deliveryAddress.line1}</p>
              {order.deliveryAddress.line2 && <p>{order.deliveryAddress.line2}</p>}
              <p>{order.deliveryAddress.city}, {order.deliveryAddress.district}</p>
              <p>{order.deliveryAddress.contactName} · {order.deliveryAddress.contactPhone}</p>
            </div>
          </div>

          {/* Actions */}
          {allowedActions.length > 0 && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Actions</h3>
              <div className="mt-3 flex flex-col gap-2">
                {allowedActions.map((def) => (
                  <Button
                    key={def.action}
                    variant={def.variant}
                    className="w-full"
                    onClick={() => setActiveAction(def)}
                  >
                    {def.label}
                  </Button>
                ))}
              </div>
              <p className="mt-3 text-[10px] text-[var(--color-ink-secondary)]">
                Actions are simulated and will not affect real data.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* Action confirmation dialog */}
      {activeAction && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="mx-4 w-full max-w-sm rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-6 shadow-lg">
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">
              {activeAction.label}
            </h2>
            <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
              {activeAction.destructive
                ? "This action cannot be undone."
                : `Proceed with "${activeAction.label}" for ${order.orderNumber}?`}
            </p>
            <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
              Actor: {session?.user.displayName ?? "System"}
            </p>

            {activeAction.requiresReason && (
              <div className="mt-4">
                <Textarea
                  label="Reason (required)"
                  value={actionReason}
                  onChange={(e) => setActionReason(e.target.value)}
                  rows={2}
                  placeholder="Enter reason..."
                />
              </div>
            )}

            <div className="mt-4 flex justify-end gap-3">
              <Button variant="outline" onClick={() => { setActiveAction(null); setActionReason(""); }}>
                Cancel
              </Button>
              <Button
                onClick={handleExecuteAction}
                loading={executing}
                disabled={activeAction.requiresReason && !actionReason.trim()}
              >
                {activeAction.label} (simulated)
              </Button>
            </div>

            <p className="mt-3 text-[10px] text-[var(--color-ink-secondary)]">
              This action is simulated. No real changes will be made.
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
