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
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { ACTION_DEFS, type ActionDef } from "@/lib/utils/order-actions";
import { ApiClientError } from "@/lib/api";
import type {
  Order,
  OrderActivity,
  InventoryItem,
  OrderActionEvaluation,
  OrderNotification,
  OrderAction,
} from "@/lib/types";
import type { PaymentAttempt } from "@/lib/types/payments";
import type { BadgeVariant } from "@/components/ui/badge";

const ORDER_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  draft: { label: "Draft", variant: "neutral" },
  awaiting_customer: { label: "Awaiting Customer", variant: "warning" },
  pending_confirmation: { label: "Pending Confirmation", variant: "warning" },
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

const NOTIF_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  delivered: { label: "Delivered", variant: "success" },
  delivering: { label: "Delivering", variant: "info" },
  pending: { label: "Pending", variant: "warning" },
  failed: { label: "Failed", variant: "danger" },
  dead_lettered: { label: "Dead Lettered", variant: "danger" },
};

function formatTemplateCode(code: string): string {
  return code
    .replace(/[._-]/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { order: orderClient, payment, inventory } = useClients();
  const { effectiveRole, session } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [order, setOrder] = useState<Order | null>(null);
  const [activities, setActivities] = useState<OrderActivity[]>([]);
  const [attempts, setAttempts] = useState<PaymentAttempt[]>([]);
  const [notifications, setNotifications] = useState<OrderNotification[]>([]);
  const [allowedActions, setAllowedActions] = useState<OrderActionEvaluation[]>([]);
  const [inventoryMap, setInventoryMap] = useState<Record<string, InventoryItem>>({});

  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [conflictError, setConflictError] = useState<string | null>(null);
  const [actionSuccessMessage, setActionSuccessMessage] = useState<string | null>(null);

  // Dialog & execution states
  const [activeAction, setActiveAction] = useState<ActionDef | null>(null);
  const [actionReason, setActionReason] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);
  const [executing, setExecuting] = useState(false);
  const [selectedAttemptId, setSelectedAttemptId] = useState<string | null>(null);

  // QR Proof preview dialog
  const [proofPreviewUrl, setProofPreviewUrl] = useState<string | null>(null);
  const [proofPreviewAttempt, setProofPreviewAttempt] = useState<PaymentAttempt | null>(null);

  const loadData = useCallback(async () => {
    setIsLoading(true);
    try {
      const [o, acts, pa, notifs, evals] = await Promise.all([
        orderClient.getOrder(id),
        orderClient.getOrderActivity(id),
        payment.getPaymentAttempts(id),
        orderClient.getOrderNotifications(id),
        orderClient.getAllowedActions(id),
      ]);

      setOrder(o);
      setActivities(acts);
      setAttempts(pa);
      setNotifications(notifs);
      setAllowedActions(evals);

      const invMap: Record<string, InventoryItem> = {};
      await Promise.all(
        o.items.map(async (item) => {
          try {
            const inv = await inventory.getInventory(item.variantId);
            invMap[item.variantId] = inv;
          } catch {
            // variant may lack stock record
          }
        }),
      );
      setInventoryMap(invMap);
      setConflictError(null);
      setLoadError(null);
    } catch (err: unknown) {
      setLoadError(err instanceof Error ? err.message : "Failed to load order details.");
    } finally {
      setIsLoading(false);
    }
  }, [id, orderClient, payment, inventory]);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      orderClient.getOrder(id),
      orderClient.getOrderActivity(id),
      payment.getPaymentAttempts(id),
      orderClient.getOrderNotifications(id),
      orderClient.getAllowedActions(id),
    ])
      .then(async ([o, acts, pa, notifs, evals]) => {
        if (cancelled) return;
        setOrder(o);
        setActivities(acts);
        setAttempts(pa);
        setNotifications(notifs);
        setAllowedActions(evals);

        const invMap: Record<string, InventoryItem> = {};
        await Promise.all(
          o.items.map(async (item) => {
            try {
              const inv = await inventory.getInventory(item.variantId);
              invMap[item.variantId] = inv;
            } catch {
              // variant may lack stock record
            }
          }),
        );
        if (!cancelled) {
          setInventoryMap(invMap);
          setIsLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setLoadError(err instanceof Error ? err.message : "Failed to load order details.");
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [id, orderClient, payment, inventory]);

  const handleOpenActionDialog = (def: ActionDef, attemptId?: string) => {
    setActiveAction(def);
    setActionReason("");
    setActionError(null);
    setSelectedAttemptId(attemptId ?? null);
  };

  const handleExecuteAction = async () => {
    if (!activeAction || !order) return;

    if (activeAction.requiresReason && actionReason.trim().length < 3) {
      setActionError("Please provide a reason (minimum 3 characters).");
      return;
    }

    setExecuting(true);
    setActionError(null);

    // Resolve target payment attempt if applicable
    const targetAttemptId =
      selectedAttemptId ||
      attempts.find((a) => a.status === "awaiting_verification")?.id ||
      attempts[0]?.id;

    try {
      const result = await orderClient.executeAction(order.id, {
        action: activeAction.action,
        reason: actionReason.trim() || undefined,
        expectedVersion: order.rowVersion ?? 0,
        paymentAttemptId: targetAttemptId,
      });

      setActiveAction(null);
      setActionReason("");
      setSelectedAttemptId(null);
      setProofPreviewUrl(null);
      setActionSuccessMessage(`Action "${activeAction.label}" succeeded. Order status is now ${result.status}.`);
      await loadData();
    } catch (err: unknown) {
      const isConflict =
        (err instanceof ApiClientError && err.status === 409) ||
        (typeof err === "object" && err !== null && "status" in err && (err as { status?: number }).status === 409) ||
        (err instanceof Error &&
          (err.message.includes("409") ||
            err.message.toLowerCase().includes("conflict") ||
            err.message.toLowerCase().includes("modified")));

      if (isConflict) {
        setActiveAction(null);
        setActionReason("");
        setConflictError(
          "Order was modified by another operator or background job. Please refresh to load the latest state before retrying.",
        );
      } else {
        setActionError(err instanceof Error ? err.message : "Failed to execute action.");
      }
    } finally {
      setExecuting(false);
    }
  };

  if (isLoading) {
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

  if (loadError || !order) {
    return (
      <div className="rounded-[var(--radius-lg)] border border-[var(--color-danger)] bg-[var(--color-danger-subtle)] p-6 text-center">
        <h2 className="text-base font-semibold text-[var(--color-danger)]">Unable to load order</h2>
        <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">{loadError ?? "Order not found."}</p>
        <div className="mt-4 flex justify-center gap-3">
          <Link href="/orders">
            <Button variant="outline">Back to Orders</Button>
          </Link>
          <Button onClick={() => loadData()}>Try Again</Button>
        </div>
      </div>
    );
  }

  const os = ORDER_STATUS[order.status] ?? { label: order.status, variant: "neutral" as const };
  const ps = PAYMENT_STATUS[order.paymentStatus] ?? { label: order.paymentStatus, variant: "neutral" as const };
  const fs = FULFILMENT_STATUS[order.fulfilmentStatus] ?? { label: order.fulfilmentStatus, variant: "neutral" as const };

  // Filter allowed actions evaluated by server
  const serverAllowedActions = allowedActions
    .filter((e) => e.isAllowed)
    .map((e) => ACTION_DEFS[e.action as OrderAction])
    .filter(Boolean);

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/orders" className="hover:underline">
          Orders
        </Link>
        <span className="mx-2" aria-hidden="true">
          /
        </span>
        <span className="text-[var(--color-ink-primary)]">{order.orderNumber}</span>
      </nav>

      {/* 409 Concurrency Conflict Banner */}
      {conflictError && (
        <div
          role="alert"
          aria-live="assertive"
          className="mb-6 flex flex-col items-start justify-between gap-3 rounded-[var(--radius-lg)] border border-[var(--color-danger)] bg-[var(--color-danger-subtle)] p-4 text-sm text-[var(--color-ink-primary)] sm:flex-row sm:items-center"
        >
          <div className="flex items-center gap-2">
            <span className="font-semibold text-[var(--color-danger)]">Version Conflict (409):</span>
            <span>{conflictError}</span>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              setConflictError(null);
              loadData();
            }}
          >
            Refresh Order
          </Button>
        </div>
      )}

      {/* Success Notification Banner */}
      {actionSuccessMessage && (
        <div
          role="status"
          className="mb-6 flex items-center justify-between rounded-[var(--radius-lg)] border border-[var(--color-success)] bg-[var(--color-success-subtle)] p-4 text-sm text-[var(--color-ink-primary)]"
        >
          <span>{actionSuccessMessage}</span>
          <button
            type="button"
            onClick={() => setActionSuccessMessage(null)}
            className="text-xs font-medium text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]"
          >
            Dismiss
          </button>
        </div>
      )}

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
          {/* Items & Financial snapshot */}
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
                      <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{item.sku || "—"}</td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">{item.quantity}</td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">
                        Rs. {item.unitPrice.amount.toLocaleString("en-IN")}
                      </td>
                      <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">
                        Rs. {item.lineTotal.amount.toLocaleString("en-IN")}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="mt-3 flex flex-col items-end gap-1 text-sm">
              <div className="flex gap-8">
                <span className="text-[var(--color-ink-secondary)]">Subtotal</span>
                <span>Rs. {order.subtotal.amount.toLocaleString("en-IN")}</span>
              </div>
              <div className="flex gap-8">
                <span className="text-[var(--color-ink-secondary)]">Delivery</span>
                <span>
                  {order.deliveryFee.amount === 0 ? "Free" : `Rs. ${order.deliveryFee.amount.toLocaleString("en-IN")}`}
                </span>
              </div>
              <div className="flex gap-8 font-semibold">
                <span>Total</span>
                <span>Rs. {order.total.amount.toLocaleString("en-IN")}</span>
              </div>
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
                    <div
                      key={item.variantId}
                      className="flex items-center justify-between rounded-[var(--radius-md)] border border-[var(--color-border)] px-4 py-2.5 text-sm"
                    >
                      <span className="text-[var(--color-ink-primary)]">
                        {item.variantName} {item.sku ? `(${item.sku})` : ""}
                      </span>
                      {inv ? (
                        <span className="text-[var(--color-ink-secondary)]">
                          {inv.available} available / {inv.onHand} on hand
                        </span>
                      ) : (
                        <span className="text-[var(--color-ink-secondary)]">No stock data</span>
                      )}
                    </div>
                  );
                })}
              </div>
            </section>
          )}

          {/* Payment Attempts & QR Proof Review */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Payment</h2>
            <p className="text-sm text-[var(--color-ink-secondary)]">
              Method:{" "}
              <span className="font-medium text-[var(--color-ink-primary)]">
                {order.paymentMethod === "cod" ? "Cash on Delivery" : "Merchant QR (Fonepay / eSewa)"}
              </span>
            </p>
            {attempts.length > 0 ? (
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
                      const proofUrl = a.proofId ? payment.getProofContentUrl(a.proofId) : a.proofUrl;

                      return (
                        <tr key={a.id} className="border-b border-[var(--color-border)] last:border-b-0">
                          <td className="px-4 py-3 capitalize text-[var(--color-ink-primary)]">
                            {a.method.replace("_", " ")}
                          </td>
                          <td className="px-4 py-3">Rs. {a.amount.amount.toLocaleString("en-IN")}</td>
                          <td className="px-4 py-3">
                            <Badge variant={as.variant}>{as.label}</Badge>
                          </td>
                          <td className="px-4 py-3">
                            {proofUrl ? (
                              <Button
                                variant="outline"
                                size="sm"
                                onClick={() => {
                                  setProofPreviewUrl(proofUrl);
                                  setProofPreviewAttempt(a);
                                }}
                              >
                                Review Proof
                              </Button>
                            ) : (
                              <span className="text-xs text-[var(--color-ink-secondary)]">—</span>
                            )}
                          </td>
                          <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                            {new Date(a.createdAt).toLocaleDateString()}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
              <p className="mt-2 text-xs text-[var(--color-ink-secondary)]">No payment attempts recorded.</p>
            )}
          </section>

          {/* Activity Timeline */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Activity Timeline</h2>
            {activities.length > 0 ? (
              <div className="flex flex-col gap-3">
                {activities.map((a) => (
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
                        <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">Reason: {a.reason}</p>
                      )}
                      {a.details && Object.keys(a.details).length > 0 && (
                        <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
                          {Object.entries(a.details)
                            .map(([k, v]) => `${k}: ${v}`)
                            .join(", ")}
                        </p>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-xs text-[var(--color-ink-secondary)]">No activity recorded for this order yet.</p>
            )}
          </section>

          {/* Real Notification Delivery Status */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Notification Delivery</h2>
            {notifications.length > 0 ? (
              <div className="flex flex-col gap-2">
                {notifications.map((n) => {
                  const nb = NOTIF_STATUS[n.status] ?? { label: n.status, variant: "neutral" as const };
                  return (
                    <div
                      key={n.id}
                      className="flex flex-col gap-2 rounded-[var(--radius-md)] border border-[var(--color-border)] p-3 sm:flex-row sm:items-center sm:justify-between text-sm"
                    >
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-medium text-[var(--color-ink-primary)]">
                            {formatTemplateCode(n.templateCode)}
                          </span>
                          <span className="text-xs text-[var(--color-ink-secondary)]">
                            via {n.channel.toUpperCase()}
                          </span>
                        </div>
                        <p className="text-xs text-[var(--color-ink-secondary)]">
                          Recipient: {n.recipientContactRedacted} · Attempts: {n.attemptCount}
                          {n.deliveredAt && ` · Delivered: ${new Date(n.deliveredAt).toLocaleTimeString()}`}
                          {n.deadLetteredAt && ` · Dead-lettered: ${new Date(n.deadLetteredAt).toLocaleTimeString()}`}
                        </p>
                      </div>
                      <div>
                        <Badge variant={nb.variant}>{nb.label}</Badge>
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <p className="text-xs text-[var(--color-ink-secondary)]">No notifications recorded for this order.</p>
            )}
          </section>
        </div>

        {/* Sidebar */}
        <div className="space-y-6 lg:self-start">
          {/* Customer snapshot */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Customer</h3>
            <div className="mt-3 space-y-1 text-sm">
              <p className="font-medium text-[var(--color-ink-primary)]">{order.customerName}</p>
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
              <p>
                {order.deliveryAddress.city}, {order.deliveryAddress.district}
              </p>
              <p>
                {order.deliveryAddress.contactName} · {order.deliveryAddress.contactPhone}
              </p>
            </div>
          </div>

          {/* Server-Evaluated Actions */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Actions</h3>
              {isViewer && <ViewerBadge />}
            </div>

            {isViewer ? (
              <p className="mt-3 text-xs text-[var(--color-ink-secondary)]">
                Viewer role has read-only access. Order state-changing actions are disabled.
              </p>
            ) : serverAllowedActions.length > 0 ? (
              <div className="mt-3 flex flex-col gap-2">
                {serverAllowedActions.map((def) => (
                  <Button
                    key={def.action}
                    variant={def.variant}
                    className="w-full"
                    onClick={() => handleOpenActionDialog(def)}
                  >
                    {def.label}
                  </Button>
                ))}
              </div>
            ) : (
              <p className="mt-3 text-xs text-[var(--color-ink-secondary)]">
                No actions available for this order in its current state.
              </p>
            )}
          </div>
        </div>
      </div>

      {/* Accessible Radix Action Confirmation Dialog */}
      <Dialog
        open={Boolean(activeAction)}
        onOpenChange={(open) => {
          if (!open) {
            setActiveAction(null);
            setActionReason("");
            setActionError(null);
          }
        }}
      >
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>{activeAction?.label}</DialogTitle>
            <DialogDescription>
              {activeAction?.destructive
                ? `This action cannot be undone. Are you sure you want to proceed with ${activeAction.label.toLowerCase()} for order ${order.orderNumber}?`
                : `Are you sure you want to proceed with "${activeAction?.label}" for order ${order.orderNumber}?`}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <p className="text-xs text-[var(--color-ink-secondary)]">
              Actor: {session?.user.displayName ?? "Current User"}
            </p>

            {actionError && (
              <div
                role="alert"
                className="rounded-[var(--radius-md)] border border-[var(--color-danger)] bg-[var(--color-danger-subtle)] p-3 text-xs text-[var(--color-danger)]"
              >
                {actionError}
              </div>
            )}

            {activeAction?.requiresReason && (
              <div>
                <Textarea
                  label="Reason (required, min 3 characters)"
                  value={actionReason}
                  onChange={(e) => {
                    setActionReason(e.target.value);
                    if (actionError) setActionError(null);
                  }}
                  rows={3}
                  placeholder="Enter reason for this action..."
                  aria-required="true"
                />
              </div>
            )}
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              disabled={executing}
              onClick={() => {
                setActiveAction(null);
                setActionReason("");
                setActionError(null);
              }}
            >
              Cancel
            </Button>
            <Button
              onClick={handleExecuteAction}
              loading={executing}
              disabled={Boolean(activeAction?.requiresReason && actionReason.trim().length < 3)}
            >
              {activeAction?.label}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Payment Proof Review Dialog */}
      <Dialog
        open={Boolean(proofPreviewUrl)}
        onOpenChange={(open) => {
          if (!open) {
            setProofPreviewUrl(null);
            setProofPreviewAttempt(null);
          }
        }}
      >
        <DialogContent className="max-w-xl">
          <DialogHeader>
            <DialogTitle>Payment Proof Review</DialogTitle>
            <DialogDescription>
              Customer-uploaded transaction receipt for Merchant QR verification.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-2">
            {proofPreviewUrl && (
              <div className="flex max-h-[60vh] items-center justify-center overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-2">
                {/* Render proof image */}
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={proofPreviewUrl}
                  alt="Customer Payment Receipt"
                  className="max-h-[55vh] w-auto object-contain rounded"
                />
              </div>
            )}

            {proofPreviewAttempt && (
              <div className="rounded-[var(--radius-md)] border border-[var(--color-border)] p-3 text-xs text-[var(--color-ink-secondary)]">
                <p>
                  <strong className="text-[var(--color-ink-primary)]">Attempt ID:</strong> {proofPreviewAttempt.id}
                </p>
                <p>
                  <strong className="text-[var(--color-ink-primary)]">Amount:</strong> Rs.{" "}
                  {proofPreviewAttempt.amount.amount.toLocaleString("en-IN")}
                </p>
                <p>
                  <strong className="text-[var(--color-ink-primary)]">Uploaded:</strong>{" "}
                  {new Date(proofPreviewAttempt.createdAt).toLocaleString()}
                </p>
              </div>
            )}
          </div>

          <DialogFooter>
            {proofPreviewAttempt?.status === "awaiting_verification" && !isViewer && (
              <div className="flex w-full justify-between gap-2">
                <Button
                  variant="ghost"
                  onClick={() => {
                    const rejectDef = ACTION_DEFS.reject_payment;
                    handleOpenActionDialog(rejectDef, proofPreviewAttempt.id);
                  }}
                >
                  Reject Proof
                </Button>
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    onClick={() => {
                      setProofPreviewUrl(null);
                      setProofPreviewAttempt(null);
                    }}
                  >
                    Close
                  </Button>
                  <Button
                    onClick={() => {
                      const verifyDef = ACTION_DEFS.verify_payment;
                      handleOpenActionDialog(verifyDef, proofPreviewAttempt.id);
                    }}
                  >
                    Verify Payment
                  </Button>
                </div>
              </div>
            )}
            {!(proofPreviewAttempt?.status === "awaiting_verification" && !isViewer) && (
              <Button
                variant="outline"
                onClick={() => {
                  setProofPreviewUrl(null);
                  setProofPreviewAttempt(null);
                }}
              >
                Close
              </Button>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
