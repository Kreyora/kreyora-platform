"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { AIActionTrace } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const FIXTURE_CONV_ID = "conv-facebook-001";

const ESCALATION_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  none: { label: "None", variant: "neutral" },
  pending: { label: "Pending", variant: "warning" },
  escalated: { label: "Escalated", variant: "danger" },
  resolved: { label: "Resolved", variant: "success" },
};

const COST_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  low: { label: "Low", variant: "success" },
  medium: { label: "Medium", variant: "warning" },
  high: { label: "High", variant: "danger" },
};

const NAV_ITEMS = [
  { label: "Overview", href: "/assistant" },
  { label: "Knowledge", href: "/assistant/knowledge" },
  { label: "Console", href: "/assistant/console" },
  { label: "History", href: "/assistant/history" },
];

export default function ActionHistoryPage() {
  const { ai } = useClients();
  const { effectiveRole } = useSession();
  const [traces, setTraces] = useState<AIActionTrace[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  useEffect(() => {
    ai.getActionTraces(FIXTURE_CONV_ID).then((t) => {
      setTraces(t);
      setIsLoading(false);
    });
  }, [ai]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-40" />
        <Skeleton className="mt-6 h-64 w-full rounded-[var(--radius-lg)]" />
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">AI Assistant</h1>
        {effectiveRole === "viewer" && <ViewerBadge />}
      </div>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Assistant navigation">
        {NAV_ITEMS.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${
              item.href === "/assistant/history"
                ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]"
                : "border-transparent text-[var(--color-ink-secondary)]"
            }`}
          >
            {item.label}
          </Link>
        ))}
      </nav>

      <h2 className="mt-6 text-base font-semibold text-[var(--color-ink-primary)]">Action History</h2>

      {traces.length === 0 ? (
        <div className="mt-6">
          <EmptyState title="No action traces" description="AI action traces will appear here." />
        </div>
      ) : (
        <div className="mt-4 flex flex-col gap-3">
          {traces.map((t) => {
            const es = ESCALATION_MAP[t.escalationState] ?? { label: t.escalationState, variant: "neutral" as const };
            const cs = COST_MAP[t.costBand] ?? { label: t.costBand, variant: "neutral" as const };
            const isExpanded = expandedId === t.id;
            return (
              <div key={t.id} className="rounded-[var(--radius-lg)] border border-[var(--color-border)]">
                <button
                  type="button"
                  onClick={() => setExpandedId(isExpanded ? null : t.id)}
                  className="flex w-full items-start justify-between gap-3 p-5 text-left"
                  aria-expanded={isExpanded}
                >
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-[var(--color-ink-primary)]">{t.intent}</p>
                    <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
                      <Link href={`/inbox/${t.conversationId}`} className="hover:underline" onClick={(e) => e.stopPropagation()}>
                        {t.conversationId}
                      </Link>
                      {" · "}{t.toolCalls.length} tool call{t.toolCalls.length !== 1 ? "s" : ""}
                      {" · "}{(t.confidenceScore * 100).toFixed(0)}% confidence
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <Badge variant={cs.variant}>{cs.label}</Badge>
                    <Badge variant={es.variant}>{es.label}</Badge>
                    <span className="text-xs text-[var(--color-ink-secondary)]">
                      {t.tokenCount} tokens · {t.latencyMs}ms
                    </span>
                  </div>
                </button>

                {isExpanded && (
                  <div className="border-t border-[var(--color-border)] p-5">
                    {/* Tool calls */}
                    <h4 className="text-xs font-semibold text-[var(--color-ink-primary)]">Tool Calls</h4>
                    <div className="mt-2 space-y-2">
                      {t.toolCalls.map((tc, i) => (
                        <div key={i} className="rounded-[var(--radius-md)] border border-[var(--color-border)] p-3">
                          <div className="flex items-center justify-between">
                            <span className="text-xs font-medium text-[var(--color-ink-primary)]">{tc.tool}</span>
                            <span className="text-[10px] text-[var(--color-ink-secondary)]">{tc.durationMs}ms</span>
                          </div>
                          <div className="mt-1 rounded bg-[var(--color-canvas-subtle)] p-2 text-[10px]">
                            <p className="text-[var(--color-ink-secondary)]">Input: {JSON.stringify(tc.input)}</p>
                            <p className="text-[var(--color-ink-secondary)]">Output: {JSON.stringify(tc.output)}</p>
                          </div>
                        </div>
                      ))}
                    </div>

                    {/* Response preview (redacted) */}
                    <h4 className="mt-4 text-xs font-semibold text-[var(--color-ink-primary)]">Generated Response</h4>
                    <p className="mt-1 rounded-[var(--radius-md)] bg-[var(--color-canvas-subtle)] p-3 text-xs text-[var(--color-ink-secondary)]">
                      {t.responseGenerated.length > 200
                        ? `${t.responseGenerated.substring(0, 200)}...`
                        : t.responseGenerated}
                    </p>

                    <p className="mt-2 text-[10px] text-[var(--color-ink-secondary)]">
                      {new Date(t.createdAt).toLocaleString()}
                    </p>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        Action traces are from fixture data. No real AI operations are logged.
      </p>
    </div>
  );
}
