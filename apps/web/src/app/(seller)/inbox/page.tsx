"use client";

import { useEffect, useState, useMemo } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Conversation } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const STATE_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  new: { label: "New", variant: "info" },
  bot_active: { label: "Bot Active", variant: "info" },
  human_assigned: { label: "Human", variant: "warning" },
  awaiting_customer: { label: "Awaiting", variant: "neutral" },
  checkout_in_progress: { label: "Checkout", variant: "info" },
  order_created: { label: "Ordered", variant: "success" },
  resolved: { label: "Resolved", variant: "success" },
  closed: { label: "Closed", variant: "neutral" },
  spam: { label: "Spam", variant: "danger" },
};

const CHANNEL_MAP: Record<string, { label: string; color: string }> = {
  facebook: { label: "FB", color: "bg-blue-500" },
  instagram: { label: "IG", color: "bg-pink-500" },
  whatsapp: { label: "WA", color: "bg-green-500" },
  tiktok: { label: "TT", color: "bg-gray-800" },
  storefront: { label: "SF", color: "bg-purple-500" },
};

export default function InboxPage() {
  const { conversation } = useClients();
  const { effectiveRole } = useSession();
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [stateFilter, setStateFilter] = useState("");
  const [channelFilter, setChannelFilter] = useState("");

  useEffect(() => {
    conversation.listConversations().then((result) => {
      setConversations(result.items);
      setIsLoading(false);
    });
  }, [conversation]);

  const filtered = useMemo(() => {
    let result = conversations;
    if (search) {
      const q = search.toLowerCase();
      result = result.filter(
        (c) =>
          c.customerName.toLowerCase().includes(q) ||
          c.lastMessage?.toLowerCase().includes(q) ||
          c.labels.some((l) => l.toLowerCase().includes(q)),
      );
    }
    if (stateFilter) result = result.filter((c) => c.state === stateFilter);
    if (channelFilter) result = result.filter((c) => c.channel === channelFilter);
    return result;
  }, [conversations, search, stateFilter, channelFilter]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-24" />
        <Skeleton className="mb-6 h-11 w-full" />
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-20 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">Inbox</h1>
        {effectiveRole === "viewer" && <ViewerBadge />}
      </div>

      <div className="mt-6 flex flex-wrap items-end gap-3">
        <div className="min-w-[200px] flex-1">
          <Input
            placeholder="Search by name, message, label..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search conversations"
          />
        </div>
        <select
          value={stateFilter}
          onChange={(e) => setStateFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by state"
        >
          <option value="">All states</option>
          <option value="bot_active">Bot Active</option>
          <option value="human_assigned">Human Assigned</option>
          <option value="awaiting_customer">Awaiting Customer</option>
          <option value="resolved">Resolved</option>
          <option value="closed">Closed</option>
        </select>
        <select
          value={channelFilter}
          onChange={(e) => setChannelFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by channel"
        >
          <option value="">All channels</option>
          <option value="facebook">Facebook</option>
          <option value="instagram">Instagram</option>
          <option value="whatsapp">WhatsApp</option>
          <option value="tiktok">TikTok</option>
          <option value="storefront">Storefront</option>
        </select>
      </div>

      {filtered.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="No conversations found"
            description={search || stateFilter || channelFilter ? "Try adjusting your filters." : "No conversations yet."}
          />
        </div>
      ) : (
        <div className="mt-6 flex flex-col gap-2">
          {filtered.map((c) => {
            const st = STATE_MAP[c.state] ?? { label: c.state, variant: "neutral" as const };
            const ch = CHANNEL_MAP[c.channel] ?? { label: c.channel.substring(0, 2).toUpperCase(), color: "bg-gray-400" };
            return (
              <Link
                key={c.id}
                href={`/inbox/${c.id}`}
                className="flex items-start gap-3 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 transition-colors hover:bg-[var(--color-canvas-subtle)]"
              >
                <span
                  className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-[10px] font-bold text-white ${ch.color}`}
                  aria-label={c.channel}
                >
                  {ch.label}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-[var(--color-ink-primary)]">
                      {c.customerName}
                    </span>
                    <Badge variant={st.variant}>{st.label}</Badge>
                    {c.unreadCount > 0 && (
                      <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-[var(--color-danger)] px-1 text-[10px] font-bold text-white">
                        {c.unreadCount}
                      </span>
                    )}
                    {c.isAutomationActive && (
                      <span className="text-[10px] text-[var(--color-info)]">Bot</span>
                    )}
                  </div>
                  {c.lastMessage && (
                    <p className="mt-0.5 truncate text-xs text-[var(--color-ink-secondary)]">
                      {c.lastMessage}
                    </p>
                  )}
                  <div className="mt-1 flex flex-wrap items-center gap-2">
                    {c.assignment && (
                      <span className="text-[10px] text-[var(--color-ink-secondary)]">
                        Assigned: {c.assignment.assigneeName}
                      </span>
                    )}
                    {c.labels.map((l) => (
                      <span
                        key={l}
                        className="rounded-[var(--radius-full)] bg-[var(--color-canvas-subtle)] px-2 py-0.5 text-[10px] text-[var(--color-ink-secondary)]"
                      >
                        {l}
                      </span>
                    ))}
                  </div>
                </div>
                <span className="shrink-0 text-[10px] text-[var(--color-ink-secondary)]">
                  {c.lastMessageAt ? new Date(c.lastMessageAt).toLocaleDateString() : ""}
                </span>
              </Link>
            );
          })}
        </div>
      )}

      <p className="mt-6 text-xs text-[var(--color-ink-secondary)]">
        {conversations.length} conversation{conversations.length !== 1 ? "s" : ""} total
        {filtered.length !== conversations.length && `, ${filtered.length} shown`}
      </p>
    </div>
  );
}
