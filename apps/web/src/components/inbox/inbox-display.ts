import type { BadgeVariant } from "@/components/ui/badge";
import type { ConversationState, ConversationStatusAction, MessageDeliveryState } from "@/lib/types";

export const STATE_MAP: Record<ConversationState, { label: string; variant: BadgeVariant }> = {
  new: { label: "New", variant: "info" },
  bot_active: { label: "Automation", variant: "info" },
  human_assigned: { label: "Human", variant: "warning" },
  awaiting_customer: { label: "Awaiting customer", variant: "neutral" },
  checkout_in_progress: { label: "Checkout", variant: "info" },
  order_created: { label: "Ordered", variant: "success" },
  resolved: { label: "Resolved", variant: "success" },
  closed: { label: "Closed", variant: "neutral" },
  spam: { label: "Spam", variant: "danger" },
};

export const CHANNEL_MAP: Record<string, { label: string; short: string; color: string }> = {
  facebook: { label: "Facebook", short: "FB", color: "bg-blue-500" },
  instagram: { label: "Instagram", short: "IG", color: "bg-pink-500" },
  whatsapp: { label: "WhatsApp", short: "WA", color: "bg-green-500" },
  tiktok: { label: "TikTok", short: "TT", color: "bg-gray-800" },
  storefront: { label: "Storefront", short: "SF", color: "bg-purple-500" },
  viber: { label: "Viber", short: "VB", color: "bg-violet-600" },
  telegram: { label: "Telegram", short: "TG", color: "bg-sky-600" },
  simulator: { label: "Simulator", short: "SIM", color: "bg-gray-600" },
};

export const DELIVERY_LABEL: Record<MessageDeliveryState, { label: string; variant: BadgeVariant }> = {
  pending: { label: "Sending…", variant: "neutral" },
  sent: { label: "Sent", variant: "info" },
  delivered: { label: "Delivered", variant: "success" },
  read: { label: "Read", variant: "success" },
  failed: { label: "Failed", variant: "danger" },
};

export const STATUS_ACTION_LABEL: Record<ConversationStatusAction, string> = {
  resolve: "Resolve",
  reopen: "Reopen",
  close: "Close",
  mark_spam: "Mark as spam",
  unmark_spam: "Not spam",
};

/** Mirrors the server's ADR-017 rules for which actions to offer; the server still decides. */
export function availableStatusActions(state: ConversationState): ConversationStatusAction[] {
  if (state === "spam") return ["unmark_spam"];
  if (state === "resolved") return ["reopen", "close", "mark_spam"];
  if (state === "closed") return ["reopen", "mark_spam"];
  return ["resolve", "close", "mark_spam"];
}

export const REPLY_WINDOW_MS = 24 * 60 * 60 * 1000;

/** Display-only hint; the server enforces the provider window. */
export function windowLikelyClosed(lastCustomerMessageAt: string | undefined, now = Date.now()): boolean {
  if (!lastCustomerMessageAt) return false;
  return now - new Date(lastCustomerMessageAt).getTime() > REPLY_WINDOW_MS;
}

export function relativeTime(iso: string | undefined, now = Date.now()): string {
  if (!iso) return "";
  const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return new Date(iso).toLocaleDateString();
}
