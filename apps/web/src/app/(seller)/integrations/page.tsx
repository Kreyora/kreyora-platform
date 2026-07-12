"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { ChannelConnection } from "@/lib/types";
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

export default function IntegrationsPage() {
  const { integration } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [connections, setConnections] = useState<ChannelConnection[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    integration.listConnections().then((result) => {
      setConnections(result);
      setIsLoading(false);
    });
  }, [integration]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-36" />
        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          {Array.from({ length: 2 }).map((_, i) => (
            <Skeleton key={i} className="h-40 rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Integrations</h1>
          {isViewer && <ViewerBadge />}
        </div>
        {!isViewer && (
          <Button variant="outline" disabled>
            Connect Channel
          </Button>
        )}
      </div>

      {connections.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="No integrations"
            description="No channel connections have been configured."
          />
        </div>
      ) : (
        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          {connections.map((c) => {
            const sm = STATUS_MAP[c.status] ?? { label: c.status, variant: "neutral" as const };
            const pm = PROVIDER_MAP[c.provider] ?? { label: c.provider, color: "bg-gray-400" };
            const caps = [
              c.capabilities.canReceiveMessages && "Receive",
              c.capabilities.canSendMessages && "Send",
              c.capabilities.canSendMedia && "Media",
              c.capabilities.supportsTemplates && "Templates",
              c.capabilities.supportsDeliveryReceipts && "Receipts",
            ].filter(Boolean);
            return (
              <Link
                key={c.id}
                href={`/integrations/${c.id}`}
                className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5 transition-colors hover:bg-[var(--color-canvas-subtle)]"
              >
                <div className="flex items-center gap-3">
                  <span className={`flex h-10 w-10 items-center justify-center rounded-full text-xs font-bold text-white ${pm.color}`}>
                    {pm.label.substring(0, 2).toUpperCase()}
                  </span>
                  <div>
                    <p className="text-sm font-medium text-[var(--color-ink-primary)]">{c.accountName}</p>
                    <p className="text-xs text-[var(--color-ink-secondary)]">{pm.label}</p>
                  </div>
                  <Badge variant={sm.variant} className="ml-auto">{sm.label}</Badge>
                </div>

                <div className="mt-4 flex flex-wrap gap-1.5">
                  {caps.map((cap) => (
                    <span key={String(cap)} className="rounded-[var(--radius-full)] bg-[var(--color-canvas-subtle)] px-2 py-0.5 text-[10px] text-[var(--color-ink-secondary)]">
                      {cap}
                    </span>
                  ))}
                </div>

                <div className="mt-3 flex justify-between text-xs text-[var(--color-ink-secondary)]">
                  <span>{c.health.eventsProcessed24h} events (24h)</span>
                  <span>Since {new Date(c.connectedAt).toLocaleDateString()}</span>
                </div>
              </Link>
            );
          })}
        </div>
      )}

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        Channel connections are simulated. No real provider accounts are connected.
      </p>
    </div>
  );
}
