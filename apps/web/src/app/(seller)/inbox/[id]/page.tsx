"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Conversation, Message } from "@/lib/types";
import type { ConnectionHealth } from "@/lib/types/integrations";
import type { AIActionTrace } from "@/lib/types/ai";
import type { BadgeVariant } from "@/components/ui/badge";

const STATE_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  new: { label: "New", variant: "info" },
  bot_active: { label: "Bot Active", variant: "info" },
  human_assigned: { label: "Human Assigned", variant: "warning" },
  awaiting_customer: { label: "Awaiting Customer", variant: "neutral" },
  checkout_in_progress: { label: "Checkout", variant: "info" },
  order_created: { label: "Ordered", variant: "success" },
  resolved: { label: "Resolved", variant: "success" },
  closed: { label: "Closed", variant: "neutral" },
  spam: { label: "Spam", variant: "danger" },
};

const CHANNEL_MAP: Record<string, { label: string; color: string }> = {
  facebook: { label: "Facebook", color: "bg-blue-500" },
  instagram: { label: "Instagram", color: "bg-pink-500" },
  whatsapp: { label: "WhatsApp", color: "bg-green-500" },
  tiktok: { label: "TikTok", color: "bg-gray-800" },
  storefront: { label: "Storefront", color: "bg-purple-500" },
};

const DELIVERY_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  pending: { label: "Pending", variant: "neutral" },
  sent: { label: "Sent", variant: "info" },
  delivered: { label: "Delivered", variant: "success" },
  read: { label: "Read", variant: "success" },
  failed: { label: "Failed", variant: "danger" },
};

const SENDER_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  customer: { label: "Customer", variant: "neutral" },
  staff: { label: "Staff", variant: "info" },
  bot: { label: "Bot", variant: "warning" },
};

const CONN_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  connected: { label: "Connected", variant: "success" },
  disconnected: { label: "Disconnected", variant: "danger" },
  error: { label: "Error", variant: "danger" },
  pending_reauth: { label: "Reauth Needed", variant: "warning" },
};

