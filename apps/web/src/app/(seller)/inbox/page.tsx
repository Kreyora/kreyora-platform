"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useClients, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { usePolling } from "@/hooks/use-polling";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import { InboxAlert } from "@/components/inbox/inbox-alert";
import { StaleBanner } from "@/components/inbox/stale-banner";
import { CHANNEL_MAP, STATE_MAP, relativeTime } from "@/components/inbox/inbox-display";
import { describeInboxError, type InboxErrorCopy } from "@/lib/utils/conversation-errors";
import type { Conversation, ConversationState, PaginatedResult } from "@/lib/types";

const LIST_POLL_MS = 15_000;
const PAGE_SIZE = 20;

type View = "all" | "unread" | "mine";

export default function InboxPage() {
  const { conversation } = useClients();
  const { effectiveRole, session } = useSession();
  const currentUserId = session?.membership.userId;
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<InboxErrorCopy | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [pages, setPages] = useState(1);
  const [view, setView] = useState<View>("all");
  const [stateFilter, setStateFilter] = useState<ConversationState | "">("");
  const [search, setSearch] = useState("");
  const [channelFilter, setChannelFilter] = useState("");

  const fetchList = useCallback(() => conversation.listConversations({
    state: stateFilter || undefined,
    unreadOnly: view === "unread",
    assignedTo: view === "mine" ? currentUserId : undefined,
    page: 1,
    pageSize: PAGE_SIZE * pages,
  }), [conversation, stateFilter, view, currentUserId, pages]);

  const apply = useCallback((result: PaginatedResult<Conversation>) => {
    setConversations(result.items);
    setHasMore(result.hasMore);
    setLoadError(null);
  }, []);

  const load = useCallback(async () => apply(await fetchList()), [apply, fetchList]);

  useEffect(() => {
    let cancelled = false;
    fetchList()
      .then((result) => { if (!cancelled) apply(result); })
      .catch((error: unknown) => { if (!cancelled) setLoadError(describeInboxError(error)); })
      .finally(() => { if (!cancelled) setIsLoading(false); });
    return () => { cancelled = true; };
  }, [apply, fetchList]);

  const polling = usePolling(load, LIST_POLL_MS, !isLoading && !loadError);

  // Search and channel filters exist only for demo data; the API has no free-text search yet.
  const visible = useMemo(() => {
    if (!USING_FIXTURE_ADAPTERS) return conversations;
    let result = conversations;
    if (search) {
      const q = search.toLowerCase();
      result = result.filter((c) => c.customerName.toLowerCase().includes(q) || c.lastMessage?.toLowerCase().includes(q) || c.labels.some((l) => l.toLowerCase().includes(q)));
    }
    if (channelFilter) result = result.filter((c) => c.channel === channelFilter);
    return result;
  }, [conversations, search, channelFilter]);

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">Inbox</h1>
        {effectiveRole === "viewer" && <ViewerBadge />}
      </div>
      {USING_FIXTURE_ADAPTERS && (
        <p className="mt-2 text-xs text-[var(--color-ink-secondary)]" role="note">Demo data — nothing is sent to real customers.</p>
      )}

      <div className="mt-4 flex flex-wrap items-center gap-2" role="tablist" aria-label="Inbox view">
        {(["all", "unread", "mine"] as View[]).map((v) => (
          <Button
            key={v}
            role="tab"
            aria-selected={view === v}
            size="sm"
            variant={view === v ? "solid" : "outline"}
            onClick={() => { setView(v); setPages(1); }}
            disabled={v === "mine" && !currentUserId}
          >
            {v === "all" ? "All" : v === "unread" ? "Unread" : "Assigned to me"}
          </Button>
        ))}
        <label htmlFor="state-filter" className="sr-only">Filter by status</label>
        <select
          id="state-filter"
          aria-label="Filter by status"
          value={stateFilter}
          onChange={(event) => { setStateFilter(event.target.value as ConversationState | ""); setPages(1); }}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm"
        >
          <option value="">All statuses</option>
          {(Object.keys(STATE_MAP) as ConversationState[]).map((s) => <option key={s} value={s}>{STATE_MAP[s].label}</option>)}
        </select>
        <Button size="sm" variant="ghost" onClick={() => void polling.refreshNow()}>Refresh</Button>
      </div>

      {USING_FIXTURE_ADAPTERS && (
        <div className="mt-3 flex flex-col gap-2 sm:flex-row">
          <Input placeholder="Search conversations…" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search conversations" className="flex-1" />
          <select
            aria-label="Filter by channel"
            value={channelFilter}
            onChange={(e) => setChannelFilter(e.target.value)}
            className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm"
          >
            <option value="">All channels</option>
            {Object.entries(CHANNEL_MAP).map(([key, c]) => <option key={key} value={key}>{c.label}</option>)}
          </select>
        </div>
      )}

      <div className="mt-4">
        <StaleBanner isStale={polling.isStale} lastSuccessAt={polling.lastSuccessAt} onRefresh={() => void polling.refreshNow()} />
        <InboxAlert error={loadError} onRefresh={() => void polling.refreshNow()} />

        {isLoading ? (
          <div className="space-y-3" aria-busy="true" aria-label="Loading conversations">
            {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-20 w-full rounded-[var(--radius-lg)]" />)}
          </div>
        ) : loadError ? null : visible.length === 0 ? (
          <EmptyState
            title={view === "all" && !stateFilter ? "No conversations yet" : "No conversations match"}
            description={view === "all" && !stateFilter ? "Messages from connected channels will appear here." : "Try a different filter."}
            action={view === "all" && !stateFilter ? <Link href="/integrations" className="text-sm underline">Connect a channel</Link> : undefined}
          />
        ) : (
          <>
            <ul className="space-y-3" aria-label="Conversations">
              {visible.map((c) => {
                const state = STATE_MAP[c.state];
                const channel = CHANNEL_MAP[c.channel] ?? CHANNEL_MAP.simulator;
                return (
                  <li key={c.id}>
                    <Link
                      href={`/inbox/${c.id}`}
                      className="block rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 transition-colors duration-[var(--duration-hover)] hover:bg-[var(--color-canvas-subtle)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)]"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-bold text-white ${channel.color}`} aria-label={channel.label}>{channel.short}</span>
                        <span className="font-medium text-[var(--color-ink-primary)]">{c.customerName}</span>
                        <Badge variant={state.variant}>{state.label}</Badge>
                        {!c.isAutomationActive && <Badge variant="warning">Human</Badge>}
                        {c.unreadCount > 0 && (
                          <span className="ml-auto rounded-full bg-[var(--color-surface-dark)] px-2 py-0.5 text-xs font-semibold text-[var(--color-on-dark)]" aria-label={`${c.unreadCount} unread`}>
                            {c.unreadCount}
                          </span>
                        )}
                      </div>
                      {c.lastMessage && <p className="mt-2 line-clamp-2 text-sm text-[var(--color-ink-secondary)]">{c.lastMessage}</p>}
                      <div className="mt-2 flex flex-wrap gap-3 text-xs text-[var(--color-ink-secondary)]">
                        <span>{relativeTime(c.lastMessageAt)}</span>
                        {c.assignment && <span>Assigned to {c.assignment.assigneeName}</span>}
                        {c.labels.map((l) => <span key={l}>#{l}</span>)}
                      </div>
                    </Link>
                  </li>
                );
              })}
            </ul>
            {hasMore && (
              <div className="mt-4 flex justify-center">
                <Button variant="outline" onClick={() => setPages((p) => p + 1)}>Load more</Button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
