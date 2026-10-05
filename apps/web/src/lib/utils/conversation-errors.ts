import { ApiClientError } from "@/lib/api/errors";

export interface InboxErrorCopy {
  /** Stable server reason code when present (from `urn:kreyora:problem:<code>`). */
  code?: string;
  title: string;
  detail: string;
  /** True when the user should refresh and retry (concurrent change). */
  refresh?: boolean;
}

const PROBLEM_PREFIX = "urn:kreyora:problem:";

const COPY: Record<string, Omit<InboxErrorCopy, "code">> = {
  window_closed: {
    title: "Reply window closed",
    detail: "The customer hasn't messaged recently enough. This channel only allows replies within 24 hours of their last message.",
  },
  window_closed_human_agent_unavailable: {
    title: "Reply window closed",
    detail: "It's been more than 24 hours since the customer's last message. Late human replies need Meta's Human Agent approval, which isn't enabled.",
  },
  conversation_is_spam: { title: "Marked as spam", detail: "Replies are blocked while this conversation is marked as spam. Unmark it first." },
  connection_inactive: { title: "Channel not connected", detail: "This channel connection isn't active. Reconnect it in Integrations, then try again." },
  capability_unsupported: { title: "Not supported", detail: "This channel can't send this kind of message." },
  text_required: { title: "Message is empty", detail: "Type a message before sending." },
  text_too_long: { title: "Message too long", detail: "Shorten the message and try again." },
  automation_paused_by_takeover: { title: "Automation paused", detail: "A team member has taken over this conversation." },
  invalid_transition: { title: "Not allowed right now", detail: "That status change isn't possible from the conversation's current status." },
  conversation_changed: { title: "Updated by someone else", detail: "This conversation changed while you were working. Refresh to see the latest.", refresh: true },
  assignee_not_member: { title: "Can't assign", detail: "That person isn't an active member of this workspace." },
  invalid_labels: { title: "Labels not saved", detail: "Use up to 20 labels of at most 48 characters." },
  delivery_unconfirmed: {
    title: "Delivery not confirmed",
    detail: "The channel didn't confirm this message. The customer may already have it.",
  },
};

export function reasonCode(error: unknown): string | undefined {
  if (error instanceof ApiClientError && error.type?.startsWith(PROBLEM_PREFIX)) {
    return error.type.slice(PROBLEM_PREFIX.length);
  }
  return undefined;
}

/** Plain-language copy for inbox failures; never exposes stack traces or raw payloads. */
export function describeInboxError(error: unknown): InboxErrorCopy {
  const code = reasonCode(error);
  if (code && COPY[code]) {
    return { code, ...COPY[code] };
  }

  if (error instanceof ApiClientError) {
    if (error.status === 404) return { title: "Not found", detail: "This conversation doesn't exist or isn't in this workspace." };
    if (error.status === 403) return { title: "No permission", detail: "Your role can view this inbox but can't make this change." };
    if (error.status === 409) return { title: "Updated by someone else", detail: "Refresh to see the latest.", refresh: true };
    return { code, title: "Something went wrong", detail: error.detail || "The request failed. Try again." };
  }

  return { title: "Connection problem", detail: "We couldn't reach Kreyora. Check your connection and try again." };
}

/** Copy for a failed outbound message's server failure code. */
export function describeFailureCode(code: string | undefined): string {
  if (!code) return "This message wasn't delivered.";
  return COPY[code]?.detail ?? "The channel rejected this message.";
}
