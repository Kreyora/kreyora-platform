"use client";

import { useEffect, useState, useCallback } from "react";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { AuditEvent } from "@/lib/types";

const ACTOR_TYPE_VARIANTS: Record<string, "info" | "neutral" | "warning"> = {
  user: "info",
  system: "neutral",
  bot: "warning",
};

export default function AuditPage() {
  const { audit } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [resourceFilter, setResourceFilter] = useState("");
  const [actionFilter, setActionFilter] = useState("");

  const load = useCallback(() => {
    setIsLoading(true);
    const params: { resourceType?: string; action?: string } = {};
    if (resourceFilter.trim()) params.resourceType = resourceFilter.trim();
    if (actionFilter.trim()) params.action = actionFilter.trim();
    audit.listAuditEvents(params).then((res) => {
      setEvents(res.items);
      setIsLoading(false);
    });
  }, [audit, resourceFilter, actionFilter]);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">Audit Log</h1>
        {isViewer && <ViewerBadge />}
      </div>

      <div className="mt-6 flex flex-wrap gap-3">
        <Input
          label=""
          placeholder="Filter by resource type..."
          value={resourceFilter}
          onChange={(e) => setResourceFilter(e.target.value)}
          className="max-w-xs"
        />
        <Input
          label=""
          placeholder="Filter by action..."
          value={actionFilter}
          onChange={(e) => setActionFilter(e.target.value)}
          className="max-w-xs"
        />
      </div>

      {isLoading ? (
        <div className="mt-6 space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      ) : events.length === 0 ? (
        <div className="mt-6"><EmptyState title="No audit events" description="No events match your filters." /></div>
      ) : (
        <div className="mt-6 overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-[var(--color-border)]">
                <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Actor</th>
                <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Action</th>
                <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Resource</th>
                <th className="hidden pb-2 text-left font-medium text-[var(--color-ink-secondary)] md:table-cell">Details</th>
                <th className="hidden pb-2 text-left font-medium text-[var(--color-ink-secondary)] lg:table-cell">Correlation</th>
                <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Time</th>
              </tr>
            </thead>
            <tbody>
              {events.map((evt) => (
                <tr key={evt.id} className="border-b border-[var(--color-border)] last:border-b-0">
                  <td className="py-2">
                    <div className="flex items-center gap-2">
                      <span className="text-[var(--color-ink-primary)]">{evt.actor.name}</span>
                      <Badge variant={ACTOR_TYPE_VARIANTS[evt.actor.type] ?? "neutral"} className="text-[10px]">{evt.actor.type}</Badge>
                      <span className="text-[10px] text-[var(--color-ink-secondary)]">{evt.actor.role}</span>
                    </div>
                  </td>
                  <td className="py-2 text-[var(--color-ink-primary)]">{evt.action}</td>
                  <td className="py-2">
                    <span className="text-[var(--color-ink-secondary)]">{evt.resourceType}</span>
                    <span className="ml-1 text-[10px] text-[var(--color-ink-secondary)]">{evt.resourceId}</span>
                  </td>
                  <td className="hidden py-2 md:table-cell">
                    {Object.entries(evt.details).length > 0 ? (
                      <span className="text-xs text-[var(--color-ink-secondary)]">
                        {Object.entries(evt.details).map(([k, v]) => `${k}: ${v}`).join(", ")}
                      </span>
                    ) : (
                      <span className="text-xs text-[var(--color-ink-secondary)]">—</span>
                    )}
                  </td>
                  <td className="hidden py-2 lg:table-cell">
                    <span className="font-mono text-[10px] text-[var(--color-ink-secondary)]">{evt.correlationId.slice(0, 12)}…</span>
                  </td>
                  <td className="py-2 text-xs text-[var(--color-ink-secondary)]">
                    {new Date(evt.createdAt).toLocaleString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <p className="mt-8 text-[10px] text-[var(--color-ink-secondary)]">
        Audit events are simulated. Data is derived from mock fixtures.
      </p>
    </div>
  );
}
