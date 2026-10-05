"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { usePolling } from "@/hooks/use-polling";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import { ConversationTimeline, type TimelineMessage } from "@/components/inbox/conversation-timeline";
import { ConversationPanel } from "@/components/inbox/conversation-panel";
import { MessageComposer } from "@/components/inbox/message-composer";
import { InboxAlert } from "@/components/inbox/inbox-alert";
import { StaleBanner } from "@/components/inbox/stale-banner";
import { CHANNEL_MAP, STATE_MAP, windowLikelyClosed } from "@/components/inbox/inbox-display";
import { ApiClientError } from "@/lib/api/errors";
import { describeInboxError, type InboxErrorCopy } from "@/lib/utils/conversation-errors";
import type { Conversation, ConversationAssignee, ConversationStatusAction, Message, PaginatedResult } from "@/lib/types";
import type { ConnectionHealth } from "@/lib/types/integrations";
import type { AIActionTrace } from "@/lib/types/ai";

const CONVERSATION_POLL_MS = 5_000;

function newKey(): string {
  return typeof crypto !== "undefined" && crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function mergeById(...lists: Message[][]): Message[] {
  const byId = new Map<string, Message>();
  for (const list of lists) for (const m of list) byId.set(m.id, m);
  return [...byId.values()].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
}

interface LocalReply {
  id: string;
  key: string;
  text: string;
  createdAt: string;
  status: "sending" | "failed";
  error?: string;
}

function isDefinitiveRefusal(error: unknown): boolean {
  return error instanceof ApiClientError && error.status >= 400 && error.status < 500;
}

export default function ConversationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { conversation: client, integration, ai } = useClients();
  const { effectiveRole, session } = useSession();
  const canWrite = effectiveRole !== "viewer";
  const currentUserId = session?.membership.userId;

  const [conv, setConv] = useState<Conversation | null>(null);
  const [latest, setLatest] = useState<Message[]>([]);
  const [older, setOlder] = useState<Message[]>([]);
  const [olderCursor, setOlderCursor] = useState<string | null>(null);
  const [isLoadingOlder, setIsLoadingOlder] = useState(false);
  const [locals, setLocals] = useState<LocalReply[]>([]);
  const [assignees, setAssignees] = useState<ConversationAssignee[]>([]);
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [traces, setTraces] = useState<AIActionTrace[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<InboxErrorCopy | null>(null);
  const [actionError, setActionError] = useState<InboxErrorCopy | null>(null);
  const [composerError, setComposerError] = useState<InboxErrorCopy | null>(null);
  const [busy, setBusy] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const markedRead = useRef(false);
  const seenIds = useRef<Set<string>>(new Set());

  const fetchAll = useCallback(
    () => Promise.all([client.getConversation(id), client.getMessages(id)]),
    [client, id],
  );

  const apply = useCallback(([c, page]: [Conversation, PaginatedResult<Message>]) => {
    setConv(c);
    setLatest(page.items);
    setOlderCursor((current) => current ?? page.cursor);

    const fresh = page.items.filter((m) => !seenIds.current.has(m.id));
    if (seenIds.current.size > 0 && fresh.some((m) => m.direction === "inbound")) {
      setAnnouncement(`New message from ${c.customerName}`);
    }
    page.items.forEach((m) => seenIds.current.add(m.id));
  }, []);

  const refresh = useCallback(async () => apply(await fetchAll()), [apply, fetchAll]);

  useEffect(() => {
    let cancelled = false;
    fetchAll()
      .then(async (result) => {
        if (cancelled) return;
        apply(result);
        const people = await client.listAssignees().catch(() => [] as ConversationAssignee[]);
        if (!cancelled) setAssignees(people);
      })
      .catch((error: unknown) => { if (!cancelled) setLoadError(describeInboxError(error)); })
      .finally(() => { if (!cancelled) setIsLoading(false); });
    return () => { cancelled = true; };
  }, [apply, client, fetchAll]);

  // Channel health and (demo-only) AI activity: secondary, never block the conversation.
  useEffect(() => {
    if (!conv) return;
    let cancelled = false;
    integration.getHealth(conv.connectionId).then((h) => { if (!cancelled) setHealth(h); }).catch(() => undefined);
    if (USING_FIXTURE_ADAPTERS) {
      ai.getActionTraces(conv.id).then((t) => { if (!cancelled) setTraces(t); }).catch(() => undefined);
    }
    return () => { cancelled = true; };
  }, [ai, integration, conv?.connectionId, conv?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  // Q4: opening a conversation with unread messages marks it read once per view (Operator and above).
  useEffect(() => {
    if (!conv || !canWrite || markedRead.current || conv.unreadCount === 0) return;
    markedRead.current = true;
    client.markRead(conv.id).then(setConv).catch(() => undefined);
  }, [canWrite, client, conv]);

  const polling = usePolling(refresh, CONVERSATION_POLL_MS, !isLoading && !loadError);

  const myName = session?.user.displayName ?? "You";
  const timeline: TimelineMessage[] = [
    ...mergeById(older, latest),
    ...locals.map((l): TimelineMessage => ({
      id: l.id,
      conversationId: id,
      direction: "outbound",
      senderName: myName,
      senderType: "staff",
      content: l.text,
      attachments: [],
      deliveryState: "pending",
      createdAt: l.createdAt,
      local: { status: l.status, error: l.error },
    })),
  ];

  const sendAttempt = useCallback(async (reply: LocalReply, isNewDraft = false): Promise<boolean> => {
    setLocals((current) => [...current.filter((l) => l.id !== reply.id), { ...reply, status: "sending", error: undefined }]);
    try {
      const accepted = await client.sendReply(id, reply.text, reply.key);
      setLocals((current) => current.filter((l) => l.id !== reply.id));
      setLatest((current) => mergeById(current, [accepted]));
      seenIds.current.add(accepted.id);
      setActionError(null);
      setConv(await client.getConversation(id));
      return true;
    } catch (error) {
      const copy = describeInboxError(error);
      // A 4xx is a definitive refusal: nothing was created. A fresh draft goes back to the composer with an
      // inline error instead of a retry bubble. Anything else (network, 5xx) is ambiguous, so the bubble stays
      // and "Try again" reuses the same idempotency key.
      if (isNewDraft && isDefinitiveRefusal(error)) {
        setLocals((current) => current.filter((l) => l.id !== reply.id));
        setComposerError(copy);
        return false;
      }
      setLocals((current) => current.map((l) => (l.id === reply.id ? { ...l, status: "failed", error: `${copy.title}: ${copy.detail}` } : l)));
      if (copy.refresh) setActionError(copy);
      return isNewDraft;
    }
  }, [client, id]);

  const sendNew = useCallback((text: string) => {
    const key = newKey();
    setComposerError(null);
    return sendAttempt({ id: `local-${key}`, key, text, createdAt: new Date().toISOString(), status: "sending" }, true);
  }, [sendAttempt]);

  const retryLocal = useCallback((localId: string) => {
    const reply = locals.find((l) => l.id === localId);
    if (reply) void sendAttempt(reply);
  }, [locals, sendAttempt]);

  const act = useCallback(async (operation: () => Promise<Conversation>) => {
    setBusy(true);
    try {
      setConv(await operation());
      setActionError(null);
    } catch (error) {
      setActionError(describeInboxError(error));
    } finally {
      setBusy(false);
    }
  }, []);

  const loadOlder = useCallback(async () => {
    if (!olderCursor) return;
    setIsLoadingOlder(true);
    try {
      const page = await client.getMessages(id, { before: olderCursor });
      setOlder((current) => mergeById(page.items, current));
      setOlderCursor(page.cursor);
    } catch (error) {
      setActionError(describeInboxError(error));
    } finally {
      setIsLoadingOlder(false);
    }
  }, [client, id, olderCursor]);

  if (isLoading) {
    return (
      <div aria-busy="true" aria-label="Loading conversation">
        <Skeleton className="mb-4 h-4 w-48" />
        <Skeleton className="mb-6 h-8 w-64" />
        <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-md)]" />)}</div>
      </div>
    );
  }

  if (loadError || !conv) {
    return (
      <div>
        <nav className="mb-4 text-sm" aria-label="Breadcrumb"><Link href="/inbox" className="underline">Inbox</Link></nav>
        <InboxAlert error={loadError ?? { title: "Not found", detail: "This conversation doesn't exist or isn't in this workspace." }} onRefresh={() => window.location.reload()} />
      </div>
    );
  }

  const state = STATE_MAP[conv.state];
  const channel = CHANNEL_MAP[conv.channel] ?? CHANNEL_MAP.simulator;
  const composerBlocked = conv.state === "spam" ? "Replies are blocked while this conversation is marked as spam." : undefined;
  const windowHint = windowLikelyClosed(conv.lastCustomerMessageAt)
    ? "The customer last wrote more than 24 hours ago. The channel may refuse replies outside its window; Kreyora will tell you if it does."
    : undefined;

  return (
    <div>
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/inbox" className="hover:underline">Inbox</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">{conv.customerName}</span>
      </nav>

      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">{conv.customerName}</h1>
        {!canWrite && <ViewerBadge />}
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-bold text-white ${channel.color}`}>{channel.label}</span>
        <Badge variant={state.variant}>{state.label}</Badge>
        <Badge variant={conv.isAutomationActive ? "info" : "warning"}>{conv.isAutomationActive ? "Automation on" : "Automation paused"}</Badge>
        {conv.assignment && <span className="text-xs text-[var(--color-ink-secondary)]">Assigned to {conv.assignment.assigneeName}</span>}
        <Button size="sm" variant="ghost" onClick={() => void polling.refreshNow()}>Refresh</Button>
      </div>
      {USING_FIXTURE_ADAPTERS && (
        <p className="mt-2 text-xs text-[var(--color-ink-secondary)]" role="note">Demo data — replies are simulated and never sent.</p>
      )}

      <div className="sr-only" aria-live="polite" role="status">{announcement}</div>

      <div className="mt-6">
        <StaleBanner isStale={polling.isStale} lastSuccessAt={polling.lastSuccessAt} onRefresh={() => void polling.refreshNow()} />
        <InboxAlert error={actionError} onRefresh={() => { setActionError(null); void polling.refreshNow(); }} onDismiss={() => setActionError(null)} />
      </div>

      <div className="mt-2 grid gap-6 lg:grid-cols-3">
        <div className="min-w-0 lg:col-span-2">
          <ConversationTimeline
            messages={timeline}
            customerName={conv.customerName}
            canWrite={canWrite}
            hasOlder={Boolean(olderCursor)}
            isLoadingOlder={isLoadingOlder}
            onLoadOlder={() => void loadOlder()}
            onSendAgain={(text) => void sendNew(text)}
            onRetryLocal={retryLocal}
          />
          {canWrite ? (
            <>
              <div className="mt-4">
                <InboxAlert error={composerError} onDismiss={() => setComposerError(null)} />
              </div>
              <MessageComposer onSend={sendNew} disabledReason={composerBlocked} hint={windowHint} />
            </>
          ) : (
            <p className="mt-4 rounded-[var(--radius-md)] border border-[var(--color-border)] p-3 text-sm text-[var(--color-ink-secondary)]">
              View-only access. Ask an owner or admin if you need to reply.
            </p>
          )}

          {USING_FIXTURE_ADAPTERS && traces.length > 0 && (
            <section className="mt-6 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-label="AI activity (demo)">
              <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">AI activity (demo)</h2>
              <ul className="mt-3 space-y-2 text-sm">
                {traces.map((t) => (
                  <li key={t.id} className="rounded-[var(--radius-md)] border border-[var(--color-border)] p-3">
                    <p className="text-xs font-medium text-[var(--color-ink-primary)]">{t.intent}</p>
                    <p className="mt-1 text-[10px] text-[var(--color-ink-secondary)]">{t.toolCalls.length} tool call{t.toolCalls.length !== 1 ? "s" : ""} · {t.latencyMs}ms</p>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>

        <ConversationPanel
          conversation={conv}
          canWrite={canWrite}
          busy={busy}
          assignees={assignees}
          currentUserId={currentUserId}
          health={health}
          onTakeOver={() => void act(() => client.takeOver(conv.id))}
          onRelease={() => void act(() => client.release(conv.id))}
          onAssign={(userId) => void act(() => client.assign(conv.id, userId))}
          onUnassign={() => void act(() => client.unassign(conv.id))}
          onSetLabels={(labels) => void act(() => client.setLabels(conv.id, labels))}
          onStatus={(action: ConversationStatusAction) => void act(() => client.changeStatus(conv.id, action))}
        />
      </div>
    </div>
  );
}
