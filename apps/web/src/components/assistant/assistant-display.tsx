"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import type { BadgeVariant } from "@/components/ui/badge";
import { ApiClientError } from "@/lib/api";
import type { AssistantTurnOutcome, KnowledgeCategory, Role } from "@/lib/types";

// M09-S08: shared copy and helpers for the assistant screens. Codes come from the server; this only labels them.

export const ASSISTANT_NAV = [
  { label: "Overview", href: "/assistant" },
  { label: "Knowledge", href: "/assistant/knowledge" },
  { label: "Console", href: "/assistant/console" },
  { label: "History", href: "/assistant/history" },
] as const;

export function AssistantNav() {
  const pathname = usePathname();
  return (
    <nav className="mt-4 flex flex-wrap gap-1 border-b border-[var(--color-border)]" aria-label="Assistant sections">
      {ASSISTANT_NAV.map((item) => {
        const current = pathname === item.href;
        return (
          <Link
            key={item.href}
            href={item.href}
            aria-current={current ? "page" : undefined}
            className={`inline-flex min-h-11 items-center px-3 text-sm transition-colors duration-[var(--duration-hover)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] ${
              current ? "border-b-2 border-[var(--color-ink-primary)] font-semibold text-[var(--color-ink-primary)]" : "text-[var(--color-ink-secondary)] hover:text-[var(--color-ink-primary)]"
            }`}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}

export function AssistantHeader({ title, description, actions }: { title: string; description: string; actions?: ReactNode }) {
  return (
    <header>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">{title}</h1>
        {actions}
      </div>
      <p className="mt-1 max-w-2xl text-sm text-[var(--color-ink-secondary)]">{description}</p>
      <AssistantNav />
    </header>
  );
}

/** Owner and Admin edit assistant settings and use the console (server-enforced; this only hides controls). */
export const canEditAssistant = (role: Role) => role === "owner" || role === "admin";

export const OUTCOME_LABEL: Record<AssistantTurnOutcome, { label: string; variant: BadgeVariant }> = {
  running: { label: "Running", variant: "info" },
  replied: { label: "Replied", variant: "success" },
  escalated: { label: "Handed to a person", variant: "warning" },
  fallback: { label: "Safe hand-off", variant: "warning" },
  skipped: { label: "Skipped", variant: "neutral" },
  superseded: { label: "Superseded", variant: "neutral" },
  blocked: { label: "Not sent", variant: "neutral" },
  abandoned: { label: "Abandoned", variant: "danger" },
};

/** Plain-language reasons for the stable reason codes (unknown codes are shown as-is). */
export const REASON_LABEL: Record<string, string> = {
  replied: "Answered",
  playground: "Console test",
  automation_paused: "A person owns the chat",
  taken_over_during_turn: "Taken over while thinking",
  received_before_release: "Sent before the hand-back",
  superseded: "A newer message came in",
  conversation_busy: "Another reply in progress",
  tenant_busy: "Shop busy",
  platform_disabled: "Assistant switched off by Kreyora",
  assistant_inactive: "Assistant not active for this shop",
  not_entitled: "Assistant not enabled for this shop",
  connection_unavailable: "Channel disconnected",
  customer_safety: "Spam or erased customer",
  outside_hours: "Outside business hours",
  reply_rate_limit: "Reply limit per hour",
  tenant_daily_limit: "Daily limit reached",
  platform_daily_limit: "Platform daily limit",
  circuit_open: "AI provider paused after errors",
  data_policy: "Data policy (real customers need the paid provider)",
  keyword_escalation: "Escalation keyword",
  person_requested: "Customer asked for a person",
  unrecognized_media: "Photo or file the assistant can't read",
  model_escalation: "Assistant chose to hand off",
  loop_limit: "Too many steps",
  tool_limit: "Too many tool calls",
  token_budget: "Token budget",
  turn_deadline: "Took too long",
  validation_failed: "Reply failed the safety checks",
  truncated: "Reply cut off",
  fallback_cooldown: "Hand-off notice already sent",
  enqueue_denied: "Message window closed",
  unexpected_error: "Unexpected error",
};

export const reasonLabel = (code: string) =>
  REASON_LABEL[code] ?? (code.startsWith("provider_failure") ? "AI provider failed" : code.replaceAll("_", " "));

export const ESCALATION_LABEL: Record<string, string> = {
  customer_requests_person: "Asked for a person",
  complaint: "Complaint",
  refund_or_exchange: "Refund or exchange",
  custom_or_wholesale_order: "Custom or wholesale order",
  health_or_safety: "Health or safety",
  legal: "Legal",
  payment_dispute: "Payment problem",
  abusive_message: "Abusive message",
  tool_unavailable: "Assistant couldn't answer",
};

export const escalationLabel = (category: string) => ESCALATION_LABEL[category] ?? category.replaceAll("_", " ");

export const CATEGORY_LABEL: Record<KnowledgeCategory, string> = {
  faq: "FAQ",
  delivery: "Delivery",
  returns: "Returns",
  payment: "Payment",
  brand: "Brand",
  other: "Other",
};

export const READINESS_LABEL: Record<string, string> = {
  store_ready: "Store can take orders",
  channel_connected: "A channel is connected",
  policy_reviewed: "Settings reviewed",
  knowledge_approved: "At least one approved FAQ",
  ai_entitled: "Assistant enabled for this shop",
  platform_enabled: "Kreyora has the assistant switched on",
};

export const READINESS_FIX: Record<string, { href: string; label: string }> = {
  store_ready: { href: "/storefront", label: "Set up the store" },
  channel_connected: { href: "/integrations", label: "Connect a channel" },
  policy_reviewed: { href: "#settings", label: "Review settings below" },
  knowledge_approved: { href: "/assistant/knowledge", label: "Add an FAQ" },
};

export type ScreenError = { kind: "denied" | "conflict" | "validation" | "rate" | "unavailable"; message: string };

/** Maps an API failure to the copy every assistant screen shows (permission-denied, conflict, validation, limits, offline). */
export function describeAssistantError(error: unknown): ScreenError {
  if (error instanceof ApiClientError) {
    if (error.status === 401 || error.status === 403) return { kind: "denied", message: "You don't have permission for this. Ask the shop owner or an admin." };
    if (error.status === 409) return { kind: "conflict", message: error.detail && !/^HTTP \d/.test(error.detail) ? error.detail : "Someone else changed this meanwhile. Refresh to see the latest, then try again." };
    if (error.status === 400 || error.status === 422) return { kind: "validation", message: error.detail || "Some values aren't valid." };
    if (error.status === 429) return { kind: "rate", message: "Too many requests. Wait a moment and try again." };
    if (error.status >= 500) return { kind: "unavailable", message: "The server had a problem. Try again in a moment." };
    return { kind: "unavailable", message: error.detail || "Something went wrong." };
  }
  return { kind: "unavailable", message: "Can't reach Kreyora right now. Check your connection and try again." };
}

export function ErrorNote({ error, onRetry }: { error: ScreenError; onRetry?: () => void }) {
  return (
    <div role="alert" className="mt-4 rounded-[var(--radius-md)] border border-[var(--color-danger)] p-3 text-sm text-[var(--color-ink-primary)]">
      <p>{error.message}</p>
      {onRetry && error.kind !== "denied" && (
        <button type="button" onClick={onRetry} className="mt-2 min-h-11 underline">
          Try again
        </button>
      )}
    </div>
  );
}

export function formatUsd(value: number) {
  return value === 0 ? "$0" : value < 0.01 ? "< $0.01" : `$${value.toFixed(2)}`;
}

export function formatWhen(iso: string) {
  return new Date(iso).toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
}
