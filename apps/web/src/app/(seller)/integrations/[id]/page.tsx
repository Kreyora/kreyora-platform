"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import type { ChannelConnection, WebhookEvent } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const STATUS_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  connected: { label: "Connected", variant: "success" },
  disconnected: { label: "Disconnected", variant: "danger" },
  error: { label: "Error", variant: "danger" },
  pending_reauth: { label: "Reauth Needed", variant: "warning" },
};

const PROVIDER_MAP: Record<string, { label: string; color: string }> = {
  facebook: { label: "Facebook", color: "bg-blue-500" },
  instagram: { label: "Instagram", color: "bg-pink-500" },
  whatsapp: { label: "WhatsApp", color: "bg-green-500" },
  tiktok: { label: "TikTok", color: "bg-gray-800" },
};

const EVENT_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  processed: { label: "Processed", variant: "success" },
  failed: { label: "Failed", variant: "danger" },
  dead_letter: { label: "Dead Letter", variant: "danger" },
};

export default function IntegrationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { integration } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [connection, setConnection] = useState<ChannelConnection | null>(null);
  const [events, setEvents] = useState<WebhookEvent[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [replayedIds, setReplayedIds] = useState<Set<string>>(new Set());
  const [reconnecting, setReconnecting] = useState(false);
  const [currentTime] = useState(() => Date.now());

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      integration.getConnection(id),
      integration.getWebhookEvents(id),
    ]).then(([conn, evts]) => {
      if (cancelled) return;
      setConnection(conn);
      setEvents(evts.items);
      setIsLoading(false);
    });
    return () => { cancelled = true; };
  }, [integration, id]);

  const handleReplay = (eventId: string) => {
    setReplayedIds((prev) => new Set(prev).add(eventId));
  };

  const handleReconnect = () => {
    setReconnecting(true);
    setTimeout(() => setReconnecting(false), 1000);
  };

  if (isLoading || !connection) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-48" />
        <Skeleton className="mb-6 h-8 w-64" />
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  const sm = STATUS_MAP[connection.status] ?? { label: connection.status, variant: "neutral" as const };
  const pm = PROVIDER_MAP[connection.provider] ?? { label: connection.provider, color: "bg-gray-400" };
  const h = connection.health;
  const tokenExpiry = h.tokenExpiresAt ? new Date(h.tokenExpiresAt) : null;
  const daysUntilExpiry = tokenExpiry ? Math.ceil((tokenExpiry.getTime() - currentTime) / (1000 * 60 * 60 * 24)) : null;

  const capabilityRows = [
    { label: "Receive messages", value: connection.capabilities.canReceiveMessages },
    { label: "Send messages", value: connection.capabilities.canSendMessages },
    { label: "Send media", value: connection.capabilities.canSendMedia },
    { label: "Receive media", value: connection.capabilities.canReceiveMedia },
    { label: "Templates", value: connection.capabilities.supportsTemplates },
    { label: "Delivery receipts", value: connection.capabilities.supportsDeliveryReceipts },
  ];

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/integrations" className="hover:underline">Integrations</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">{connection.accountName}</span>
      </nav>

      {/* Header */}
      <div className="flex flex-wrap items-center gap-3">
        <span className={`flex h-10 w-10 items-center justify-center rounded-full text-xs font-bold text-white ${pm.color}`}>
          {pm.label.substring(0, 2).toUpperCase()}
        </span>
        <div>
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">{connection.accountName}</h1>
          <p className="text-xs text-[var(--color-ink-secondary)]">{pm.label} · {connection.accountIdentifier}</p>
        </div>
        <Badge variant={sm.variant} className="ml-auto">{sm.label}</Badge>
        {isViewer && <ViewerBadge />}
      </div>

      <div className="mt-8 grid gap-8 lg:grid-cols-3">
        <div className="space-y-8 lg:col-span-2">
          {/* Capabilities */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Capabilities</h2>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {capabilityRows.map((cap) => (
                <div
                  key={cap.label}
                  className="flex items-center gap-2 rounded-[var(--radius-md)] border border-[var(--color-border)] px-3 py-2.5 text-sm"
                >
                  <span className={cap.value ? "text-[var(--color-success)]" : "text-[var(--color-ink-secondary)]"}>
                    {cap.value ? "✓" : "✗"}
                  </span>
                  <span className="text-[var(--color-ink-primary)]">{cap.label}</span>
                </div>
              ))}
            </div>
          </section>

          {/* Webhook events */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">Webhook Events</h2>
            {events.length === 0 ? (
              <p className="text-sm text-[var(--color-ink-secondary)]">No webhook events recorded.</p>
            ) : (
              <div className="overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
                <table className="w-full text-left text-sm">
                  <thead>
                    <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Event</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Status</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Retries</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Reason</th>
                      <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Date</th>
                      {!isViewer && <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Action</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {events.map((e) => {
                      const es = EVENT_STATUS[e.status] ?? { label: e.status, variant: "neutral" as const };
                      const replayed = replayedIds.has(e.id);
                      return (
                        <tr key={e.id} className="border-b border-[var(--color-border)] last:border-b-0">
                          <td className="px-4 py-3 text-[var(--color-ink-primary)]">{e.eventType}</td>
                          <td className="px-4 py-3"><Badge variant={es.variant}>{es.label}</Badge></td>
                          <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{e.retryCount}</td>
                          <td className="px-4 py-3 text-xs text-[var(--color-ink-secondary)]">{e.failureReason ?? "—"}</td>
                          <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{new Date(e.createdAt).toLocaleString()}</td>
                          {!isViewer && (
                            <td className="px-4 py-3">
                              {e.status === "failed" && !replayed && (
                                <Button size="sm" variant="outline" onClick={() => handleReplay(e.id)}>
                                  Replay
                                </Button>
                              )}
                              {replayed && (
                                <span className="text-xs text-[var(--color-success)]">Replayed</span>
                              )}
                            </td>
                          )}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>

        {/* Sidebar */}
        <div className="space-y-6 lg:self-start">
          {/* Health */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Health</h3>
            <div className="mt-3 space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Status</span>
                <Badge variant={sm.variant}>{sm.label}</Badge>
              </div>
              {h.lastEventAt && (
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Last event</span>
                  <span className="text-[var(--color-ink-primary)]">{new Date(h.lastEventAt).toLocaleString()}</span>
                </div>
              )}
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Events (24h)</span>
                <span className="text-[var(--color-ink-primary)]">{h.eventsProcessed24h}</span>
              </div>
              {h.eventsFailed24h > 0 && (
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Failed (24h)</span>
                  <span className="text-[var(--color-danger)]">{h.eventsFailed24h}</span>
                </div>
              )}
              {tokenExpiry && (
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Token expires</span>
                  <span className={daysUntilExpiry !== null && daysUntilExpiry < 30 ? "text-[var(--color-warning)]" : "text-[var(--color-ink-primary)]"}>
                    {tokenExpiry.toLocaleDateString()}
                    {daysUntilExpiry !== null && daysUntilExpiry < 30 && ` (${daysUntilExpiry}d)`}
                  </span>
                </div>
              )}
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Webhook URL</span>
                <span className="max-w-[140px] truncate text-[10px] text-[var(--color-ink-primary)]">{h.webhookUrl}</span>
              </div>
            </div>
          </div>

          {/* Reconnect */}
          {!isViewer && (connection.status === "disconnected" || connection.status === "error") && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Actions</h3>
              <Button
                className="mt-3 w-full"
                variant="outline"
                loading={reconnecting}
                onClick={handleReconnect}
              >
                Reconnect
              </Button>
            </div>
          )}

          {/* Meta */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Details</h3>
            <div className="mt-3 space-y-1 text-sm text-[var(--color-ink-secondary)]">
              <p>Connected: {new Date(connection.connectedAt).toLocaleDateString()}</p>
              <p>Updated: {new Date(connection.updatedAt).toLocaleDateString()}</p>
            </div>
          </div>
        </div>
      </div>

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        Webhook replay, reconnect, and all actions are simulated. No real provider changes are made.
      </p>
    </div>
  );
}
