"use client";

import { Suspense, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useClients, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import {
  AssistantHeader,
  ErrorNote,
  OUTCOME_LABEL,
  describeAssistantError,
  formatUsd,
  formatWhen,
  reasonLabel,
  type ScreenError,
} from "@/components/assistant/assistant-display";
import type { AssistantTurn, AssistantTurnOutcome } from "@/lib/types";

const WRITE_TOOLS = new Set(["ReserveInventory", "ReleaseReservation", "CreateCheckoutLink", "QuoteCart"]);

export default function HistoryPage() {
  return (
    <Suspense fallback={null}>
      <History />
    </Suspense>
  );
}

function History() {
  const { assistant } = useClients();
  const { effectiveRole } = useSession();
  const conversationId = useSearchParams().get("conversationId") ?? undefined;
  const [turns, setTurns] = useState<AssistantTurn[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ScreenError | null>(null);
  const [outcome, setOutcome] = useState<AssistantTurnOutcome | "">("");

  const load = useCallback(async (after?: string) => {
    setError(null);
    try {
      const page = await assistant.listTurns({ conversationId, cursor: after, pageSize: 25 });
      setTurns((existing) => (after ? [...existing, ...page.items] : page.items));
      setCursor(page.nextCursor);
    } catch (failure) {
      setError(describeAssistantError(failure));
    } finally {
      setLoading(false);
    }
  }, [assistant, conversationId]);

  useEffect(() => {
    let cancelled = false;
    assistant.listTurns({ conversationId, pageSize: 25 })
      .then((page) => { if (!cancelled) { setTurns(page.items); setCursor(page.nextCursor); setError(null); } })
      .catch((failure: unknown) => { if (!cancelled) setError(describeAssistantError(failure)); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [assistant, conversationId]);

  const visible = useMemo(() => (outcome ? turns.filter((t) => t.outcome === outcome) : turns), [turns, outcome]);

  return (
    <div>
      <AssistantHeader
        title="History"
        description="Every assistant decision: what it did, which tools it used and why it stopped. Message text is never stored here."
        actions={effectiveRole === "viewer" ? <ViewerBadge /> : undefined}
      />
      {USING_FIXTURE_ADAPTERS && <p role="note" className="mt-3 text-xs text-[var(--color-ink-secondary)]">Demo data.</p>}

      <div className="mt-4 flex flex-wrap items-center gap-2">
        {conversationId && (
          <span className="text-sm text-[var(--color-ink-secondary)]">
            One conversation · <Link href="/assistant/history" className="inline-flex min-h-11 items-center underline">Show all</Link>
          </span>
        )}
        <label htmlFor="outcome-filter" className="sr-only">Filter by outcome</label>
        <select
          id="outcome-filter"
          value={outcome}
          onChange={(e) => setOutcome(e.target.value as AssistantTurnOutcome | "")}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm"
        >
          <option value="">All outcomes</option>
          {(Object.keys(OUTCOME_LABEL) as AssistantTurnOutcome[]).map((o) => <option key={o} value={o}>{OUTCOME_LABEL[o].label}</option>)}
        </select>
        <Button size="sm" variant="ghost" onClick={() => { setLoading(true); void load(); }}>Refresh</Button>
      </div>

      {error && <ErrorNote error={error} onRetry={() => void load()} />}
      {loading ? (
        <div className="mt-4 space-y-3" aria-busy="true" aria-label="Loading history">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-20 w-full rounded-[var(--radius-lg)]" />)}
        </div>
      ) : !error && visible.length === 0 ? (
        <EmptyState title={turns.length === 0 ? "No assistant activity yet" : "Nothing matches this filter"} description={turns.length === 0 ? "Replies, hand-offs and console tests will appear here." : "Try another outcome."} />
      ) : (
        <>
          <ul className="mt-4 space-y-3" aria-label="Assistant turns">
            {visible.map((turn) => <TurnRow key={turn.id} turn={turn} />)}
          </ul>
          {cursor && (
            <div className="mt-4 flex justify-center">
              <Button variant="outline" onClick={() => void load(cursor)}>Load older</Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function TurnRow({ turn }: { turn: AssistantTurn }) {
  const label = OUTCOME_LABEL[turn.outcome];
  return (
    <li className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <Badge variant={label.variant}>{label.label}</Badge>
        <span className="text-[var(--color-ink-primary)]">{reasonLabel(turn.reasonCode)}</span>
        <span className="ml-auto text-xs text-[var(--color-ink-secondary)]">{formatWhen(turn.startedAt)}</span>
      </div>
      <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
        {turn.isPlayground ? "Console test" : turn.conversationId ? <Link href={`/inbox/${turn.conversationId}`} className="inline-flex min-h-11 items-center underline">Open conversation</Link> : "No conversation"}
      </p>
      {turn.tools.length > 0 && (
        <ol className="mt-2 flex flex-wrap gap-2" aria-label="Tools used">
          {turn.tools.map((t, i) => (
            <li key={i} className="rounded-[var(--radius-full)] bg-[var(--color-canvas-subtle)] px-2.5 py-0.5 text-xs text-[var(--color-ink-primary)]">
              {t.tool}{WRITE_TOOLS.has(t.tool) ? " (shop action)" : ""} · {t.outcome}{t.dryRun ? " · test" : ""}{t.replayed ? " · repeated" : ""}
            </li>
          ))}
        </ol>
      )}
      {turn.validationCodes.length > 0 && <p className="mt-2 text-xs text-[var(--color-ink-secondary)]">Safety checks: {turn.validationCodes.join(", ")}</p>}
      <details className="mt-2 text-xs text-[var(--color-ink-secondary)]">
        <summary className="inline-flex min-h-11 cursor-pointer items-center">Details</summary>
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
          <dt>AI calls</dt><dd>{turn.modelCalls}{turn.models.length > 0 && ` (${turn.models.join(", ")})`}</dd>
          <dt>AI time</dt><dd>{(turn.providerLatencyMs / 1000).toFixed(1)} s</dd>
          <dt>Tokens</dt><dd>{turn.inputTokens} in / {turn.outputTokens} out · {formatUsd(turn.estimatedCostUsd)}</dd>
          <dt>Knowledge</dt><dd>{turn.citations} passage{turn.citations === 1 ? "" : "s"}</dd>
          <dt>Versions</dt><dd>{[turn.promptVersion, turn.registryVersion, turn.policyVersion && `policy ${turn.policyVersion}`].filter(Boolean).join(" · ") || "—"}</dd>
        </dl>
      </details>
    </li>
  );
}
