import type { TenantId, Timestamp } from "./common";

export type ConversationState =
  | "new"
  | "bot_active"
  | "human_assigned"
  | "awaiting_customer"
  | "checkout_in_progress"
  | "order_created"
  | "resolved"
  | "closed"
  | "spam";

export type Channel = "facebook" | "instagram" | "whatsapp" | "tiktok" | "storefront" | "viber" | "telegram" | "simulator";

export type MessageDirection = "inbound" | "outbound";
export type MessageDeliveryState = "pending" | "sent" | "delivered" | "read" | "failed";

export interface Assignment {
  assigneeId: string;
  assigneeName: string;
  assignedAt: Timestamp;
}

/** `external` = a business message sent outside Kreyora (e.g. typed in the Instagram app). */
export type MessageSenderType = "customer" | "staff" | "bot" | "external";

export interface Message {
  id: string;
  conversationId: string;
  direction: MessageDirection;
  senderName: string;
  senderType: MessageSenderType;
  content: string;
  attachments: Array<{ url: string; type: string; name: string }>;
  deliveryState: MessageDeliveryState;
  createdAt: Timestamp;
  /** Server-assigned failure code for failed outbound messages (e.g. `delivery_unconfirmed`). */
  failureCode?: string;
  /** True when content was removed by an erasure request. */
  isRedacted?: boolean;
  reactions?: Array<{ emoji: string; count: number }>;
}

export interface Conversation {
  id: string;
  tenantId: TenantId;
  channel: Channel;
  state: ConversationState;
  customerName: string;
  customerIdentifier: string;
  lastMessage?: string;
  lastMessageAt?: Timestamp;
  unreadCount: number;
  assignment?: Assignment;
  labels: string[];
  isAutomationActive: boolean;
  connectionId: string;
  createdAt: Timestamp;
  updatedAt: Timestamp;
  /** Last customer message time; the server decides reply windows, this is only a display hint. */
  lastCustomerMessageAt?: Timestamp;
}

export interface ConversationAssignee {
  userId: string;
  displayName: string;
  role: string;
}

export type ConversationStatusAction = "resolve" | "reopen" | "close" | "mark_spam" | "unmark_spam";
