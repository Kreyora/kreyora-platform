import type {
  Conversation,
  ConversationAssignee,
  ConversationState,
  ConversationStatusAction,
  Message,
  PaginatedResult,
} from "@/lib/types";

export interface ConversationListParams {
  state?: ConversationState;
  unreadOnly?: boolean;
  assignedTo?: string;
  /** The staff queue: person-owned chats where the customer is waiting, longest wait first (M09-S07/S08). */
  needsPerson?: boolean;
  /** 1-based page; `PaginatedResult.cursor` holds the next page number. */
  page?: number;
  pageSize?: number;
  /** Demo adapter only: client-side search over fixtures. */
  search?: string;
  channel?: string;
}

/**
 * Inbox port (M08-S06). The API adapter talks to `/v1/conversations`; the demo adapter mutates fixtures only.
 * Every write is authorized by the server; callers must treat returned objects as the source of truth.
 */
export interface ConversationClient {
  listConversations(params?: ConversationListParams): Promise<PaginatedResult<Conversation>>;
  getConversation(id: string): Promise<Conversation>;
  /** Messages in chronological order; `cursor` is the ID to page older messages with `before`. */
  getMessages(conversationId: string, options?: { before?: string }): Promise<PaginatedResult<Message>>;
  /** Same idempotency key → same message (safe to retry an attempt). */
  sendReply(conversationId: string, text: string, idempotencyKey: string): Promise<Message>;
  markRead(conversationId: string): Promise<Conversation>;
  takeOver(conversationId: string): Promise<Conversation>;
  release(conversationId: string): Promise<Conversation>;
  assign(conversationId: string, userId: string): Promise<Conversation>;
  unassign(conversationId: string): Promise<Conversation>;
  setLabels(conversationId: string, labels: string[]): Promise<Conversation>;
  changeStatus(conversationId: string, action: ConversationStatusAction): Promise<Conversation>;
  listAssignees(): Promise<ConversationAssignee[]>;
}
