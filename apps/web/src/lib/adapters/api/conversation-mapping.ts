import type { components } from "@/lib/api/generated/v1";
import type {
  Channel,
  Conversation,
  ConversationAssignee,
  ConversationState,
  ConversationStatusAction,
  Message,
  MessageDeliveryState,
  MessageSenderType,
} from "@/lib/types";

type Schemas = components["schemas"];

/**
 * The generated schema types enums as integers, but the API serializes them as camelCase strings
 * (recorded as an OpenAPI generation gap in M08-S06). Wire types keep the generated object shapes and
 * widen enum fields to what actually arrives.
 */
type WireEnum = string | number;

export type WireConversationSummary = Omit<Schemas["ConversationSummaryItem"], "channel" | "status"> & {
  channel: WireEnum;
  status: WireEnum;
};

export type WireConversationDetail = Omit<Schemas["ConversationDetailItem"], "channel" | "status" | "automationMode" | "customer"> & {
  channel: WireEnum;
  status: WireEnum;
  automationMode: WireEnum;
  customer: Omit<Schemas["CustomerIdentitySummary"], "channel"> & { channel: WireEnum };
};

export type WireMessage = Omit<Schemas["MessageItem"], "direction" | "origin" | "kind" | "deliveryStatus"> & {
  direction: WireEnum;
  origin: WireEnum;
  kind: WireEnum;
  deliveryStatus: WireEnum | null;
};

export type WireAssignee = Omit<Schemas["ConversationAssigneeItem"], "role"> & { role: WireEnum };

/** Lowercase without separators, so "HumanAssigned", "humanAssigned" and "human_assigned" compare equal. */
function norm(value: WireEnum | null | undefined): string {
  return String(value ?? "").replace(/[_\s-]/g, "").toLowerCase();
}

const STATUS_TO_STATE: Record<string, ConversationState> = {
  new: "new",
  botactive: "bot_active",
  humanassigned: "human_assigned",
  awaitingcustomer: "awaiting_customer",
  checkoutinprogress: "checkout_in_progress",
  ordercreated: "order_created",
  resolved: "resolved",
  closed: "closed",
  spam: "spam",
  // Numeric fallback (ConversationStatus enum order).
  "1": "new", "2": "bot_active", "3": "human_assigned", "4": "awaiting_customer", "5": "checkout_in_progress",
  "6": "order_created", "7": "resolved", "8": "closed", "9": "spam",
};

export function toConversationState(status: WireEnum): ConversationState {
  return STATUS_TO_STATE[norm(status)] ?? "new";
}

/** API query value for a UI state (camelCase enum name). */
export function toApiStatus(state: ConversationState): string {
  return state.replace(/_([a-z])/g, (_, c: string) => c.toUpperCase());
}

const CHANNELS: Record<string, Channel> = {
  instagram: "instagram",
  messenger: "facebook",
  whatsapp: "whatsapp",
  viber: "viber",
  telegram: "telegram",
  simulator: "simulator",
};

export function toChannel(channel: WireEnum): Channel {
  return CHANNELS[norm(channel)] ?? "simulator";
}

const CHANNEL_APP_NAMES: Partial<Record<Channel, string>> = {
  instagram: "Instagram",
  facebook: "Messenger",
  whatsapp: "WhatsApp",
  viber: "Viber",
  telegram: "Telegram",
};

export function toSenderType(origin: WireEnum): MessageSenderType {
  switch (norm(origin)) {
    case "staff":
      return "staff";
    case "automation":
      return "bot";
    case "providernative":
      return "external";
    default:
      return "customer";
  }
}

export function toDeliveryState(message: Pick<WireMessage, "isPending" | "deliveryStatus" | "direction">): MessageDeliveryState {
  if (norm(message.direction) !== "outbound") {
    return "delivered";
  }

  if (message.isPending || message.deliveryStatus === null || message.deliveryStatus === undefined) {
    return "pending";
  }

  const status = norm(message.deliveryStatus);
  return status === "read" || status === "delivered" || status === "failed" ? status : "sent";
}

export function assigneeName(userId: string | null | undefined, assignees: readonly ConversationAssignee[]): string | undefined {
  if (!userId) return undefined;
  return assignees.find((a) => a.userId === userId)?.displayName ?? "Team member";
}

function base(item: WireConversationSummary | WireConversationDetail, assignees: readonly ConversationAssignee[]) {
  const assignedTo = assigneeName(item.assignedUserId, assignees);
  return {
    id: item.id,
    tenantId: "",
    channel: toChannel(item.channel),
    state: toConversationState(item.status),
    unreadCount: Number(item.unreadCount) || 0,
    labels: [...item.labels],
    isAutomationActive: item.isAutomationActive,
    connectionId: item.connectionId,
    lastMessageAt: item.lastMessageAt ?? undefined,
    assignment: item.assignedUserId && assignedTo
      ? { assigneeId: item.assignedUserId, assigneeName: assignedTo, assignedAt: item.assignedAt ?? item.createdAt }
      : undefined,
    createdAt: item.createdAt,
    updatedAt: item.modifiedAt,
  };
}

export function toConversationFromSummary(item: WireConversationSummary, assignees: readonly ConversationAssignee[]): Conversation {
  return {
    ...base(item, assignees),
    customerName: item.customerLabel,
    customerIdentifier: item.customerLabel,
    lastMessage: item.lastMessagePreview ?? undefined,
  };
}

export function toConversationFromDetail(item: WireConversationDetail, assignees: readonly ConversationAssignee[]): Conversation {
  return {
    ...base(item, assignees),
    customerName: item.customer.customerLabel,
    customerIdentifier: item.customer.isErased ? "Customer data erased" : item.customer.customerLabel,
    lastCustomerMessageAt: item.lastCustomerMessageAt ?? undefined,
  };
}

export function toMessage(item: WireMessage, channel: Channel, assignees: readonly ConversationAssignee[]): Message {
  const senderType = toSenderType(item.origin);
  const senderName =
    senderType === "staff" ? assigneeName(item.actorUserId, assignees) ?? "Staff"
    : senderType === "bot" ? "Automation"
    : senderType === "external" ? `Sent from the ${CHANNEL_APP_NAMES[channel] ?? "provider"} app`
    : "Customer";
  const isMedia = norm(item.kind) === "media";

  return {
    id: item.id,
    conversationId: item.conversationId,
    direction: norm(item.direction) === "outbound" ? "outbound" : "inbound",
    senderName,
    senderType,
    content: item.isRedacted ? "Message removed" : item.text ?? "",
    attachments: isMedia && item.mediaUrl && !item.isRedacted
      ? [{ url: item.mediaUrl, type: item.mediaContentType ?? "file", name: `${item.mediaContentType ?? "file"} attachment` }]
      : [],
    deliveryState: toDeliveryState(item),
    createdAt: item.occurredAt,
    failureCode: item.deliveryFailureCode ?? undefined,
    isRedacted: item.isRedacted,
    reactions: item.reactions.map((r) => ({ emoji: r.emoji, count: Number(r.count) || 0 })),
  };
}

export function toAssignee(item: WireAssignee): ConversationAssignee {
  return { userId: item.userId, displayName: item.displayName, role: norm(item.role) };
}

const ACTIONS: Record<ConversationStatusAction, string> = {
  resolve: "resolve",
  reopen: "reopen",
  close: "close",
  mark_spam: "markSpam",
  unmark_spam: "unmarkSpam",
};

export function toApiStatusAction(action: ConversationStatusAction): string {
  return ACTIONS[action];
}