export default function ConversationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { conversation: convClient, integration, ai } = useClients();
  const { effectiveRole, session } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [conv, setConv] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [localMessages, setLocalMessages] = useState<Message[]>([]);
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [traces, setTraces] = useState<AIActionTrace[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const [automationActive, setAutomationActive] = useState(true);
  const [composerText, setComposerText] = useState("");

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      convClient.getConversation(id),
      convClient.getMessages(id),
      ai.getActionTraces(id),
    ]).then(async ([c, msgs, tr]) => {
      if (cancelled) return;
      setConv(c);
      setMessages(msgs.items);
      setTraces(tr);
      setAutomationActive(c.isAutomationActive);

      try {
        const h = await integration.getHealth(c.connectionId);
        if (!cancelled) setHealth(h);
      } catch {
        /* connection may not exist in fixtures */
      }

      if (!cancelled) setIsLoading(false);
    });
    return () => { cancelled = true; };
  }, [convClient, ai, integration, id]);

  const handleTakeover = useCallback(() => {
    setAutomationActive(false);
  }, []);

  const handleRelease = useCallback(() => {
    setAutomationActive(true);
  }, []);

  const handleSend = useCallback(() => {
    if (!composerText.trim() || !conv) return;
    const newMsg: Message = {
      id: `msg-local-${Date.now()}`,
      conversationId: conv.id,
      direction: "outbound",
      senderName: session?.user.displayName ?? "Staff",
      senderType: "staff",
      content: composerText.trim(),
      attachments: [],
      deliveryState: "sent",
      createdAt: new Date().toISOString(),
    };
    setLocalMessages((prev) => [...prev, newMsg]);
    setComposerText("");
  }, [composerText, conv, session]);

  if (isLoading || !conv) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-48" />
        <Skeleton className="mb-6 h-8 w-64" />
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-md)]" />
          ))}
        </div>
      </div>
    );
  }

  const st = STATE_MAP[conv.state] ?? { label: conv.state, variant: "neutral" as const };
  const ch = CHANNEL_MAP[conv.channel] ?? { label: conv.channel, color: "bg-gray-400" };
  const allMessages = [...messages, ...localMessages].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
  );
  const canCompose = !isViewer && !automationActive;

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/inbox" className="hover:underline">Inbox</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">{conv.customerName}</span>
      </nav>

      {/* Header */}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">{conv.customerName}</h1>
        {isViewer && <ViewerBadge />}
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-bold text-white ${ch.color}`}>
          {ch.label}
        </span>
        <Badge variant={st.variant}>{st.label}</Badge>
        {conv.assignment && (
          <span className="text-xs text-[var(--color-ink-secondary)]">
            Assigned to {conv.assignment.assigneeName}
          </span>
        )}
        {automationActive ? (
          <Badge variant="info">Bot Active</Badge>
        ) : (
          <Badge variant="warning">Bot Paused</Badge>
        )}
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        {/* Message timeline */}
        <div className="lg:col-span-2">
          <div className="flex flex-col gap-3">
            {allMessages.map((m) => {
              const ds = DELIVERY_MAP[m.deliveryState] ?? { label: m.deliveryState, variant: "neutral" as const };
              const ss = SENDER_MAP[m.senderType] ?? { label: m.senderType, variant: "neutral" as const };
              const isOutbound = m.direction === "outbound";
              return (
                <div
                  key={m.id}
                  className={`flex flex-col gap-1 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 ${isOutbound ? "ml-8 bg-[var(--color-canvas-subtle)]" : "mr-8"}`}
                >
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-medium text-[var(--color-ink-primary)]">{m.senderName}</span>
                    <Badge variant={ss.variant}>{ss.label}</Badge>
                    <Badge variant={ds.variant}>{ds.label}</Badge>
                    {m.deliveryState === "failed" && (
                      <span className="text-[10px] text-[var(--color-danger)]">Retry needed</span>
                    )}
                  </div>
                  <p className="text-sm text-[var(--color-ink-primary)]">{m.content}</p>
                  {m.attachments.length > 0 && (
                    <div className="mt-1 flex gap-2">
                      {m.attachments.map((att) => (
                        <span key={att.name} className="rounded-[var(--radius-md)] bg-[var(--color-canvas-subtle)] px-2 py-1 text-[10px] text-[var(--color-ink-secondary)]">
                          {att.name}
                        </span>
                      ))}
                    </div>
                  )}
                  <span className="text-[10px] text-[var(--color-ink-secondary)]">
                    {new Date(m.createdAt).toLocaleString()}
                  </span>
                </div>
              );
            })}
          </div>

          {/* Staff composer */}
          {canCompose && (
            <div className="mt-4 flex gap-2">
              <Input
                placeholder="Type a message..."
                value={composerText}
                onChange={(e) => setComposerText(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); handleSend(); } }}
                aria-label="Message composer"
                className="flex-1"
              />
              <Button onClick={handleSend} disabled={!composerText.trim()}>
                Send
              </Button>
            </div>
          )}

          {automationActive && !isViewer && (
            <p className="mt-2 text-xs text-[var(--color-ink-secondary)]">
              Bot is active. Take over to send messages manually.
            </p>
          )}

          <p className="mt-4 text-[10px] text-[var(--color-ink-secondary)]">
            Messages are simulated. No real messages are sent or received.
          </p>
        </div>

        {/* Sidebar */}
        <div className="space-y-6 lg:self-start">
          {/* Takeover / Release */}
          {!isViewer && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Automation Control</h3>
              <div className="mt-3">
                {automationActive ? (
                  <Button variant="outline" className="w-full" onClick={handleTakeover}>
                    Take Over
                  </Button>
                ) : (
                  <Button variant="outline" className="w-full" onClick={handleRelease}>
                    Release to Bot
                  </Button>
                )}
              </div>
              <p className="mt-2 text-[10px] text-[var(--color-ink-secondary)]">
                {automationActive
                  ? "AI assistant is handling this conversation."
                  : "Human control is active. Bot will not send messages."}
              </p>
            </div>
          )}

          {/* Provider health */}
          {health && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Channel Health</h3>
              <div className="mt-3 space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Status</span>
                  <Badge variant={CONN_STATUS[health.status]?.variant ?? "neutral"}>
                    {CONN_STATUS[health.status]?.label ?? health.status}
                  </Badge>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Events (24h)</span>
                  <span className="text-[var(--color-ink-primary)]">{health.eventsProcessed24h}</span>
                </div>
                {health.eventsFailed24h > 0 && (
                  <div className="flex justify-between">
                    <span className="text-[var(--color-ink-secondary)]">Failed (24h)</span>
                    <span className="text-[var(--color-danger)]">{health.eventsFailed24h}</span>
                  </div>
                )}
                {health.lastEventAt && (
                  <div className="flex justify-between">
                    <span className="text-[var(--color-ink-secondary)]">Last event</span>
                    <span className="text-[var(--color-ink-primary)]">{new Date(health.lastEventAt).toLocaleString()}</span>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Labels */}
          {conv.labels.length > 0 && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Labels</h3>
              <div className="mt-3 flex flex-wrap gap-2">
                {conv.labels.map((l) => (
                  <span
                    key={l}
                    className="rounded-[var(--radius-full)] bg-[var(--color-canvas-subtle)] px-2.5 py-0.5 text-xs text-[var(--color-ink-secondary)]"
                  >
                    {l}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* AI traces */}
          {traces.length > 0 && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">AI Activity</h3>
              <div className="mt-3 space-y-2 text-sm">
                {traces.map((t) => (
                  <div key={t.id} className="rounded-[var(--radius-md)] border border-[var(--color-border)] p-3">
                    <p className="text-xs font-medium text-[var(--color-ink-primary)]">{t.intent}</p>
                    <p className="mt-1 text-[10px] text-[var(--color-ink-secondary)]">
                      {t.toolCalls.length} tool call{t.toolCalls.length !== 1 ? "s" : ""} · {t.latencyMs}ms · confidence {(t.confidenceScore * 100).toFixed(0)}%
                    </p>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Customer info */}
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Customer</h3>
            <div className="mt-3 space-y-1 text-sm">
              <p className="text-[var(--color-ink-primary)]">{conv.customerName}</p>
              <p className="text-[var(--color-ink-secondary)]">{conv.customerIdentifier}</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
