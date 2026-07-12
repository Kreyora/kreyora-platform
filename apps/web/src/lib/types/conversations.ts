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

export type Channel = "facebook" | "instagram" | "whatsapp" | "tiktok" | "storefront";

export type MessageDirection = "inbound" | "outbound";
export type MessageDeliveryState = "pending" | "sent" | "delivered" | "read" | "failed";

export interface Assignment {
  assigneeId: string;
  assigneeName: string;
  assignedAt: Timestamp;
}

export interface Message {
  id: string;
  conversationId: string;
  direction: MessageDirection;
  senderName: string;
  senderType: "customer" | "staff" | "bot";
  content: string;
  attachments: Array<{ url: string; type: string; name: string }>;
  deliveryState: MessageDeliveryState;
  createdAt: Timestamp;
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
}
