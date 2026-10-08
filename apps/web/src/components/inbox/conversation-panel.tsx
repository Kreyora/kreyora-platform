"use client";

import Link from "next/link";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { Conversation, ConversationAssignee, ConversationStatusAction } from "@/lib/types";
import type { ConnectionHealth } from "@/lib/types/integrations";
import { availableStatusActions, STATUS_ACTION_LABEL } from "./inbox-display";
import { escalationLabel } from "@/components/assistant/assistant-display";

const CONNECTION_LABEL: Record<string, { label: string; variant: "success" | "warning" | "danger" | "neutral" }> = {
  connected: { label: "Connected", variant: "success" },
  disconnected: { label: "Disconnected", variant: "danger" },
  error: { label: "Error", variant: "danger" },
  pending_reauth: { label: "Reauthorize needed", variant: "warning" },
};

export interface ConversationPanelProps {
  conversation: Conversation;
  canWrite: boolean;
  busy: boolean;
  assignees: ConversationAssignee[];
  currentUserId?: string;
  health: ConnectionHealth | null;
  onTakeOver: () => void;
  onRelease: () => void;
  onAssign: (userId: string) => void;
  onUnassign: () => void;
  onSetLabels: (labels: string[]) => void;
  onStatus: (action: ConversationStatusAction) => void;
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-label={title}>
      <h2 className="text-sm font-semibold text-[var(--color-ink-primary)]">{title}</h2>
      <div className="mt-3">{children}</div>
    </section>
  );
}

export function ConversationPanel({
  conversation,
  canWrite,
  busy,
  assignees,
  currentUserId,
  health,
  onTakeOver,
  onRelease,
  onAssign,
  onUnassign,
  onSetLabels,
  onStatus,
}: ConversationPanelProps) {
  const [labelDraft, setLabelDraft] = useState("");
  const connection = health ? CONNECTION_LABEL[health.status] ?? { label: health.status, variant: "neutral" as const } : null;

  return (
    <div className="space-y-6 lg:self-start">
      <Card title="Automation">
        <p className="text-sm text-[var(--color-ink-secondary)]">
          {conversation.isAutomationActive
            ? "The assistant owns this conversation. Any reply or takeover by your team stops it at once."
            : "A team member owns this conversation. The assistant is paused and won't send messages until you hand it back."}
        </p>
        {!conversation.isAutomationActive && conversation.escalationCategory && (
          <p className="mt-2 text-sm text-[var(--color-ink-primary)]">
            <Badge variant="warning">Handed over</Badge> <span className="ml-1">{escalationLabel(conversation.escalationCategory)}</span>
          </p>
        )}
        {canWrite && (
          <Button variant="outline" className="mt-3 w-full" disabled={busy} onClick={conversation.isAutomationActive ? onTakeOver : onRelease}>
            {conversation.isAutomationActive ? "Take over" : "Hand back to automation"}
          </Button>
        )}
      </Card>

      {canWrite && (
        <Card title="Status">
          <div className="flex flex-wrap gap-2">
            {availableStatusActions(conversation.state).map((action) => (
              <Button key={action} size="sm" variant="outline" disabled={busy} onClick={() => onStatus(action)}>
                {STATUS_ACTION_LABEL[action]}
              </Button>
            ))}
          </div>
        </Card>
      )}

      <Card title="Assignment">
        <p className="text-sm text-[var(--color-ink-primary)]">
          {conversation.assignment ? `Assigned to ${conversation.assignment.assigneeName}` : "Unassigned"}
        </p>
        {canWrite && (
          <div className="mt-3 flex flex-col gap-2">
            <label className="text-xs text-[var(--color-ink-secondary)]" htmlFor="assignee">Assign to</label>
            <select
              id="assignee"
              className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm"
              value={conversation.assignment?.assigneeId ?? ""}
              disabled={busy}
              onChange={(event) => (event.target.value ? onAssign(event.target.value) : onUnassign())}
            >
              <option value="">Unassigned</option>
              {assignees.map((a) => (
                <option key={a.userId} value={a.userId}>
                  {a.displayName}{a.userId === currentUserId ? " (you)" : ""}
                </option>
              ))}
            </select>
            {currentUserId && conversation.assignment?.assigneeId !== currentUserId && (
              <Button size="sm" variant="ghost" disabled={busy} onClick={() => onAssign(currentUserId)}>Assign to me</Button>
            )}
          </div>
        )}
      </Card>

      <Card title="Labels">
        <ul className="flex flex-wrap gap-2" aria-label="Labels">
          {conversation.labels.length === 0 && <li className="text-sm text-[var(--color-ink-secondary)]">No labels</li>}
          {conversation.labels.map((l) => (
            <li key={l} className="flex items-center gap-1 rounded-[var(--radius-full)] bg-[var(--color-canvas-subtle)] px-2.5 py-0.5 text-xs text-[var(--color-ink-secondary)]">
              {l}
              {canWrite && (
                <button
                  type="button"
                  className="min-h-11 min-w-11 text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]"
                  aria-label={`Remove label ${l}`}
                  disabled={busy}
                  onClick={() => onSetLabels(conversation.labels.filter((x) => x !== l))}
                >
                  ×
                </button>
              )}
            </li>
          ))}
        </ul>
        {canWrite && (
          <form
            className="mt-3 flex gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              const value = labelDraft.trim();
              if (!value) return;
              onSetLabels([...conversation.labels, value]);
              setLabelDraft("");
            }}
          >
            <label htmlFor="new-label" className="sr-only">New label</label>
            <input
              id="new-label"
              value={labelDraft}
              maxLength={48}
              onChange={(event) => setLabelDraft(event.target.value)}
              placeholder="Add label"
              className="min-h-11 flex-1 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm"
            />
            <Button type="submit" size="sm" variant="outline" disabled={busy || !labelDraft.trim()}>Add</Button>
          </form>
        )}
      </Card>

      <Card title="Channel connection">
        {connection ? (
          <div className="flex items-center justify-between gap-2 text-sm">
            <span className="text-[var(--color-ink-secondary)]">Status</span>
            <Badge variant={connection.variant}>{connection.label}</Badge>
          </div>
        ) : (
          <p className="text-sm text-[var(--color-ink-secondary)]">Health unavailable.</p>
        )}
        {health?.lastErrorMessage && <p className="mt-2 text-xs text-[var(--color-danger)]">{health.lastErrorMessage}</p>}
        <Link
          href={`/integrations/${encodeURIComponent(conversation.connectionId)}`}
          className="mt-3 inline-flex min-h-11 items-center text-sm text-[var(--color-ink-primary)] underline"
        >
          View connection diagnostics
        </Link>
      </Card>

      <Card title="Customer">
        <p className="text-sm text-[var(--color-ink-primary)]">{conversation.customerName}</p>
        <p className="text-xs text-[var(--color-ink-secondary)]">{conversation.customerIdentifier}</p>
      </Card>
    </div>
  );
}
